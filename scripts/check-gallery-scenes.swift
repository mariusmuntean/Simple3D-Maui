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
    // Restrict comparisons to the panel's own background color. It moves with
    // the layout, so simulator safe-area and screen-size changes do not matter.
    func isPanel(_ pixel: [Int]) -> Bool {
        let rgb = [24, 36, 59]
        return (0..<3).allSatisfy { abs(pixel[$0] - rgb[$0]) <= 8 } ||
               (0..<3).allSatisfy { abs(pixel[2 - $0] - rgb[$0]) <= 8 }
    }
    for y in stride(from: 0, to: height, by: 8) {
        for x in stride(from: 0, to: width, by: 8) {
            first.getPixel(&left, atX: x, y: y)
            second.getPixel(&right, atX: x, y: y)
            if (isPanel(left) || isPanel(right)) &&
                (0..<3).contains(where: { abs(left[$0] - right[$0]) > 28 }) {
                changed += 1
            }
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
