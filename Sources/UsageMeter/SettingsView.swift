import AppKit
import UsageMeterCore
import SwiftUI

struct SettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        NavigationSplitView {
            sidebar
        } detail: {
            detail
        }
        .frame(minWidth: 820, minHeight: 560)
    }

    // MARK: - Sidebar

    private var sidebar: some View {
        List(selection: $model.settingsSelection) {
            Section {
                Label("General", systemImage: "gearshape").tag(SettingsSelection.general)
                Label("About & Support", systemImage: "heart").tag(SettingsSelection.about)
            }
            Section {
                ForEach(filteredProviders) { provider in
                    ProviderRowLabel(provider: provider, snapshot: model.snapshots.first { $0.id == provider.id })
                        .tag(SettingsSelection.provider(provider.id))
                        .contextMenu {
                            Button("Move Up") { model.moveProvider(provider.id, delta: -1) }
                            Button("Move Down") { model.moveProvider(provider.id, delta: 1) }
                        }
                }
                // Offsets only line up with the stored order when nothing is
                // filtered out, so dragging is offered on the unfiltered list.
                .onMove(perform: model.settingsQuery.isEmpty ? { source, destination in
                    model.preferences.providers.move(fromOffsets: source, toOffset: destination)
                    model.scheduleSave()
                } : nil)
            } header: {
                Text("Providers")
            } footer: {
                Text(model.settingsQuery.isEmpty
                     ? "Drag to reorder the rail, or use the arrows on a provider."
                     : "Clear the search to reorder.")
                .font(.caption)
                .foregroundStyle(.secondary)
            }
        }
        .searchable(text: $model.settingsQuery, placement: .sidebar, prompt: "Search providers")
        .navigationSplitViewColumnWidth(min: 224, ideal: 244, max: 300)
    }

    private var filteredProviders: [ProviderConfiguration] {
        guard !model.settingsQuery.isEmpty else { return model.preferences.providers }
        return model.preferences.providers.filter { $0.id.displayName.localizedCaseInsensitiveContains(model.settingsQuery) }
    }

    // MARK: - Detail

    @ViewBuilder private var detail: some View {
        switch model.settingsSelection {
        case .provider(let id):
            if let index = model.preferences.providers.firstIndex(where: { $0.id == id }) {
                ProviderSettings(model: model, index: index)
            } else {
                ContentUnavailableView("Select a provider", systemImage: "gauge.with.dots.needle.33percent")
            }
        case .general:
            GeneralSettings(model: model)
        case .about:
            AboutSettings(model: model)
        }
    }
}

struct ProviderRowLabel: View {
    let provider: ProviderConfiguration
    let snapshot: ProviderSnapshot?

    var body: some View {
        HStack(spacing: 9) {
            GlyphView(glyph: provider.id.glyph, provider: provider.id, size: 16, color: .primary)
                .frame(width: 18)
            Text(provider.id.displayName).lineLimit(1)
            Spacer(minLength: 6)
            if provider.enabled {
                Text(value)
                    .font(.caption.monospacedDigit())
                    .foregroundStyle(.secondary)
            }
        }
        .padding(.vertical, 1)
    }

    private var value: String {
        guard let snapshot else { return "—" }
        return snapshot.status.isReady ? "\(Int(snapshot.primaryPercent.rounded()))%" : "!"
    }
}

// MARK: - Provider detail

struct ProviderSettings: View {
    @ObservedObject var model: AppModel
    let index: Int

    private var provider: ProviderConfiguration { model.preferences.providers[index] }
    private var snapshot: ProviderSnapshot? { model.snapshots.first { $0.id == provider.id } }
    private var reading: Double { snapshot?.status.isReady == true ? snapshot?.primaryPercent ?? 0 : 0 }

