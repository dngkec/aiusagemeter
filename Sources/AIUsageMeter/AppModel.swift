import AppKit
import Combine
import AIUsageMeterCore
import ServiceManagement
import SwiftUI

enum SettingsSelection: Hashable {
    case provider(ProviderID)
    case general
    case about

    var title: String {
        switch self {
        case .provider(let id): return id.displayName
        case .general: return "General"
        case .about: return "About & Support"
        }
    }
}

enum SettingsNotice: Equatable {
    case success(String)
    case failure(String)

    var text: String {
        switch self {
        case .success(let text), .failure(let text): return text
        }
    }

    var isFailure: Bool {
        if case .failure = self { return true }
        return false
    }

    var symbol: String { isFailure ? "exclamationmark.triangle.fill" : "checkmark.circle.fill" }
}

struct ScreenOption: Identifiable, Hashable {
    let id: String
    let name: String
}

enum SupportSurface: Hashable {
    case rail
    case card
}

/// Everything a reading depends on. Where the gauges sit, which of them the notch shows, and how the
/// overlay is drawn are not in here: those change what is on screen, never what was read.
struct FetchInputs: Equatable {
    let demoData: Bool
    let providers: [ProviderConfiguration]

    init(_ preferences: AppPreferences) {
        demoData = preferences.demoData
        providers = preferences.providers
            .filter(\.enabled)
            .map { provider in
                var fetchable = provider
                fetchable.showInNotch = true
                return fetchable
            }
            .sorted { $0.id.rawValue < $1.id.rawValue }
    }
}

/// Which card a capture wants open. `1` takes the first gauge and a provider identifier takes that
/// one — which is how a capture checks that a card lands beside its own gauge far down a long rail.
/// Anything else, `0` included, leaves every card closed.
enum DemoExpansion {
    case none
    case first
    case provider(ProviderID)

    static func requested(_ environment: [String: String] = ProcessInfo.processInfo.environment) -> DemoExpansion {
        guard let raw = environment["AIUSAGEMETER_DEMO_EXPANDED"] else { return .none }
        if raw == "1" { return .first }
        if let id = ProviderID(rawValue: raw) { return .provider(id) }
        return .none
    }

    var isRequested: Bool {
        if case .none = self { return false }
        return true
    }
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
    /// What the rail draws, in preferences order — and what the menu and the menu-bar gauge read.
    @Published private(set) var visibleSnapshots: [ProviderSnapshot] = []
    /// Which providers the rail draws, kept apart from their readings so animating the rail's shape
    /// does not mean rebuilding an identifier array on every body pass.
    @Published private(set) var railKey: [ProviderID] = []
    /// Measured gauge centres, in panel coordinates. The rail scrolls once the list outgrows the
    /// screen, so a card's position cannot be derived from the gauge's index alone.
    @Published private(set) var gaugeCentres: [ProviderID: CGFloat] = [:]
    @Published var expandedProvider: ProviderID?
    @Published var pinnedProvider: ProviderID?
    @Published var refreshing: Set<ProviderID> = []
    @Published var idleMini = false
    @Published var settingsNotice: SettingsNotice?
    @Published var liveSecret = ""
    @Published var customSecret = ""
    @Published var settingsSelection: SettingsSelection = .provider(.claude)
    @Published var settingsQuery = ""
    @Published private(set) var storedSecrets: Set<String> = []
    @Published private(set) var screens: [ScreenOption] = []
    @Published private(set) var lastRefresh: Date?
    /// Hover state lives here because this toolchain ships no SwiftUI macro plugin, so the overlay has no @State.
    @Published var hoveredSupport: SupportSurface?
    /// Mirrors `updater.state` so SwiftUI redraws; the updater itself is not observable.
    @Published private(set) var updateState = UpdateState()

    let updater = Updater()
    let store: PreferencesStore
    let secrets: SecretStore
    let coordinator: RefreshCoordinator
    var onPresentationChange: (() -> Void)?
    var onOpenSettings: (() -> Void)?

    private var refreshLoop: Task<Void, Never>?
    private var hoverTask: Task<Void, Never>?
    private var idleTask: Task<Void, Never>?
    private var saveTask: Task<Void, Never>?
    private var pointer = HoverTracker()
    /// What the readings on screen were fetched for, so a settings change that invalidates them is
    /// told apart from one that only moves them around.
    private var fetched: FetchInputs?
    /// A demo launch rewrites the provider list in memory to whatever the capture asked for. Saving
    /// that would replace the real one on disk, so a session holding borrowed preferences never writes.
    private var borrowedPreferences = false

