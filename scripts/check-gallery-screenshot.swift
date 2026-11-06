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

// The scene background (Theme.SceneBackground) behind every scene, and blue scene accents: the interface itself has no
// blue unless the renderer fell back to software.
var background = 0
var blueShape = 0
var pixel = [Int](repeating: 0, count: image.samplesPerPixel)
for y in stride(from: 0, to: image.pixelsHigh, by: 6) {
    for x in stride(from: 0, to: image.pixelsWide, by: 6) {
        image.getPixel(&pixel, atX: x, y: y)
        if near(pixel, 20, 12, 7) { background += 1 }
        // Simulator screenshots may be RGB or BGR.
        for (red, blue) in [(pixel[0], pixel[2]), (pixel[2], pixel[0])] where blue >= 150 && blue > red + 60 && blue > pixel[1] + 15 {
            blueShape += 1
            break
        }
    }
}

// Thresholds scale with the screenshot, like the Android checker's.
let samples = ((image.pixelsHigh + 5) / 6) * ((image.pixelsWide + 5) / 6)
fputs("Gallery pixels: scene background=\(background), blue shape=\(blueShape) of \(samples) samples\n", stderr)
guard Double(background) > Double(samples) * 0.05, Double(blueShape) > Double(samples) * 0.003 else {
    exit(1)
}
