import AppKit
import Foundation

guard CommandLine.arguments.count == 2,
      let bytes = try? Data(contentsOf: URL(fileURLWithPath: CommandLine.arguments[1])),
      let image = NSBitmapImageRep(data: bytes) else {
    fputs("Cannot read simulator screenshot\n", stderr)
    exit(2)
}
guard image.bitsPerSample == 8, image.samplesPerPixel >= 3 else {
    fputs("Unsupported screenshot pixel format\n", stderr)
    exit(2)
}

func near(_ pixel: [Int], _ red: Int, _ green: Int, _ blue: Int) -> Bool {
    let forward = abs(pixel[0] - red) <= 8 && abs(pixel[1] - green) <= 8 && abs(pixel[2] - blue) <= 8
    let reversed = abs(pixel[2] - red) <= 8 && abs(pixel[1] - green) <= 8 && abs(pixel[0] - blue) <= 8
    return forward || reversed
}

var background = 0
var panel = 0
var blueShape = 0
var pixel = [Int](repeating: 0, count: image.samplesPerPixel)
for y in stride(from: 0, to: image.pixelsHigh, by: 6) {
    for x in stride(from: 0, to: image.pixelsWide, by: 6) {
        image.getPixel(&pixel, atX: x, y: y)
        if near(pixel, 13, 19, 34) { background += 1 }
        if near(pixel, 24, 36, 59) { panel += 1 }
        let green = pixel[1]
        let blue = max(pixel[0], pixel[2]), red = min(pixel[0], pixel[2])
        if red >= 60 && blue >= 120 && blue > red + 35 && blue > green + 25 { blueShape += 1 }
    }
}

fputs("Gallery pixels: background=\(background), panel=\(panel), blue shape=\(blueShape)\n", stderr)
guard background > 1000, panel > 1000, blueShape > 100 else {
    exit(1)
}
