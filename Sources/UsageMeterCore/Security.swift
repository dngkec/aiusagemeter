import Foundation
import Security

public protocol SecretStore: Sendable {
    func read(_ account: String) throws -> String?
    func write(_ value: String?, account: String) throws
}

public struct KeychainSecretStore: SecretStore {
    public let service: String
    public init(service: String = "app.usagemeter.UsageMeter") { self.service = service }

    public func read(_ account: String) throws -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
        guard let data = result as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    public func write(_ value: String?, account: String) throws {
        let match: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
        SecItemDelete(match as CFDictionary)
        guard let value, !value.isEmpty else { return }
        let add = match.merging([
            kSecValueData as String: Data(value.utf8),
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock,
        ]) { _, new in new }
        let status = SecItemAdd(add as CFDictionary, nil)
        guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
    }
}

public final class MemorySecretStore: SecretStore, @unchecked Sendable {
    private let lock = NSLock()
    private var values: [String: String]
    public init(_ values: [String: String] = [:]) { self.values = values }
    public func read(_ account: String) throws -> String? { lock.withLock { values[account] } }
    public func write(_ value: String?, account: String) throws { lock.withLock { values[account] = value } }
}

/// A credential another application owns in the login keychain. Reading one can
/// raise a system prompt and returns a real secret, so it is injected rather
/// than reached for directly: nothing but the running app should touch it.
public protocol ExternalCredentials: Sendable {
    func credential(service: String) throws -> Data
}

public struct SystemExternalCredentials: ExternalCredentials {
    public init() {}
    public func credential(service: String) throws -> Data { try CredentialResolver.externalKeychain(service: service) }
}

/// Stands in for a machine where the other application is not signed in.
public struct NoExternalCredentials: ExternalCredentials {
    public init() {}
    public func credential(service: String) throws -> Data {
        throw UsageMeterError.setupNeeded("\(service) is not signed in.")
    }
}

public protocol LocalFiles: Sendable {
    var homeDirectory: URL { get }
    func read(relativePath: String, maximumBytes: Int) throws -> Data
    /// Immediate subdirectory names, newest first. Providers that write their
    /// own quota to disk keep it under a directory named for the app version,
    /// so the newest one is the reading that counts.
    func subdirectories(relativePath: String) throws -> [String]
}

public struct DiskLocalFiles: LocalFiles {
    public let homeDirectory: URL
    public init(homeDirectory: URL = FileManager.default.homeDirectoryForCurrentUser) { self.homeDirectory = homeDirectory }
    public func read(relativePath: String, maximumBytes: Int = 2_000_000) throws -> Data {
        let url = try resolve(relativePath)
        let values = try url.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
        guard values.isRegularFile == true, (values.fileSize ?? 0) <= maximumBytes else { throw UsageMeterError.oversizedResponse }
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
        guard url.path.hasPrefix(homeDirectory.standardizedFileURL.path + "/") else { throw UsageMeterError.invalidURL("Credential path escaped the home directory.") }
        return url
    }
}

public struct MemoryLocalFiles: LocalFiles {
    public var homeDirectory = URL(fileURLWithPath: "/test-home")
    public var files: [String: Data]
    /// Subdirectory names per parent path, in the order the disk store would
    /// return them: newest first.
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
