import AppKit
import Foundation

guard CommandLine.arguments.count == 2,
      let bytes = try? Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])),
      let image = NSBitmapImageRep(data: bytes) else {
    fatalError("Cannot read simulator screenshot")
}

func near(_ color: NSColor, _ red: Int, _ green: Int, _ blue: Int) -> Bool {
    guard let rgb = color.usingColorSpace(.deviceRGB) else { return false }
    return abs(Int(rgb.redComponent * 255) - red) <= 8 &&
           abs(Int(rgb.greenComponent * 255) - green) <= 8 &&
           abs(Int(rgb.blueComponent * 255) - blue) <= 8
}

var background = 0
var panel = 0
var blueShape = 0
for y in stride(from: 0, to: image.pixelsHigh, by: 6) {
    for x in stride(from: 0, to: image.pixelsWide, by: 6) {
        guard let color = image.colorAt(x: x, y: y) else { continue }
        if near(color, 13, 19, 34) { background += 1 }
        if near(color, 24, 36, 59) { panel += 1 }
        if let rgb = color.usingColorSpace(.deviceRGB) {
            let red = rgb.redComponent * 255
            let green = rgb.greenComponent * 255
            let blue = rgb.blueComponent * 255
            if red >= 60 && blue >= 120 && blue > red + 35 && blue > green + 25 {
                blueShape += 1
            }
        }
    }
}

print("Gallery pixels: background=\(background), panel=\(panel), blue shape=\(blueShape)")
guard background > 1000, panel > 1000, blueShape > 100 else {
    fatalError("The expected gallery and 3D shape are not visible in the screenshot")
}
