namespace Simple3D.Shared.Mirror;

/// <summary>A bundled character the Mirror scene can show.</summary>
/// <param name="Name">Display name.</param>
/// <param name="File">VRM file name among the app's assets.</param>
/// <param name="Tracks">What the model can show beyond the body, for the picker.</param>
/// <param name="Credit">Author and license.</param>
public sealed record MirrorModel(string Name, string File, string Tracks, string Credit)
{
    /// <summary>How the character shows the tracked face; pass it to <see cref="MirrorCharacter.FromVrm"/>.</summary>
    public MirrorFace Face { get; init; } = MirrorFace.Default;
}

/// <summary>The characters bundled with the gallery, best face support first.</summary>
public static class MirrorModels
{
    /// <summary>All bundled characters.</summary>
    /// <remarks>Face tables were chosen from close-up renders of every expression (<c>--render-faces</c>, see
    /// docs/DEVELOPMENT.md); the grids are in docs/site/images/mirror-faces-*.png. The mouth uses the blend that opens it
    /// clearly, and the smile keeps the eyes open so blinks still show.</remarks>
    public static IReadOnlyList<MirrorModel> All { get; } =
    [
        // "a" opens a fanged cat mouth. "joy" squeezes the eyes into >< and looks broken half-way, so the smile is "fun" (:3).
        new("Chubby Tubby Cat", "cat.vrm", "mouth, each eye, smile, frown, head", "ToxSam, CC0")
        {
            Face = new() { Smile = MirrorFace.Table(("fun", 1)) },
        },
        // Her "a" only parts the lips: "o", "e" and some "u" on top make a round, wide-open mouth. "joy" shuts her eyes;
        // "fun" (a closed-mouth smile) plus some "i" shows teeth with the eyes open.
        new("Juanita", "juanita.vrm", "mouth, each eye, smile, frown, head", "Polygonal Mind (100Avatars R3), CC0")
        {
            Face = new() { Mouth = MirrorFace.Table(("a", 1), ("o", 1), ("e", 1), ("u", .5f)), Smile = MirrorFace.Table(("fun", 1), ("i", 1)) },
        },
        // The visor mouth glows: "a" plus "o" makes a large teal oval. "joy" curves the eyes into happy arcs.
        new("CosmicBot", "cosmicbot.vrm", "mouth, each eye, smile, frown, head", "Polygonal Mind (100Avatars R3), CC0")
        {
            Face = new() { Mouth = MirrorFace.Table(("a", 1), ("o", 1)) },
        },
        // Visemes and one blink for both eyes only.
        new("Froggy", "froggy.vrm", "mouth, blinks, head", "Polygonal Mind (100Avatars), CC0")
        {
            Face = new() { Mouth = MirrorFace.Table(("a", 1), ("o", .5f)), Smile = MirrorFace.Table(), Frown = MirrorFace.Table(), Surprise = MirrorFace.Table(), SeparateEyes = false },
        },
    ];

    /// <summary>The model with <paramref name="file"/>, or the first one.</summary>
    public static MirrorModel Find(string? file) => All.FirstOrDefault(m => m.File == file) ?? All[0];
}
