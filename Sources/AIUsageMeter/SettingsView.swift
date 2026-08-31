import AppKit
import AIUsageMeterCore
import SwiftUI

struct SettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        NavigationSplitView {
            sidebar
        } detail: {
            detail.frame(minWidth: 540)
        }
        .frame(minWidth: 820, minHeight: 560)
        .onAppear { model.settingsPaneChanged() }
        .onChange(of: model.settingsSelection) { _, _ in model.settingsPaneChanged() }
    }

    // MARK: - Sidebar

    private var sidebar: some View {
        List(selection: $model.settingsSelection) {
            Section {
                Label("General", systemImage: "gearshape").tag(SettingsSelection.general)
                Label("About & Support", systemImage: "heart").tag(SettingsSelection.about)
            }
            Section("Providers") {
                ForEach(filteredProviders) { provider in
                    ProviderRowLabel(provider: provider, snapshot: model.snapshots.first { $0.id == provider.id })
                        .tag(SettingsSelection.provider(provider.id))
                        .contextMenu {
                            Button(provider.enabled ? "Disable" : "Enable") { model.toggleProvider(provider.id) }
                            Divider()
                            Button("Move Up") { model.moveProvider(provider.id, delta: -1) }
                                .disabled(!canReorder || position(of: provider) == 0)
                            Button("Move Down") { model.moveProvider(provider.id, delta: 1) }
                                .disabled(!canReorder || position(of: provider) == model.preferences.providers.count - 1)
                        }
                }
                // Offsets only line up with the stored order when nothing is filtered out.
                .onMove(perform: canReorder ? reorder : nil)

                if filteredProviders.isEmpty {
                    Text("No provider matches “\(model.settingsQuery)”.")
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .padding(.vertical, 2)
                }
            }
        }
        .searchable(text: $model.settingsQuery, placement: .sidebar, prompt: "Search providers")
        .contentMargins(.bottom, 34, for: .scrollContent)
        .overlay(alignment: .bottom) { sidebarHint }
        .navigationSplitViewColumnWidth(min: 232, ideal: 250, max: 320)
    }

    /// An overlay rather than a footer or a safe-area inset: both push the list's content height into the
    /// window's minimum size, and the split view then overflows the window.
    private var sidebarHint: some View {
        VStack(spacing: 0) {
            Divider()
            Text(canReorder ? "Drag a provider to reorder the rail." : "Clear the search to reorder.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .lineLimit(1)
                .truncationMode(.tail)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
        }
        .background(.bar)
    }

    private var canReorder: Bool { model.settingsQuery.isEmpty }

    private var filteredProviders: [ProviderConfiguration] {
        guard !model.settingsQuery.isEmpty else { return model.preferences.providers }
        return model.preferences.providers.filter { $0.id.displayName.localizedCaseInsensitiveContains(model.settingsQuery) }
    }

    private func position(of provider: ProviderConfiguration) -> Int {
        model.preferences.providers.firstIndex { $0.id == provider.id } ?? 0
    }

    private func reorder(_ source: IndexSet, _ destination: Int) {
        model.preferences.providers.move(fromOffsets: source, toOffset: destination)
        model.scheduleSave()
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
                .frame(width: 18, height: 18)
            Text(provider.id.displayName)
                .lineLimit(1)
                .truncationMode(.tail)
            Spacer(minLength: 6)
            badge
        }
        .opacity(provider.enabled ? 1 : 0.55)
        .padding(.vertical, 1)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(provider.id.displayName), \(accessibilityValue)")
    }

    @ViewBuilder private var badge: some View {
        if !provider.enabled {
            Text("Off")
                .font(.caption)
                .foregroundStyle(.secondary)
        } else if let snapshot, snapshot.status.isReady {
            Text("\(Int(snapshot.primaryPercent.rounded()))%")
                .font(.caption.monospacedDigit())
                .foregroundStyle(.secondary)
        } else if let snapshot {
            Image(systemName: snapshot.status.symbol)
                .font(.caption)
                .foregroundStyle(.secondary)
                .help(snapshot.status.shortLabel)
        } else {
            Text("—")
                .font(.caption)
                .foregroundStyle(.tertiary)
        }
    }

    private var accessibilityValue: String {
        guard provider.enabled else { return "off" }
        guard let snapshot else { return "no reading" }
        return snapshot.status.isReady ? "\(Int(snapshot.primaryPercent.rounded())) percent" : snapshot.status.shortLabel
    }
}

