import Foundation
import Security

public struct ProviderContext: Sendable {
    public let http: HTTPClient
    public let files: LocalFiles
    public let secrets: SecretStore
    public let external: ExternalCredentials
    public init(http: HTTPClient, files: LocalFiles, secrets: SecretStore, external: ExternalCredentials = NoExternalCredentials()) {
        self.http = http; self.files = files; self.secrets = secrets; self.external = external
    }
}

public protocol ProviderAdapter: Sendable {
    var id: ProviderID { get }
    func fetch(configuration: ProviderConfiguration, context: ProviderContext) async throws -> ProviderSnapshot
}

public struct GenericProviderAdapter: ProviderAdapter {
    public let id: ProviderID
    public init(id: ProviderID) { self.id = id }

    public func fetch(configuration: ProviderConfiguration, context: ProviderContext) async throws -> ProviderSnapshot {
        switch configuration.mode {
        case .manual:
            let item = configuration.manual
            return ProviderSnapshot(id: id, windows: [UsageWindow(id: "manual", label: "Manual budget", used: item.used, limit: item.limit, resetsAt: item.resetDate)], source: .manual, message: "Entered manually")
        case .customJSON:
            return try await fetchCustom(configuration, context)
        case .live:
            return try await fetchLive(configuration, context)
        }
    }

    private func fetchCustom(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        let connector = configuration.custom
        let url = try EndpointValidator.validate(connector.endpoint)
        let secret = try context.secrets.read("custom.\(id.rawValue)")
        var headers: [String: String] = [:]
        var bearer: String?
        switch connector.secretPlacement {
        case .bearer: bearer = secret
        case .apiKeyHeader: if let secret { headers[connector.apiKeyHeader] = secret }
        case .none: break
        }
        let request = RequestFactory.request(url: url, method: connector.method, bearer: bearer, headers: headers)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        let windows = try UsageParsers.custom(data, connector: connector)
        return ProviderSnapshot(id: id, name: connector.name.isEmpty ? id.displayName : connector.name, windows: windows, source: .customJSON, message: "Configured endpoint", dashboardURL: try? connector.dashboardURL.isEmpty ? nil : EndpointValidator.validate(connector.dashboardURL))
    }

    private func fetchLive(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        switch id {
        case .claude: return try await claude(context)
        case .anthropicCost: return try await anthropicCost(configuration, context)
        case .codex: return try await codex(context)
        case .grok: return try await grok(context)
        case .cursor: return try await cursor(context)
        case .copilot: return try await copilot(context)
        case .gemini: return try await gemini(context)
        case .kimi: return try await kimi(context)
        case .openAIAPI: return try await openAICost(configuration, context)
        case .openRouter: return try await openRouter(configuration, context)
        case .deepSeek: return try await deepSeek(configuration, context)
        case .mistral: return try await mistral(configuration, context)
        case .xaiAPI: return try await xaiPlatform(configuration, context)
        case .moonshot: return try await moonshot(configuration, context)
        case .zai: return try await zai(configuration, context)
        case .openCode: return try await openCode(context)
        case .warp: return try await warp(context)
        case .jetBrainsAI: return try jetBrains(context)
        default: throw AIUsageMeterError.setupNeeded("No safe built-in usage endpoint is available. Choose Custom JSON or Manual Budget.")
        }
    }

