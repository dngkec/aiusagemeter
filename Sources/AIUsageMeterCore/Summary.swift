import Foundation

/// What the menu bar reads across every subscription the user tracks.
///
/// The gauge shows the average, not the worst: one exhausted quota is a fact about that provider,
/// and letting it drive the icon made every subscription look spent. The peak and the exhausted
/// count travel alongside it so the detail is still one glance away in the menu and the tooltip.
public struct UsageSummary: Equatable, Sendable {
    public struct Reading: Equatable, Sendable {
        public var id: ProviderID
        public var name: String
        public var percent: Double

        public init(id: ProviderID, name: String, percent: Double) {
            self.id = id
            self.name = name
            self.percent = percent
        }
    }

    /// The mean of every reading that arrived, or nil while none has.
    public var average: Double?
    public var peak: Reading?
    public var trackedCount: Int
    public var readingCount: Int
    public var exhaustedCount: Int
    /// `.ready` once anything has a reading; otherwise what the tracked providers are waiting on.
    public var status: ProviderStatus

    public init(_ snapshots: [ProviderSnapshot]) {
        trackedCount = snapshots.count
        let readable = snapshots.filter { $0.status == .ready }
        readingCount = readable.count
        let percents = readable.map { Self.clamped($0.primaryPercent) }
        average = percents.isEmpty ? nil : percents.reduce(0, +) / Double(percents.count)
        exhaustedCount = percents.filter { $0 >= 100 }.count
        peak = readable.max { $0.primaryPercent < $1.primaryPercent }
            .map { Reading(id: $0.id, name: $0.name, percent: Self.clamped($0.primaryPercent)) }
        status = Self.pending(snapshots, hasReading: !percents.isEmpty)
    }

    /// A reading over its limit still counts as full: it must not pull the average past 100.
    private static func clamped(_ percent: Double) -> Double {
        guard percent.isFinite else { return 0 }
        return min(max(percent, 0), 100)
    }

    private static func pending(_ snapshots: [ProviderSnapshot], hasReading: Bool) -> ProviderStatus {
        if hasReading { return .ready }
        guard let first = snapshots.first else { return .setupNeeded }
        // A single provider still loading is worth saying so; anything else reports the first problem.
        if snapshots.contains(where: { $0.status == .loading }) { return .loading }
        return first.status
    }
}
