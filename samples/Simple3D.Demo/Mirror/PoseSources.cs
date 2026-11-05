using Simple3D.Shared.Mirror;

namespace Simple3D.Demo.Mirror;

public static class PoseSources
{
    public static IPoseSource CreateCamera() => new CameraPoseSource();
    /// <summary>A looping video; with <paramref name="landmarksPath"/>, recorded landmarks replace live detection (for simulators without a body-pose model).</summary>
    public static IPoseSource CreateVideo(string path, string? landmarksPath = null) => new VideoPoseSource(path, loop: true,
        landmarksPath is null ? null : PoseRecording.Read(new StreamReader(landmarksPath)));
}
