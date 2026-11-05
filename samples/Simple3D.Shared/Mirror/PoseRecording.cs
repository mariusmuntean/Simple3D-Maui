using System.Globalization;
using System.Text.Json;

namespace Simple3D.Shared.Mirror;

/// <summary>Recorded pose frames as JSON lines: <c>{"t":seconds,"w":width,"h":height,"lm":[[x,y,confidence],…]|null,"face":[yaw,pitch,roll,mouth,smile,brows,blinkL,blinkR]}</c>
/// (<c>face</c> optional).</summary>
/// <remarks>Written by <c>scripts/extract-pose-landmarks.swift</c> and the gallery's pose probe. Replaying a recording drives the
/// Mirror scene without a camera or detector, for tests and for simulators that cannot run body-pose models.</remarks>
public static class PoseRecording
{
    /// <summary>Reads every frame; blank lines are skipped.</summary>
    public static IReadOnlyList<PoseFrame> Read(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var frames = new List<PoseFrame>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            Landmark[]? landmarks = null;
            if (root.GetProperty("lm") is { ValueKind: JsonValueKind.Array } points)
            {
                landmarks = points.EnumerateArray().Select(p => new Landmark(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
                if (landmarks.Length != PoseFrame.JointCount) throw new InvalidDataException($"Expected {PoseFrame.JointCount} landmarks: {line}");
            }
            FaceFrame? face = null;
            if (root.TryGetProperty("face", out var f) && f.ValueKind == JsonValueKind.Array)
            {
                var v = f.EnumerateArray().Select(x => x.GetSingle()).ToArray();
                if (v.Length != 8) throw new InvalidDataException($"Expected 8 face values: {line}");
                face = new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7]);
            }
            frames.Add(new(TimeSpan.FromSeconds(root.GetProperty("t").GetDouble()), root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(), landmarks) { Face = face });
        }
        return frames;
    }

    /// <summary>Formats one frame as a JSON line.</summary>
    public static string Format(PoseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var landmarks = frame.Landmarks is null ? "null"
            : "[" + string.Join(",", frame.Landmarks.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.X:0.####},{p.Y:0.####},{p.Confidence:0.###}]"))) + "]";
        var face = frame.Face is { } f ? string.Create(CultureInfo.InvariantCulture,
            $",\"face\":[{f.Yaw:0.###},{f.Pitch:0.###},{f.Roll:0.###},{f.MouthOpen:0.###},{f.Smile:0.###},{f.Brows:0.###},{f.BlinkLeft:0.###},{f.BlinkRight:0.###}]") : "";
        return string.Create(CultureInfo.InvariantCulture, $"{{\"t\":{frame.Timestamp.TotalSeconds:0.####},\"w\":{frame.ImageWidth},\"h\":{frame.ImageHeight},\"lm\":{landmarks}{face}}}");
    }

    /// <summary>The recorded frame closest to <paramref name="timestamp"/>; frames must be in timestamp order.</summary>
    public static PoseFrame Nearest(IReadOnlyList<PoseFrame> frames, TimeSpan timestamp)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("The recording is empty.", nameof(frames));
        int low = 0, high = frames.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (frames[middle].Timestamp < timestamp) low = middle + 1; else high = middle;
        }
        return low > 0 && timestamp - frames[low - 1].Timestamp < frames[low].Timestamp - timestamp ? frames[low - 1] : frames[low];
    }
}
