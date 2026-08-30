import AppKit
import UsageMeterCore
import SwiftUI

// MARK: - Root

struct NotchRootView: View {
    @ObservedObject var model: AppModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion

    /// Snapshot captures must be deterministic, so they render with motion off.
    private var still: Bool { ProcessInfo.processInfo.environment["USAGEMETER_SNAPSHOT_PATH"] != nil }
    private var reduced: Bool { still || reduceMotion }

    var body: some View {
        GeometryReader { proxy in
            let layout = NotchLayout(count: model.visibleSnapshots.count, available: proxy.size.height)
            ZStack(alignment: .trailing) {
                if model.idleMini {
                    MiniTab(reveal: model.revealFromMini)
                        .transition(.opacity)
                } else {
                    ProviderRail(model: model, layout: layout, reduced: reduced)
                        .transition(.scale(scale: 0.86, anchor: .trailing).combined(with: .opacity))

                    if let snapshot = model.selectedSnapshot {
                        card(snapshot: snapshot, layout: layout, size: proxy.size)
                    }
                }
            }
            .frame(width: proxy.size.width, height: proxy.size.height, alignment: .trailing)
        }
        .animation(still ? nil : Motion.reveal(reduced), value: model.idleMini)
        .animation(still ? nil : Motion.geometry(reduced), value: model.expandedProvider)
        // Keyed on the identities rather than the count, so a reorder made in
        // Settings slides into place instead of snapping.
        .animation(still ? nil : Motion.geometry(reduced), value: model.visibleSnapshots.map(\.id))
    }

    /// The card is placed, not padded. A padded frame would lay transparent
    /// card over the rail, and the rail would lose the hover that is holding
    /// the card open the very moment the card appeared.
    ///
    /// Its hover region then reaches back across the gap to touch the rail, so
    /// that a pointer travelling from a gauge to the card it opened is never
    /// over neither of them — the one thing that reads as the card flinching
    /// away from you.
    @ViewBuilder private func card(snapshot: ProviderSnapshot, layout: NotchLayout, size: CGSize) -> some View {
        let index = model.visibleSnapshots.firstIndex { $0.id == snapshot.id } ?? 0
        let placement = layout.card(height: CardMetrics.height(for: snapshot), index: index)
        let reach = Metrics.cardWidth + Metrics.tailWidth + Metrics.tailGap
        DetailCard(
            snapshot: snapshot,
            pinned: model.pinnedProvider == snapshot.id,
            tailCentre: placement.tailCentre,
            reduced: reduced,
            supportHovering: model.hoveredSupport == .card,
            openDashboard: { model.openDashboard(for: snapshot) },
            openSettings: { model.onOpenSettings?() },
            openSupport: { model.openSupport() },
            hoverSupport: { model.hoverSupport(.card, inside: $0) }
        )
        .frame(width: reach, alignment: .leading)
        .contentShape(Rectangle())
        .onHover { model.hoverCard($0) }
        .transition(.scale(scale: 0.92, anchor: .trailing).combined(with: .opacity))
        .position(x: size.width - Metrics.railWidth - reach / 2, y: size.height / 2 + placement.centre)
    }
}

/// Every vertical position in the overlay, derived once per render from the
/// panel height SwiftUI actually gave us.
struct NotchLayout {
    let count: Int
    let available: CGFloat
    let spacing: CGFloat
    let railHeight: CGFloat
    let scrolls: Bool

    init(count: Int, available: CGFloat) {
        let visible = max(1, count)
        self.count = visible
        self.available = available
        self.spacing = Metrics.itemSpacing(for: visible)
        let natural = Metrics.railHeight(count: visible, spacing: Metrics.itemSpacing(for: visible))
        self.scrolls = natural > available
        self.railHeight = min(natural, available)
    }

    var pitch: CGFloat { Metrics.item + spacing }

    /// Offset of a gauge centre from the middle of the panel.
    func gaugeCentre(index: Int) -> CGFloat {
        guard !scrolls else { return 0 }
        return -railHeight / 2 + Metrics.railPadding + CGFloat(index) * pitch + Metrics.gauge / 2
    }

