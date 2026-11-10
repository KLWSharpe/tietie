// 產生貼貼的圖示（1024×1024 PNG）。用法：swift scripts/make_icon.swift 輸出路徑.png
import AppKit

let out = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "icon_1024.png"
let side = 1024
let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: side, pixelsHigh: side,
                           bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                           colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)

// 底：米白色圓角方塊，細細的外框。整個圖示只用一個墨色，素色線條。
let inset: CGFloat = 100
let body = NSRect(x: inset, y: inset, width: CGFloat(side) - inset * 2, height: CGFloat(side) - inset * 2)
let bodyPath = NSBezierPath(roundedRect: body, xRadius: 185, yRadius: 185)
let paper = NSColor(calibratedRed: 0.98, green: 0.975, blue: 0.96, alpha: 1)
let ink = NSColor(calibratedRed: 0.13, green: 0.14, blue: 0.15, alpha: 1)
paper.setFill()
bodyPath.fill()
NSColor.black.withAlphaComponent(0.10).setStroke()
bodyPath.lineWidth = 6
bodyPath.stroke()

let stroke: CGFloat = 34

func sheet(_ rect: NSRect, rotate: CGFloat, lines: Bool) {
    let transform = NSAffineTransform()
    transform.translateX(by: rect.midX, yBy: rect.midY)
    transform.rotate(byDegrees: rotate)
    transform.translateX(by: -rect.midX, yBy: -rect.midY)
    NSGraphicsContext.saveGraphicsState()
    transform.concat()
    let outline = NSBezierPath(roundedRect: rect, xRadius: 54, yRadius: 54)
    // 先用底色蓋掉後面那張紙被擋住的線，再描邊
    paper.setFill()
    outline.fill()
    ink.setStroke()
    outline.lineWidth = stroke
    outline.lineJoinStyle = .round
    outline.stroke()
    if lines {
        let widths: [CGFloat] = [0.74, 0.52, 0.66]
        for (i, w) in widths.enumerated() {
            let y = rect.maxY - 118 - CGFloat(i) * 104
            let line = NSBezierPath()
            line.move(to: NSPoint(x: rect.minX + 78, y: y))
            line.line(to: NSPoint(x: rect.minX + 78 + (rect.width - 156) * w, y: y))
            line.lineWidth = stroke
            line.lineCapStyle = .round
            line.stroke()
        }
    }
    NSGraphicsContext.restoreGraphicsState()
}

// 兩張疊在一起的紙，代表「貼貼」
sheet(NSRect(x: 290, y: 340, width: 380, height: 450), rotate: 9, lines: false)
sheet(NSRect(x: 350, y: 240, width: 380, height: 450), rotate: -6, lines: true)

NSGraphicsContext.restoreGraphicsState()
try! rep.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: out))
print("icon written: \(out)")
