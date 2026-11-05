// Extracts Apple Vision body landmarks from a video as JSON lines, in the Mirror sample's BodyJoint order:
//   swift scripts/extract-pose-landmarks.swift input.mp4 [every-nth-frame] > landmarks.jsonl
// Each line: {"t":seconds,"w":width,"h":height,"lm":[[x,y,confidence] x13] or null}; x right and y down in [0,1],
// plus "face":[yaw,pitch,roll,mouthOpen,smile,brows,blinkLeft,blinkRight] when a face is found (see FaceFrame).
import AVFoundation
import Vision

let joints: [VNHumanBodyPoseObservation.JointName] = [.nose, .leftShoulder, .rightShoulder, .leftElbow, .rightElbow,
    .leftWrist, .rightWrist, .leftHip, .rightHip, .leftKnee, .rightKnee, .leftAnkle, .rightAnkle]

// The same math and constants as FaceGeometry.Measure in samples/Simple3D.Shared/Mirror/FaceGeometry.cs; keep them in step.
func faceValues(_ face: VNFaceObservation, _ size: CGSize) -> [Double] {
    // Vision reports pitch positive looking down; FaceFrame's is positive looking up. Yaw and roll already match.
    let yaw = face.yaw?.doubleValue ?? 0, pitch = -(face.pitch?.doubleValue ?? 0), roll = face.roll?.doubleValue ?? 0
    guard let l = face.landmarks, let le = l.leftEye, let re = l.rightEye, let lb = l.leftEyebrow, let rb = l.rightEyebrow,
          let outer = l.outerLips, let inner = l.innerLips, outer.pointCount >= 8, inner.pointCount >= 6 else { return [yaw, pitch, roll, 0, 0, 0, 0, 0] }
    // Pixel points (y up): face-box-relative points would stretch X against Y when the box is not square in pixels.
    func pts(_ r: VNFaceLandmarkRegion2D) -> [CGPoint] { r.pointsInImage(imageSize: size) }
    func mean(_ p: [CGPoint]) -> CGPoint { CGPoint(x: p.map(\.x).reduce(0, +) / CGFloat(p.count), y: p.map(\.y).reduce(0, +) / CGFloat(p.count)) }
    func dot(_ a: CGPoint, _ b: CGPoint) -> Double { Double(a.x * b.x + a.y * b.y) }
    func unit(_ v: Double) -> Double { v.isFinite ? min(1, max(0, v)) : 0 }
    let leftEye = pts(le), rightEye = pts(re), lips = pts(outer), innerLips = pts(inner)
    let a = mean(leftEye), b = mean(rightEye), eyeDistance = Double(hypot(b.x - a.x, b.y - a.y))
    guard eyeDistance > 0 else { return [yaw, pitch, roll, 0, 0, 0, 0, 0] }
    let x = CGPoint(x: CGFloat(Double(b.x - a.x) / eyeDistance), y: CGFloat(Double(b.y - a.y) / eyeDistance)), y = CGPoint(x: -x.y, y: x.x)
    let height = eyeDistance / max(cos(yaw), 0.5)
    // Outer lips run from the image-left corner (0) along the upper lip to the other corner (7); inner lips are three upper then three lower points.
    let gap = (dot(mean(Array(innerLips[0...2])), y) - dot(mean(Array(innerLips[3...5])), y)) / height
    let width = abs(dot(lips[7], x) - dot(lips[0], x)) / eyeDistance
    func brow(_ brow: [CGPoint], _ eye: [CGPoint]) -> Double {
        let inner = eye.min { dot($0, x) < dot($1, x) }!, outer = eye.max { dot($0, x) < dot($1, x) }!
        return dot(mean(brow), y) - (dot(inner, y) + dot(outer, y)) / 2
    }
    let brows = (brow(pts(lb), leftEye) + brow(pts(rb), rightEye)) / 2 / height
    func blink(_ eye: [CGPoint]) -> Double {
        let u = eye.map { dot($0, x) }, v = eye.map { dot($0, y) }
        let w = u.max()! - u.min()!
        return w > 0 ? unit((0.28 - (v.max()! - v.min()!) / w) / (0.28 - 0.12)) : 0
    }
    // Vision's leftEye is on the image's left, which is the person's right eye.
    return [yaw, pitch, roll, unit((gap - 0.05) / (0.40 - 0.05)), unit((width - 0.75) / (1 - 0.75)),
            min(1, max(-1, (brows - 0.26) / 0.10)), blink(rightEye), blink(leftEye)]
}
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
    // The rectangles request gives the landmarks request continuous (revision 3) yaw, pitch and roll; alone it reports 45° steps.
    let faceRectangles = VNDetectFaceRectanglesRequest(), faces = VNDetectFaceLandmarksRequest()
    faces.constellation = .constellation76Points // the lip indices below assume it
    try? VNImageRequestHandler(cvPixelBuffer: pixels).perform([faceRectangles, faces, request])
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
    // The face nearest the body's nose (or shoulders), else the largest face.
    let size = CGSize(width: CVPixelBufferGetWidth(pixels), height: CVPixelBufferGetHeight(pixels))
    let anchor: CGPoint? = best.flatMap { points in
        if let nose = points[.nose], nose.confidence > 0.1 { return nose.location }
        guard let l = points[.leftShoulder], let r = points[.rightShoulder] else { return nil }
        return CGPoint(x: (l.x + r.x) / 2, y: (l.y + r.y) / 2)
    }
    let face = (faces.results ?? []).min { f, g in
        func score(_ o: VNFaceObservation) -> Double {
            guard let anchor else { return -Double(o.boundingBox.width * o.boundingBox.height) }
            return Double(hypot(o.boundingBox.midX - anchor.x, o.boundingBox.midY - anchor.y))
        }
        return score(f) < score(g)
    }
    let faceJson = face.map { ",\"face\":[" + faceValues($0, size).map { String(format: "%.4f", $0) }.joined(separator: ",") + "]" } ?? ""
    print(String(format: "{\"t\":%.4f,\"w\":%d,\"h\":%d,\"lm\":%@%@}", time, Int(size.width), Int(size.height), landmarks, faceJson))
}
