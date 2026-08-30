import Foundation

public enum ProviderID: String, Codable, CaseIterable, Sendable {
    case claude, anthropicCost, codex, grok, cursor, copilot, gemini, kimi
    case openAIAPI, openRouter, deepSeek, mistral, xaiAPI, moonshot, perplexity, windsurf, zai
    case openCode, localModels, jetBrainsAI, warp, amp, kilo, augment, devin, antigravity, custom

    public var displayName: String {
        switch self {
        case .claude: return "Claude Code"
        case .anthropicCost: return "Anthropic API"
        case .codex: return "Codex / ChatGPT"
        case .grok: return "Grok / xAI"
        case .cursor: return "Cursor"
        case .copilot: return "GitHub Copilot"
        case .gemini: return "Gemini Code Assist"
        case .kimi: return "Kimi Code"
        case .openAIAPI: return "OpenAI API"
        case .openRouter: return "OpenRouter"
        case .deepSeek: return "DeepSeek"
        case .mistral: return "Mistral"
        case .xaiAPI: return "xAI Platform"
        case .moonshot: return "Moonshot / Kimi"
        case .perplexity: return "Perplexity"
        case .windsurf: return "Windsurf"
        case .zai: return "Z.ai / GLM"
        case .openCode: return "OpenCode"
        case .localModels: return "Ollama / LM Studio"
        case .jetBrainsAI: return "JetBrains AI"
        case .warp: return "Warp"
        case .amp: return "Amp"
        case .kilo: return "Kilo"
        case .augment: return "Augment"
        case .devin: return "Devin"
        case .antigravity: return "Antigravity"
        case .custom: return "Custom"
        }
    }

    public var monogram: String {
        switch self {
        case .claude: return "✳"
        case .anthropicCost: return "A"
        case .codex, .openAIAPI: return "◎"
        case .grok: return "𝕏"
        case .cursor: return "C"
        case .copilot: return "GH"
        case .gemini: return "✦"
        case .kimi: return "K"
        case .openRouter: return "OR"
        case .deepSeek: return "DS"
        case .mistral: return "M"
        case .xaiAPI: return "xAI"
        case .moonshot: return "MS"
        case .perplexity: return "P"
        case .windsurf: return "W"
        case .zai: return "Z"
        case .openCode: return "OC"
        case .localModels: return "L"
        case .jetBrainsAI: return "JB"
        case .warp: return "W"
        case .amp: return "A"
        case .kilo: return "K"
        case .augment: return "AU"
        case .devin: return "D"
        case .antigravity: return "AG"
        case .custom: return "+"
        }
    }
}

/// Where a provider's usage lives in the two regions that serve it separately.
public enum ProviderRegion: String, Codable, Sendable, CaseIterable {
    case global, china

    public var label: String {
        switch self {
        case .global: return "Global"
        case .china: return "China mainland"
        }
    }
}

/// Built-in live sources that store a key in UsageMeter’s Keychain item.
public enum LiveCredential {
    public static func account(for id: ProviderID) -> String? {
        switch id {
        case .anthropicCost: return "anthropic.adminKey"
        case .openAIAPI: return "openai.adminKey"
        case .openRouter: return "openrouter.apiKey"
        case .deepSeek: return "deepseek.apiKey"
        case .mistral: return "mistral.adminKey"
        case .xaiAPI: return "xai.managementKey"
        case .moonshot: return "moonshot.apiKey"
        case .zai: return "zai.apiKey"
        case .openCode: return "opencode.apiKey"
        case .warp: return "warp.apiKey"
        default: return nil
        }
    }

    /// True only where the reading is money measured against a budget the user
    /// sets. A percentage or request-count quota carries its own limit.
    public static func usesMonthlyBudget(_ id: ProviderID) -> Bool {
        switch id {
        case .anthropicCost, .openAIAPI, .openRouter, .deepSeek, .mistral, .xaiAPI, .moonshot: return true
        default: return false
        }
    }

    public static func prompt(for id: ProviderID) -> String {
        switch id {
        case .anthropicCost, .openAIAPI, .mistral: return "Admin key"
        case .xaiAPI: return "Management key"
        case .openRouter, .deepSeek, .moonshot, .zai, .openCode, .warp: return "API key"
        default: return "Key"
        }
    }

    /// Providers whose endpoint puts an account identifier in the path.
    public static func workspacePrompt(for id: ProviderID) -> String? {
        switch id {
        case .xaiAPI: return "Team ID"
        default: return nil
        }
    }

    /// Providers served from a separate host inside mainland China.
    public static func usesRegion(_ id: ProviderID) -> Bool {
        switch id {
        case .moonshot, .zai: return true
        default: return false
        }
    }
}

