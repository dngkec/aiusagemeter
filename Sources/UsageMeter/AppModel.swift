import AppKit
import Combine
import UsageMeterCore
import ServiceManagement
import SwiftUI

enum SettingsSelection: Hashable {
    case provider(ProviderID)
    case general
    case about
}

/// Which of the two support hearts the pointer is on. There can be one in the
/// rail and one on an open card at the same time, so they are told apart.
enum SupportSurface: Hashable {
    case rail
    case card
}

@MainActor
final class AppModel: ObservableObject {
    @Published var preferences = AppPreferences() {
        didSet {
            Metrics.scale = CGFloat(preferences.overlaySize.scale)
            recomputeVisible()
        }
    }
    @Published var snapshots: [ProviderSnapshot] = [] {
        didSet { recomputeVisible() }
    }
    /// What the rail draws, in preferences order. Stored rather than computed
    /// because every render reads it several times.
    @Published private(set) var visibleSnapshots: [ProviderSnapshot] = []
    @Published var expandedProvider: ProviderID?
    @Published var pinnedProvider: ProviderID?
    @Published var refreshing: Set<ProviderID> = []
    @Published var idleMini = false
    @Published var settingsMessage: String?
    @Published var liveSecret = ""
    @Published var customSecret = ""
    @Published var settingsSelection: SettingsSelection = .provider(.claude)
    @Published var settingsQuery = ""
    /// Hover for the support hearts. It lives here rather than in the views
    /// because this toolchain ships no SwiftUI macro plugin, so `@State` is
    /// unavailable to the overlay.
    @Published var hoveredSupport: SupportSurface?

    let store: PreferencesStore
    let secrets: SecretStore
    let coordinator: RefreshCoordinator
    /// Fires only when the window's frame or the menu-bar contents can have
    /// changed. Hover is deliberately not one of those: the panel is sized for
    /// an open card from the start, so opening one moves no window.
    var onPresentationChange: (() -> Void)?
    var onOpenSettings: (() -> Void)?

    private var refreshLoop: Task<Void, Never>?
    private var hoverTask: Task<Void, Never>?
    private var idleTask: Task<Void, Never>?
    private var saveTask: Task<Void, Never>?
    private var pointer = HoverTracker()

    init(store: PreferencesStore = PreferencesStore(), secrets: SecretStore = KeychainSecretStore(), http: HTTPClient = BoundedHTTPClient(), files: LocalFiles = DiskLocalFiles(), external: ExternalCredentials = SystemExternalCredentials()) {
        self.store = store
        self.secrets = secrets
        self.coordinator = RefreshCoordinator(context: ProviderContext(http: http, files: files, secrets: secrets, external: external))
    }

    func start() {
        Task {
            var loaded = await store.load()
            if ProcessInfo.processInfo.environment["USAGEMETER_DEMO"] == "1" { loaded.demoData = true }
            if let list = ProcessInfo.processInfo.environment["USAGEMETER_DEMO_PROVIDERS"] {
                let wanted = Set(list.split(separator: ",").compactMap { ProviderID(rawValue: $0.trimmingCharacters(in: .whitespaces)) })
                if !wanted.isEmpty {
                    loaded.providers = loaded.providers.map {
                        var provider = $0
                        provider.enabled = wanted.contains(provider.id)
                        provider.showInNotch = true
                        return provider
                    }
                }
            }
            preferences = loaded
            await refresh()
            if ProcessInfo.processInfo.environment["USAGEMETER_DEMO_EXPANDED"] == "1", let first = visibleSnapshots.first {
                expandedProvider = first.id
                pinnedProvider = first.id
            }
            beginRefreshLoop()
            scheduleIdle()
            onPresentationChange?()
        }
    }

    // MARK: - Derived state

    private func recomputeVisible() {
        let next = ProviderOrdering.arrange(snapshots, by: preferences.providers)
        guard next != visibleSnapshots else { return }
        visibleSnapshots = next
    }

    var selectedSnapshot: ProviderSnapshot? {
        guard let id = expandedProvider else { return nil }
        return visibleSnapshots.first { $0.id == id }
    }

