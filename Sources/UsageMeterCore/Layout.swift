import Foundation

public struct Rect: Equatable, Sendable {
    public var x: Double, y: Double, width: Double, height: Double
    public init(x: Double, y: Double, width: Double, height: Double) { self.x = x; self.y = y; self.width = width; self.height = height }
    public var maxX: Double { x + width }
    public var maxY: Double { y + height }
    public var midY: Double { y + height / 2 }
}

/// How big the overlay is drawn. The view layer multiplies every measurement by
/// the chosen scale, so one preference moves geometry and type together.
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

/// Where the overlay sits on a display. Widths are supplied by the view layer
/// because they depend on the size preference; this type owns only placement.
public enum OverlayLayout {
    /// Reference widths at `OverlaySize.medium`, for callers that only need a
    /// sense of scale rather than the live measurement.
    public static let collapsedWidth = 72.0
    public static let expandedWidth = 358.0

    /// Where the rail sits, honouring the vertical preference and never leaving
    /// the visible frame.
    public static func frame(visibleScreen: Rect, contentHeight: Double, expanded: Bool, position: VerticalPosition, offset: Double = 0) -> Rect {
        let width = expanded ? expandedWidth : collapsedWidth
        let height = min(max(116, contentHeight), visibleScreen.height)
        let y = origin(visibleScreen: visibleScreen, height: height, position: position, offset: offset)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: height)
    }

    /// The overlay window. It wraps the rail symmetrically so a card anchored to
    /// the first or last gauge still has room, which is what lets the window be
    /// sized once per session instead of resized under an animation.
    public static func panelFrame(visibleScreen: Rect, width: Double, railHeight: Double, panelHeight: Double, position: VerticalPosition, offset: Double = 0) -> Rect {
        let rail = frame(visibleScreen: visibleScreen, contentHeight: railHeight, expanded: true, position: position, offset: offset)
        let height = min(max(panelHeight, rail.height), visibleScreen.height)
        let y = min(max(visibleScreen.y, rail.midY - height / 2), visibleScreen.maxY - height)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: height)
    }

    /// The resting tab, centred on wherever the rail would have been.
    public static func miniFrame(visibleScreen: Rect, width: Double, height: Double, railHeight: Double, position: VerticalPosition, offset: Double = 0) -> Rect {
        let rail = frame(visibleScreen: visibleScreen, contentHeight: railHeight, expanded: false, position: position, offset: offset)
        let tab = min(height, visibleScreen.height)
        let y = min(max(visibleScreen.y, rail.midY - tab / 2), visibleScreen.maxY - tab)
        return Rect(x: visibleScreen.maxX - width, y: y, width: width, height: tab)
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
