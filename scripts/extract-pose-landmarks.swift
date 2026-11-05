// Extracts Apple Vision body landmarks from a video as JSON lines, in the Mirror sample's BodyJoint order:
//   swift scripts/extract-pose-landmarks.swift input.mp4 [every-nth-frame] > landmarks.jsonl
// Each line: {"t":seconds,"w":width,"h":height,"lm":[[x,y,confidence] x13] or null}; x right and y down in [0,1].
import AVFoundation
import Vision

let joints: [VNHumanBodyPoseObservation.JointName] = [.nose, .leftShoulder, .rightShoulder, .leftElbow, .rightElbow,
    .leftWrist, .rightWrist, .leftHip, .rightHip, .leftKnee, .rightKnee, .leftAnkle, .rightAnkle]
let arguments = CommandLine.arguments
guard arguments.count >= 2 else { FileHandle.standardError.write("usage: input.mp4 [step]\n".data(using: .utf8)!); exit(2) }
let step = arguments.count > 2 ? max(1, Int(arguments[2]) ?? 1) : 1
let asset = AVURLAsset(url: URL(fileURLWithPath: arguments[1]))
guard let track = asset.tracks(withMediaType: .video).first, let reader = try? AVAssetReader(asset: asset) else { exit(1) }
let output = AVAssetReaderTrackOutput(track: track, outputSettings: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
reader.add(output)
reader.startReading()
var index = 0
while let sample = output.copyNextSampleBuffer() {
    defer { index += 1 }
    guard index % step == 0, let pixels = CMSampleBufferGetImageBuffer(sample) else { continue }
    let time = CMSampleBufferGetPresentationTimeStamp(sample).seconds
    let request = VNDetectHumanBodyPoseRequest()
    try? VNImageRequestHandler(cvPixelBuffer: pixels).perform([request])
    // The most prominent person: largest bounding box of confident landmarks.
    var best: [VNHumanBodyPoseObservation.JointName: VNRecognizedPoint]? = nil
    var bestArea = -1.0
    for observation in request.results ?? [] {
        guard let points = try? observation.recognizedPoints(.all) else { continue }
        let confident = points.values.filter { $0.confidence > 0.1 }
        guard let minX = confident.map(\.x).min(), let maxX = confident.map(\.x).max(),
              let minY = confident.map(\.y).min(), let maxY = confident.map(\.y).max() else { continue }
        let area = (maxX - minX) * (maxY - minY)
        if area > bestArea { bestArea = area; best = points }
    }
    var landmarks = "null"
    if let points = best {
        landmarks = "[" + joints.map { name -> String in
            guard let p = points[name], p.confidence > 0 else { return "[0,0,0]" }
            return String(format: "[%.4f,%.4f,%.3f]", p.location.x, 1 - p.location.y, p.confidence)
        }.joined(separator: ",") + "]"
    }
    print(String(format: "{\"t\":%.4f,\"w\":%d,\"h\":%d,\"lm\":%@}", time, CVPixelBufferGetWidth(pixels), CVPixelBufferGetHeight(pixels), landmarks))
}
