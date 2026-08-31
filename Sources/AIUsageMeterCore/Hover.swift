import Foundation

/// Enter and exit arrive in either order and one can go missing, so the last gauge entered wins.
public struct HoverTracker: Equatable, Sendable {
    public enum Decision: Equatable, Sendable {
        case open(ProviderID)
        case keep
        case dismiss
    }

    private var gauges: [ProviderID] = []
    private var onRail = false
    private var onCard = false

    public init() {}

    public var target: ProviderID? { gauges.last }

    public var decision: Decision {
        if let target { return .open(target) }
        return onRail || onCard ? .keep : .dismiss
    }

    public var holdsOpen: Bool { decision != .dismiss }

    public mutating func gauge(_ id: ProviderID, inside: Bool) {
        gauges.removeAll { $0 == id }
        if inside {
            gauges.append(id)
            onRail = true
        }
    }

    public mutating func rail(_ inside: Bool) {
        onRail = inside
        if !inside { gauges.removeAll() }
    }

    public mutating func card(_ inside: Bool) { onCard = inside }

    public mutating func reset() {
        gauges.removeAll()
        onRail = false
        onCard = false
    }
}