    /// Where the card sits, and where its pointer must aim to still hit the gauge.
    func card(height: CGFloat, index: Int) -> (centre: CGFloat, tailCentre: CGFloat) {
        let gauge = gaugeCentre(index: index)
        let room = max(0, available / 2 - height / 2 - 6)
        let centre = min(max(gauge, -room), room)
        let ideal = height / 2 + (gauge - centre)
        let inset = Metrics.cardCorner + Metrics.tailHeight / 2
        return (centre, min(max(ideal, inset), height - inset))
    }
}

// MARK: - Rail

/// The rail silhouette. `overhang` runs the shape past the trailing edge, which
/// is flush with the side of the screen: the surface has no visible boundary
/// there, so the border shape is given the overhang and the fill is not, and the
/// hairline is drawn on the three edges that are a boundary and on no other.
struct RailShape: InsettableShape {
    var inset: CGFloat = 0
    var overhang: CGFloat = 0

    func path(in rect: CGRect) -> Path {
        let box = CGRect(
            x: rect.minX + inset,
            y: rect.minY + inset,
            width: rect.width - inset * 2 + overhang,
            height: rect.height - inset * 2
        )
        let radius = max(0, Metrics.railCorner - inset)
        return UnevenRoundedRectangle(topLeadingRadius: radius, bottomLeadingRadius: radius, style: .continuous)
            .path(in: box)
    }

    func inset(by amount: CGFloat) -> RailShape {
        RailShape(inset: inset + amount, overhang: overhang)
    }
}

struct ProviderRail: View {
    @ObservedObject var model: AppModel
    let layout: NotchLayout
    let reduced: Bool

    var body: some View {
        Group {
            if layout.scrolls {
                ScrollView(.vertical, showsIndicators: false) { column }
            } else {
                column
            }
        }
        .frame(width: Metrics.railWidth, height: layout.railHeight)
        .background {
            RailShape().fill(Palette.surface)
            // strokeBorder, not stroke: a centred stroke puts half the hairline
            // outside the fill, which both halves its weight and lays a
            // translucent fringe over the desktop for the shadow to blur.
            RailShape(overhang: Metrics.hairline * 2)
                .strokeBorder(Palette.edge, lineWidth: Metrics.hairline)
        }
        .compositingGroup()
        .shadow(color: .black.opacity(0.34), radius: Metrics.shadowSlack * 0.6, x: -4, y: 5)
        .onHover { model.railHover($0) }
        .accessibilityElement(children: .contain)
        .accessibilityLabel("UsageMeter usage rail")
    }

    /// Gauges, then the support footer under a hairline. The footer is measured
    /// into `Metrics.railHeight`, so the rail is exactly as tall as this comes
    /// out and the pointer aimed at a gauge still lands on it.
    private var column: some View {
        VStack(spacing: 0) {
            VStack(spacing: layout.spacing) {
                if model.visibleSnapshots.isEmpty {
                    SetupButton { model.onOpenSettings?() }
                } else {
                    ForEach(model.visibleSnapshots) { snapshot in
                        GaugeItem(
                            snapshot: snapshot,
                            active: model.expandedProvider == snapshot.id,
                            pinned: model.pinnedProvider == snapshot.id,
                            refreshing: model.isRefreshing(snapshot.id),
                            reduced: reduced,
                            action: { model.togglePin(snapshot.id) }
                        )
                        .onHover { model.hover(snapshot.id, inside: $0) }
                    }
                }
            }
            Spacer(minLength: 0).frame(height: Metrics.supportGap)
            Rectangle()
                .fill(Palette.divider)
                .frame(width: Metrics.railWidth * 0.44, height: Metrics.hairline)
            SupportHeart(
                hovering: model.hoveredSupport == .rail,
                reduced: reduced,
                onHover: { model.hoverSupport(.rail, inside: $0) },
                action: model.openSupport
            )
            .frame(width: Metrics.railWidth, height: Metrics.supportButton)
        }
        .padding(.vertical, Metrics.railPadding)
    }
}

/// The only place the app asks for anything. Dim until the pointer finds it,
/// warm once it has, and it opens a browser rather than anything in-app — so a
/// mis-click costs a glance, never a page of UI.
struct SupportHeart: View {
    let hovering: Bool
    var reduced: Bool = false
    let onHover: (Bool) -> Void
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: hovering ? "heart.fill" : "heart")
                .font(Typo.support)
                .foregroundStyle(hovering ? Palette.heartActive : Palette.heart)
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .scaleEffect(hovering && !reduced ? 1.14 : 1)
        .animation(reduced ? nil : .easeOut(duration: 0.14), value: hovering)
        .onHover(perform: onHover)
        .help("Support UsageMeter")
        .accessibilityLabel("Support UsageMeter")
        .accessibilityHint("Opens Buy Me a Coffee in your browser")
    }
}

