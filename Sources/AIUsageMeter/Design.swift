import AIUsageMeterCore
import SwiftUI

enum Metrics {
    nonisolated(unsafe) static var scale: CGFloat = 1

    private static func s(_ value: CGFloat) -> CGFloat { (value * scale).rounded() }

    static var railWidth: CGFloat { s(72) }
    static var railCorner: CGFloat { s(44) }
    static var railPadding: CGFloat { s(18) }

    static var gauge: CGFloat { s(46) }
    static var gaugeRing: CGFloat { s(5) }
    static var glyph: CGFloat { s(21) }
    static var gaugeLabel: CGFloat { s(19) }
    static var gaugeLabelGap: CGFloat { s(8) }
    static var baseSpacing: CGFloat { s(24) }

    static var item: CGFloat { gauge + gaugeLabelGap + gaugeLabel }

    static var cardWidth: CGFloat { s(248) }
    static var cardCorner: CGFloat { s(22) }
    static var cardPaddingH: CGFloat { s(13) }
    static var cardPaddingV: CGFloat { s(14) }

    static var tailWidth: CGFloat { s(26) }
    static var tailHeight: CGFloat { s(30) }
    static var tailGap: CGFloat { s(12) }

    static var supportGap: CGFloat { s(11) }
    static var supportButton: CGFloat { s(22) }
    static var supportBlock: CGFloat { supportGap + hairline + supportButton }

    static var miniWidth: CGFloat { s(8) }
    static var miniHeight: CGFloat { s(52) }
    static var miniTarget: CGFloat { max(24, s(24)) }

    /// Not scaled: a hairline stays a hairline at every overlay size.
    static let hairline: CGFloat = 1

    static var barHeight: CGFloat { s(5) }
    static var rowLine: CGFloat { s(16) }
    static var rowMeta: CGFloat { s(14) }
    static var shadowSlack: CGFloat { s(22) }

    static var cardTrailingInset: CGFloat { railWidth + tailGap }
    static var panelWidth: CGFloat { cardTrailingInset + cardWidth + tailWidth + shadowSlack }

    static func itemSpacing(for count: Int) -> CGFloat {
        switch count {
        case ...4: return baseSpacing
        case 5...6: return s(16)
        case 7...9: return s(9)
        default: return s(6)
        }
    }

    static func railHeight(count: Int, spacing: CGFloat) -> CGFloat {
        let n = max(1, count)
        return CGFloat(n) * item + CGFloat(n - 1) * spacing + railPadding * 2 + supportBlock
    }
}

enum CardMetrics {
    private static func s(_ value: CGFloat) -> CGFloat { (value * Metrics.scale).rounded() }

    static var header: CGFloat { s(22) }
    static var headerGap: CGFloat { s(11) }
    static var row: CGFloat { s(46) }
    static var rowSpacing: CGFloat { s(11) }
    static var state: CGFloat { s(66) }
    static var footerLead: CGFloat { s(10) }
    static var footerTrail: CGFloat { s(9) }
    static var footer: CGFloat { s(30) }
    static let maximumRows = 3

    static func rowCount(_ snapshot: ProviderSnapshot) -> Int {
        min(maximumRows, max(1, snapshot.featuredWindows(limit: maximumRows).count))
    }

    static func height(for snapshot: ProviderSnapshot) -> CGFloat {
        let body: CGFloat
        if snapshot.status == .ready, !snapshot.windows.isEmpty {
            let n = CGFloat(rowCount(snapshot))
            body = n * row + (n - 1) * rowSpacing
        } else {
            body = state
        }
        return Metrics.cardPaddingV * 2 + header + headerGap + body + footerLead + 1 + footerTrail + footer
    }
}

enum Typo {
    private static func s(_ value: CGFloat) -> CGFloat { value * Metrics.scale }

