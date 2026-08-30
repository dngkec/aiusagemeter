#!/usr/bin/env swift
import AppKit
import Foundation

// Renders the app iconset. The mark is `Resources/icons/usagemeter.png` when it
// is passed in; without it the script still produces a complete iconset by
// drawing the gauge itself, so a checkout that is missing the artwork can
// still build a signed app.

guard CommandLine.arguments.count >= 2 else { fatalError("Usage: make-icon.swift output.iconset [logo.png]") }
let output = URL(fileURLWithPath: CommandLine.arguments[1], isDirectory: true)
let logo: NSImage? = CommandLine.arguments.count > 2 ? NSImage(contentsOfFile: CommandLine.arguments[2]) : nil
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)

let variants: [(String, Int)] = [
    ("icon_16x16.png", 16), ("icon_16x16@2x.png", 32),
    ("icon_32x32.png", 32), ("icon_32x32@2x.png", 64),
    ("icon_128x128.png", 128), ("icon_128x128@2x.png", 256),
    ("icon_256x256.png", 256), ("icon_256x256@2x.png", 512),
    ("icon_512x512.png", 512), ("icon_512x512@2x.png", 1024),
]

/// The rounded square every macOS icon sits in. Apple's own ratio: the corner
/// radius is a little under a quarter of the tile, and the tile is inset so the
/// icon does not touch its neighbours in the Dock.
func tile(_ pixels: CGFloat) -> NSBezierPath {
    let inset = pixels * 0.065
    let rect = NSRect(x: inset, y: inset, width: pixels - inset * 2, height: pixels - inset * 2)
    let radius = rect.width * 0.2237
    return NSBezierPath(roundedRect: rect, xRadius: radius, yRadius: radius)
}

/// The fallback mark: the three gauges, drawn the way the rail draws them.
func drawGauges(_ pixels: CGFloat) {
    let centers = [0.30, 0.50, 0.70]
    let colors = [
        NSColor(calibratedRed: 1, green: 0.25, blue: 0.04, alpha: 1),
        NSColor(calibratedRed: 0.05, green: 0.91, blue: 0.60, alpha: 1),
        NSColor(calibratedRed: 0.9, green: 0.96, blue: 0.03, alpha: 1),
    ]
    let radius = pixels * 0.115
    for index in 0..<3 {
        let center = NSPoint(x: pixels * CGFloat(centers[index]), y: pixels * 0.5)
        let ringRect = NSRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
        let track = NSBezierPath(ovalIn: ringRect); track.lineWidth = max(1, pixels * 0.026)
        NSColor(calibratedWhite: 0.28, alpha: 1).setStroke(); track.stroke()
        let arc = NSBezierPath(); arc.appendArc(withCenter: center, radius: radius, startAngle: 90, endAngle: -150 - CGFloat(index * 26), clockwise: true)
        arc.lineWidth = max(1, pixels * 0.026); colors[index].setStroke(); arc.stroke()
        let dot = NSBezierPath(ovalIn: NSRect(x: center.x - radius * 0.23, y: center.y - radius * 0.23, width: radius * 0.46, height: radius * 0.46))
        NSColor.white.setFill(); dot.fill()
    }
}

for (name, size) in variants {
    let pixels = CGFloat(size)
    let image = NSImage(size: NSSize(width: pixels, height: pixels))
    image.lockFocus()
    NSGraphicsContext.current?.imageInterpolation = .high
    NSColor.clear.setFill(); NSRect(x: 0, y: 0, width: pixels, height: pixels).fill()

    let panel = tile(pixels)
    if logo == nil {
        NSColor.black.setFill(); panel.fill()
        drawGauges(pixels)
    } else {
        // A near-white tile, because the mark is a saturated blue: on black it
        // loses the contrast that makes it readable at 16 pt in the menu bar.
        let ground = NSGradient(
            starting: NSColor(srgbRed: 1.0, green: 1.0, blue: 1.0, alpha: 1),
            ending: NSColor(srgbRed: 0.914, green: 0.937, blue: 0.980, alpha: 1)
        )
        ground?.draw(in: panel, angle: -90)
        let hairline = tile(pixels)
        hairline.lineWidth = max(1, pixels * 0.004)
        NSColor(srgbRed: 0.11, green: 0.16, blue: 0.30, alpha: 0.10).setStroke()
        hairline.stroke()

        let side = pixels * 0.66
        let origin = (pixels - side) / 2
        logo?.draw(
            in: NSRect(x: origin, y: origin, width: side, height: side),
            from: .zero,
            operation: .sourceOver,
            fraction: 1,
            respectFlipped: true,
            hints: [.interpolation: NSImageInterpolation.high.rawValue]
        )
    }
    image.unlockFocus()

    guard let tiff = image.tiffRepresentation, let rep = NSBitmapImageRep(data: tiff), let png = rep.representation(using: .png, properties: [:]) else {
        fatalError("Could not render \(name)")
    }
    try png.write(to: output.appendingPathComponent(name), options: .atomic)
}