public enum DataSourceKind: String, Codable, Sendable {
    case live = "Live"
    case customJSON = "Custom JSON"
    case manual = "Manual"
    case demo = "Demo data"
}

public enum ProviderStatus: String, Codable, Sendable, Equatable {
    case ready, loading, setupNeeded, offline, unauthorized, rateLimited, error, expired
}

public enum UsageKind: String, Codable, Sendable, Equatable {
    case quota
    case extraUsage
    case apiCost
    case credits
}

public struct UsageWindow: Codable, Equatable, Sendable, Identifiable {
    public var id: String
    public var label: String
    public var used: Double
    public var limit: Double
    public var resetsAt: Date?
    public var kind: UsageKind

    public init(id: String, label: String, used: Double, limit: Double, resetsAt: Date? = nil, kind: UsageKind = .quota) {
        self.id = id
        self.label = label
        self.used = used.isFinite ? max(0, used) : 0
        self.limit = limit.isFinite ? max(0, limit) : 0
        self.resetsAt = resetsAt
        self.kind = kind
    }

    private enum CodingKeys: String, CodingKey { case id, label, used, limit, resetsAt, kind }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            id: try c.decode(String.self, forKey: .id),
            label: try c.decode(String.self, forKey: .label),
            used: try c.decode(Double.self, forKey: .used),
            limit: try c.decode(Double.self, forKey: .limit),
            resetsAt: try c.decodeIfPresent(Date.self, forKey: .resetsAt),
            kind: try c.decodeIfPresent(UsageKind.self, forKey: .kind) ?? .quota
        )
    }

    public var fraction: Double { limit > 0 ? max(0, used / limit) : 0 }
    public var percent: Double { fraction * 100 }

    /// Gauge/card value: a percentage for plan quotas, amounts for spend and credits.
    public var readingCaption: String {
        switch kind {
        case .quota: return "\(Int(percent.rounded()))%"
        case .extraUsage, .apiCost: return "\(Self.money(used)) / \(Self.money(limit))"
        case .credits:
            if used == used.rounded(), limit == limit.rounded(), abs(limit) >= 1 {
                return "\(Int(used.rounded())) / \(Int(limit.rounded()))"
            }
            return "\(Self.money(used)) / \(Self.money(limit))"
        }
    }

    /// Fallback under the bar when the provider did not report a reset.
    public var remainingCaption: String? {
        switch kind {
        case .quota: return nil
        case .extraUsage, .apiCost, .credits:
            let left = max(0, limit - used)
            if kind == .credits, left == left.rounded() {
                return "\(Int(left.rounded())) remaining"
            }
            return "\(Self.money(left)) remaining"
        }
    }

    private static func money(_ value: Double) -> String {
        if value == value.rounded() { return String(format: "$%.0f", value) }
        return String(format: "$%.2f", value)
    }
}

public struct ProviderSnapshot: Codable, Equatable, Sendable, Identifiable {
    public var id: ProviderID
    public var name: String
    public var windows: [UsageWindow]
    public var status: ProviderStatus
    public var source: DataSourceKind
    public var message: String?
    public var dashboardURL: URL?
    public var updatedAt: Date

    public init(id: ProviderID, name: String? = nil, windows: [UsageWindow] = [], status: ProviderStatus = .ready, source: DataSourceKind = .live, message: String? = nil, dashboardURL: URL? = nil, updatedAt: Date = Date()) {
        self.id = id
        self.name = name ?? id.displayName
        self.windows = windows
        self.status = status
        self.source = source
        self.message = message
        self.dashboardURL = dashboardURL
        self.updatedAt = updatedAt
    }

    public var primaryPercent: Double { windows.first?.percent ?? 0 }

    /// Card rows. Extra usage and credits take a slot when present so they
    /// are not buried under a third model-specific quota window.
    public func featuredWindows(limit: Int = 3) -> [UsageWindow] {
        guard windows.count > limit else { return windows }
        let extras = windows.filter { $0.kind != .quota }
        let quotas = windows.filter { $0.kind == .quota }
        let extraCount = min(extras.count, limit)
        let quotaCount = min(quotas.count, max(0, limit - extraCount))
        return Array(quotas.prefix(quotaCount) + extras.prefix(extraCount))
    }
}

public enum UsageColor: String, Equatable, Sendable {
    case green, yellow, orange, red
    public static func threshold(percent: Double) -> UsageColor {
        switch percent {
        case ..<50: return .green
        case ..<70: return .yellow
        case ..<90: return .orange
        default: return .red
        }
    }
}

public enum ProviderMode: String, Codable, Sendable, CaseIterable {
    case live, customJSON, manual
}

