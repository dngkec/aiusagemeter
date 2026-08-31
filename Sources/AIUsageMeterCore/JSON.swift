import Foundation

public enum JSONValue: Equatable, Sendable {
    case object([String: JSONValue]), array([JSONValue]), string(String), number(Double), bool(Bool), null

    public static func decode(_ data: Data) throws -> JSONValue {
        let raw = try JSONSerialization.jsonObject(with: data, options: [.fragmentsAllowed])
        return try convert(raw)
    }
    private static func convert(_ raw: Any) throws -> JSONValue {
        switch raw {
        case let value as [String: Any]: return .object(try value.mapValues(convert))
        case let value as [Any]: return .array(try value.map(convert))
        case let value as String: return .string(value)
        case let value as NSNumber:
            if CFGetTypeID(value) == CFBooleanGetTypeID() { return .bool(value.boolValue) }
            return .number(value.doubleValue)
        case is NSNull: return .null
        default: throw AIUsageMeterError.invalidResponse
        }
    }

    public func value(at path: String) -> JSONValue? {
        guard !path.isEmpty else { return self }
        var current: JSONValue? = self
        for part in path.split(separator: ".").map(String.init) {
            guard let node = current else { return nil }
            if case .object(let object) = node { current = object[part] }
            else if case .array(let array) = node, let index = Int(part), array.indices.contains(index) { current = array[index] }
            else { return nil }
        }
        return current
    }
    public var double: Double? {
        switch self {
        case .number(let n): return n
        case .string(let s): return Double(s)
        case .object(let o): return o["val"]?.double ?? o["value"]?.double ?? o["amount"]?.double
        default: return nil
        }
    }
    public var string: String? {
        switch self { case .string(let s): return s; case .number(let n): return String(n); default: return nil }
    }
    public var boolValue: Bool? {
        switch self {
        case .bool(let b): return b
        case .number(let n): return n != 0
        case .string(let s):
            switch s.lowercased() {
            case "true", "1", "yes": return true
            case "false", "0", "no": return false
            default: return nil
            }
        default: return nil
        }
    }
    public var object: [String: JSONValue]? { if case .object(let o) = self { return o }; return nil }
    public var array: [JSONValue]? { if case .array(let a) = self { return a }; return nil }

    /// Providers use null and absent interchangeably, so a null must not shadow a later candidate path.
    public var nonNull: JSONValue? { self == .null ? nil : self }
}

public enum DateParser {
    public static func parse(_ value: JSONValue?) -> Date? {
        guard let value else { return nil }
        if let seconds = value.double {
            let normalized = seconds > 10_000_000_000 ? seconds / 1_000 : seconds
            return Date(timeIntervalSince1970: normalized)
        }
        guard let raw = value.string else { return nil }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = iso.date(from: raw) { return date }
        iso.formatOptions = [.withInternetDateTime]
        if let date = iso.date(from: raw) { return date }
        // Copilot reports its reset as a bare UTC calendar day.
        return dayOnly.date(from: raw)
    }

    private static let dayOnly: DateFormatter = {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone(identifier: "UTC")
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter
    }()
}

public enum JSONPicking {
    public static func first(_ root: JSONValue, paths: [String]) -> JSONValue? {
        paths.lazy.compactMap { root.value(at: $0)?.nonNull }.first
    }
    public static func number(_ root: JSONValue, _ paths: [String]) -> Double? { first(root, paths: paths)?.double }
    public static func date(_ root: JSONValue, _ paths: [String]) -> Date? { DateParser.parse(first(root, paths: paths)) }
    public static func flag(_ root: JSONValue, _ paths: [String]) -> Bool? { first(root, paths: paths)?.boolValue }
}