struct SettingsFootnote: View {
    private let key: LocalizedStringKey
    init(_ key: LocalizedStringKey) { self.key = key }

    var body: some View {
        Text(key)
            .font(.footnote)
            .foregroundStyle(.secondary)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct SettingsWarning: View {
    let text: String

    var body: some View {
        Label(text, systemImage: "exclamationmark.triangle.fill")
            .font(.footnote)
            .foregroundStyle(.orange)
            .multilineTextAlignment(.leading)
            .fixedSize(horizontal: false, vertical: true)
            .frame(maxWidth: .infinity, alignment: .leading)
    }
}

struct NoticeRow: View {
    let notice: SettingsNotice
    let dismiss: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            Image(systemName: notice.symbol)
                .foregroundStyle(notice.isFailure ? Color.orange : Color.green)
            Text(notice.text)
                .font(.callout)
                .fixedSize(horizontal: false, vertical: true)
            Spacer(minLength: 8)
            Button {
                dismiss()
            } label: {
                Image(systemName: "xmark")
            }
            .buttonStyle(.borderless)
            .foregroundStyle(.secondary)
            .help("Dismiss")
            .accessibilityLabel("Dismiss")
        }
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
            Section { header }

            if let notice = model.settingsNotice {
                Section { NoticeRow(notice: notice) { model.settingsNotice = nil } }
            }

            statusSection

            Section {
                Toggle("Enable this provider", isOn: bind(\.enabled))
                Toggle("Show in the side notch", isOn: bind(\.showInNotch))
                    .disabled(!provider.enabled)
                LabeledContent("Position in the rail") {
                    HStack(spacing: 8) {
                        Text("\(index + 1) of \(model.preferences.providers.count)")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                        Button { model.moveProvider(provider.id, delta: -1) } label: {
                            Image(systemName: "chevron.up").frame(width: 11)
                        }
                        .controlSize(.small)
                        .disabled(index == 0)
                        .help("Move up")
                        .accessibilityLabel("Move up")
                        Button { model.moveProvider(provider.id, delta: 1) } label: {
                            Image(systemName: "chevron.down").frame(width: 11)
                        }
                        .controlSize(.small)
                        .disabled(index == model.preferences.providers.count - 1)
                        .help("Move down")
                        .accessibilityLabel("Move down")
                    }
                }
            } header: {
                Text("Rail")
            } footer: {
                SettingsFootnote("A provider appears in the rail, the menu, and the menu-bar gauge only while it is enabled and shown in the notch. There is nothing to save: changes apply as you make them.")
            }

            Section {
                Picker("Read usage from", selection: bind(\.mode)) {
                    Text("Built-in").tag(ProviderMode.live)
                    Text("Custom JSON").tag(ProviderMode.customJSON)
                    Text("Manual budget").tag(ProviderMode.manual)
                }
                .pickerStyle(.segmented)
                .labelsHidden()
            } header: {
                Text("Data source")
            } footer: {
                SettingsFootnote(modeExplanation)
            }

            switch provider.mode {
            case .live: live
            case .manual: manual
            case .customJSON: custom
            }
        }
        .formStyle(.grouped)
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 16) {
            ProviderBadge(provider: provider.id, percent: reading, status: snapshot?.status ?? .setupNeeded)
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

    @ViewBuilder private var statusSection: some View {
        if provider.enabled, let snapshot, snapshot.status != .ready {
            Section {
                HStack(alignment: .firstTextBaseline, spacing: 8) {
                    Image(systemName: snapshot.status.symbol)
                        .foregroundStyle(statusTint(snapshot.status))
                    VStack(alignment: .leading, spacing: 2) {
                        Text(snapshot.status.shortLabel).font(.callout.weight(.semibold))
                        if let message = snapshot.message, message != snapshot.status.shortLabel {
                            Text(message)
                                .font(.callout)
                                .foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                    }
                    Spacer(minLength: 0)
                }
            }
        }
    }

    private func statusTint(_ status: ProviderStatus) -> Color {
        switch status {
        case .ready: return .green
        case .loading: return .secondary
        case .setupNeeded, .rateLimited: return .orange
        case .offline, .unauthorized, .expired, .error: return .red
        }
    }

    private var modeExplanation: LocalizedStringKey {
        switch provider.mode {
        case .live: return "Built-in reads the service's own usage endpoint with the credential it already has."
        case .customJSON: return "Custom JSON calls an endpoint you define and reads the numbers out of the response."
        case .manual: return "Manual budget tracks figures you type in. Nothing is fetched and nothing leaves the Mac."
        }
    }

    // MARK: Built-in

    @ViewBuilder private var live: some View {
        if let account = LiveCredential.account(for: provider.id) {
            Section {
                if LiveCredential.usesMonthlyBudget(provider.id) {
                    LabeledContent("Monthly budget (USD)") { numberField(bind(\.monthlyBudget)) }
                }
                if let workspacePrompt = LiveCredential.workspacePrompt(for: provider.id) {
                    TextField(workspacePrompt, text: bind(\.workspaceID), prompt: Text("Required"))
                }
                if LiveCredential.usesRegion(provider.id) {
                    Picker("Region", selection: bind(\.region)) {
                        ForEach(ProviderRegion.allCases, id: \.self) { Text($0.label).tag($0) }
                    }
                }
                SecureField(LiveCredential.prompt(for: provider.id), text: $model.liveSecret, prompt: Text("Paste to replace"))
                keyActions(account: account, typed: model.liveSecret)
            } header: {
                Text("Built-in access")
            } footer: {
                SettingsFootnote("The key is written straight to the Keychain and is never held in preferences, logs, or this window after it is saved.")
            }
        } else if BuiltInProviders.supported.contains(provider.id) {
            Section {
                Label("AIUsageMeter reads an existing local sign-in and never refreshes, rotates, or rewrites it. Extra usage and credits appear on the card only when the account has them.", systemImage: "lock.shield")
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } header: {
                Text("Built-in access")
            }
        } else {
            Section {
                Label("No safe built-in usage endpoint is known for this service. Choose Custom JSON or Manual budget.", systemImage: "info.circle")
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } header: {
                Text("Built-in access")
            }
        }
    }

    // MARK: Manual

    @ViewBuilder private var manual: some View {
        Section {
            LabeledContent("Used") { numberField(bind(\.manual.used)) }
            LabeledContent("Limit") { numberField(bind(\.manual.limit)) }
            DatePicker("Resets", selection: manualDate, displayedComponents: [.date, .hourAndMinute])
        } header: {
            Text("Manual budget")
        } footer: {
            if provider.manual.limit <= 0 {
                SettingsWarning(text: "Set a limit above zero, or the gauge has nothing to measure against.")
            } else {
                SettingsFootnote("The gauge shows used against limit. The reset date is a reminder only — nothing is cleared for you.")
            }
        }
    }

    // MARK: Custom JSON

    @ViewBuilder private var custom: some View {
        Section {
            TextField("Display name", text: bind(\.custom.name))
            TextField("HTTPS endpoint", text: bind(\.custom.endpoint), prompt: Text("https://example.com/v1/usage"))
            Picker("Method", selection: bind(\.custom.method)) {
                ForEach(HTTPMethod.allCases, id: \.self) { Text($0.rawValue).tag($0) }
            }
            TextField("Dashboard URL", text: bind(\.custom.dashboardURL), prompt: Text("Optional"))
        } header: {
            Text("Connector")
        } footer: {
            if let warning = endpointWarning {
                SettingsWarning(text: warning)
            } else {
                SettingsFootnote("The endpoint is read with the method above. Plain HTTP is refused.")
            }
        }

        Section {
            Picker("Send the secret as", selection: bind(\.custom.secretPlacement)) {
                Text("Bearer token").tag(SecretPlacement.bearer)
                Text("API-key header").tag(SecretPlacement.apiKeyHeader)
                Text("None").tag(SecretPlacement.none)
            }
            if provider.custom.secretPlacement == .apiKeyHeader {
                TextField("Header name", text: bind(\.custom.apiKeyHeader), prompt: Text("X-API-Key"))
            }
            if provider.custom.secretPlacement != .none {
                SecureField("Secret", text: $model.customSecret, prompt: Text("Paste to replace"))
                keyActions(account: AppModel.customAccount(for: provider.id), typed: model.customSecret)
            }
        } header: {
            Text("Authentication")
        }

        Section {
            TextField("Percent", text: bind(\.custom.percentPath), prompt: Text("usage.percent"))
            TextField("Used", text: bind(\.custom.usedPath), prompt: Text("usage.used"))
            TextField("Limit", text: bind(\.custom.limitPath), prompt: Text("usage.limit"))
            TextField("Resets", text: bind(\.custom.resetPath), prompt: Text("usage.resets_at"))
        } header: {
            Text("JSON paths")
        } footer: {
            SettingsFootnote("Dot-separated, with array indexes — for example `data.0.usage.percent`.")
        }
    }

    private var endpointWarning: String? {
        let value = provider.custom.endpoint.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return nil }
        guard let url = URL(string: value), url.scheme?.lowercased() == "https", !(url.host ?? "").isEmpty else {
            return "Enter a full https:// address, including the host."
        }
        return nil
    }

    // MARK: Shared rows

    private func keyActions(account: String, typed: String) -> some View {
        HStack(spacing: 10) {
            Label(
                model.hasStoredSecret(account) ? "A secret is saved in your Keychain" : "No secret saved yet",
                systemImage: model.hasStoredSecret(account) ? "checkmark.seal.fill" : "key"
            )
            .font(.callout)
            .foregroundStyle(.secondary)
            Spacer(minLength: 8)
            if model.hasStoredSecret(account) {
                Button("Remove") { model.removeSecret(account: account, provider: provider.id) }
            }
            Button("Save") { model.saveSecrets(for: provider.id) }
                .keyboardShortcut(.defaultAction)
                .disabled(typed.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
        }
    }

    private func numberField(_ binding: Binding<Double>) -> some View {
        TextField("", value: binding, format: .number.precision(.fractionLength(0...2)))
            .labelsHidden()
            .multilineTextAlignment(.trailing)
            .frame(width: 120)
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

struct ProviderBadge: View {
    let provider: ProviderID
    let percent: Double
    let status: ProviderStatus

    private let side: CGFloat = 56
    private let ring: CGFloat = 5

    var body: some View {
        ZStack {
            Circle().fill(Palette.surface)
            Circle().strokeBorder(Palette.edge, lineWidth: Metrics.hairline)
            ZStack {
                Circle().stroke(Palette.ringTrack, lineWidth: ring)
                Circle()
                    .trim(from: 0, to: min(max(percent, 0), 100) / 100)
                    .stroke(Color.usage(percent: percent, status: status), style: StrokeStyle(lineWidth: ring, lineCap: .round))
                    .rotationEffect(.degrees(-90))
                GlyphView(glyph: provider.glyph, provider: provider, size: 20, color: Palette.primary)
            }
            .padding(ring / 2 + 7)
        }
        .frame(width: side, height: side)
        .accessibilityHidden(true)
    }
}

// MARK: - General detail

struct GeneralSettings: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Form {
            if let notice = model.settingsNotice {
                Section { NoticeRow(notice: notice) { model.settingsNotice = nil } }
            }

            Section {
                Picker("Overlay size", selection: bind(\.overlaySize)) {
                    ForEach(OverlaySize.allCases, id: \.self) { size in
                        Text(size.label).tag(size)
                    }
                }
                .pickerStyle(.segmented)
            } header: {
                Text("Appearance")
            } footer: {
                SettingsFootnote("The size applies to the rail, its gauges, and the detail card together.")
            }

            Section {
                Picker("Display", selection: bind(\.screenIdentifier)) {
                    Text("Display with the pointer").tag(String?.none)
                    ForEach(model.screens) { screen in
                        Text(screen.name).tag(Optional(screen.id))
                    }
                    if let stored = model.preferences.screenIdentifier, !model.screens.contains(where: { $0.id == stored }) {
                        // An unplugged display is still the stored choice; without a row the picker would show blank.
                        Text("Display not connected").tag(Optional(stored))
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
                            .frame(minWidth: 130)
                            .accessibilityLabel("Fine adjustment")
                        Text("\(Int(model.preferences.verticalOffset)) pt")
                            .monospacedDigit()
                            .foregroundStyle(.secondary)
                            .frame(width: 64, alignment: .trailing)
                        Button("Reset") {
                            model.preferences.verticalOffset = 0
                            model.scheduleSave(refetch: false)
                        }
                        .controlSize(.small)
                        .disabled(model.preferences.verticalOffset == 0)
                    }
                }
            } header: {
                Text("Placement")
            } footer: {
                SettingsFootnote("Fine adjustment nudges the overlay away from the position above, in points.")
            }

            Section {
                Picker("Refresh every", selection: bind(\.refreshInterval)) {
                    Text("30 seconds").tag(30.0)
                    Text("1 minute").tag(60.0)
                    Text("5 minutes").tag(300.0)
                    Text("15 minutes").tag(900.0)
                    Text("1 hour").tag(3600.0)
                }
                HStack(spacing: 10) {
                    refreshState
                    Spacer(minLength: 12)
                    Button("Refresh Now") { Task { await model.refresh() } }
                        .disabled(model.isRefreshingAny)
                }
            } header: {
                Text("Updating")
            }

            Section {
                Toggle("Show the side notch", isOn: bind(\.overlayVisible))
                Toggle("Launch at login", isOn: bind(\.launchAtLogin))
                Toggle("Use demo data", isOn: bind(\.demoData))
            } header: {
                Text("Behaviour")
            } footer: {
                VStack(alignment: .leading, spacing: 6) {
                    Text("Demo data is deterministic, clearly labelled, and never replaces a failed live reading.")
                    Text("Provider access is read-only. Secrets live in the Keychain, never in preferences or logs.")
                    Text("Settings apply as you make them and are saved for you. A change that alters a reading refetches it, so the rail and the menu-bar gauge never wait on the refresh timer.")
                }
                .font(.footnote)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.leading)
                .fixedSize(horizontal: false, vertical: true)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .formStyle(.grouped)
    }

    @ViewBuilder private var refreshState: some View {
        if model.isRefreshingAny {
            HStack(spacing: 8) {
                ProgressView().controlSize(.small)
                Text("Reading usage…")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        } else if let last = model.lastRefresh {
            Text("Last read \(last, style: .relative) ago")
                .font(.callout)
                .monospacedDigit()
                .foregroundStyle(.secondary)
        } else {
            Text("No reading yet")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private func bind<T>(_ path: WritableKeyPath<AppPreferences, T>) -> Binding<T> {
        Binding(
            get: { model.preferences[keyPath: path] },
            set: { model.preferences[keyPath: path] = $0; model.scheduleSave() }
        )
    }
}

// MARK: - About and support

struct AboutSettings: View {
    @ObservedObject var model: AppModel

    private var icon: NSImage? { NSImage(named: NSImage.applicationIconName) }

    var body: some View {
        Form {
            Section { header }

            Section {
                VStack(alignment: .leading, spacing: 12) {
                    Text(SupportLinks.sponsorBlurb)
                        .font(.callout)
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                        .frame(maxWidth: .infinity, alignment: .leading)
                    HStack(spacing: 10) {
                        Button(action: model.openSupport) {
                            Label(SupportLinks.sponsorLabel, systemImage: "cup.and.saucer.fill")
                        }
                        .buttonStyle(SupportButtonStyle(tone: .sponsor))
                        Button { model.openRepository() } label: {
                            Label(SupportLinks.repositoryLabel, systemImage: "star")
                        }
                        .buttonStyle(SupportButtonStyle(tone: .neutral))
                        Button { model.openIssues() } label: {
                            Label("Report an issue", systemImage: "ladybug")
                        }
                        .buttonStyle(SupportButtonStyle(tone: .neutral))
                        Spacer(minLength: 0)
                    }
                }
                .padding(.vertical, 4)
            } header: {
                Text("Support")
            }

            Section {
                LabeledContent("Design inspired by") {
                    Button(SupportLinks.designerHandle) { model.openDesigner() }
                        .buttonStyle(.link)
                }
                LabeledContent("Source") {
                    Button("github.com/dngkec/aiusagemeter") { model.openRepository() }
                        .buttonStyle(.link)
                }
                LabeledContent("Licence", value: "MIT")
            } header: {
                Text("Credits")
            } footer: {
                SettingsFootnote("The notch, the gauges, and the detail card are modelled on work by \(SupportLinks.designerHandle). Thank you.")
            }
        }
        .formStyle(.grouped)
    }

    private var header: some View {
        HStack(alignment: .top, spacing: 16) {
            Group {
                if let icon {
                    Image(nsImage: icon).resizable().interpolation(.high)
                } else {
                    RoundedRectangle(cornerRadius: 14, style: .continuous).fill(.quaternary)
                }
            }
            .frame(width: 64, height: 64)
            .accessibilityHidden(true)

            VStack(alignment: .leading, spacing: 4) {
                Text("AIUsageMeter").font(.title2.bold())
                Text(model.versionSummary)
                    .font(.callout)
                    .monospacedDigit()
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
}

struct SupportButtonStyle: ButtonStyle {
    enum Tone { case sponsor, neutral }
    let tone: Tone

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.system(size: 13, weight: .semibold))
            .foregroundStyle(foreground)
            .padding(.horizontal, 14)
            .frame(height: 30)
            .background(
                RoundedRectangle(cornerRadius: 8, style: .continuous)
                    .fill(background(pressed: configuration.isPressed))
            )
            .contentShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
    }

    private var foreground: AnyShapeStyle {
        switch tone {
        case .sponsor: return AnyShapeStyle(Color.black)
        case .neutral: return AnyShapeStyle(.primary)
        }
    }

    private func background(pressed: Bool) -> AnyShapeStyle {
        switch tone {
        case .sponsor: return AnyShapeStyle(Palette.sponsor.opacity(pressed ? 0.80 : 1))
        case .neutral: return AnyShapeStyle(Color.primary.opacity(pressed ? 0.20 : 0.10))
        }
    }
}

enum BuiltInProviders {
    static let supported: Set<ProviderID> = [.claude, .codex, .grok, .cursor, .copilot, .gemini, .kimi, .jetBrainsAI]
}

enum SupportCopy {
    static func text(for id: ProviderID) -> String {
        switch id {
        case .claude: return "Reads Claude Code’s saved sign-in and the official subscription usage endpoint, read-only. Extra usage appears when the account has it enabled."
        case .anthropicCost: return "Reads organisation API cost with an Admin key stored in AIUsageMeter’s Keychain item."
        case .codex: return "Reads ~/.codex/auth.json and the Codex usage endpoint without altering the CLI login. Credits appear when the account reports them."
        case .grok: return "Reads ~/.grok/auth.json and both Grok CLI billing response shapes, including credits when present."
        case .cursor: return "Opens Cursor’s state database read-only and tries the known usage endpoints. On-demand spend appears when enabled."
        case .copilot: return "Finds an existing Copilot or GitHub CLI token and normalises the official quota data."
        case .gemini: return "Uses a valid Gemini CLI access token. Reopen Gemini CLI once it expires."
        case .kimi: return "Uses a valid Kimi Code token and never refreshes or modifies it."
        case .openAIAPI: return "Reads organisation API cost with an OpenAI Admin key stored in AIUsageMeter’s Keychain item."
        case .openRouter: return "Reads OpenRouter credits and the current key’s spend cap. Add an API key in Settings."
        case .deepSeek: return "Reads the documented DeepSeek balance endpoint. Credits remaining are shown against your monthly budget."
        case .mistral: return "Reads organisation API cost with a Mistral Admin key stored in AIUsageMeter’s Keychain item."
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
