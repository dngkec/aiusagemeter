import AppKit
import Combine
import AIUsageMeterCore
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
    private var titleObserver: AnyCancellable?

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)
        configureMainMenu()
        configurePanel()
        configureStatusItem()
        configureEvents()
        model.onPresentationChange = { [weak self] in self?.updatePresentation() }
        model.onOpenSettings = { [weak self] in self?.showSettings() }
        model.start()
        if ProcessInfo.processInfo.environment["AIUSAGEMETER_SNAPSHOT_TARGET"] == "settings" {
            switch ProcessInfo.processInfo.environment["AIUSAGEMETER_SNAPSHOT_PANE"] {
            case "about": model.settingsSelection = .about
            case "general": model.settingsSelection = .general
            case let named?:
                if let id = ProviderID(rawValue: named) { model.settingsSelection = .provider(id) }
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

    private func updatePresentation() {
        guard let screen = selectedScreen() else { return }
        guard model.preferences.overlayVisible else {
            shrinkTask?.cancel()
            panel.orderOut(nil)
            rebuildMenu()
            return
        }

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

    private func rebuildMenu() {
        let shown = model.visibleSnapshots
        let signature = shown.map { "\($0.id.rawValue):\($0.status.rawValue):\(Int($0.primaryPercent.rounded()))" }
            + [model.preferences.overlayVisible ? "shown" : "hidden"]
            + [model.updateState.menuTitle ?? "no-update"]
        guard signature != menuSignature else { return }
        menuSignature = signature
        let summary = model.summary

        let menu = NSMenu()
        if let heading = MenuBarText.heading(summary) {
            let item = NSMenuItem(title: heading, action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
            menu.addItem(.separator())
        }
        for snapshot in shown {
            let value = snapshot.status.isReady ? "\(Int(snapshot.primaryPercent.rounded()))%" : snapshot.status.shortLabel
            let item = NSMenuItem(title: "\(snapshot.name)   \(value)", action: nil, keyEquivalent: "")
            item.isEnabled = false
            menu.addItem(item)
        }
        if !shown.isEmpty { menu.addItem(.separator()) }
        menu.addItem(withTitle: "Refresh Now", action: #selector(refreshNow), keyEquivalent: "r")
        menu.addItem(withTitle: model.preferences.overlayVisible ? "Hide Notch" : "Show Notch", action: #selector(toggleOverlay), keyEquivalent: "")
        // Present only while there is something to install: the menu is not the place to report
        // that a background check found nothing.
        if let update = model.updateState.menuTitle {
            menu.addItem(.separator())
            let item = NSMenuItem(title: update, action: #selector(installUpdate), keyEquivalent: "")
            item.image = NSImage(systemSymbolName: "arrow.down.circle.fill", accessibilityDescription: nil)
            item.isEnabled = model.updateState.canInstall
            menu.addItem(item)
        }
        menu.addItem(.separator())
        menu.addItem(withTitle: "Settings…", action: #selector(showSettingsAction), keyEquivalent: ",")
        menu.addItem(.separator())
        let support = NSMenuItem(title: "Buy Me a Coffee…", action: #selector(openSupport), keyEquivalent: "")
        support.image = NSImage(systemSymbolName: "heart.fill", accessibilityDescription: nil)
        menu.addItem(support)
        menu.addItem(withTitle: "AIUsageMeter on GitHub…", action: #selector(openRepository), keyEquivalent: "")
        menu.addItem(withTitle: "\(SupportLinks.designerCredit)…", action: #selector(openDesigner), keyEquivalent: "")
        menu.addItem(.separator())
        menu.addItem(withTitle: "Quit AIUsageMeter", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        for item in menu.items where item.action != nil { item.target = self }
        menu.items.last?.target = NSApp
        statusItem.menu = menu

        statusItem.button?.image = StatusGauge.image(percent: summary.average, status: summary.status)
        statusItem.button?.toolTip = MenuBarText.tooltip(summary, snapshots: shown)
    }

    // MARK: - Main menu

    /// A menu-bar-only app gets no menu bar of its own, and without one ⌘V never reaches a text field.
    private func configureMainMenu() {
        let name = "AIUsageMeter"
        let main = NSMenu()

        let appItem = NSMenuItem()
        let appMenu = NSMenu()
        appMenu.addItem(withTitle: "About \(name)", action: #selector(showAboutAction), keyEquivalent: "").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Settings…", action: #selector(showSettingsAction), keyEquivalent: ",").target = self
        appMenu.addItem(withTitle: "Refresh Now", action: #selector(refreshNow), keyEquivalent: "r").target = self
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Hide \(name)", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h").target = NSApp
        appMenu.addItem(.separator())
        appMenu.addItem(withTitle: "Quit \(name)", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q").target = NSApp
        appItem.submenu = appMenu
        main.addItem(appItem)

        let editItem = NSMenuItem()
        let editMenu = NSMenu(title: "Edit")
        editMenu.addItem(withTitle: "Undo", action: Selector(("undo:")), keyEquivalent: "z")
        let redo = editMenu.addItem(withTitle: "Redo", action: Selector(("redo:")), keyEquivalent: "z")
        redo.keyEquivalentModifierMask = [.command, .shift]
        editMenu.addItem(.separator())
        editMenu.addItem(withTitle: "Cut", action: #selector(NSText.cut(_:)), keyEquivalent: "x")
        editMenu.addItem(withTitle: "Copy", action: #selector(NSText.copy(_:)), keyEquivalent: "c")
        editMenu.addItem(withTitle: "Paste", action: #selector(NSText.paste(_:)), keyEquivalent: "v")
        editMenu.addItem(withTitle: "Delete", action: #selector(NSText.delete(_:)), keyEquivalent: "")
        editMenu.addItem(withTitle: "Select All", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a")
        editItem.submenu = editMenu
        main.addItem(editItem)

        let windowItem = NSMenuItem()
        let windowMenu = NSMenu(title: "Window")
        windowMenu.addItem(withTitle: "Minimize", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m")
        windowMenu.addItem(withTitle: "Zoom", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: "")
        windowMenu.addItem(.separator())
        windowMenu.addItem(withTitle: "Close", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w")
        windowItem.submenu = windowMenu
        main.addItem(windowItem)

        let helpItem = NSMenuItem()
        let helpMenu = NSMenu(title: "Help")
        helpMenu.addItem(withTitle: "\(name) on GitHub…", action: #selector(openRepository), keyEquivalent: "")
        helpMenu.addItem(withTitle: "Report an Issue…", action: #selector(openIssues), keyEquivalent: "")
        for item in helpMenu.items { item.target = self }
        helpItem.submenu = helpMenu
        main.addItem(helpItem)

        NSApp.mainMenu = main
        NSApp.windowsMenu = windowMenu
        NSApp.helpMenu = helpMenu
    }

    // MARK: - Events

    private func configureEvents() {
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: [.keyDown, .leftMouseDown, .rightMouseDown]) { [weak self] event in
            guard let self else { return event }
            guard !self.isSettingsEvent(event.window) else { return event }
            if event.type == .keyDown, event.keyCode == 53 { self.model.collapse(); return nil }
            if event.window != self.panel { self.model.collapse() }
            return event
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            Task { @MainActor in self?.model.collapse() }
        }
        if let monitor = NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved, .leftMouseDragged], handler: { [weak self] _ in
            MainActor.assumeIsolated { self?.pointerMoved() }
        }) { moveMonitors.append(monitor) }
        if let monitor = NSEvent.addLocalMonitorForEvents(matching: [.mouseMoved, .leftMouseDragged], handler: { [weak self] event in
            MainActor.assumeIsolated { self?.pointerMoved() }
            return event
        }) { moveMonitors.append(monitor) }
        let center = NotificationCenter.default
        observers.append(center.addObserver(forName: NSApplication.didChangeScreenParametersNotification, object: nil, queue: .main) { [weak self] _ in
            Task { @MainActor in
                self?.model.reloadScreens()
                self?.updatePresentation()
            }
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
    @objc private func showAboutAction() { model.settingsSelection = .about; showSettings() }
    @objc private func openSupport() { model.openSupport() }
    @objc private func openRepository() { model.openRepository() }
    @objc private func openIssues() { model.openIssues() }
    @objc private func openDesigner() { model.openDesigner() }
    @objc private func installUpdate() { model.installUpdate() }

    private func showSettings() {
        if settingsWindow == nil {
            // A hosting controller with sizing options off: a bare hosting view misses the title bar's safe area,
            // and sizing options would pin the window to the form's ideal height.
            let controller = NSHostingController(rootView: SettingsView(model: model))
            controller.sizingOptions = []
            let window = NSWindow(
                contentRect: NSRect(x: 0, y: 0, width: 900, height: 700),
                styleMask: [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView],
                backing: .buffered,
                defer: false
            )
            window.titlebarAppearsTransparent = true
            window.title = "AIUsageMeter Settings"
            window.subtitle = model.settingsSelection.title
            window.isReleasedWhenClosed = false
            window.contentMinSize = NSSize(width: 820, height: 560)
            window.contentViewController = controller
            controller.view.autoresizingMask = [.width, .height]
            window.setContentSize(NSSize(width: 900, height: 700))
            window.setFrameAutosaveName("AIUsageMeterSettings")
            if !window.setFrameUsingName("AIUsageMeterSettings") { window.center() }
            settingsWindow = window
            titleObserver = model.$settingsSelection
                .removeDuplicates()
                .receive(on: RunLoop.main)
                .sink { [weak self] selection in self?.settingsWindow?.subtitle = selection.title }
        }
        NSApp.activate(ignoringOtherApps: true)
        settingsWindow?.makeKeyAndOrderFront(nil)
    }

    private func isSettingsEvent(_ window: NSWindow?) -> Bool {
        guard let settingsWindow, let window else { return false }
        return window == settingsWindow || window.sheetParent == settingsWindow
    }

    // MARK: - Deterministic capture

    private func captureIfRequested() {
        guard !snapshotCaptured, let path = ProcessInfo.processInfo.environment["AIUSAGEMETER_SNAPSHOT_PATH"] else { return }
        if ProcessInfo.processInfo.environment["AIUSAGEMETER_SNAPSHOT_TARGET"] == "settings" {
            // The frame view, not the content view, which runs under the title bar on current macOS.
            guard let view = settingsWindow?.contentView?.superview ?? settingsWindow?.contentView else { return }
            snapshotCaptured = true
            capture(view: view, path: path, attempt: 0, best: nil, trim: false)
            return
        }
        guard !DemoExpansion.requested().isRequested || model.expandedProvider != nil,
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
            if ProcessInfo.processInfo.environment["AIUSAGEMETER_EXIT_AFTER_SNAPSHOT"] == "1" { NSApp.terminate(nil) }
        }
    }
}

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

/// The menu bar gauge is one ring for several subscriptions, so the wording has to say what it averaged.
enum MenuBarText {
    /// Nil for a single reading: the row underneath already says the same thing.
    static func heading(_ summary: UsageSummary) -> String? {
        guard let average = summary.average, summary.readingCount > 1 else { return nil }
        var text = "Average \(percent(average)) across \(summary.readingCount) subscriptions"
        if summary.exhaustedCount > 0 { text += " · \(summary.exhaustedCount) at limit" }
        return text
    }

    static func tooltip(_ summary: UsageSummary, snapshots: [ProviderSnapshot]) -> String {
        guard !snapshots.isEmpty else { return "AIUsageMeter" }
        var lines: [String] = []
        if let heading = heading(summary) { lines.append(heading) }
        if let peak = summary.peak, summary.readingCount > 1 { lines.append("Highest: \(peak.name) \(percent(peak.percent))") }
        lines += snapshots.map { "\($0.name): \($0.status.isReady ? percent($0.primaryPercent) : $0.status.shortLabel)" }
        return lines.joined(separator: "\n")
    }

    private static func percent(_ value: Double) -> String { "\(Int(value.rounded()))%" }
}

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