    private func claude(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let credential = try CredentialResolver.claude(files: context.files, external: context.external)
        let token = try CredentialResolver.token(from: credential, paths: ["claudeAiOauth.accessToken", "accessToken", "access_token"])
        let url = URL(string: "https://api.anthropic.com/api/oauth/usage")!
        let request = RequestFactory.request(url: url, bearer: token, headers: ["anthropic-beta": "oauth-2025-04-20", "anthropic-version": "2023-06-01"], userAgent: RequestFactory.claudeCodeUserAgent)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.claude(data), dashboardURL: URL(string: "https://claude.ai/settings/usage"))
    }

    private func anthropicCost(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("anthropic.adminKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add an Anthropic Admin key in Settings.") }
        let calendar = Calendar(identifier: .gregorian)
        let start = calendar.date(from: calendar.dateComponents([.year, .month], from: Date()))!
        let f = ISO8601DateFormatter(); f.formatOptions = [.withInternetDateTime]
        var components = URLComponents(string: "https://api.anthropic.com/v1/organizations/cost_report")!
        components.queryItems = [
            URLQueryItem(name: "starting_at", value: f.string(from: start)),
            URLQueryItem(name: "ending_at", value: f.string(from: Date())),
            URLQueryItem(name: "bucket_width", value: "1d"),
            URLQueryItem(name: "limit", value: "31"),
        ]
        let request = RequestFactory.request(url: components.url!, headers: ["x-api-key": key, "anthropic-version": "2023-06-01"])
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.anthropicCost(data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://console.anthropic.com/settings/billing"))
    }

    private func codex(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let root = try CredentialResolver.jsonFile(".codex/auth.json", files: context.files)
        let token = try CredentialResolver.token(from: root, paths: ["tokens.access_token", "access_token"])
        let account = root.value(at: "tokens.account_id")?.string ?? root.value(at: "account_id")?.string
        var headers: [String: String] = [:]
        if let account, !account.isEmpty { headers["ChatGPT-Account-Id"] = account }
        let request = RequestFactory.request(url: URL(string: "https://chatgpt.com/backend-api/wham/usage")!, bearer: token, headers: headers)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.codex(data), dashboardURL: URL(string: "https://chatgpt.com/codex/settings/usage"))
    }

    private func grok(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let root = try CredentialResolver.jsonFile(".grok/auth.json", files: context.files)
        let token = try CredentialResolver.grokToken(root)
        let headers = ["x-xai-token-auth": "xai-grok-cli"]
        let base = URL(string: "https://cli-chat-proxy.grok.com/v1/billing")!
        let credits = URL(string: "https://cli-chat-proxy.grok.com/v1/billing?format=credits")!
        async let a = fetchResult(RequestFactory.request(url: base, bearer: token, headers: headers), context)
        async let b = fetchResult(RequestFactory.request(url: credits, bearer: token, headers: headers), context)
        let (monthlyResult, creditsResult) = await (a, b)
        let monthly = try? monthlyResult.get(), creditData = try? creditsResult.get()
        if monthly == nil && creditData == nil { throw failure(from: creditsResult) }
        return ProviderSnapshot(id: id, windows: try UsageParsers.grok(monthly: monthly, credits: creditData), dashboardURL: URL(string: "https://grok.com"))
    }

    private func cursor(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let token = try await CredentialResolver.cursorToken(files: context.files)
        let dashboard = URL(string: "https://cursor.com/dashboard/usage")
        do {
            let request = RequestFactory.request(url: URL(string: "https://cursor.com/api/usage-summary")!, bearer: token)
            let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
            return ProviderSnapshot(id: id, windows: try UsageParsers.cursor(data), dashboardURL: dashboard)
        } catch {
            let me = RequestFactory.request(url: URL(string: "https://cursor.com/api/auth/me")!, bearer: token)
            guard let identity = try? await context.http.data(for: me, maximumBytes: 1_000_000).0,
                  let account = try? JSONValue.decode(identity),
                  let user = account.value(at: "sub")?.string ?? account.value(at: "id")?.string ?? account.value(at: "userId")?.string,
                  var components = URLComponents(string: "https://cursor.com/api/usage") else { throw error }
            components.queryItems = [URLQueryItem(name: "user", value: user)]
            let legacy = RequestFactory.request(url: components.url!, bearer: token)
            let (data, _) = try await context.http.data(for: legacy, maximumBytes: 2_000_000)
            return ProviderSnapshot(id: id, windows: try UsageParsers.cursor(data), dashboardURL: dashboard)
        }
    }

    private func copilot(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let token = try CredentialResolver.copilotToken(files: context.files)
        let headers = ["X-GitHub-Api-Version": "2025-04-01", "Editor-Version": "vscode/1.90.0", "Editor-Plugin-Version": "copilot/1.0.0"]
        let request = RequestFactory.request(url: URL(string: "https://api.github.com/copilot_internal/user")!, bearer: token, headers: headers)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.copilot(data), dashboardURL: URL(string: "https://github.com/settings/copilot"))
    }

    private func gemini(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let creds = try CredentialResolver.jsonFile(".gemini/oauth_creds.json", files: context.files)
        let token = try CredentialResolver.token(from: creds, paths: ["access_token"])
        if let expiry = JSONPicking.date(creds, ["expiry_date", "expires_at"]), expiry <= Date() { throw AIUsageMeterError.expiredCredential("Gemini’s saved token expired. Reopen Gemini CLI to sign in again.") }
        let base = "https://cloudcode-pa.googleapis.com/v1internal:"
        var load = RequestFactory.request(url: URL(string: base + "loadCodeAssist")!, method: .post, bearer: token)
        load.httpBody = try JSONSerialization.data(withJSONObject: ["metadata": ["ideType": "GEMINI_CLI", "pluginType": "GEMINI"]])
        let (loadData, _) = try await context.http.data(for: load, maximumBytes: 1_000_000)
        let loadRoot = try JSONValue.decode(loadData)
        try CredentialResolver.rejectIneligibleCodeAssistTier(loadRoot)
        var quota = RequestFactory.request(url: URL(string: base + "retrieveUserQuota")!, method: .post, bearer: token)
        let project = loadRoot.value(at: "cloudaicompanionProject")?.string
        quota.httpBody = try JSONSerialization.data(withJSONObject: project.map { ["project": $0] } ?? [:])
        let (data, _) = try await context.http.data(for: quota, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.gemini(data), dashboardURL: URL(string: "https://gemini.google.com"))
    }

    private func openAICost(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("openai.adminKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add an OpenAI Admin key in Settings.") }
        let calendar = Calendar(identifier: .gregorian)
        let start = calendar.date(from: calendar.dateComponents([.year, .month], from: Date()))!
        var components = URLComponents(string: "https://api.openai.com/v1/organization/costs")!
        components.queryItems = [URLQueryItem(name: "start_time", value: String(Int(start.timeIntervalSince1970))), URLQueryItem(name: "limit", value: "31")]
        let request = RequestFactory.request(url: components.url!, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.openAICost(data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://platform.openai.com/usage"))
    }

    private func openRouter(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("openrouter.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add an OpenRouter API key in Settings.") }
        async let creditsCall = fetchResult(RequestFactory.request(url: URL(string: "https://openrouter.ai/api/v1/credits")!, bearer: key), context)
        async let keyCall = fetchResult(RequestFactory.request(url: URL(string: "https://openrouter.ai/api/v1/key")!, bearer: key), context)
        let (creditsResult, keyResult) = await (creditsCall, keyCall)
        let credits = try? creditsResult.get(), keyData = try? keyResult.get()
        if credits == nil && keyData == nil { throw failure(from: creditsResult) }
        return ProviderSnapshot(id: id, windows: try UsageParsers.openRouter(credits: credits, key: keyData, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://openrouter.ai/credits"))
    }

    private func deepSeek(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("deepseek.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add a DeepSeek API key in Settings.") }
        let request = RequestFactory.request(url: URL(string: "https://api.deepseek.com/user/balance")!, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.deepSeek(data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://platform.deepseek.com/usage"))
    }

    private func mistral(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("mistral.adminKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add a Mistral Admin key in Settings.") }
        let request = RequestFactory.request(url: URL(string: "https://api.mistral.ai/v1/admin/spend-limit")!, headers: ["x-api-key": key])
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.mistral(spendLimit: data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://admin.mistral.ai/organization/usage"))
    }

    private func kimi(_ context: ProviderContext) async throws -> ProviderSnapshot {
        let creds = try CredentialResolver.jsonFile(".kimi-code/credentials/kimi-code.json", files: context.files)
        let token = try CredentialResolver.token(from: creds, paths: ["access_token"])
        if let expiry = JSONPicking.date(creds, ["expires_at"]), expiry <= Date() { throw AIUsageMeterError.expiredCredential("Kimi Code’s saved token expired. Reopen Kimi Code to sign in again.") }
        let request = RequestFactory.request(url: URL(string: "https://api.kimi.com/coding/v1/usages")!, bearer: token)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.kimi(data), dashboardURL: URL(string: "https://www.kimi.com/code"))
    }

    private func xaiPlatform(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("xai.managementKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add an xAI Management key in Settings. Inference keys are not accepted.") }
        let team = configuration.workspaceID.trimmingCharacters(in: .whitespacesAndNewlines)
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_")
        guard !team.isEmpty, team.unicodeScalars.allSatisfy(allowed.contains),
              let url = URL(string: "https://management-api.x.ai/v1/billing/teams/\(team)/prepaid/balance") else {
            throw AIUsageMeterError.setupNeeded("Add your xAI team ID in Settings. It appears in the console URL.")
        }
        let request = RequestFactory.request(url: url, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.xaiBalance(data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: "https://console.x.ai"))
    }

    private func moonshot(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("moonshot.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add a Moonshot API key in Settings.") }
        let china = configuration.region == .china
        let host = china ? "https://api.moonshot.cn" : "https://api.moonshot.ai"
        let request = RequestFactory.request(url: URL(string: host + "/v1/users/me/balance")!, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 1_000_000)
        let dashboard = china ? "https://platform.kimi.com/console/account" : "https://platform.moonshot.ai/console/account"
        return ProviderSnapshot(id: id, windows: try UsageParsers.moonshot(data, monthlyBudget: max(0.01, configuration.monthlyBudget)), dashboardURL: URL(string: dashboard))
    }

    private func zai(_ configuration: ProviderConfiguration, _ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("zai.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add a z.ai or BigModel API key in Settings.") }
        let china = configuration.region == .china
        let host = china ? "https://open.bigmodel.cn" : "https://api.z.ai"
        let request = RequestFactory.request(url: URL(string: host + "/api/monitor/usage/quota/limit")!, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        let dashboard = china ? "https://bigmodel.cn/coding-plan/personal/usage" : "https://z.ai/manage-apikey/coding-plan/personal/my-plan"
        return ProviderSnapshot(id: id, windows: try UsageParsers.zai(data), dashboardURL: URL(string: dashboard))
    }

    private func openCode(_ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("opencode.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add an OpenCode API key in Settings.") }
        let request = RequestFactory.request(url: URL(string: "https://opencode.ai/zen/go/v1/usage")!, bearer: key)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.openCodeZen(data), dashboardURL: URL(string: "https://opencode.ai"))
    }

    private func warp(_ context: ProviderContext) async throws -> ProviderSnapshot {
        guard let key = try context.secrets.read("warp.apiKey"), !key.isEmpty else { throw AIUsageMeterError.setupNeeded("Add a Warp API key in Settings.") }
        var request = RequestFactory.request(url: URL(string: "https://app.warp.dev/graphql/v2?op=GetRequestLimitInfo")!, method: .post, bearer: key, headers: WarpQuery.headers)
        request.httpBody = try JSONSerialization.data(withJSONObject: WarpQuery.body)
        let (data, _) = try await context.http.data(for: request, maximumBytes: 2_000_000)
        return ProviderSnapshot(id: id, windows: try UsageParsers.warp(data), dashboardURL: URL(string: "https://app.warp.dev/settings/billing"))
    }

    private func jetBrains(_ context: ProviderContext) throws -> ProviderSnapshot {
        let quota = try CredentialResolver.jetBrainsQuota(files: context.files)
        return ProviderSnapshot(id: id, windows: try UsageParsers.jetBrains(quotaInfo: quota.quotaInfo, nextRefill: quota.nextRefill), message: quota.ide, dashboardURL: URL(string: "https://www.jetbrains.com/ai/"))
    }

    private func fetchResult(_ request: URLRequest, _ context: ProviderContext) async -> Result<Data, Error> {
        do { return .success(try await context.http.data(for: request, maximumBytes: 2_000_000).0) }
        catch { return .failure(error) }
    }

    private func failure(from result: Result<Data, Error>) -> Error {
        switch result { case .success: return AIUsageMeterError.invalidResponse; case .failure(let error): return error }
    }
}

