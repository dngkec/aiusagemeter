import AppKit
import UsageMeterCore
import SwiftUI

final class PassivePanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private let model = AppModel()
    private var panel: PassivePanel!
    private var settingsWindow: NSWindow?
    private var statusItem: NSStatusItem!
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private var moveMonitors: [Any] = []
    private var observers: [NSObjectProtocol] = []
    private var shrinkTask: Task<Void, Never>?
    private var snapshotCaptured = false
    private var pointerInsidePanel = true
    private var pointerLeftTask: Task<Void, Never>?
    private var menuSignature: [String] = []

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        configurePanel()
        configureStatusItem()
        configureEvents()
        model.onPresentationChange = { [weak self] in self?.updatePresentation() }
        model.onOpenSettings = { [weak self] in self?.showSettings() }
        model.start()
        if ProcessInfo.processInfo.environment["USAGEMETER_SNAPSHOT_TARGET"] == "settings" {
            // Which pane a documentation capture wants. Only the deterministic
            // ones are addressable; a provider pane depends on live readings.
            switch ProcessInfo.processInfo.environment["USAGEMETER_SNAPSHOT_PANE"] {
            case "about": model.settingsSelection = .about
            case "general": model.settingsSelection = .general
            default: break
            }
            showSettings()
            captureIfRequested()
        }
    }

    func applicationWillTerminate(_ notification: Notification) {
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        for monitor in moveMonitors { NSEvent.removeMonitor(monitor) }
        for observer in observers {
            NotificationCenter.default.removeObserver(observer)
            NSWorkspace.shared.notificationCenter.removeObserver(observer)
        }
    }

    // MARK: - Panel

    private func configurePanel() {
        panel = PassivePanel(contentRect: .zero, styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.level = NSWindow.Level(rawValue: NSWindow.Level.statusBar.rawValue + 2)
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.hidesOnDeactivate = false
        panel.isMovable = false
        panel.acceptsMouseMovedEvents = true
        let hosting = NSHostingView(rootView: NotchRootView(model: model))
        hosting.autoresizingMask = [.width, .height]
        panel.contentView = hosting
        panel.orderFrontRegardless()
    }

    private func selectedScreen() -> NSScreen? {
        if let selected = model.preferences.screenIdentifier,
           let screen = NSScreen.screens.first(where: { $0.quotaIdentifier == selected }) { return screen }
        let mouse = NSEvent.mouseLocation
        return NSScreen.screens.first(where: { NSMouseInRect(mouse, $0.frame, false) }) ?? NSScreen.main ?? NSScreen.screens.first
    }

    /// The window grows before the overlay opens and shrinks only once it has
    /// finished closing, so a window resize never runs under an animation.
    private func updatePresentation() {
        guard let screen = selectedScreen() else { return }
        guard model.preferences.overlayVisible else { shrinkTask?.cancel(); panel.orderOut(nil); return }

        let visible = screen.visibleFrame
        let bounds = Rect(x: visible.minX, y: visible.minY, width: visible.width, height: visible.height)
        let expanded = OverlayLayout.panelFrame(
            visibleScreen: bounds,
            width: Double(Metrics.panelWidth),
            railHeight: model.railHeight,
            panelHeight: model.panelHeight,
            position: model.preferences.verticalPosition,
            offset: model.preferences.verticalOffset
        )

        shrinkTask?.cancel()
        if model.idleMini {
            let mini = OverlayLayout.miniFrame(
                visibleScreen: bounds,
                width: Double(Metrics.miniTarget),
                height: Double(Metrics.miniHeight + Metrics.miniTarget),
                railHeight: model.railHeight,
                position: model.preferences.verticalPosition,
                offset: model.preferences.verticalOffset
            )
            shrinkTask = Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(420))
                guard !Task.isCancelled, let self, self.model.idleMini else { return }
                self.apply(frame: mini)
            }
        } else {
            apply(frame: expanded)
        }
        panel.orderFrontRegardless()
        rebuildMenu()
        captureIfRequested()
    }

    /// The window server, not SwiftUI, has the last word on where the pointer
    /// is. A passive panel never becomes key, so a hover exit can be dropped;
    /// this is what stops a dropped one from stranding a card on screen.
    ///
    /// It corrects, and never leads. A pointer mid-flight samples untidily, so
    /// the overlay is only told the pointer has gone once it has stayed gone —
    /// long enough that the answer cannot be a transient.
    private func pointerMoved() {
        guard panel.isVisible else { return }
        let inside = panel.frame.insetBy(dx: -8, dy: -8).contains(NSEvent.mouseLocation)
        guard inside != pointerInsidePanel else { return }
        pointerInsidePanel = inside
        pointerLeftTask?.cancel()
        guard !inside else { return }
        pointerLeftTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(500))
            guard !Task.isCancelled, let self, !self.pointerInsidePanel else { return }
            self.model.pointerLeftOverlay()
        }
    }

    private func apply(frame: Rect) {
        let rect = NSRect(x: frame.x, y: frame.y, width: frame.width, height: frame.height)
        guard panel.frame != rect else { return }
        panel.setFrame(rect, display: true)
    }

    // MARK: - Menu bar

    private func configureStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        statusItem.button?.image = StatusGauge.image(percent: nil, status: .loading)
        rebuildMenu()
    }

    /// Rebuilding renders a fresh status image, so it is worth asking first
    /// whether anything the menu shows has actually moved.
    private func rebuildMenu() {
        let signature = model.visibleSnapshots.map { "\($0.id.rawValue):\($0.status.rawValue):\(Int($0.primaryPercent.rounded()))" }
            + [model.preferences.overlayVisible ? "shown" : "hidden"]
        guard signature != menuSignature else { return }
        menuSignature = signature

        let menu = NSMenu()
        for snapshot in model.visibleSnapshots {
            let value = snapshot.status.isReady ? "\(Int(snapshot.primaryPercent.rounded()))%" : snapshot.status.shortLabel
            let item = NSMenuItem(title: "\(snapshot.name)   \(value)", action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
        }
        if !model.visibleSnapshots.isEmpty { menu.addItem(.separator()) }
        menu.addItem(withTitle: "Refresh Now", action: #selector(refreshNow), keyEquivalent: "r")
        menu.addItem(withTitle: model.preferences.overlayVisible ? "Hide Notch" : "Show Notch", action: #selector(toggleOverlay), keyEquivalent: "h")
        menu.addItem(.separator())
        menu.addItem(withTitle: "Settings…", action: #selector(showSettingsAction), keyEquivalent: ",")
        menu.addItem(.separator())
        let support = NSMenuItem(title: "Buy Me a Coffee…", action: #selector(openSupport), keyEquivalent: "")
        support.image = NSImage(systemSymbolName: "heart.fill", accessibilityDescription: nil)
        menu.addItem(support)
        menu.addItem(withTitle: "UsageMeter on GitHub…", action: #selector(openRepository), keyEquivalent: "")
        menu.addItem(withTitle: "Design by \(SupportLinks.designerHandle)…", action: #selector(openDesigner), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit UsageMeter", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        // Everything here is handled by the delegate except Quit, which the
        // application itself answers: pointing that one at the delegate would
        // leave a menu-bar-only app with no way out.
        for item in menu.items where item.action != nil { item.target = self }
        menu.items.last?.target = NSApp
        statusItem.menu = menu

        let headline = model.headline
        statusItem.button?.image = StatusGauge.image(
            percent: headline?.status.isReady == true ? headline?.primaryPercent : nil,
            status: headline?.status ?? .setupNeeded
        )
        statusItem.button?.toolTip = model.visibleSnapshots.isEmpty
            ? "UsageMeter"
            : model.visibleSnapshots.map { "\($0.name): \($0.status.isReady ? "\(Int($0.primaryPercent.rounded()))%" : $0.status.shortLabel)" }.joined(separator: "\n")
    }

    // MARK: - Events

    private func configureEvents() {
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .leftMouseDown, .rightMouseDown]) { [weak self] event in
            guard let self else { return event }
            if event.type == .keyDown, event.keyCode == 53 { self.model.collapse(); return nil }
            if event.window != self.panel { self.model.collapse() }
            return event
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.model.collapse() }
        }
        // Both halves are needed: the global monitor sees the pointer while the
        // app is passive, the local one while a menu or Settings has focus.
        if let monitor = NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved, .leftMouseDragged], handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.pointerMoved() }
        }) { moveMonitors.append(monitor) }
        if let monitor = NSEvent.addLocalMonitorForEvents(matching: [.mouseMoved, .leftMouseDragged], handler: { [weak self] event in
            MainActor.assumeIsolated { self?.pointerMoved() }
            return event
        }) { moveMonitors.append(monitor) }
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.updatePresentation() }
        })
        let workspace = NSWorkspace.shared.notificationCenter
        observers.append(workspace.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in await self?.model.refresh() }
        })
        observers.append(workspace.addObserver(forName: NSWorkspace.activeSpaceDidChangeNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in self?.updatePresentation() }
        })
    }

    @objc private func refreshNow() { Task { await model.refresh() } }
    @objc private func toggleOverlay() { model.toggleOverlay() }
    @objc private func showSettingsAction() { showSettings() }
    @objc private func openSupport() { model.openSupport() }
    @objc private func openRepository() { model.openRepository() }
    @objc private func openDesigner() { model.openDesigner() }

    private func showSettings() {
        if settingsWindow == nil {
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 880, height: 660), styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView], backing: .buffered, defer: false)
            window.title = "UsageMeter"
            window.titlebarAppearsTransparent = true
            window.center()
            window.isReleasedWhenClosed = false
            window.contentView = NSHostingView(rootView: SettingsView(model: model))
            settingsWindow = window
        }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    // MARK: - Deterministic capture

    private func captureIfRequested() {
        guard !snapshotCaptured, let path = ProcessInfo.processInfo.environment["USAGEMETER_SNAPSHOT_PATH"] else { return }
        if ProcessInfo.processInfo.environment["USAGEMETER_SNAPSHOT_TARGET"] == "settings" {
            guard let view = settingsWindow?.contentView else { return }
            snapshotCaptured = true
            capture(view: view, path: path, attempt: 0, best: nil, trim: false)
            return
        }
        guard ProcessInfo.processInfo.environment["USAGEMETER_DEMO_EXPANDED"] != "1" || model.expandedProvider != nil,
              let view = panel.contentView else { return }
        snapshotCaptured = true
        capture(view: view, path: path, attempt: 0, best: nil, trim: true)
    }

    private func capture(view: NSView, path: String, attempt: Int, best: (data: Data, score: Int)?, trim: Bool) {
        DispatchQueue.main.asyncAfter(deadline: .now() + (attempt == 0 ? 0.6 : 0.16)) { [weak self, weak view] in
            guard let self, let view else { return }
            view.layoutSubtreeIfNeeded()
            view.displayIfNeeded()
            var candidate = best
            if let rep = view.bitmapImageRepForCachingDisplay(in: view.bounds) {
                view.cacheDisplay(in: view.bounds, to: rep)
                if let written = SnapshotWriter.png(rep, trim: trim), written.score > (candidate?.score ?? -1) { candidate = written }
            }
            if (candidate?.score ?? 0) < 2_500, attempt < 15 {
                self.capture(view: view, path: path, attempt: attempt + 1, best: candidate, trim: trim)
                return
            }
            if let data = candidate?.data {
                try? FileManager.default.createDirectory(at: URL(fileURLWithPath: path).deletingLastPathComponent(), withIntermediateDirectories: true)
                try? data.write(to: URL(fileURLWithPath: path), options: .atomic)
            }
            if ProcessInfo.processInfo.environment["USAGEMETER_EXIT_AFTER_SNAPSHOT"] == "1" { NSApp.terminate(nil) }
        }
    }
}