    static var gaugeValue: Font { .system(size: s(16), weight: .semibold) }
    static var cardTitle: Font { .system(size: s(16), weight: .bold) }
    static var headerMeta: Font { .system(size: s(10)) }
    static var rowLabel: Font { .system(size: s(12.5), weight: .semibold) }
    static var rowMeta: Font { .system(size: s(10.5)) }
    static var rowValue: Font { .system(size: s(13), weight: .bold) }
    static var stateTitle: Font { .system(size: s(12.5), weight: .semibold) }
    static var stateBody: Font { .system(size: s(11.5)) }
    static var footerPrimary: Font { .system(size: s(11.5), weight: .semibold) }
    static var footerSecondary: Font { .system(size: s(10.5)) }
    static var action: Font { .system(size: s(10.5), weight: .medium) }
    static var actionGlyph: Font { .system(size: s(7.5), weight: .bold) }
    static var pin: Font { .system(size: s(8.5), weight: .semibold) }
    static var setup: Font { .system(size: s(11), weight: .medium) }
    static var support: Font { .system(size: s(11), weight: .semibold) }
    static var setupGlyph: Font { .system(size: s(15), weight: .semibold) }
}

enum Palette {
    static let surface = Color.black
    static let edge = Color.white.opacity(0.13)
    static let ringTrack = Color.white.opacity(0.21)
    static let barTrack = Color.white.opacity(0.19)
    static let primary = Color.white
    static let secondary = Color.white.opacity(0.55)
    static let tertiary = Color.white.opacity(0.38)
    static let divider = Color.white.opacity(0.10)
    static let dormant = Color.white.opacity(0.30)
    static let activeFill = Color.white.opacity(0.11)
    static let heart = Color.white.opacity(0.34)
    static let heartActive = Color(red: 1.000, green: 0.435, blue: 0.502)
    static let sponsor = Color(red: 1.000, green: 0.867, blue: 0.000)
}

enum Motion {
    static let openDelay = 30
    static let dismissDelay = 240

    static func reveal(_ reduced: Bool) -> Animation {
        reduced ? .easeOut(duration: 0.14) : .spring(response: 0.30, dampingFraction: 0.82)
    }

    static func geometry(_ reduced: Bool) -> Animation {
        reduced ? .easeOut(duration: 0.11) : .spring(response: 0.24, dampingFraction: 0.85)
    }

    static func value(_ reduced: Bool) -> Animation {
        reduced ? .easeOut(duration: 0.16) : .spring(response: 0.45, dampingFraction: 0.90)
    }

    static let sweep: Double = 0.9

    static var sweepCycle: Animation { .linear(duration: sweep).repeatForever(autoreverses: false) }
    static var sweepStop: Animation { .easeOut(duration: 0.18) }
}

extension Color {
    static func usage(percent: Double, status: ProviderStatus) -> Color {
        guard status == .ready else { return Palette.dormant }
        switch UsageColor.threshold(percent: percent) {
        case .green: return Color(red: 0.078, green: 1.000, blue: 0.592)
        case .yellow: return Color(red: 0.929, green: 1.000, blue: 0.020)
        case .orange: return Color(red: 1.000, green: 0.624, blue: 0.039)
        case .red: return Color(red: 1.000, green: 0.271, blue: 0.227)
        }
    }
}

extension ProviderStatus {
    var isReady: Bool { self == .ready }

    var shortLabel: String {
        switch self {
        case .ready: return "Ready"
        case .loading: return "Refreshing"
        case .setupNeeded: return "Setup needed"
        case .offline: return "Offline"
        case .unauthorized, .expired: return "Sign-in needed"
        case .rateLimited: return "Rate limited"
        case .error: return "Unavailable"
        }
    }

    var symbol: String {
        switch self {
        case .ready: return "checkmark.circle"
        case .loading: return "arrow.triangle.2.circlepath"
        case .setupNeeded: return "slider.horizontal.3"
        case .offline: return "wifi.slash"
        case .unauthorized, .expired: return "person.badge.key"
        case .rateLimited: return "hourglass"
        case .error: return "exclamationmark.triangle"
        }
    }
}
