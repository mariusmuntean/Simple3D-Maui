using System.Globalization;
using System.Text.Json;

namespace Simple3D.Shared.Mirror;

/// <summary>Recorded pose frames as JSON lines: <c>{"t":seconds,"w":width,"h":height,"lm":[[x,y,confidence],…]|null}</c>.</summary>
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
            frames.Add(new(TimeSpan.FromSeconds(root.GetProperty("t").GetDouble()), root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(), landmarks));
        }
        return frames;
    }

    /// <summary>Formats one frame as a JSON line.</summary>
    public static string Format(PoseFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var landmarks = frame.Landmarks is null ? "null"
            : "[" + string.Join(",", frame.Landmarks.Select(p => string.Create(CultureInfo.InvariantCulture, $"[{p.X:0.####},{p.Y:0.####},{p.Confidence:0.###}]"))) + "]";
        return string.Create(CultureInfo.InvariantCulture, $"{{\"t\":{frame.Timestamp.TotalSeconds:0.####},\"w\":{frame.ImageWidth},\"h\":{frame.ImageHeight},\"lm\":{landmarks}}}");
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
