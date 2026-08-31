import Foundation

public actor PreferencesStore {
    private let fileURL: URL
    private var cached: AppPreferences?

    public init(fileURL: URL? = nil) {
        if let fileURL { self.fileURL = fileURL }
        else {
            let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            let directory = base.appendingPathComponent("AIUsageMeter", isDirectory: true)
            Self.carryForward(from: base.appendingPathComponent("UsageMeter", isDirectory: true), to: directory)
            self.fileURL = directory.appendingPathComponent("preferences.json")
        }
    }

    /// Preferences lived under the app's old name before it was renamed to
    /// AIUsageMeter. Move that folder across once so an existing install keeps
    /// its providers instead of starting from defaults. A folder already at the
    /// new name wins, and a move that fails just leaves the old one in place —
    /// `load()` then falls back to defaults rather than failing.
    static func carryForward(from old: URL, to new: URL) {
        let files = FileManager.default
        guard files.fileExists(atPath: old.path), !files.fileExists(atPath: new.path) else { return }
        try? files.moveItem(at: old, to: new)
    }

    public func load() -> AppPreferences {
        if let cached { return cached }
        guard let data = try? Data(contentsOf: fileURL), var decoded = try? JSONDecoder.quota.decode(AppPreferences.self, from: data) else {
            let defaults = AppPreferences(); cached = defaults; return defaults
        }
        decoded = Self.migrate(decoded)
        cached = decoded
        return decoded
    }

    public func save(_ preferences: AppPreferences) throws {
        var value = Self.migrate(preferences)
        value.schemaVersion = AppPreferences.currentSchemaVersion
        try FileManager.default.createDirectory(at: fileURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let data = try JSONEncoder.quota.encode(value)
        try data.write(to: fileURL, options: [.atomic, .completeFileProtectionUnlessOpen])
        cached = value
    }

    public static func migrate(_ input: AppPreferences) -> AppPreferences {
        var value = input
        var seen = Set(value.providers.map(\.id))
        for id in ProviderID.allCases where !seen.contains(id) {
            value.providers.append(ProviderConfiguration(id: id))
            seen.insert(id)
        }
        value.providers = value.providers.uniqued(by: \.id)
        value.refreshInterval = min(max(value.refreshInterval, 30), 86_400)
        value.verticalOffset = min(max(value.verticalOffset, -2_000), 2_000)
        value.schemaVersion = AppPreferences.currentSchemaVersion
        return value
    }
}

private extension Array {
    func uniqued<Key: Hashable>(by keyPath: KeyPath<Element, Key>) -> [Element] {
        var seen = Set<Key>()
        return filter { seen.insert($0[keyPath: keyPath]).inserted }
    }
}

extension JSONEncoder {
    static var quota: JSONEncoder { let e = JSONEncoder(); e.outputFormatting = [.prettyPrinted, .sortedKeys]; e.dateEncodingStrategy = .iso8601; return e }
}
extension JSONDecoder {
    static var quota: JSONDecoder { let d = JSONDecoder(); d.dateDecodingStrategy = .iso8601; return d }
}
