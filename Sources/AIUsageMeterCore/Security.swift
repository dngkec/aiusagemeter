import Foundation
import Security

public protocol SecretStore: Sendable {
    func read(_ account: String) throws -> String?
    func write(_ value: String?, account: String) throws
}

enum NonInteractiveKeychain {
    static func query(service: String, account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
            // Reads happen during automatic refreshes. A protected or stale
            // item must fail closed instead of putting up a password dialog.
            kSecUseAuthenticationUI as String: kSecUseAuthenticationUISkip,
        ]
    }

    static func read(service: String, account: String) throws -> Data? {
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query(service: service, account: account) as CFDictionary, &result)
        if status == errSecItemNotFound || status == errSecInteractionNotAllowed { return nil }
        guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
        return result as? Data
    }
}

public struct KeychainSecretStore: SecretStore {
    public static let defaultService = "app.aiusagemeter.AIUsageMeter"
    /// The service keys were stored under before the app was renamed to
    /// AIUsageMeter. Reads fall through to it so an existing install does not
    /// look signed out after the rename.
    public static let serviceBeforeRename = "app.usagemeter.UsageMeter"

    public let service: String
    /// Only the app's own service falls back to its old name; a caller that
    /// names a service means that one and nothing else.
    private let priorService: String?

    public init(service: String = KeychainSecretStore.defaultService) {
        self.service = service
        priorService = service == Self.defaultService ? Self.serviceBeforeRename : nil
    }

    public func read(_ account: String) throws -> String? {
        if let value = try Self.read(account, service: service) { return value }
        guard let priorService else { return nil }
        // Keep the rename fallback read-only. A nil current result can also mean the
        // item was protected and deliberately skipped, not that it is absent.
        return try Self.read(account, service: priorService)
    }

    public func write(_ value: String?, account: String) throws {
        let match: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        SecItemDelete(match as CFDictionary)
        // Drop the pre-rename item too, or it would shadow a cleared key.
        if let priorService {
            SecItemDelete([
                kSecClass as String: kSecClassGenericPassword,
                kSecAttrService as String: priorService,
                kSecAttrAccount as String: account,
            ] as CFDictionary)
        }
        guard let value, !value.isEmpty else { return }
        let add = match.merging([
            kSecValueData as String: Data(value.utf8),
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock,
        ]) { _, new in new }
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
    }

    private static func read(_ account: String, service: String) throws -> String? {
        guard let data = try NonInteractiveKeychain.read(service: service, account: account) else { return nil }
        return String(data: data, encoding: .utf8)
    }
}

public final class MemorySecretStore: SecretStore, @unchecked Sendable {
    private let lock = NSLock()
    private var values: [String: String]
    public init(_ values: [String: String] = [:]) { self.values = values }
    public func read(_ account: String) throws -> String? { lock.withLock { values[account] } }
    public func write(_ value: String?, account: String) throws { lock.withLock { values[account] = value } }
}

public protocol ExternalCredentials: Sendable {
    func credential(service: String) throws -> Data
}

public struct SystemExternalCredentials: ExternalCredentials {
    public init() {}
    public func credential(service: String) throws -> Data { try CredentialResolver.externalKeychain(service: service) }
}

public struct NoExternalCredentials: ExternalCredentials {
    public init() {}
    public func credential(service: String) throws -> Data {
        throw AIUsageMeterError.setupNeeded("\(service) is not signed in.")
    }
}

public protocol LocalFiles: Sendable {
    var homeDirectory: URL { get }
    func read(relativePath: String, maximumBytes: Int) throws -> Data
    /// Newest first: a provider that writes its own quota keeps it under a directory named for the app version.
    func subdirectories(relativePath: String) throws -> [String]
}

public struct DiskLocalFiles: LocalFiles {
    public let homeDirectory: URL
    public init(homeDirectory: URL = FileManager.default.homeDirectoryForCurrentUser) { self.homeDirectory = homeDirectory }
    public func read(relativePath: String, maximumBytes: Int = 2_000_000) throws -> Data {
        let url = try resolve(relativePath)
        let values = try url.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard values.isRegularFile == true, (values.fileSize ?? 0) <= maximumBytes else { throw AIUsageMeterError.oversizedResponse }
        return try Data(contentsOf: url, options: .mappedIfSafe)
    }

    public func subdirectories(relativePath: String) throws -> [String] {
        let root = try resolve(relativePath)
        let keys: [URLResourceKey] = [.isDirectoryKey, .contentModificationDateKey]
        let entries = try FileManager.default.contentsOfDirectory(at: root, includingPropertiesForKeys: keys, options: [.skipsHiddenFiles])
        return entries.compactMap { url -> (String, Date)? in
            guard let values = try? url.resourceValues(forKeys: Set(keys)), values.isDirectory == true else { return nil }
            return (url.lastPathComponent, values.contentModificationDate ?? .distantPast)
        }
        .sorted { $0.1 > $1.1 }
        .map(\.0)
    }

    private func resolve(_ relativePath: String) throws -> URL {
        let normalized = relativePath.hasPrefix("/") ? String(relativePath.dropFirst()) : relativePath
        let url = homeDirectory.appendingPathComponent(normalized).standardizedFileURL
        guard url.path.hasPrefix(homeDirectory.standardizedFileURL.path + "/") else { throw AIUsageMeterError.invalidURL("Credential path escaped the home directory.") }
        return url
    }
}

public struct MemoryLocalFiles: LocalFiles {
    public var homeDirectory = URL(fileURLWithPath: "/test-home")
    public var files: [String: Data]
    public var directories: [String: [String]]
    public init(_ strings: [String: String], directories: [String: [String]] = [:]) {
        files = strings.mapValues { Data($0.utf8) }
        self.directories = directories
    }
    public func read(relativePath: String, maximumBytes: Int) throws -> Data {
        guard let data = files[relativePath], data.count <= maximumBytes else { throw CocoaError(.fileReadNoSuchFile) }
        return data
    }
    public func subdirectories(relativePath: String) throws -> [String] {
        guard let names = directories[relativePath] else { throw CocoaError(.fileReadNoSuchFile) }
        return names
    }
}

extension NSLock {
    fileprivate func withLock<T>(_ body: () -> T) -> T { lock(); defer { unlock() }; return body() }
}