struct SetupButton: View {
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: Metrics.gaugeLabelGap) {
                ZStack {
                    Circle().stroke(Palette.ringTrack, style: StrokeStyle(lineWidth: Metrics.gaugeRing, dash: [3, 7]))
                    Image(systemName: "plus").font(Typo.setupGlyph).foregroundStyle(Palette.primary)
                }
                .frame(width: Metrics.gauge, height: Metrics.gauge)
                Text("Set up")
                    .font(Typo.setup)
                    .foregroundStyle(Palette.secondary)
                    .frame(height: Metrics.gaugeLabel)
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Set up providers")
    }
}

struct GaugeItem: View {
    let snapshot: ProviderSnapshot
    let active: Bool
    let pinned: Bool
    let refreshing: Bool
    let reduced: Bool
    let action: () -> Void

    private var percent: Double { min(max(snapshot.primaryPercent, 0), 100) }
    private var tint: Color { Color.usage(percent: percent, status: snapshot.status) }

    var body: some View {
        Button(action: action) {
            VStack(spacing: Metrics.gaugeLabelGap) {
                UsageRing(percent: percent, tint: tint, glyph: snapshot.id.glyph, provider: snapshot.id, refreshing: refreshing, reduced: reduced)
                    .frame(width: Metrics.gauge, height: Metrics.gauge)
                    // Says which gauge the open card belongs to, from the rail's
                    // side of the gap as well as the pointer's.
                    .background { if active { Circle().fill(Palette.activeFill).padding(-Metrics.gaugeRing) } }
                Text(caption)
                    .font(Typo.gaugeValue)
                    .monospacedDigit()
                    .contentTransition(.numericText())
                    .foregroundStyle(captionTint)
                    .frame(height: Metrics.gaugeLabel)
            }
            .frame(width: Metrics.railWidth, height: Metrics.item)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .scaleEffect(active ? 1.05 : 1)
        .animation(reduced ? nil : Motion.geometry(reduced), value: active)
        .animation(reduced ? .none : Motion.value(reduced), value: percent)
        .accessibilityLabel(snapshot.name)
        .accessibilityValue(snapshot.status.isReady ? "\(Int(percent.rounded())) percent used" : snapshot.status.shortLabel)
        .accessibilityHint(pinned ? "Unpin details" : "Keep details open")
        .accessibilityAddTraits(pinned ? [.isButton, .isSelected] : .isButton)
    }

    private var caption: String {
        snapshot.status.isReady ? "\(Int(percent.rounded()))%" : "—"
    }

    private var captionTint: Color {
        guard snapshot.status.isReady else { return Palette.tertiary }
        return active ? Palette.primary : Palette.primary.opacity(0.86)
    }
}

/// Track, value arc, and — while a refresh is in flight — the white sweep that
/// the value arc dims behind.
struct UsageRing: View {
    let percent: Double
    let tint: Color
    let glyph: ProviderGlyph
    var provider: ProviderID?
    let refreshing: Bool
    let reduced: Bool

    var body: some View {
        ZStack {
            Circle()
                .stroke(Palette.ringTrack, lineWidth: Metrics.gaugeRing)

            Circle()
                .trim(from: 0, to: percent / 100)
                .stroke(tint, style: StrokeStyle(lineWidth: Metrics.gaugeRing, lineCap: .round))
                .rotationEffect(.degrees(-90))
                .opacity(refreshing ? (reduced ? 0.55 : 0.42) : 1)

            if refreshing, !reduced {
                TimelineView(.animation) { context in
                    Circle()
                        .trim(from: 0, to: 0.25)
                        .stroke(Palette.primary, style: StrokeStyle(lineWidth: Metrics.gaugeRing, lineCap: .round))
                        .rotationEffect(.degrees(Motion.sweepAngle(at: context.date)))
                }
            }

            GlyphView(glyph: glyph, provider: provider, size: Metrics.glyph, color: Palette.primary)
        }
        .padding(Metrics.gaugeRing / 2)
    }
}

// MARK: - Card

/// Card and pointer as one silhouette, so a single fill, one hairline, and one
/// shadow wrap both and no seam shows where they meet.
struct CardShape: InsettableShape {
    /// Centre of the pointer, measured down from the top of the card.
    let tailCentre: CGFloat
    /// Set by `inset(by:)`, so `strokeBorder` can trace the silhouette from
    /// inside the fill instead of straddling it.
    var inset: CGFloat = 0