    var body: some View {
        Form {
            Section {
                HStack(spacing: 16) {
                    UsageRing(
                        percent: reading,
                        tint: Color.usage(percent: reading, status: snapshot?.status ?? .setupNeeded),

                        glyph: provider.id.glyph,
                        provider: provider.id,
                        refreshing: false,
                        reduced: true
                    )
                    .frame(width: 54, height: 54)
                    .padding(8)
                    .background(Circle().fill(Color.black.opacity(0.92)))

                    VStack(alignment: .leading, spacing: 4) {
                        Text(provider.id.displayName).font(.title2.bold())
                        Text(SupportCopy.text(for: provider.id))
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    Spacer(minLength: 0)
                }
                .padding(.vertical, 6)
            }

            Section {
                Toggle("Enable this provider", isOn: bind(\.enabled))
                Toggle("Show in the side notch", isOn: bind(\.showInNotch))
                    .disabled(!provider.enabled)
                LabeledContent("Position in the rail") {
                    HStack(spacing: 8) {
                        Text("\(index + 1) of \(model.preferences.providers.count)")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                        Button { model.moveProvider(provider.id, delta: -1) } label: { Image(systemName: "chevron.up") }
                            .disabled(index == 0)
                            .help("Move up")
                        Button { model.moveProvider(provider.id, delta: 1) } label: { Image(systemName: "chevron.down") }
                            .disabled(index == model.preferences.providers.count - 1)
                            .help("Move down")
                    }
                }
            } footer: {

                if let snapshot, snapshot.status != .ready, provider.enabled {
                    Label(snapshot.message ?? snapshot.status.shortLabel, systemImage: snapshot.status.symbol)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                }
            }

            Section("Data source") {
                Picker("Read usage from", selection: bind(\.mode)) {
                    Text("Built-in").tag(ProviderMode.live)
                    Text("Custom JSON").tag(ProviderMode.customJSON)
                    Text("Manual budget").tag(ProviderMode.manual)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            }

            switch provider.mode {
            case .live: live
            case .manual: manual
            case .customJSON: custom
            }
        }
        .formStyle(.grouped)
        .scrollContentBackground(.visible)
        .navigationTitle(provider.id.displayName)
    }

    @ViewBuilder private var live: some View {
        Section {
            if LiveCredential.account(for: provider.id) != nil {
                if LiveCredential.usesMonthlyBudget(provider.id) {
                    TextField("Monthly budget (USD)", value: bind(\.monthlyBudget), format: .number)
                }
                if let workspacePrompt = LiveCredential.workspacePrompt(for: provider.id) {
                    TextField(workspacePrompt, text: bind(\.workspaceID))
                }
                if LiveCredential.usesRegion(provider.id) {
                    Picker("Region", selection: bind(\.region)) {
                        ForEach(ProviderRegion.allCases, id: \.self) { Text($0.label).tag($0) }
                    }
                }
                SecureField(LiveCredential.prompt(for: provider.id), text: $model.liveSecret)
                Button("Save key in Keychain") { model.saveSecrets(for: provider.id) }
                    .disabled(model.liveSecret.isEmpty)
            } else if BuiltInProviders.supported.contains(provider.id) {
                Label("UsageMeter reads an existing local sign-in and never refreshes, rotates, or rewrites it. Extra usage and credits appear on the card only when the account has them.", systemImage: "lock.shield")
                    .foregroundStyle(.secondary)
            } else {
                Label("No safe built-in usage endpoint is known for this service. Choose Custom JSON or Manual budget.", systemImage: "info.circle")
                    .foregroundStyle(.secondary)
            }
        } header: {
            Text("Built-in access")
        }
    }

    @ViewBuilder private var manual: some View {
        Section("Manual budget") {
            TextField("Used", value: bind(\.manual.used), format: .number)
            TextField("Limit", value: bind(\.manual.limit), format: .number)
            DatePicker("Resets", selection: manualDate, displayedComponents: [.date, .hourAndMinute])
        }
    }

    @ViewBuilder private var custom: some View {
        Section("Connector") {
            TextField("Display name", text: bind(\.custom.name))
            TextField("HTTPS endpoint", text: bind(\.custom.endpoint))
            Picker("Method", selection: bind(\.custom.method)) {
                ForEach(HTTPMethod.allCases, id: \.self) { Text($0.rawValue).tag($0) }
            }
            TextField("Dashboard URL", text: bind(\.custom.dashboardURL))
        }
        Section("Authentication") {
            Picker("Secret", selection: bind(\.custom.secretPlacement)) {
                Text("Bearer token").tag(SecretPlacement.bearer)
                Text("API-key header").tag(SecretPlacement.apiKeyHeader)
                Text("None").tag(SecretPlacement.none)
            }
            if provider.custom.secretPlacement == .apiKeyHeader {
                TextField("Header name", text: bind(\.custom.apiKeyHeader))
            }
            if provider.custom.secretPlacement != .none {
                SecureField("Secret", text: $model.customSecret)
                Button("Save Secret in Keychain") { model.saveSecrets(for: provider.id) }
                    .disabled(model.customSecret.isEmpty)
            }
        }
        Section {
            TextField("Percent", text: bind(\.custom.percentPath))
            TextField("Used", text: bind(\.custom.usedPath))
            TextField("Limit", text: bind(\.custom.limitPath))
            TextField("Resets", text: bind(\.custom.resetPath))
        } header: {
            Text("JSON paths")
        } footer: {
            Text("Dot-separated, with array indexes — for example `data.0.usage.percent`.")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private func bind<T>(_ path: WritableKeyPath<ProviderConfiguration, T>) -> Binding<T> {
        Binding(
            get: { model.preferences.providers[index][keyPath: path] },
            set: { model.preferences.providers[index][keyPath: path] = $0; model.scheduleSave() }
        )
    }

    private var manualDate: Binding<Date> {
        Binding(
            get: { model.preferences.providers[index].manual.resetDate ?? Date().addingTimeInterval(86_400) },
            set: { model.preferences.providers[index].manual.resetDate = $0; model.scheduleSave() }
        )
    }
}

// MARK: - General detail

struct GeneralSettings: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Form {
            Section("Appearance") {
                Picker("Overlay size", selection: bind(\.overlaySize)) {
                    ForEach(OverlaySize.allCases, id: \.self) { size in
                        Text(size.label).tag(size)
                    }
                }
                .pickerStyle(.segmented)
            }

            Section("Placement") {
                Picker("Display"
, selection: bind(\.screenIdentifier)) {
                    Text("Display with the pointer").tag(String?.none)
                    ForEach(NSScreen.screens, id: \.quotaIdentifier) { screen in
                        Text(screen.localizedName).tag(Optional(screen.quotaIdentifier))
                    }
                }
                Picker("Vertical position", selection: bind(\.verticalPosition)) {
                    Text("Top").tag(VerticalPosition.top)
                    Text("Centre").tag(VerticalPosition.center)
                    Text("Bottom").tag(VerticalPosition.bottom)
                }
                .pickerStyle(.segmented)
                LabeledContent("Fine adjustment") {
                    HStack(spacing: 10) {
                        Slider(value: bind(\.verticalOffset), in: -300...300, step: 1)
                        Text("\(Int(model.preferences.verticalOffset)) pt")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                            .frame(width: 54, alignment: .trailing)
                    }
                }
            }

            Section("Updating") {
                Picker("Refresh every", selection: bind(\.refreshInterval)) {
                    Text("30 seconds").tag(30.0)
                    Text("1 minute").tag(60.0)
                    Text("5 minutes").tag(300.0)
                    Text("15 minutes").tag(900.0)
                    Text("1 hour").tag(3600.0)
                }
                Button("Refresh now") { Task { await model.refresh() } }
            }

            Section {
                Toggle("Show the side notch", isOn: bind(\.overlayVisible))
                Toggle("Launch at login", isOn: bind(\.launchAtLogin))
                Toggle("Use demo data", isOn: bind(\.demoData))
            } footer: {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Demo data is deterministic, clearly labelled, and never replaces a failed live reading.")
                    Text("Provider access is read-only. Secrets live in the Keychain, never in preferences or logs.")
                    if let message = model.settingsMessage {
                        Text(message).foregroundStyle(.orange)
                    }
                }
                .font(.callout)
                .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .navigationTitle("General")
    }

    private func bind<T>(_ path: WritableKeyPath<AppPreferences, T>) -> Binding<T> {
        Binding(
            get: { model.preferences[keyPath: path] },
            set: { model.preferences[keyPath: path] = $0; model.scheduleSave() }
        )
    }
}

// MARK: - About and support

/// Where the project asks for support and says where its design came from.
/// Both are one click and both leave the app: nothing here reads a provider,
/// touches the Keychain, or sends anything anywhere.
struct AboutSettings: View {
    @ObservedObject var model: AppModel

    private var icon: NSImage? { NSImage(named: NSImage.applicationIconName) }

    var body: some View {
        Form {
            Section {
                HStack(alignment: .top, spacing: 16) {
                    if let icon {
                        Image(nsImage: icon)
                            .resizable()
                            .frame(width: 64, height: 64)
                    }
                    VStack(alignment: .leading, spacing: 4) {
                        Text("UsageMeter").font(.title2.bold())
                        Text(model.versionSummary)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                        Text("A native macOS usage monitor for AI coding services. Free, open source, and MIT licensed.")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                            .fixedSize(horizontal: false, vertical: true)
                    }
                    Spacer(minLength: 0)
                }
                .padding(.vertical, 6)
            }

            Section {
                VStack(alignment: .leading, spacing: 12) {
                    Text(SupportLinks.sponsorBlurb)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                    HStack(spacing: 10) {
                        Button(action: model.openSupport) {
                            Label(SupportLinks.sponsorLabel, systemImage: "cup.and.saucer.fill")
                        }
                        .buttonStyle(SponsorButtonStyle())
                        Button(SupportLinks.repositoryLabel, systemImage: "star") { model.openRepository() }
                        Button("Report an issue", systemImage: "ladybug") { model.openIssues() }
                    }
                }
                .padding(.vertical, 4)
            } header: {
                Text("Support")
            }

            Section {
                LabeledContent("Design") {
                    Button(SupportLinks.designerHandle) { model.openDesigner() }
                        .buttonStyle(.link)
                }
                LabeledContent("Source", value: "github.com/dngkec/usagemeter")
                LabeledContent("Licence", value: "MIT")
            } header: {
                Text("Credits")
            } footer: {
                Text("The notch, the gauges, and the detail card follow a design by \(SupportLinks.designerHandle). Provider marks are drawn in code, so no vendor artwork is bundled and no affiliation is implied.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .navigationTitle("About & Support")
    }
}

/// Buy Me a Coffee's own yellow, so the button is recognisable as what it is
/// before the label is read.
struct SponsorButtonStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(.black)
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
            .background(RoundedRectangle(cornerRadius: 9, style: .continuous).fill(Palette.sponsor))
            .opacity(configuration.isPressed ? 0.80 : 1)
            .contentShape(Rectangle())
    }
}

enum BuiltInProviders {
    static let supported: Set<ProviderID> = [.claude, .codex, .grok, .cursor, .copilot, .gemini, .kimi, .jetBrainsAI]
}

enum SupportCopy {
    static func text(for id: ProviderID) -> String {
        switch id {
        case .claude: return "Reads Claude Code’s saved sign-in and the official subscription usage endpoint, read-only. Extra usage appears when the account has it enabled."
        case .anthropicCost: return "Reads organisation API cost with an Admin key stored in UsageMeter’s Keychain item."
        case .codex: return "Reads ~/.codex/auth.json and the Codex usage endpoint without altering the CLI login. Credits appear when the account reports them."
        case .grok: return "Reads ~/.grok/auth.json and both Grok CLI billing response shapes, including credits when present."
        case .cursor: return "Opens Cursor’s state database read-only and tries the known usage endpoints. On-demand spend appears when enabled."
        case .copilot: return "Finds an existing Copilot or GitHub CLI token and normalises the official quota data."
        case .gemini: return "Uses a valid Gemini CLI access token. Reopen Gemini CLI once it expires."
        case .kimi: return "Uses a valid Kimi Code token and never refreshes or modifies it."
        case .openAIAPI: return "Reads organisation API cost with an OpenAI Admin key stored in UsageMeter’s Keychain item."
        case .openRouter: return "Reads OpenRouter credits and the current key’s spend cap. Add an API key in Settings."
        case .deepSeek: return "Reads the documented DeepSeek balance endpoint. Credits remaining are shown against your monthly budget."
        case .mistral: return "Reads organisation API cost with a Mistral Admin key stored in UsageMeter’s Keychain item."
        case .xaiAPI: return "Reads the xAI developer platform’s prepaid balance with a Management key. Needs your team ID; inference keys are not accepted."
        case .moonshot: return "Reads the documented Moonshot balance endpoint. Pick the region your account was created in."
        case .zai: return "Reads the z.ai Coding Plan quota windows with an API key. Pick China mainland for a BigModel account."
        case .openCode: return "Reads the OpenCode Zen usage endpoint with an API key from your OpenCode account."
        case .warp: return "Reads Warp’s request limit with an API key created in Warp’s settings."
        case .jetBrainsAI: return "Reads the quota your JetBrains IDE already wrote to disk. No network request and no credential."
        default: return "Catalog entry with Custom JSON and Manual budget options. Extra usage and credits appear when the JSON includes them."
        }
    }
}