/// The overlay window is mostly transparent; captures crop to what was drawn.
enum SnapshotWriter {
    static func png(_ rep: NSBitmapImageRep, trim: Bool) -> (data: Data, score: Int)? {
        var minX = rep.pixelsWide, minY = rep.pixelsHigh, maxX = -1, maxY = -1, score = 0
        for y in 0..<rep.pixelsHigh {
            for x in 0..<rep.pixelsWide {
                guard let colour = rep.colorAt(x: x, y: y), colour.alphaComponent > 0.02 else { continue }
                if x < minX { minX = x }
                if y < minY { minY = y }
                if x > maxX { maxX = x }
                if y > maxY { maxY = y }
                if let rgb = colour.usingColorSpace(.deviceRGB),
                   max(rgb.redComponent, rgb.greenComponent, rgb.blueComponent) > 0.18 { score += 1 }
            }
        }
        guard maxX >= minX, maxY >= minY, let source = rep.cgImage else { return nil }
        let box = trim
            ? CGRect(x: minX, y: minY, width: maxX - minX + 1, height: maxY - minY + 1)
            : CGRect(x: 0, y: 0, width: rep.pixelsWide, height: rep.pixelsHigh)
        guard let cropped = source.cropping(to: box),
              let data = NSBitmapImageRep(cgImage: cropped).representation(using: .png, properties: [:]) else { return nil }
        return (data, score)
    }
}

