namespace Simple3D.Shared.Mirror;

/// <summary>How a character shows the tracked face: which VRM expressions each face measurement drives, and how strongly
/// when that measurement is at full strength.</summary>
/// <remarks>Models differ a lot. One model's "a" opens the mouth wide while another's barely parts the lips; one "joy"
/// smiles with open eyes while another squeezes them shut; some "angry" shapes are invisible. Each bundled model therefore
/// has its own table (see <see cref="MirrorModels"/>), chosen by rendering every expression and measuring how far it moves the
/// face. Expressions a model lacks are ignored, and an empty table leaves that part of the face still.</remarks>
public sealed record MirrorFace
{
    /// <summary>The usual VRM presets: "a" for the mouth, "joy", "angry", "surprised" and one blink per eye.</summary>
    public static MirrorFace Default { get; } = new();

    /// <summary>Expressions an open mouth drives, with their intensity when the mouth is fully open. Several shapes may be
    /// blended, for example "a" plus "o" for a model whose "a" alone only parts the lips.</summary>
    public IReadOnlyDictionary<string, float> Mouth { get; init; } = Table(("a", 1));
    /// <summary>Expressions a smile drives.</summary>
    public IReadOnlyDictionary<string, float> Smile { get; init; } = Table(("joy", 1));
    /// <summary>Expressions lowered brows (a frown) drive.</summary>
    public IReadOnlyDictionary<string, float> Frown { get; init; } = Table(("angry", 1));
    /// <summary>Expressions raised brows drive.</summary>
    public IReadOnlyDictionary<string, float> Surprise { get; init; } = Table(("surprised", 1));
    /// <summary>Whether each eye closes on its own ("blink_l", "blink_r"); otherwise both close together ("blink") when both
    /// of the person's eyes do.</summary>
    public bool SeparateEyes { get; init; } = true;

    /// <summary>Builds an expression table from (name, intensity) pairs.</summary>
    public static IReadOnlyDictionary<string, float> Table(params (string Name, float Intensity)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Intensity);

    /// <summary>Maps face measurements to VRM expression intensities. A mirrored preview swaps the eyes, like the body.</summary>
    public IReadOnlyDictionary<string, float> Expressions(FaceFrame face, bool mirrored)
    {
        ArgumentNullException.ThrowIfNull(face);
        var (left, right) = mirrored ? (face.BlinkRight, face.BlinkLeft) : (face.BlinkLeft, face.BlinkRight);
        var result = new Dictionary<string, float>();
        Add(Mouth, face.MouthOpen);
        Add(Smile, face.Smile);
        Add(Frown, Math.Clamp(-face.Brows, 0, 1));
        Add(Surprise, Math.Clamp(face.Brows, 0, 1));
        if (SeparateEyes) { result["blink_l"] = left; result["blink_r"] = right; }
        else result["blink"] = MathF.Min(left, right);
        return result;

        // Channels may share an expression; their contributions add up (and clamp when applied).
        void Add(IReadOnlyDictionary<string, float> table, float value)
        {
            foreach (var (name, intensity) in table)
                result[name] = MathF.Min(1, result.GetValueOrDefault(name) + intensity * Math.Clamp(value, 0, 1));
        }
    }
}