    func inset(by amount: CGFloat) -> CardShape {
        CardShape(tailCentre: tailCentre, inset: inset + amount)
    }

    func path(in rect: CGRect) -> Path {
        let rect = rect.insetBy(dx: inset, dy: inset)
        let radius = max(0, min(Metrics.cardCorner - inset, min(rect.width - Metrics.tailWidth, rect.height) / 2))
        let bodyMaxX = rect.maxX - Metrics.tailWidth
        let half = max(0, Metrics.tailHeight / 2 - inset)
        // Measured from the un-inset top, so an inset outline still points at
        // the same gauge the fill does.
        let aim = min(max(tailCentre - inset, radius + half), rect.height - radius - half)
        let centre = rect.minY + aim
        let tip = Metrics.tailHeight * 0.11

        var path = Path()
        path.move(to: CGPoint(x: rect.minX + radius, y: rect.minY))
        path.addLine(to: CGPoint(x: bodyMaxX - radius, y: rect.minY))
        path.addArc(center: CGPoint(x: bodyMaxX - radius, y: rect.minY + radius), radius: radius,
                    startAngle: .degrees(-90), endAngle: .degrees(0), clockwise: false)
        path.addLine(to: CGPoint(x: bodyMaxX, y: centre - half))
        path.addLine(to: CGPoint(x: rect.maxX - tip * 1.7, y: centre - tip * 0.85))
        path.addQuadCurve(to: CGPoint(x: rect.maxX - tip * 1.7, y: centre + tip * 0.85),
                          control: CGPoint(x: rect.maxX, y: centre))
        path.addLine(to: CGPoint(x: bodyMaxX, y: centre + half))
        path.addLine(to: CGPoint(x: bodyMaxX, y: rect.maxY - radius))
        path.addArc(center: CGPoint(x: bodyMaxX - radius, y: rect.maxY - radius), radius: radius,
                    startAngle: .degrees(0), endAngle: .degrees(90), clockwise: false)
        path.addLine(to: CGPoint(x: rect.minX + radius, y: rect.maxY))
        path.addArc(center: CGPoint(x: rect.minX + radius, y: rect.maxY - radius), radius: radius,
                    startAngle: .degrees(90), endAngle: .degrees(180), clockwise: false)
        path.addLine(to: CGPoint(x: rect.minX, y: rect.minY + radius))
        path.addArc(center: CGPoint(x: rect.minX + radius, y: rect.minY + radius), radius: radius,
                    startAngle: .degrees(180), endAngle: .degrees(270), clockwise: false)
        path.closeSubpath()
        return path
    }
}

struct DetailCard: View {
    let snapshot: ProviderSnapshot
    let pinned: Bool
    let tailCentre: CGFloat
    var reduced: Bool = false
    var supportHovering: Bool = false
    let openDashboard: () -> Void
    let openSettings: () -> Void
    let openSupport: () -> Void
    let hoverSupport: (Bool) -> Void

    private var height: CGFloat { CardMetrics.height(for: snapshot) }

    var body: some View {
        let shape = CardShape(tailCentre: tailCentre)
        ZStack(alignment: .topLeading) {
            shape.fill(Palette.surface)
            shape.strokeBorder(Palette.edge, lineWidth: Metrics.hairline)
            CardContent(
                snapshot: snapshot,
                pinned: pinned,
                reduced: reduced,
                supportHovering: supportHovering,
                openDashboard: openDashboard,
                openSettings: openSettings,
                openSupport: openSupport,
                hoverSupport: hoverSupport
            )
                .frame(width: Metrics.cardWidth, height: height)
                .id(snapshot.id)
                .transition(.opacity)
        }
        .frame(width: Metrics.cardWidth + Metrics.tailWidth, height: height)
        .compositingGroup()
        .shadow(color: .black.opacity(0.45), radius: Metrics.shadowSlack * 0.8, x: -5, y: 8)
        .accessibilityElement(children: .contain)
    }
}

struct CardContent: View {
    let snapshot: ProviderSnapshot
    let pinned: Bool
    var reduced: Bool = false
    var supportHovering: Bool = false
    let openDashboard: () -> Void
    let openSettings: () -> Void
    let openSupport: () -> Void
    let hoverSupport: (Bool) -> Void

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            header
                .frame(height: CardMetrics.header)
            Spacer(minLength: 0).frame(height: CardMetrics.headerGap)