public enum WarpQuery {
    public static let document = """
    query GetRequestLimitInfo($requestContext: RequestContext!) {
      user(requestContext: $requestContext) {
        __typename
        ... on UserOutput {
          user {
            requestLimitInfo {
              isUnlimited
              nextRefreshTime
              requestLimit
              requestsUsedSinceLastRefresh
            }
          }
        }
      }
    }
    """

    static var osVersion: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        return "\(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
    }

    public static var headers: [String: String] {
        ["x-warp-os-category": "macOS", "x-warp-os-name": "macOS", "x-warp-os-version": osVersion]
    }

    public static var body: [String: Any] {
        [
            "query": document,
            "operationName": "GetRequestLimitInfo",
            "variables": [
                "requestContext": [
                    "clientContext": [String: Any](),
                    "osContext": ["category": "macOS", "name": "macOS", "version": osVersion],
                ],
            ],
        ]
    }
}

public enum CredentialResolver {
    public static func jsonFile(_ path: String, files: LocalFiles) throws -> JSONValue {
        do { return try JSONValue.decode(files.read(relativePath: path, maximumBytes: 2_000_000)) }
        catch { throw AIUsageMeterError.setupNeeded("No usable credential was found at ~/\(path).") }
    }

    public static func token(from root: JSONValue, paths: [String]) throws -> String {
        for path in paths { if let token = root.value(at: path)?.string, !token.isEmpty { return token } }
        throw AIUsageMeterError.setupNeeded("The saved credential does not contain an access token.")
    }

