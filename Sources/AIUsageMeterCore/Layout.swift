import Foundation

public struct Rect: Equatable, Sendable {
    public var x: Double, y: Double, width: Double, height: Double
    public init(x: Double, y: Double, width: Double, height: Double) { self.x = x; self.y = y; self.width = width; self.height = height }
    public var maxX: Double { x + width }
    public var maxY: Double { y + height }
    public var midY: Double { y + height / 2 }
}

public enum OverlaySize: String, Codable, Sendable, CaseIterable {
    case small, medium, large

    public var scale: Double {
        switch self {
        case .small: return 0.86
        case .medium: return 1.0
        case .large: return 1.18
        }
    }

    public var label: String {
        switch self {
        case .small: return "Small"
        case .medium: return "Medium"
        case .large: return "Large"
        }
    }
}

public enum OverlayLayout {
    public static let collapsedWidth = 72.0
    public static let expandedWidth = 358.0

    public static func frame(visibleScreen: Rect, contentHeight: Double, expanded: Bool, position: VerticalPosition, offset: Double = 0) -> Rect {
        let width = expanded ? expandedWidth : collapsedWidth
        let height = min(max(116, contentHeight), visibleScreen.height)
        let y = origin(visibleScreen: visibleScreen, height: height, position: position, offset: offset)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: height)
    }

    public static func panelFrame(visibleScreen: Rect, width: Double, railHeight: Double, panelHeight: Double, position: VerticalPosition, offset: Double = 0) -> Rect {
        let rail = frame(visibleScreen: visibleScreen, contentHeight: railHeight, expanded: true, position: position, offset: offset)
        let height = min(max(panelHeight, rail.height), visibleScreen.height)
        let y = min(max(visibleScreen.y, rail.midY - height / 2), visibleScreen.maxY - height)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: height)
    }

    public static func miniFrame(visibleScreen: Rect, width: Double, height: Double, railHeight: Double, position: VerticalPosition, offset: Double = 0) -> Rect {
        let rail = frame(visibleScreen: visibleScreen, contentHeight: railHeight, expanded: false, position: position, offset: offset)
        let tab = min(height, visibleScreen.height)
        let y = min(max(visibleScreen.y, rail.midY - tab / 2), visibleScreen.maxY - tab)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: tab)
    }

    /// Where the detail card sits, given the gauge it belongs to.
    ///
    /// `gaugeCentre` and the returned `centre` are offsets from the middle of the panel; `tailCentre`
    /// is measured down from the top of the card. The card is kept inside the panel, and the tail then
    /// takes up whatever slack that clamping introduced so it still points at its own gauge.
    public static func cardPlacement(gaugeCentre: Double, cardHeight: Double, available: Double, tailInset: Double, margin: Double = 6) -> (centre: Double, tailCentre: Double) {
        let room = max(0, available / 2 - cardHeight / 2 - margin)
        let centre = min(max(gaugeCentre, -room), room)
        let ideal = cardHeight / 2 + (gaugeCentre - centre)
        let lowest = min(tailInset, cardHeight / 2)
        let highest = max(cardHeight - tailInset, lowest)
        return (centre, min(max(ideal, lowest), highest))
    }

    private static func origin(visibleScreen: Rect, height: Double, position: VerticalPosition, offset: Double) -> Double {
        let base: Double
        switch position {
        case .top: base = visibleScreen.maxY - height - 40
        case .center: base = visibleScreen.y + (visibleScreen.height - height) / 2
        case .bottom: base = visibleScreen.y + 40
        }
        return min(max(visibleScreen.y, base + offset), visibleScreen.maxY - height)
    }
}