            if snapshot.status.isReady, !snapshot.windows.isEmpty {
                VStack(spacing: CardMetrics.rowSpacing) {
                    ForEach(snapshot.featuredWindows(limit: CardMetrics.maximumRows)) { window in
                        UsageRow(window: window)
                    }
                }
            } else {
                CardState(snapshot: snapshot)
                    .frame(height: CardMetrics.state, alignment: .top)
            }

            Spacer(minLength: CardMetrics.footerLead)
            Rectangle().fill(Palette.divider).frame(height: 1)
            Spacer(minLength: 0).frame(height: CardMetrics.footerTrail)
            footer
                .frame(height: CardMetrics.footer)
        }
        .padding(.horizontal, Metrics.cardPaddingH)
        .padding(.vertical, Metrics.cardPaddingV)
    }

    private var header: some View {
        HStack(spacing: 9) {
            GlyphView(glyph: snapshot.id.glyph, provider: snapshot.id, size: Metrics.glyph * 0.9, color: Palette.primary)
            Text(snapshot.name)
                .font(Typo.cardTitle)
                .foregroundStyle(Palette.primary)
                .lineLimit(1)
                .minimumScaleFactor(0.8)
            Spacer(minLength: 6)
            if pinned {
                Image(systemName: "pin.fill")
                    .font(Typo.pin)
                    .foregroundStyle(Palette.tertiary)
            }
            Text(RelativeTime.short(snapshot.updatedAt))
                .font(Typo.headerMeta)
                .foregroundStyle(Palette.tertiary)
                .fixedSize()
        }
    }

    private var footer: some View {
        VStack(alignment: .leading, spacing: 3) {
            HStack(spacing: 6) {
                Text(snapshot.source.rawValue)
                    .font(Typo.footerPrimary)
                    .foregroundStyle(Palette.primary.opacity(0.82))
                Spacer(minLength: 4)
                Text(snapshot.status.shortLabel)
                    .font(Typo.footerPrimary)
                    .foregroundStyle(statusTint)
            }
            HStack(spacing: 6) {
                Text(subtitle)
                    .font(Typo.footerSecondary)
                    .foregroundStyle(Palette.tertiary)
                    .lineLimit(1)
                    .truncationMode(.tail)
                Spacer(minLength: 4)
                SupportHeart(hovering: supportHovering, reduced: reduced, onHover: hoverSupport, action: openSupport)
                    .frame(width: Metrics.rowLine, height: Metrics.rowMeta)
                if snapshot.dashboardURL != nil {
                    CardAction(title: "Dashboard", symbol: "arrow.up.right", action: openDashboard)
                        .accessibilityLabel("Open \(snapshot.name) dashboard")
                } else if needsAttention {
                    CardAction(title: "Settings", symbol: "chevron.right", action: openSettings)
                        .accessibilityLabel("Open UsageMeter settings")
                }
            }
        }
    }

    private var statusTint: Color {
        switch snapshot.status {
        case .ready: return Color.usage(percent: 0, status: .ready)
        case .loading: return Palette.secondary
        case .rateLimited, .setupNeeded: return Color.usage(percent: 75, status: .ready)
        default: return Color.usage(percent: 95, status: .ready)
        }
    }

    private var needsAttention: Bool {
        [.setupNeeded, .unauthorized, .expired].contains(snapshot.status)
    }

    /// The state block already carries the failure message, so the footer keeps
    /// to provenance instead of repeating it.
    private var subtitle: String {
        if snapshot.source == .demo { return "Deterministic sample data" }
        return snapshot.windows.count > CardMetrics.maximumRows
            ? "\(snapshot.windows.count) usage windows"
            : "Updated \(RelativeTime.clock(snapshot.updatedAt))"
    }
}

/// Name and reading on one line, the bar under them, and the reset underneath
/// in the quiet voice — so the eye lands on the number it came for.
struct UsageRow: View {
    let window: UsageWindow