    public static func claude(files: LocalFiles, external: ExternalCredentials = NoExternalCredentials()) throws -> JSONValue {
        if let data = try? external.credential(service: "Claude Code-credentials"), let root = try? JSONValue.decode(data) { return root }
        return try jsonFile(".claude/.credentials.json", files: files)
    }

    static func externalKeychain(service: String) throws -> Data {
        guard let data = try NonInteractiveKeychain.read(service: service, account: NSUserName()) else {
            throw AIUsageMeterError.setupNeeded("Claude Code is not signed in.")
        }
        return data
    }

    public static func grokToken(_ root: JSONValue) throws -> String {
        if let direct = try? token(from: root, paths: ["access_token", "token", "key"]) { return direct }
        guard let entries = root.object else { throw AIUsageMeterError.setupNeeded("Grok is not signed in. Run `grok login`.") }
        let candidates = entries.compactMap { scope, node -> (scope: String, token: String, expiry: Date?)? in
            guard let value = node.value(at: "key")?.string ?? node.value(at: "access_token")?.string, !value.isEmpty else { return nil }
            return (scope, value, JSONPicking.date(node, ["expires_at", "expiry", "expires"]))
        }
        guard !candidates.isEmpty else { throw AIUsageMeterError.setupNeeded("Grok is not signed in. Run `grok login`.") }
        let now = Date()
        let live = candidates.filter { $0.expiry.map { $0 > now } ?? true }
        guard !live.isEmpty else { throw AIUsageMeterError.expiredCredential("Grok’s saved token expired. Run `grok login` to sign in again.") }
        let ranked = live.sorted { left, right in
            let leftPreferred = left.scope.contains("auth.x.ai"), rightPreferred = right.scope.contains("auth.x.ai")
            if leftPreferred != rightPreferred { return leftPreferred }
            return (left.expiry ?? .distantPast) > (right.expiry ?? .distantPast)
        }
        return ranked[0].token
    }

