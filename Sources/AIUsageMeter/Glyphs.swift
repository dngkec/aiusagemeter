import AIUsageMeterCore
import CoreGraphics
import SwiftUI

enum ProviderGlyph {
    case burst
    case knot
    case cube
    case spark
    case slash
    case visor
    case symbol(String)
    case monogram(String)
}

extension ProviderID {
    var glyph: ProviderGlyph {
        switch self {
        case .claude: return .burst
        case .anthropicCost: return .symbol("dollarsign.circle")
        case .codex, .openAIAPI: return .knot
        case .cursor: return .cube
        case .grok: return .slash
        case .copilot: return .visor
        case .gemini: return .spark
        case .kimi: return .monogram("K")
        case .openRouter: return .symbol("arrow.triangle.branch")
        case .deepSeek: return .symbol("water.waves")
        case .mistral: return .symbol("wind")
        case .xaiAPI: return .symbol("dollarsign.circle")
        case .moonshot: return .symbol("moon")
        case .perplexity: return .symbol("magnifyingglass")
        case .windsurf: return .symbol("sailboat")
        case .zai: return .monogram("Z")
        case .openCode: return .symbol("chevron.left.forwardslash.chevron.right")
        case .localModels: return .symbol("cpu")
        case .jetBrainsAI: return .monogram("JB")
        case .warp: return .symbol("terminal")
        case .amp: return .symbol("bolt")
        case .kilo: return .monogram("Ki")
        case .augment: return .symbol("wand.and.stars")
        case .devin: return .symbol("figure.mind.and.body")
        case .antigravity: return .symbol("arrow.up.forward.circle")
        case .custom: return .symbol("puzzlepiece.extension")
        }
    }
}

struct GlyphView: View {
    let glyph: ProviderGlyph
    var provider: ProviderID?
    var size: CGFloat = Metrics.glyph
    var color: Color = .white

    var body: some View {
        Group {
            if let provider, let mark = ProviderMarks.mark(for: provider) {
                Image(nsImage: mark.image)
                    .resizable()
                    .renderingMode(mark.template ? .template : .original)
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
                    .foregroundStyle(color)
            } else {
                drawn
            }
        }
        .frame(width: size, height: size)
    }

    @ViewBuilder private var drawn: some View {
        Group {
            switch glyph {
            case .burst:
                BurstShape().fill(color)
            case .knot:
                KnotShape().stroke(color, style: StrokeStyle(lineWidth: size * 0.100, lineCap: .round, lineJoin: .round))
            case .cube:
                CubeMark(color: color)
            case .spark:
                SparkShape().fill(color)
            case .slash:
                SlashShape().fill(color)
            case .visor:
                VisorShape().fill(color, style: FillStyle(eoFill: true))
            case .symbol(let name):
                Image(systemName: name)
                    .font(.system(size: size * 0.86, weight: .semibold))
                    .foregroundStyle(color)
            case .monogram(let text):
                Text(text)
                    .font(.system(size: size * (text.count > 1 ? 0.52 : 0.82), weight: .bold, design: .rounded))
                    .foregroundStyle(color)
            }
        }
    }
}

struct BurstShape: Shape {
    var rays = 16

    func path(in rect: CGRect) -> Path {
        let centre = CGPoint(x: rect.midX, y: rect.midY)
        let radius = min(rect.width, rect.height) / 2
        let inner = radius * 0.19
        let half = radius * 0.036
        var path = Path()
        for index in 0..<rays {
            let angle = Double(index) / Double(rays) * 2 * .pi
            let along = CGVector(dx: CoreGraphics.cos(angle), dy: CoreGraphics.sin(angle))
            let across = CGVector(dx: -CoreGraphics.sin(angle), dy: CoreGraphics.cos(angle))
            func point(_ radial: CGFloat, _ lateral: CGFloat) -> CGPoint {
                CGPoint(x: centre.x + radial * along.dx + lateral * across.dx,
                        y: centre.y + radial * along.dy + lateral * across.dy)
            }
            path.move(to: point(inner, -half))
            path.addLine(to: point(radius, 0))
            path.addLine(to: point(inner, half))
            path.closeSubpath()
        }
        path.addEllipse(in: CGRect(x: centre.x - radius * 0.165, y: centre.y - radius * 0.165,
                                   width: radius * 0.33, height: radius * 0.33))
        return path
    }
}

