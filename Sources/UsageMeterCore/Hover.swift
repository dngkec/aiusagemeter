import Foundation

/// The rail's pointer state machine.
///
/// Enter and exit arrive in either order — and one of the pair goes missing
/// altogether when the pointer crosses quickly — so hover is tracked as an
/// ordered list whose last entry wins, rather than as a set whose first element
/// is whichever one hashing happened to put there. Keeping the rule here, free
/// of AppKit, is what lets open/keep/dismiss be tested directly.
public struct HoverTracker: Equatable, Sendable {
    public enum Decision: Equatable, Sendable {
        /// Open, or move to, this gauge's card.
        case open(ProviderID)
        /// The pointer is still somewhere that owns the card; leave it alone.
        case keep
        /// Nothing is under the pointer, so start the dismissal grace period.
        case dismiss
    }

    private var gauges: [ProviderID] = []
    private var onRail = false
    private var onCard = false

    public init() {}

    /// The gauge the card should be showing, if any.
    public var target: ProviderID? { gauges.last }

    public var decision: Decision {
        if let target { return .open(target) }
        return onRail || onCard ? .keep : .dismiss
    }

    /// True while the pointer is anywhere that must hold a card open.
    public var holdsOpen: Bool { decision != .dismiss }

    public mutating func gauge(_ id: ProviderID, inside: Bool) {
        gauges.removeAll { $0 == id }
        if inside {
            gauges.append(id)
            onRail = true
        }
    }

    /// The gaps between gauges belong to the rail, so crossing one keeps the
    /// open card rather than closing and reopening it under the pointer.
    public mutating func rail(_ inside: Bool) {
        onRail = inside
        if !inside { gauges.removeAll() }
    }

    /// The card holds itself open, so its links stay reachable.
    public mutating func card(_ inside: Bool) { onCard = inside }

    /// A dropped exit would otherwise strand a card on screen, so the app can
    /// always state outright that the pointer has gone.
    public mutating func reset() {
        gauges.removeAll()
        onRail = false
        onCard = false
    }
}