    /// Highest reading on show, for the menu-bar gauge.
    var headline: ProviderSnapshot? {
        visibleSnapshots.filter { $0.status.isReady }.max { $0.primaryPercent < $1.primaryPercent }
            ?? visibleSnapshots.first
    }

    func isRefreshing(_ id: ProviderID) -> Bool { refreshing.contains(id) }

    /// The panel is sized once per revealed session so that no window resize
    /// ever runs underneath a SwiftUI animation.
    var panelHeight: CGFloat {
        let count = max(1, visibleSnapshots.count)
        let spacing = Metrics.itemSpacing(for: count)
        let rail = Metrics.railHeight(count: count, spacing: spacing)
        // The first gauge sits closest to an edge of the rail, so it sets the reach.
        let reach = max(0, rail / 2 - Metrics.railPadding - Metrics.gauge / 2)
        let card = visibleSnapshots.map(CardMetrics.height(for:)).max() ?? CardMetrics.height(for: ProviderSnapshot(id: .claude))
        return max(rail, 2 * (reach + card / 2)) + 24
    }

    var railHeight: CGFloat {
        let count = max(1, visibleSnapshots.count)
        return Metrics.railHeight(count: count, spacing: Metrics.itemSpacing(for: count))
    }

    // MARK: - Refreshing

    func refresh() async {
        let enabled = preferences.providers.filter(\.enabled).map(\.id)
        refreshing = Set(enabled)
        if snapshots.isEmpty {
            snapshots = enabled.map { ProviderSnapshot(id: $0, status: .loading, message: "Reading usage…") }
            onPresentationChange?()
        }
        let value = await coordinator.refresh(preferences: preferences)
        guard !Task.isCancelled else { refreshing = []; return }
        snapshots = value
        refreshing = []
        onPresentationChange?()
    }