    init(store: PreferencesStore = PreferencesStore(), secrets: SecretStore = KeychainSecretStore(), http: HTTPClient = BoundedHTTPClient(), files: LocalFiles = DiskLocalFiles(), external: ExternalCredentials = SystemExternalCredentials()) {
        self.store = store
        self.secrets = secrets
        self.coordinator = RefreshCoordinator(context: ProviderContext(http: http, files: files, secrets: secrets, external: external))
    }

    func start() {
        reloadScreens()
        updater.onChange = { [weak self] in
            guard let self else { return }
            updateState = updater.state
            // The menu bar carries the update entry too, so it is rebuilt alongside the pane.
            onPresentationChange?()
        }
        updater.start()
        Task {
            var loaded = await store.load()
            if ProcessInfo.processInfo.environment["AIUSAGEMETER_DEMO"] == "1" {
                loaded.demoData = true
                borrowedPreferences = true
            }
            if let list = ProcessInfo.processInfo.environment["AIUSAGEMETER_DEMO_PROVIDERS"] {
                let wanted = Set(list.split(separator: ",").compactMap { ProviderID(rawValue: $0.trimmingCharacters(in: .whitespaces)) })
                if !wanted.isEmpty {
                    loaded.providers = loaded.providers.map {
                        var provider = $0
                        provider.enabled = wanted.contains(provider.id)
                        provider.showInNotch = true
                        return provider
                    }
                    borrowedPreferences = true
                }
            }
            preferences = loaded
            await refresh()
            switch DemoExpansion.requested() {
            case .none:
                break
            case .first:
                expand(visibleSnapshots.first?.id)
            case .provider(let id):
                expand(visibleSnapshots.first { $0.id == id }?.id ?? visibleSnapshots.first?.id)
            }
            beginRefreshLoop()
            scheduleIdle()
            onPresentationChange?()
        }
    }

    private func expand(_ id: ProviderID?) {
        guard let id else { return }
        expandedProvider = id
        pinnedProvider = id
    }

    // MARK: - Derived state

    private func recomputeVisible() {
        let next = ProviderOrdering.arrange(snapshots, by: preferences.providers)
        guard next != visibleSnapshots else { return }
        visibleSnapshots = next
        let key = next.map(\.id)
        if key != railKey {
            railKey = key
            gaugeCentres = gaugeCentres.filter { key.contains($0.key) }
        }
    }

    /// Reported by the rail after layout, so the card follows its gauge even when the rail is scrolled.
    func recordGaugeCentres(_ centres: [ProviderID: CGFloat]) {
        guard centres.count != gaugeCentres.count
            || centres.contains(where: { abs($0.value - (gaugeCentres[$0.key] ?? .infinity)) > 0.5 }) else { return }
        gaugeCentres = centres
    }

    var selectedSnapshot: ProviderSnapshot? {
        guard let id = expandedProvider else { return nil }
        return visibleSnapshots.first { $0.id == id }
    }

    /// What the menu bar gauge reads: the average across the subscriptions the notch shows.
    var summary: UsageSummary { UsageSummary(visibleSnapshots) }

    func isRefreshing(_ id: ProviderID) -> Bool { refreshing.contains(id) }

    var isRefreshingAny: Bool { !refreshing.isEmpty }