public struct ManualBudget: Codable, Equatable, Sendable {
    public var used: Double
    public var limit: Double
    public var resetDate: Date?
    public init(used: Double = 0, limit: Double = 100, resetDate: Date? = nil) {
        self.used = used; self.limit = limit; self.resetDate = resetDate
    }
}

public enum HTTPMethod: String, Codable, Sendable, CaseIterable { case get = "GET", post = "POST" }

public enum SecretPlacement: String, Codable, Sendable, CaseIterable {
    case bearer, apiKeyHeader, none
}

public struct CustomConnector: Codable, Equatable, Sendable {
    public var name: String
    public var endpoint: String
    public var method: HTTPMethod
    public var secretPlacement: SecretPlacement
    public var apiKeyHeader: String
    public var percentPath: String
    public var usedPath: String
    public var limitPath: String
    public var resetPath: String
    public var dashboardURL: String

    public init(name: String = "Custom", endpoint: String = "", method: HTTPMethod = .get, secretPlacement: SecretPlacement = .bearer, apiKeyHeader: String = "X-API-Key", percentPath: String = "usage.percent", usedPath: String = "usage.used", limitPath: String = "usage.limit", resetPath: String = "usage.resets_at", dashboardURL: String = "") {
        self.name = name; self.endpoint = endpoint; self.method = method
        self.secretPlacement = secretPlacement; self.apiKeyHeader = apiKeyHeader
        self.percentPath = percentPath; self.usedPath = usedPath; self.limitPath = limitPath
        self.resetPath = resetPath; self.dashboardURL = dashboardURL
    }

    private enum CodingKeys: String, CodingKey { case name, endpoint, method, secretPlacement, apiKeyHeader, percentPath, usedPath, limitPath, resetPath, dashboardURL }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            name: try c.decodeIfPresent(String.self, forKey: .name) ?? "Custom",
            endpoint: try c.decodeIfPresent(String.self, forKey: .endpoint) ?? "",
            method: try c.decodeIfPresent(HTTPMethod.self, forKey: .method) ?? .get,
            secretPlacement: try c.decodeIfPresent(SecretPlacement.self, forKey: .secretPlacement) ?? .bearer,
            apiKeyHeader: try c.decodeIfPresent(String.self, forKey: .apiKeyHeader) ?? "X-API-Key",
            percentPath: try c.decodeIfPresent(String.self, forKey: .percentPath) ?? "usage.percent",
            usedPath: try c.decodeIfPresent(String.self, forKey: .usedPath) ?? "usage.used",
            limitPath: try c.decodeIfPresent(String.self, forKey: .limitPath) ?? "usage.limit",
            resetPath: try c.decodeIfPresent(String.self, forKey: .resetPath) ?? "usage.resets_at",
            dashboardURL: try c.decodeIfPresent(String.self, forKey: .dashboardURL) ?? ""
        )
    }
}

public struct ProviderConfiguration: Codable, Equatable, Sendable, Identifiable {
    public var id: ProviderID
    public var enabled: Bool
    public var showInNotch: Bool
    public var mode: ProviderMode
    public var monthlyBudget: Double
    /// Account identifier some endpoints put in the path, such as an xAI team.
    public var workspaceID: String
    public var region: ProviderRegion
    public var manual: ManualBudget
    public var custom: CustomConnector

    public init(id: ProviderID, enabled: Bool = false, showInNotch: Bool = true, mode: ProviderMode = .live, monthlyBudget: Double = 100, workspaceID: String = "", region: ProviderRegion = .global, manual: ManualBudget = .init(), custom: CustomConnector = .init()) {
        self.id = id; self.enabled = enabled; self.showInNotch = showInNotch; self.mode = mode
        self.monthlyBudget = monthlyBudget; self.workspaceID = workspaceID; self.region = region
        self.manual = manual; self.custom = custom
    }

    private enum CodingKeys: String, CodingKey { case id, enabled, showInNotch, mode, monthlyBudget, workspaceID, region, manual, custom }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            id: try c.decode(ProviderID.self, forKey: .id),
            enabled: try c.decodeIfPresent(Bool.self, forKey: .enabled) ?? false,
            showInNotch: try c.decodeIfPresent(Bool.self, forKey: .showInNotch) ?? true,
            mode: try c.decodeIfPresent(ProviderMode.self, forKey: .mode) ?? .live,
            monthlyBudget: try c.decodeIfPresent(Double.self, forKey: .monthlyBudget) ?? 100,
            workspaceID: try c.decodeIfPresent(String.self, forKey: .workspaceID) ?? "",
            region: try c.decodeIfPresent(ProviderRegion.self, forKey: .region) ?? .global,
            manual: try c.decodeIfPresent(ManualBudget.self, forKey: .manual) ?? .init(),
            custom: try c.decodeIfPresent(CustomConnector.self, forKey: .custom) ?? .init()
        )
    }
}