struct KnotShape: Shape {
    func path(in rect: CGRect) -> Path {
        let centre = CGPoint(x: rect.midX, y: rect.midY)
        let radius = min(rect.width, rect.height) / 2 * 0.96
        let offset = radius * 0.634
        let lobe = radius * 0.366
        var path = Path()
        for index in 0..<6 {
            let angle = Double(index) / 6 * 2 * .pi - .pi / 2
            let origin = CGPoint(x: centre.x + offset * CoreGraphics.cos(angle),
                                 y: centre.y + offset * CoreGraphics.sin(angle))
            path.addArc(center: origin, radius: lobe,
                        startAngle: .radians(angle - .pi / 2), endAngle: .radians(angle + .pi / 2),
                        clockwise: false)
        }
        path.closeSubpath()

        let core = radius * 0.40
        for index in 0..<6 {
            let angle = Double(index) / 6 * 2 * .pi - .pi / 3
            let point = CGPoint(x: centre.x + core * CoreGraphics.cos(angle),
                                y: centre.y + core * CoreGraphics.sin(angle))
            if index == 0 { path.move(to: point) } else { path.addLine(to: point) }
        }
        path.closeSubpath()
        return path
    }
}

struct CubeMark: View {
    let color: Color

    var body: some View {
        ZStack {
            CubeFace(face: .top).fill(color)
            CubeFace(face: .left).fill(color.opacity(0.88))
            CubeFace(face: .right).fill(color.opacity(0.52))
        }
    }
}

struct CubeFace: Shape {
    enum Face { case top, left, right }
    let face: Face

    func path(in rect: CGRect) -> Path {
        let corners: [(CGFloat, CGFloat)]
        switch face {
        case .top: corners = [(0.50, 0.04), (0.94, 0.29), (0.50, 0.54), (0.06, 0.29)]
        case .left: corners = [(0.06, 0.29), (0.50, 0.54), (0.50, 0.96), (0.06, 0.71)]
        case .right: corners = [(0.94, 0.29), (0.94, 0.71), (0.50, 0.96), (0.50, 0.54)]
        }
        var path = Path()
        path.addLines(corners.map { CGPoint(x: rect.minX + $0.0 * rect.width, y: rect.minY + $0.1 * rect.height) })
        path.closeSubpath()
        return path
    }
}

struct SparkShape: Shape {
    func path(in rect: CGRect) -> Path {
        let w = rect.width, h = rect.height
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: rect.minX + x * w, y: rect.minY + y * h) }
        var path = Path()
        path.move(to: point(0.5, 0))
        path.addQuadCurve(to: point(1, 0.5), control: point(0.58, 0.42))
        path.addQuadCurve(to: point(0.5, 1), control: point(0.58, 0.58))
        path.addQuadCurve(to: point(0, 0.5), control: point(0.42, 0.58))
        path.addQuadCurve(to: point(0.5, 0), control: point(0.42, 0.42))
        path.closeSubpath()
        return path
    }
}

struct SlashShape: Shape {
    func path(in rect: CGRect) -> Path {
        let w = rect.width, h = rect.height
        func point(_ x: CGFloat, _ y: CGFloat) -> CGPoint { CGPoint(x: rect.minX + x * w, y: rect.minY + y * h) }
        var path = Path()
        path.addLines([point(0.04, 0.05), point(0.26, 0.05), point(0.96, 0.95), point(0.74, 0.95)])
        path.closeSubpath()
        path.addLines([point(0.96, 0.05), point(0.74, 0.05), point(0.04, 0.95), point(0.26, 0.95)])
        path.closeSubpath()
        return path
    }
}

struct VisorShape: Shape {
    func path(in rect: CGRect) -> Path {
        let w = rect.width, h = rect.height
        var path = Path()
        path.addRoundedRect(in: CGRect(x: rect.minX, y: rect.minY + h * 0.22, width: w, height: h * 0.56),
                            cornerSize: CGSize(width: h * 0.28, height: h * 0.28))
        let lens = w * 0.24
        path.addEllipse(in: CGRect(x: rect.minX + w * 0.16, y: rect.minY + h * 0.38, width: lens, height: lens))
        path.addEllipse(in: CGRect(x: rect.maxX - w * 0.16 - lens, y: rect.minY + h * 0.38, width: lens, height: lens))
        return path
    }
}