    /// Sized once per revealed session, so no window resize runs underneath an animation.
    var panelHeight: CGFloat {
        let count = max(1, visibleSnapshots.count)
        let spacing = Metrics.itemSpacing(for: count)
        let rail = Metrics.railHeight(count: count, spacing: spacing)
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
        fetched = FetchInputs(preferences)
        // A provider enabled in Settings has no reading yet. Standing one in for it now is what puts
        // its gauge in the rail and its row in the menu straight away, rather than at the end of the fetch.
        let known = Set(snapshots.map(\.id))
        let missing = enabled.filter { !known.contains($0) }
        if !missing.isEmpty {
            snapshots += missing.map { ProviderSnapshot(id: $0, status: .loading, message: "Reading usage…") }
            onPresentationChange?()
        }
        let value = await coordinator.refresh(preferences: preferences)
        guard !Task.isCancelled else { refreshing = []; return }
        snapshots = value
        refreshing = []
        lastRefresh = Date()
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

    /// Settings apply as they are made: the rail, the menu, and the menu-bar gauge all read
    /// `preferences` directly, so this reaches them before the debounced write to disk.
    ///
    /// `refetch` defaults to whatever the change calls for — a provider turned on, or any setting a
    /// reading depends on, is fetched again, so nothing waits on the refresh timer to appear.
    func scheduleSave(refetch: Bool? = nil) {
        onPresentationChange?()
        let wanted = refetch ?? (FetchInputs(preferences) != fetched)
        saveTask?.cancel()
        saveTask = Task { [weak self] in
            try? await Task.sleep(for: .milliseconds(350))
            guard !Task.isCancelled else { return }
            await self?.savePreferences(refetch: wanted)
        }
    }

    func savePreferences(refetch: Bool = true) async {
        do {
            if !borrowedPreferences { try await store.save(preferences) }
            if settingsNotice?.isFailure == true { settingsNotice = nil }
            beginRefreshLoop()
            applyLaunchAtLogin()
            onPresentationChange?()
            if refetch { await refresh() }
        } catch {
            settingsNotice = .failure("Could not save: \(error.localizedDescription)")
        }
    }

    private func tidied(_ secret: String) -> String {
        secret.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    func saveSecrets(for provider: ProviderID? = nil) {
        guard let provider else { return }
        do {
            var saved = false
            if let account = LiveCredential.account(for: provider), !tidied(liveSecret).isEmpty {
                try secrets.write(tidied(liveSecret), account: account)
                liveSecret = ""
                saved = true
            }
            if !tidied(customSecret).isEmpty {
                try secrets.write(tidied(customSecret), account: Self.customAccount(for: provider))
                customSecret = ""
                saved = true
            }
            guard saved else { return }
            loadSecretState(for: provider)
            settingsNotice = .success("Saved in Keychain")
            Task { await refresh() }
        } catch {
            settingsNotice = .failure("Keychain error: \(error.localizedDescription)")
        }
    }

    static func customAccount(for provider: ProviderID) -> String { "custom.\(provider.rawValue)" }

    func removeSecret(account: String, provider: ProviderID) {
        do {
            try secrets.write(nil, account: account)
            loadSecretState(for: provider)
            settingsNotice = .success("Removed from Keychain")
            Task { await refresh() }
        } catch {
            settingsNotice = .failure("Keychain error: \(error.localizedDescription)")
        }
    }

    func hasStoredSecret(_ account: String?) -> Bool {
        guard let account else { return false }
        return storedSecrets.contains(account)
    }

    func loadSecretState(for provider: ProviderID) {
        var found: Set<String> = []
        for account in [LiveCredential.account(for: provider), Self.customAccount(for: provider)].compactMap({ $0 }) {
            if let stored = try? secrets.read(account), !stored.isEmpty { found.insert(account) }
        }
        storedSecrets = found
    }

    /// A typed-but-unsaved secret must not follow the pane change onto another provider.
    func settingsPaneChanged() {
        liveSecret = ""
        customSecret = ""
        settingsNotice = nil
        if case .provider(let id) = settingsSelection { loadSecretState(for: id) } else { storedSecrets = [] }
    }

    func reloadScreens() {
        screens = NSScreen.screens.map { ScreenOption(id: $0.quotaIdentifier, name: $0.localizedName) }
    }

    func moveProvider(_ id: ProviderID, delta: Int) {
        ProviderOrdering.move(id, by: delta, in: &preferences.providers)
        scheduleSave()
    }

    func toggleProvider(_ id: ProviderID) {
        guard let index = preferences.providers.firstIndex(where: { $0.id == id }) else { return }
        preferences.providers[index].enabled.toggle()
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
            settingsNotice = .failure("Launch at login unavailable for this build: \(error.localizedDescription)")
        }
    }

    // MARK: - Pointer

    func hover(_ id: ProviderID, inside: Bool) {
        pointer.gauge(id, inside: inside)
        evaluatePointer()
    }

    func railHover(_ inside: Bool) {
        pointer.rail(inside)
        evaluatePointer()
    }

    func hoverCard(_ inside: Bool) {
        pointer.card(inside)
        evaluatePointer()
    }

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

    func checkForUpdates() {
        Task { await updater.check() }
    }

    func installUpdate() { updater.install() }

    func openReleaseNotes() {
        guard let page = updateState.package?.page else { return }
        // Not a SupportLinks entry, so it is checked the way a provider endpoint is.
        guard (try? EndpointValidator.validate(page.absoluteString)) != nil else { return }
        NSWorkspace.shared.open(page)
    }

    var versionSummary: String {
        let info = Bundle.main.infoDictionary
        let version = info?["CFBundleShortVersionString"] as? String ?? "1.0.0"
        let build = info?["CFBundleVersion"] as? String ?? "1"
        return build == version ? "Version \(version)" : "Version \(version) (\(build))"
    }
}
