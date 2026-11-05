using Simple3D.Shared.Mirror;

namespace Simple3D.Demo.Mirror;

public static class PoseSources
{
    public static IPoseSource CreateCamera() => new CameraPoseSource();
    public static IPoseSource CreateVideo(string path) => new VideoPoseSource(path, loop: true);
}
