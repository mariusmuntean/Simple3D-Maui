namespace Simple3D.Shared.Mirror;

/// <summary>A bundled character the Mirror scene can show.</summary>
/// <param name="Name">Display name.</param>
/// <param name="File">VRM file name among the app's assets.</param>
/// <param name="Tracks">What the model can show beyond the body, for the picker.</param>
/// <param name="Credit">Author and license.</param>
public sealed record MirrorModel(string Name, string File, string Tracks, string Credit);

/// <summary>The characters bundled with the gallery, best face support first.</summary>
public static class MirrorModels
{
    /// <summary>All bundled characters.</summary>
    public static IReadOnlyList<MirrorModel> All { get; } =
    [
        new("Chubby Tubby Cat", "cat.vrm", "mouth, each eye, smile, frown, head", "ToxSam, CC0"),
        new("Froggy", "froggy.vrm", "mouth, blinks, head", "Polygonal Mind (100Avatars), CC0"),
    ];

    /// <summary>The model with <paramref name="file"/>, or the first one.</summary>
    public static MirrorModel Find(string? file) => All.FirstOrDefault(m => m.File == file) ?? All[0];
}