public enum VerticalPosition: String, Codable, Sendable, CaseIterable { case top, center, bottom }

public struct AppPreferences: Codable, Equatable, Sendable {
    public static let currentSchemaVersion = 2
    public var schemaVersion: Int
    public var providers: [ProviderConfiguration]
    public var refreshInterval: TimeInterval
    public var screenIdentifier: String?
    public var verticalPosition: VerticalPosition
    public var verticalOffset: Double
    public var launchAtLogin: Bool
    public var demoData: Bool
    public var overlayVisible: Bool
    public var overlaySize: OverlaySize

    public init(schemaVersion: Int = currentSchemaVersion, providers: [ProviderConfiguration] = AppPreferences.defaultProviders, refreshInterval: TimeInterval = 300, screenIdentifier: String? = nil, verticalPosition: VerticalPosition = .center, verticalOffset: Double = 0, launchAtLogin: Bool = false, demoData: Bool = false, overlayVisible: Bool = true, overlaySize: OverlaySize = .medium) {
        self.schemaVersion = schemaVersion; self.providers = providers; self.refreshInterval = refreshInterval
        self.screenIdentifier = screenIdentifier; self.verticalPosition = verticalPosition; self.verticalOffset = verticalOffset
        self.launchAtLogin = launchAtLogin; self.demoData = demoData; self.overlayVisible = overlayVisible
        self.overlaySize = overlaySize
    }

    private enum CodingKeys: String, CodingKey { case schemaVersion, providers, refreshInterval, screenIdentifier, verticalPosition, verticalOffset, launchAtLogin, demoData, overlayVisible, overlaySize }
    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.init(
            schemaVersion: try c.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 1,
            providers: try c.decodeIfPresent([ProviderConfiguration].self, forKey: .providers) ?? Self.defaultProviders,
            refreshInterval: try c.decodeIfPresent(TimeInterval.self, forKey: .refreshInterval) ?? 300,
            screenIdentifier: try c.decodeIfPresent(String.self, forKey: .screenIdentifier),
            verticalPosition: try c.decodeIfPresent(VerticalPosition.self, forKey: .verticalPosition) ?? .center,
            verticalOffset: try c.decodeIfPresent(Double.self, forKey: .verticalOffset) ?? 0,
            launchAtLogin: try c.decodeIfPresent(Bool.self, forKey: .launchAtLogin) ?? false,
            demoData: try c.decodeIfPresent(Bool.self, forKey: .demoData) ?? false,
            overlayVisible: try c.decodeIfPresent(Bool.self, forKey: .overlayVisible) ?? true,
            overlaySize: try c.decodeIfPresent(OverlaySize.self, forKey: .overlaySize) ?? .medium
        )
    }

    public static var defaultProviders: [ProviderConfiguration] {
        ProviderID.allCases.map { id in
            ProviderConfiguration(id: id, enabled: [.claude, .codex, .grok].contains(id), showInNotch: true)
        }
    }
}

public enum ProviderOrdering {
    public static func move(_ id: ProviderID, by delta: Int, in providers: inout [ProviderConfiguration]) {
        guard let index = providers.firstIndex(where: { $0.id == id }) else { return }
        let target = index + delta
        guard providers.indices.contains(target) else { return }
        providers.swapAt(index, target)
    }

    /// What the rail shows, in the order the settings list puts it.
    ///
    /// Readings arrive from whichever refresh last finished, so the order has to
    /// come from the preferences rather than from the readings; otherwise a
    /// reorder would not reach the rail until the next fetch.
    public static func arrange(_ snapshots: [ProviderSnapshot], by providers: [ProviderConfiguration]) -> [ProviderSnapshot] {
        var byID = Dictionary(snapshots.map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        return providers.compactMap { provider in
            guard provider.enabled, provider.showInNotch else { return nil }
            return byID.removeValue(forKey: provider.id)
        }
    }
}

public enum UsageMeterError: LocalizedError, Equatable {
    case setupNeeded(String), unauthorized, rateLimited, server(Int), offline, timeout, oversizedResponse, invalidResponse, expiredCredential(String), invalidURL(String), missingField(String)
    public var errorDescription: String? {
        switch self {
        case .setupNeeded(let s): return s
        case .unauthorized: return "Sign-in is required."
        case .rateLimited: return "The provider is rate limiting requests."
        case .server(let code): return "Provider returned HTTP \(code)."
        case .offline: return "No network connection."
        case .timeout: return "The request timed out."
        case .oversizedResponse: return "The provider response exceeded the safe size limit."
        case .invalidResponse: return "The provider returned an unsupported response."
        case .expiredCredential(let s): return s
        case .invalidURL(let s): return s
        case .missingField(let s): return "Missing field: \(s)"
        }
    }
}
