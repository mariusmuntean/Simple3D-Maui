using System.Numerics;
using Simple3D.Core;

internal static class AppearanceTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("mesh attributes are copied validated and normals normalized", Attributes),
        ("calculated normals preserve attributes and shared vertex smoothing", CalculatedNormals),
        ("textures validate storage and sample orientation filtering and repeat", Textures),
        ("light settings validate and notify only effective changes", Lights),
    ];
    private static readonly Vector3[] Triangle = [new(-1,-1,0), new(1,-1,0), new(0,1,0)];
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (ArgumentException) { return; }
        throw new Exception("invalid input accepted");
    }
    private static void Attributes()
    {
        var uv = new Vector2[] { new(0,0), new(1,0), new(.5f,1) };
        var normals = Enumerable.Repeat(new Vector3(0,0,8),3).ToArray();
        var colors = new uint[] { 0xFFFF0000, 0xFF00FF00, 0xFF0000FF };
        var mesh = new Mesh(Triangle, [0,1,2], uv, normals, colors);
        uv[0] = Vector2.One; normals[0] = Vector3.Zero; colors[0] = 0;
        Check(mesh.TextureCoordinates![0] == Vector2.Zero && mesh.Normals![0] == Vector3.UnitZ && mesh.Colors![0] == 0xFFFF0000, "asset mutated");
        Reject(() => new Mesh(Triangle,[0,1,2], [Vector2.Zero]));
        Reject(() => new Mesh(Triangle,[0,1,2], normals: [Vector3.Zero,Vector3.UnitZ,Vector3.UnitZ]));
        Reject(() => new Mesh(Triangle,[0,1,2], colors: [0,0,0]));
        Reject(() => new Mesh(Triangle,[0,1,2], [new(float.NaN,0),Vector2.Zero,Vector2.Zero]));
        var huge = new Mesh(Triangle,[0,1,2], normals: Enumerable.Repeat(new Vector3(float.MaxValue),3));
        Check(MathF.Abs(huge.Normals![0].Length()-1)<1e-6f, "finite large normal not normalized");
    }
    private static void CalculatedNormals()
    {
        var mesh = new Mesh([Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ], [0,1,2,0,3,1], colors: Enumerable.Repeat(0xFFFFFFFFu,4));
        var smooth = mesh.WithCalculatedNormals();
        Check(mesh.Normals is null && smooth.Colors!.Count == 4, "source or attributes changed");
        Check(Vector3.Distance(smooth.Normals![0],Vector3.Normalize(new(0,1,1))) < 1e-6f, "shared normals wrong");
        Check(smooth.Normals[2] == Vector3.UnitZ && smooth.Normals[3] == Vector3.UnitY, "unshared normals wrong");
    }
    private static void Textures()
    {
        uint[] pixels = [0xFFFF0000,0xFF00FF00,0xFF0000FF,0xFFFFFFFF];
        var texture = new Texture2D(2,2,pixels); pixels[0] = 0;
        Check(texture.Sample(0,1)==0xFFFF0000 && texture.Sample(1,0)==0xFFFFFFFF, "UV orientation or copy");
        Check(texture.Sample(-100,100)==0xFFFF0000, "clamp");
        var repeat = new Texture2D(2,2,texture.Pixels, addressMode: TextureAddressMode.Repeat);
        Check(repeat.Sample(0,0) == 0xFF0000FF && repeat.Sample(1,1) == 0xFF0000FF, "repeat seam orientation");
        Check(repeat.Sample(-.25f,.25f) == repeat.Sample(.75f,1.25f), "negative repeat");
        var linear = new Texture2D(2,2,texture.Pixels, TextureFilter.Bilinear);
        Check(linear.Sample(.5f,.5f)==0xFF808080, "bilinear center");
        Reject(() => new Texture2D(0,2,[])); Reject(() => new Texture2D(4097,1,[]));
        Reject(() => new Texture2D(1,1,[0x88FFFFFF])); Reject(() => new Texture2D(1,1,[]));
        Reject(() => texture.Sample(float.NaN,0));
        Reject(() => new Texture2D(1,1,[0xFFFFFFFF], (TextureFilter)9));
    }
    private static void Lights()
    {
        var scene = new Scene(); var changed = 0; scene.Changed += (_,_) => changed++;
        var light = new DirectionalLight(new(0,0,-5),0xFFFF0000,.5f);
        scene.Light = light; scene.Light = light; scene.AmbientLight = .1f; scene.AmbientLight = .1f;
        Check(changed == 2 && light.Direction == -Vector3.UnitZ, "light normalization/notification");
        scene.Light = null; Check(changed==3,"light disabling");
        Reject(() => scene.AmbientLight = float.NaN); Reject(() => scene.AmbientLight = -1);
        Reject(() => new DirectionalLight(Vector3.Zero)); Reject(() => new DirectionalLight(Vector3.One,intensity:float.PositiveInfinity));
        Reject(() => new DirectionalLight(Vector3.One,0x80FFFFFF));
    }
}
