#!/usr/bin/env swift
import AppKit
import Foundation

// Draws the disk-image window background as a TIFF at 2× its point size, so
// Finder places it at 640 × 400 points and still draws it sharp on Retina.

guard CommandLine.arguments.count >= 2 else { fatalError("Usage: make-dmg-background.swift output.tiff") }
let output = URL(fileURLWithPath: CommandLine.arguments[1])

let points = NSSize(width: 640, height: 400)
let scale: CGFloat = 2

guard let rep = NSBitmapImageRep(
    bitmapDataPlanes: nil,
    pixelsWide: Int(points.width * scale),
    pixelsHigh: Int(points.height * scale),
    bitsPerSample: 8,
    samplesPerPixel: 4,
    hasAlpha: true,
    isPlanar: false,
    colorSpaceName: .deviceRGB,
    bytesPerRow: 0,
    bitsPerPixel: 0
) else { fatalError("Could not allocate the background bitmap") }
rep.size = points

NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
NSGraphicsContext.current?.imageInterpolation = .high

let bounds = NSRect(origin: .zero, size: points)
NSGradient(
    starting: NSColor(srgbRed: 1.0, green: 1.0, blue: 1.0, alpha: 1),
    ending: NSColor(srgbRed: 0.906, green: 0.929, blue: 0.976, alpha: 1)
)?.draw(in: bounds, angle: -90)

/// Finder measures from the top of the window, so coordinates are flipped here.
func y(_ fromTop: CGFloat) -> CGFloat { points.height - fromTop }

func draw(_ text: String, font: NSFont, color: NSColor, centeredAt centre: CGFloat, fromTop: CGFloat) {
    let attributes: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
    let size = (text as NSString).size(withAttributes: attributes)
    (text as NSString).draw(at: NSPoint(x: centre - size.width / 2, y: y(fromTop) - size.height), withAttributes: attributes)
}

draw("AIUsageMeter", font: .systemFont(ofSize: 30, weight: .bold), color: NSColor(srgbRed: 0.043, green: 0.071, blue: 0.125, alpha: 1), centeredAt: points.width / 2, fromTop: 54)
draw("Drag AIUsageMeter into your Applications folder", font: .systemFont(ofSize: 13, weight: .regular), color: NSColor(srgbRed: 0.30, green: 0.34, blue: 0.42, alpha: 1), centeredAt: points.width / 2, fromTop: 96)

// Between the two icon slots, on the axis the DMG script positions them on.
let arrow = NSBezierPath()
let axis = y(230)
arrow.move(to: NSPoint(x: 258, y: axis))
arrow.line(to: NSPoint(x: 382, y: axis))
arrow.move(to: NSPoint(x: 366, y: axis + 13))
arrow.line(to: NSPoint(x: 382, y: axis))
arrow.line(to: NSPoint(x: 366, y: axis - 13))
arrow.lineWidth = 3
arrow.lineCapStyle = .round
arrow.lineJoinStyle = .round
NSColor(srgbRed: 0.0, green: 0.341, blue: 0.988, alpha: 0.55).setStroke()
arrow.stroke()

draw("aiusagemeter is free and open source · buymeacoffee.com/dngkec", font: .systemFont(ofSize: 10.5, weight: .medium), color: NSColor(srgbRed: 0.42, green: 0.46, blue: 0.54, alpha: 1), centeredAt: points.width / 2, fromTop: 366)

NSGraphicsContext.restoreGraphicsState()

guard let data = rep.representation(using: .tiff, properties: [:]) else { fatalError("Could not encode the background") }
try data.write(to: output, options: .atomic)
