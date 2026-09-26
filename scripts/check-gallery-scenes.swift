import AppKit
import Foundation

let files = Array(CommandLine.arguments.dropFirst())
let images = files.compactMap { path -> NSBitmapImageRep? in
    guard let data = try? Data(contentsOf: URL(fileURLWithPath: path)) else { return nil }
    return NSBitmapImageRep(data: data)
}
guard files.count == 3, images.count == 3,
      images.allSatisfy({ $0.pixelsWide == images[0].pixelsWide &&
                           $0.pixelsHigh == images[0].pixelsHigh &&
                           $0.bitsPerSample == 8 && $0.samplesPerPixel >= 3 }) else {
    fputs("Expected three compatible scene screenshots\n", stderr)
    exit(2)
}

func changedPixels(_ first: NSBitmapImageRep, _ second: NSBitmapImageRep) -> Int {
    var left = [Int](repeating: 0, count: first.samplesPerPixel)
    var right = [Int](repeating: 0, count: second.samplesPerPixel)
    var changed = 0
    let width = first.pixelsWide, height = first.pixelsHigh
    // Compare the drawing panel, excluding the clock and captions.
    for y in stride(from: height * 26 / 100, to: height * 69 / 100, by: 8) {
        for x in stride(from: width * 12 / 100, to: width * 88 / 100, by: 8) {
            first.getPixel(&left, atX: x, y: y)
            second.getPixel(&right, atX: x, y: y)
            if (0..<3).contains(where: { abs(left[$0] - right[$0]) > 28 }) { changed += 1 }
        }
    }
    return changed
}

let differences = [changedPixels(images[0], images[1]), changedPixels(images[0], images[2]),
                   changedPixels(images[1], images[2])]
fputs("Scene drawing differences: \(differences)\n", stderr)
guard differences.allSatisfy({ $0 > 200 }) else {
    fputs("Gallery scene switch did not change the 3D drawing\n", stderr)
    exit(1)
}