    private var tint: Color { Color.usage(percent: window.percent, status: .ready) }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack(alignment: .firstTextBaseline, spacing: 8) {
                Text(window.label)
                    .font(Typo.rowLabel)
                    .foregroundStyle(Palette.primary)
                    .lineLimit(1)
                Spacer(minLength: 4)
                Text(window.readingCaption)
                    .font(Typo.rowValue)
                    .monospacedDigit()
                    .contentTransition(.numericText())
                    .foregroundStyle(tint)
                    .lineLimit(1)
                    .minimumScaleFactor(0.72)
                    .fixedSize(horizontal: true, vertical: false)
            }
            .frame(height: Metrics.rowLine)

            Spacer(minLength: 0)

            GeometryReader { proxy in
                ZStack(alignment: .leading) {
                    Capsule().fill(Palette.barTrack)
                    Capsule()
                        .fill(tint)
                        .frame(width: max(Metrics.barHeight, proxy.size.width * min(window.fraction, 1)))
                }
            }
            .frame(height: Metrics.barHeight)

            Spacer(minLength: 0)

            Text(reset)
                .font(Typo.rowMeta)
                .foregroundStyle(Palette.secondary)
                .lineLimit(1)
                .frame(height: Metrics.rowMeta, alignment: .center)
        }
        .frame(height: CardMetrics.row)
        .accessibilityElement(children: .ignore)
        .accessibilityLabel(window.label)
        .accessibilityValue("\(window.readingCaption) used. \(reset)")
    }

    private var reset: String {
        if let resetsAt = window.resetsAt { return RelativeTime.reset(resetsAt) }
        return window.remainingCaption ?? "No reset scheduled"
    }
}

struct CardState: View {
    let snapshot: ProviderSnapshot

    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Label(snapshot.status.shortLabel, systemImage: snapshot.status.symbol)
                .font(Typo.stateTitle)
                .foregroundStyle(Palette.primary)
            Text(snapshot.message ?? "Usage is temporarily unavailable.")
                .font(Typo.stateBody)
                .foregroundStyle(Palette.secondary)
                .fixedSize(horizontal: false, vertical: true)
                .lineLimit(3)
            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

// MARK: - Idle tab

struct MiniTab: View {
    let reveal: () -> Void

    var body: some View {
        UnevenRoundedRectangle(
            topLeadingRadius: Metrics.miniWidth / 2,
            bottomLeadingRadius: Metrics.miniWidth / 2,
            style: .continuous
        )
        .fill(Palette.surface)
        .frame(width: Metrics.miniWidth, height: Metrics.miniHeight)
        .shadow(color: .black.opacity(0.25), radius: 6, x: -2, y: 2)
        .frame(width: Metrics.miniTarget, height: Metrics.miniHeight + 26, alignment: .trailing)
        .contentShape(Rectangle())
        .onHover { if $0 { reveal() } }
        .onTapGesture(perform: reveal)
        .accessibilityLabel("Show UsageMeter")
        .accessibilityAddTraits(.isButton)
    }
}

// MARK: - Formatting

enum RelativeTime {
    static func short(_ date: Date, now: Date = Date()) -> String {
        let delta = now.timeIntervalSince(date)
        if delta < 45 { return "just now" }
        if delta < 3600 { return "\(Int(delta / 60)) min ago" }
        if delta < 86_400 { return "\(Int(delta / 3600)) h ago" }
        return "\(Int(delta / 86_400)) d ago"
    }

    static func clock(_ date: Date) -> String {
        date.formatted(.dateTime.hour().minute())
    }

    static func reset(_ date: Date, now: Date = Date()) -> String {
        let delta = date.timeIntervalSince(now)
        if delta <= 0 { return "Resetting…" }
        if delta < 3600 { return "Resets in \(max(1, Int(delta / 60))) min" }
        if delta < 12 * 3600 { return "Resets \(clock(date))" }
        return "Resets \(date.formatted(.dateTime.weekday(.abbreviated).hour().minute()))"
    }
}

struct CardAction: View {
    let title: String
    let symbol: String
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 2) {
                Text(title).font(Typo.action)
                Image(systemName: symbol).font(Typo.actionGlyph)
            }
            .foregroundStyle(Palette.secondary)
        }
        .buttonStyle(.plain)
        .fixedSize()
    }
}
