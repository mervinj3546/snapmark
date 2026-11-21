// Draws the Snapmark app icon (design C: viewfinder corners + red pointer)
// and writes an .iconset folder. Usage: swift make_icon.swift <out.iconset>
import AppKit

func draw(px: Int) -> Data {
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                               bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let ctx = NSGraphicsContext.current!.cgContext
    // Design space is 1024 with y pointing down.
    let s = CGFloat(px) / 1024
    ctx.translateBy(x: 0, y: CGFloat(px))
    ctx.scaleBy(x: s, y: -s)

    // Small sizes get thicker strokes so the marks stay readable.
    let small = px <= 32
    let medium = px <= 64
    let bracketW: CGFloat = small ? 84 : (medium ? 56 : 44)
    let shaftW: CGFloat = small ? 92 : (medium ? 66 : 56)

    // Squircle base
    let base = CGPath(roundedRect: CGRect(x: 100, y: 100, width: 824, height: 824), cornerWidth: 186, cornerHeight: 186, transform: nil)
    ctx.addPath(base)
    ctx.setFillColor(NSColor(srgbRed: 0.98, green: 0.98, blue: 0.98, alpha: 1).cgColor)
    ctx.fillPath()
    ctx.addPath(base)
    ctx.setStrokeColor(NSColor(white: 0, alpha: small ? 0.18 : 0.10).cgColor)
    ctx.setLineWidth(small ? 8 : 3)
    ctx.strokePath()

    // Corner brackets
    ctx.setStrokeColor(NSColor(srgbRed: 0.114, green: 0.114, blue: 0.122, alpha: 1).cgColor)
    ctx.setLineWidth(bracketW)
    ctx.setLineCap(.round)
    ctx.setLineJoin(.round)
    let corners: [[CGPoint]] = [
        [CGPoint(x: 250, y: 390), CGPoint(x: 250, y: 290), CGPoint(x: 250, y: 250), CGPoint(x: 290, y: 250), CGPoint(x: 390, y: 250)],
        [CGPoint(x: 634, y: 250), CGPoint(x: 734, y: 250), CGPoint(x: 774, y: 250), CGPoint(x: 774, y: 290), CGPoint(x: 774, y: 390)],
        [CGPoint(x: 774, y: 634), CGPoint(x: 774, y: 734), CGPoint(x: 774, y: 774), CGPoint(x: 734, y: 774), CGPoint(x: 634, y: 774)],
        [CGPoint(x: 390, y: 774), CGPoint(x: 290, y: 774), CGPoint(x: 250, y: 774), CGPoint(x: 250, y: 734), CGPoint(x: 250, y: 634)],
    ]
    for c in corners {
        ctx.move(to: c[0]); ctx.addLine(to: c[1])
        ctx.addQuadCurve(to: c[3], control: c[2])
        ctx.addLine(to: c[4])
    }
    ctx.strokePath()

    // Red pointer arrow
    let red = NSColor(srgbRed: 1, green: 0.231, blue: 0.188, alpha: 1).cgColor
    ctx.setStrokeColor(red)
    ctx.setFillColor(red)
    ctx.setLineWidth(shaftW)
    ctx.move(to: CGPoint(x: 680, y: 680)); ctx.addLine(to: CGPoint(x: 459, y: 459))
    ctx.strokePath()
    ctx.setLineWidth(18)
    ctx.move(to: CGPoint(x: 360, y: 360))
    ctx.addLine(to: CGPoint(x: 519, y: 399))
    ctx.addLine(to: CGPoint(x: 399, y: 519))
    ctx.closePath()
    ctx.drawPath(using: .fillStroke)

    NSGraphicsContext.restoreGraphicsState()
    return rep.representation(using: .png, properties: [:])!
}

let out = URL(fileURLWithPath: CommandLine.arguments[1])
try? FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)
for pt in [16, 32, 128, 256, 512] {
    try! draw(px: pt).write(to: out.appendingPathComponent("icon_\(pt)x\(pt).png"))
    try! draw(px: pt * 2).write(to: out.appendingPathComponent("icon_\(pt)x\(pt)@2x.png"))
}
