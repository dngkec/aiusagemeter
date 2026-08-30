import Foundation

public actor PreferencesStore {
    private let fileURL: URL
    private var cached: AppPreferences?

    public init(fileURL: URL? = nil) {
        if let fileURL { self.fileURL = fileURL }
        else {
            let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
            self.fileURL = base.appendingPathComponent("UsageMeter", isDirectory: true).appendingPathComponent("preferences.json")
        }
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