/// A miniature of the rail gauge, drawn for the menu bar.
enum StatusGauge {
    static func image(percent: Double?, status: ProviderStatus) -> NSImage {
        let side: CGFloat = 18
        let image = NSImage(size: NSSize(width: side, height: side))
        image.lockFocus()
        let inset: CGFloat = 2.2
        let ring = NSBezierPath(ovalIn: NSRect(x: inset, y: inset, width: side - inset * 2, height: side - inset * 2))
        ring.lineWidth = 2.4
        NSColor.labelColor.withAlphaComponent(0.30).setStroke()
        ring.stroke()

        if let percent, status.isReady {
            let radius = (side - inset * 2) / 2
            let arc = NSBezierPath()
            arc.appendArc(
                withCenter: NSPoint(x: side / 2, y: side / 2),
                radius: radius,
                startAngle: 90,
                endAngle: 90 - 360 * min(max(percent, 0), 100) / 100,
                clockwise: true
            )
            arc.lineWidth = 2.4
            arc.lineCapStyle = .round
            colour(for: percent).setStroke()
            arc.stroke()
        } else {
            let dot = NSBezierPath(ovalIn: NSRect(x: side / 2 - 1.6, y: side / 2 - 1.6, width: 3.2, height: 3.2))
            NSColor.labelColor.withAlphaComponent(0.45).setFill()
            dot.fill()
        }
        image.unlockFocus()
        image.isTemplate = percent == nil
        return image
    }

    private static func colour(for percent: Double) -> NSColor {
        switch UsageColor.threshold(percent: percent) {
        case .green: return NSColor(srgbRed: 0.078, green: 1.0, blue: 0.592, alpha: 1)
        case .yellow: return NSColor(srgbRed: 0.780, green: 0.840, blue: 0.020, alpha: 1)
        case .orange: return NSColor(srgbRed: 1.0, green: 0.624, blue: 0.039, alpha: 1)
        case .red: return NSColor(srgbRed: 1.0, green: 0.271, blue: 0.227, alpha: 1)
        }
    }
}

extension NSScreen {
    var quotaIdentifier: String {
        (deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? NSNumber)?.stringValue ?? localizedName
    }
}