    private func beginRefreshLoop() {
        refreshLoop?.cancel()
        let interval = preferences.refreshInterval
        refreshLoop = Task { [weak self] in
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(interval))
                guard !Task.isCancelled else { return }
                await self?.refresh()
            }
        }
    }

    // MARK: - Preferences

    /// Settings apply as they are changed — the rail reads `preferences`
    /// directly, so a reorder lands on it immediately — and only the write to
    /// disk is debounced, so a slider drag does not hammer the file.
    func scheduleSave(refetch: Bool = false) {
        onPresentationChange?()
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(350))
            guard !Task.isCancelled else { return }
            await self?.savePreferences(refetch: refetch)
        }
    }

    func savePreferences(refetch: Bool = true) async {
        do {
            try await store.save(preferences)
            settingsMessage = nil
            beginRefreshLoop()
            applyLaunchAtLogin()
            onPresentationChange?()
            if refetch { await refresh() }
        } catch {
            settingsMessage = "Could not save: \(error.localizedDescription)"
        }
    }

    func saveSecrets(for provider: ProviderID? = nil) {
        do {
            if let provider, let account = LiveCredential.account(for: provider), !liveSecret.isEmpty {
                try secrets.write(liveSecret, account: account)
                liveSecret = ""
            }
            if let provider, !customSecret.isEmpty { try secrets.write(customSecret, account: "custom.\(provider.rawValue)"); customSecret = "" }
            settingsMessage = "Saved in Keychain"
        } catch {
            settingsMessage = "Keychain error: \(error.localizedDescription)"
        }
    }

    func moveProvider(_ id: ProviderID, delta: Int) {
        ProviderOrdering.move(id, by: delta, in: &preferences.providers)
        scheduleSave()
    }

    func toggleOverlay() {
        preferences.overlayVisible.toggle()
        scheduleSave(refetch: false)
    }

    private func applyLaunchAtLogin() {
        do {
            if preferences.launchAtLogin { try SMAppService.mainApp.register() }
            else if SMAppService.mainApp.status == .enabled { try SMAppService.mainApp.unregister() }
        } catch {
            settingsMessage = "Launch at login unavailable for this build: \(error.localizedDescription)"
        }
    }

    // MARK: - Pointer

    func hover(_ id: ProviderID, inside: Bool) {
        pointer.gauge(id, inside: inside)
        evaluatePointer()
    }

    /// Leaving the rail outright clears anything a missed exit left behind.
    func railHover(_ inside: Bool) {
        pointer.rail(inside)
        evaluatePointer()
    }

    /// Moving onto the card must not dismiss the card.
    func hoverCard(_ inside: Bool) {
        pointer.card(inside)
        evaluatePointer()
    }

    /// The last word on where the pointer is, from the window server rather than
    /// from SwiftUI. A passive panel never becomes key, so a hover exit can go
    /// missing; without this a card could be stranded on screen for good.
    func pointerLeftOverlay() {
        guard pointer.holdsOpen else { return }
        pointer.reset()
        evaluatePointer()
    }

    private func evaluatePointer() {
        hoverTask?.cancel()
        switch pointer.decision {
        case .open(let id):
            idleTask?.cancel()
            wakeFromMini()
            guard expandedProvider != id else { return }
            hoverTask = Task { [weak self] in
                try? await Task.sleep(for: .milliseconds(Motion.openDelay))
                guard !Task.isCancelled, let self, self.pointer.target == id else { return }
                self.expandedProvider = id
            }
        case .keep:
            idleTask?.cancel()
        case .dismiss:
            scheduleDismiss()
        }
    }

    private func scheduleDismiss() {
        guard pinnedProvider == nil, expandedProvider != nil else { scheduleIdle(); return }
        hoverTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(Motion.dismissDelay))
            guard !Task.isCancelled, let self, !self.pointer.holdsOpen, self.pinnedProvider == nil else { return }
            self.expandedProvider = nil
            self.scheduleIdle()
        }
    }

    func togglePin(_ id: ProviderID) {
        wakeFromMini()
        if pinnedProvider == id {
            pinnedProvider = nil
            expandedProvider = nil
            scheduleIdle()
        } else {
            pinnedProvider = id
            expandedProvider = id
        }
    }

    func collapse() {
        guard pinnedProvider != nil || expandedProvider != nil else { return }
        pinnedProvider = nil
        expandedProvider = nil
        pointer.reset()
        scheduleIdle()
    }

    func revealFromMini() {
        idleTask?.cancel()
        guard idleMini else { return }
        idleMini = false
        onPresentationChange?()
        scheduleIdle()
    }

    /// The resting tab and the rail are different window sizes, so coming back
    /// from one is the one hover transition the window has to hear about.
    private func wakeFromMini() {
        guard idleMini else { return }
        idleMini = false
        onPresentationChange?()
    }

    func scheduleIdle() {
        idleTask?.cancel()
        guard pinnedProvider == nil, expandedProvider == nil, !pointer.holdsOpen else { return }
        idleTask = Task { [weak self] in
            try? await Task.sleep(for: .seconds(8))
            guard !Task.isCancelled, let self, self.pinnedProvider == nil, self.expandedProvider == nil,
                  !self.pointer.holdsOpen else { return }
            self.idleMini = true
            self.onPresentationChange?()
        }
    }

    func openDashboard(for snapshot: ProviderSnapshot) {
        guard let url = snapshot.dashboardURL else { return }
        NSWorkspace.shared.open(url)
    }

    // MARK: - Support

    /// The outward links the app offers, all through one door: a support
    /// surface can name a link, but it cannot invent one.
    func openSupport() { open(SupportLinks.sponsor) }
    func openRepository() { open(SupportLinks.repository) }
    func openIssues() { open(SupportLinks.issues) }
    func openDesigner() { open(SupportLinks.designer) }

    func hoverSupport(_ surface: SupportSurface, inside: Bool) {
        if inside { hoveredSupport = surface }
        else if hoveredSupport == surface { hoveredSupport = nil }
    }

    private func open(_ url: URL) {
        guard SupportLinks.isSupported(url) else { return }
        NSWorkspace.shared.open(url)
    }

    /// Marketing version and build, read from the bundle so the About pane and
    /// a packaged release can never disagree.
    var versionSummary: String {
        let info = Bundle.main.infoDictionary
        let version = info?["CFBundleShortVersionString"] as? String ?? "1.0.0"
        let build = info?["CFBundleVersion"] as? String ?? "1"
        return build == version ? "Version \(version)" : "Version \(version) (\(build))"
    }
}