    public static func cursorToken(files: LocalFiles) async throws -> String {
        let db = files.homeDirectory.appendingPathComponent("Library/Application Support/Cursor/User/globalStorage/state.vscdb")
        guard FileManager.default.fileExists(atPath: db.path) else { throw AIUsageMeterError.setupNeeded("Cursor’s local sign-in database was not found.") }
        let hex: String = try await Task.detached {
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/usr/bin/sqlite3")
            process.arguments = ["-readonly", db.path, "SELECT hex(value) FROM ItemTable WHERE key='cursorAuth/accessToken' LIMIT 1;"]
            let out = Pipe(); process.standardOutput = out; process.standardError = Pipe()
            try process.run(); process.waitUntilExit()
            let data = out.fileHandleForReading.readDataToEndOfFile()
            guard process.terminationStatus == 0, let text = String(data: data, encoding: .utf8) else { throw AIUsageMeterError.setupNeeded("Cursor is installed but not signed in.") }
            return text.trimmingCharacters(in: .whitespacesAndNewlines)
        }.value
        guard let token = decodeStoredToken(hex), !token.isEmpty else { throw AIUsageMeterError.setupNeeded("Cursor is installed but not signed in.") }
        return token
    }

    static func decodeStoredToken(_ hex: String) -> String? {
        var bytes = [UInt8]()
        bytes.reserveCapacity(hex.count / 2)
        var digits = Array(hex.utf8)
        guard !digits.isEmpty, digits.count % 2 == 0 else { return nil }
        for index in stride(from: 0, to: digits.count, by: 2) {
            guard let high = hexValue(digits[index]), let low = hexValue(digits[index + 1]) else { return nil }
            bytes.append(high << 4 | low)
        }
        digits.removeAll()
        let data = Data(bytes)
        if !bytes.contains(0), let utf8 = String(data: data, encoding: .utf8) { return utf8.trimmingCharacters(in: .whitespacesAndNewlines) }
        return String(data: data, encoding: .utf16LittleEndian)?.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private static func hexValue(_ byte: UInt8) -> UInt8? {
        switch byte {
        case 0x30...0x39: return byte - 0x30
        case 0x41...0x46: return byte - 0x41 + 10
        case 0x61...0x66: return byte - 0x61 + 10
        default: return nil
        }
    }

    public static func rejectIneligibleCodeAssistTier(_ root: JSONValue) throws {
        guard root.value(at: "currentTier") == nil else { return }
        if root.value(at: "paidTier.name")?.string?.isEmpty == false { return }
        let ineligible = root.value(at: "ineligibleTiers")?.array ?? []
        guard ineligible.contains(where: { ($0.value(at: "reasonCode")?.string ?? "") == "UNSUPPORTED_CLIENT" }) else { return }
        throw AIUsageMeterError.setupNeeded("Google no longer serves Code Assist to this account over Gemini CLI sign-in. Only Standard, Enterprise, and Workspace accounts remain supported.")
    }

    public struct JetBrainsQuota: Sendable {
        public let ide: String
        public let quotaInfo: String
        public let nextRefill: String?
    }

    public static func jetBrainsQuota(files: LocalFiles) throws -> JetBrainsQuota {
        let roots = ["Library/Application Support/JetBrains", "Library/Application Support/Google"]
        for root in roots {
            guard let ides = try? files.subdirectories(relativePath: root) else { continue }
            for ide in ides {
                let path = "\(root)/\(ide)/options/AIAssistantQuotaManager2.xml"
                guard let data = try? files.read(relativePath: path, maximumBytes: 1_000_000),
                      let attributes = XMLAttributeReader.attributes(in: data),
                      let quotaInfo = attributes["quotaInfo"], !quotaInfo.isEmpty else { continue }
                return JetBrainsQuota(ide: ide, quotaInfo: quotaInfo, nextRefill: attributes["nextRefill"])
            }
        }
        throw AIUsageMeterError.setupNeeded("No JetBrains IDE on this Mac has written an AI Assistant quota yet.")
    }

    public static func copilotToken(files: LocalFiles) throws -> String {
        let jsonCandidates = [".config/github-copilot/apps.json", ".config/github-copilot/hosts.json", "Library/Application Support/github-copilot/apps.json", "Library/Application Support/github-copilot/hosts.json"]
        for path in jsonCandidates {
            if let root = try? JSONValue.decode(files.read(relativePath: path, maximumBytes: 1_000_000)), let token = recursiveToken(root) { return token }
        }
        let yamlCandidates = [".config/gh/hosts.yml", "Library/Application Support/GitHub CLI/hosts.yml"]
        for path in yamlCandidates {
            guard let data = try? files.read(relativePath: path, maximumBytes: 1_000_000), let text = String(data: data, encoding: .utf8) else { continue }
            for line in text.split(separator: "\n") {
                let bits = line.split(separator: ":", maxSplits: 1).map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
                if bits.count == 2, ["oauth_token", "oauth-token"].contains(bits[0]), !bits[1].isEmpty { return bits[1].trimmingCharacters(in: CharacterSet(charactersIn: "\"'")) }
            }
        }
        throw AIUsageMeterError.setupNeeded("No existing GitHub Copilot or GitHub CLI token was found.")
    }

    private static func recursiveToken(_ root: JSONValue) -> String? {
        if let o = root.object {
            for key in ["oauth_token", "access_token", "token"] { if let value = o[key]?.string, !value.isEmpty { return value } }
            for value in o.values { if let found = recursiveToken(value) { return found } }
        }
        if let a = root.array { for value in a { if let found = recursiveToken(value) { return found } } }
        return nil
    }
}

enum XMLAttributeReader {
    static func attributes(in data: Data) -> [String: String]? {
        let collector = Collector()
        let parser = XMLParser(data: data)
        parser.shouldResolveExternalEntities = false
        parser.delegate = collector
        guard parser.parse() else { return nil }
        return collector.found
    }

    private final class Collector: NSObject, XMLParserDelegate {
        var found: [String: String] = [:]
        func parser(_ parser: XMLParser, didStartElement element: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) {
            for (key, value) in attributes { found[key] = value }
        }
    }
}
