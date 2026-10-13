using System.Numerics;
using Simple3D.Core;

internal static class AppearanceRenderingTests
{
    internal static readonly (string Name, Action Run)[] Cases =
    [
        ("configured lighting changes pixels and unlit ignores it", Lighting),
        ("constant textured colors remain exact across triangle interiors", ConstantTexture),
        ("textured primitive instances share UV geometry and reject missing UVs", PrimitiveTextures),
        ("attribute pixels agree with ray oracle across projection and near clipping", AttributeOracle),
        ("smooth lighting uses inverse transpose through nonuniform groups", NormalTransform),
        ("singular group transforms fall back to surviving face normals", SingularNormalTransform),
        ("attributed rendering reuses buffers without per-triangle allocations", Allocation),
        ("legacy renderer rejects appearance features", LegacyGuard),
    ];
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Vector3 Rgb(uint c) => new((c >> 16) & 255, (c >> 8) & 255, c & 255);
    private static void Close(uint actual, Vector3 expected, string message)
    {
        var delta = Vector3.Abs(Rgb(actual) - Vector3.Clamp(expected, Vector3.Zero, new(255)));
        Check(Math.Max(delta.X,Math.Max(delta.Y,delta.Z)) <= 2, $"{message}: {actual:X8} vs {expected}");
    }
    private static void Lighting()
    {
        var shape = Shape.FromMesh(new Mesh([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2]),new Material(0xFFFFFFFF));
        var scene = new Scene().Add(shape); var renderer = new DepthRenderer(); var camera = new Camera(5,0,0);
        uint Pixel() => renderer.Render(scene,camera,32,32).Pixels.Span[16*32+16];
        var oldShade = .38f+.62f*MathF.Max(0,Vector3.Dot(Vector3.UnitZ,Vector3.Normalize(new(-.4f,.8f,1))));
        Check(Pixel() == (0xFF000000u | (uint)(255*oldShade)*0x010101u),"default lighting changed");
        scene.AmbientLight=0; scene.Light=new(-Vector3.UnitZ,0xFFFF0000);
        Check(Pixel()==0xFFFF0000,"colored directional light ignored");
        scene.Light=new(Vector3.UnitZ); Check(Pixel()==0xFF000000,"light ray direction reversed");
        scene.Light=null; scene.AmbientLight=.5f; Check(Pixel()==0xFF7F7F7F,"ambient-only");
        scene.Replace(shape,shape.WithMaterial(new Material(0xFF123456,lit:false)));
        Check(Pixel()==0xFF123456,"unlit changed");
    }
    private static void ConstantTexture()
    {
        var shape=Shape.Box().WithMaterial(new Material(new Texture2D(1,1,[0xFFABCDEF]),lit:false));
        var frame=new DepthRenderer().Render(new Scene().Add(shape),new Camera(3,.37f,.21f),81,67);
        for(var y=0;y<frame.Height;y++) for(var x=0;x<frame.Width;x++)
            if(frame.Pick(x,y) is not null) Check(frame.Pixels.Span[y*frame.Width+x]==0xFFABCDEF,"constant texel changed");
    }
    private static void PrimitiveTextures()
    {
        var texture = new Texture2D(2,1,[0xFFFF0000,0xFF0000FF]); var material = new Material(texture,lit:false);
        foreach (var primitive in new[] { Shape.Box(),Shape.Sphere(),Shape.Cylinder(),Shape.Pyramid() })
        {
            var a=primitive.WithMaterial(material); var b=primitive.WithMaterial(material);
            Check(a.Geometry?.TextureCoordinates?.Count == a.Geometry?.Vertices.Count && a.Geometry is not null,"primitive lacks UVs");
            Check(ReferenceEquals(a.Geometry,b.Geometry),"UV geometry not cached");
            var f=new DepthRenderer().Render(new Scene().Add(a),new Camera(3,.3f,.2f),64,64);
            Check(f.Pixels.Span.Contains(0xFFFF0000) && f.Pixels.Span.Contains(0xFF0000FF),"primitive texture invisible");
        }
        var mesh=new Mesh([Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[0,1,2]);
        try { Shape.FromMesh(mesh,material); } catch(ArgumentException) { return; }
        throw new Exception("missing UVs accepted");
    }
    private static void AttributeOracle()
    {
        const int size=40;
        var uv=new Vector2[] {new(0,0),new(1,0),new(.3f,1)};
        uint[] colors=[0xFFFF8040,0xFF40FF80,0xFF8040FF];
        Vector3[] normals=[Vector3.UnitZ,Vector3.Normalize(new(1,0,1)),Vector3.Normalize(new(0,1,1))];
        var texture=new Texture2D(2,2,[0xFFFFD080,0xFF80FFFF,0xFFFF80FF,0xFFFFFFFF],TextureFilter.Bilinear);
        foreach(var projection in new[] {CameraProjection.Perspective,CameraProjection.Orthographic})
        foreach(var clip in new[] {false,true})
        {
            Vector3[] vertices=[new(-2,-1,clip?4.8f:1),new(2,-1,-1),new(0,2,0)];
            var mesh=new Mesh(vertices,[0,1,2],uv,normals,colors);
            var shape=Shape.FromMesh(mesh,new Material(texture,0xFFDDEEFF));
            var scene=new Scene {AmbientLight=.2f,Light=new(-Vector3.UnitZ,intensity:.7f)}; scene.Add(shape);
            var camera=new Camera(5,0,0){NearPlane=.5f,Projection=projection,OrthographicHeight=5};
            var frame=new DepthRenderer().Render(scene,camera,size,size); var checkedPixels=0;
            for(var y=0;y<size;y++) for(var x=0;x<size;x++)
            {
                var scale=size/(2*Math.Tan(camera.FieldOfView/2));
                var origin=new Vector3(0,0,5);
                var direction=new Vector3((float)((x+.5-size/2)/scale),(float)(-(y+.5-size/2)/scale),-1);
                if(projection==CameraProjection.Orthographic)
                {
                    origin+=new Vector3((x+.5f-size/2)*5/size,-(y+.5f-size/2)*5/size,0); direction=-Vector3.UnitZ;
                }
                var e1=vertices[1]-vertices[0]; var e2=vertices[2]-vertices[0];
                var p=Vector3.Cross(direction,e2); var det=Vector3.Dot(e1,p);
                var t=origin-vertices[0]; var b=Vector3.Dot(t,p)/det;
                var q=Vector3.Cross(t,e1); var c=Vector3.Dot(direction,q)/det; var a=1-b-c;
                var depth=Vector3.Dot(e2,q)/det;
                if(Math.Min(a,Math.Min(b,c))<.001f || depth<camera.NearPlane+.001f) continue;
                Check(ReferenceEquals(frame.Pick(x,y),shape),"attribute lost picking");
                var coord=uv[0]*a+uv[1]*b+uv[2]*c;
                var color=Rgb(colors[0])/255*(.2f+.7f*normals[0].Z)*a+
                    Rgb(colors[1])/255*(.2f+.7f*normals[1].Z)*b+Rgb(colors[2])/255*(.2f+.7f*normals[2].Z)*c;
                var expected=Rgb(texture.Sample(coord.X,coord.Y))*Rgb(0xFFDDEEFF)/255*color;
                Close(frame.Pixels.Span[y*size+x],expected,$"{projection}, clipping {clip}, ({x},{y})"); checkedPixels++;
            }
            Check(checkedPixels>40,"oracle did not cover visible surface");
        }
    }
    private static void NormalTransform()
    {
        var n=Vector3.Normalize(new Vector3(1,0,1));
        var mesh=new Mesh([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2],normals:Enumerable.Repeat(n,3));
        var shape=Shape.FromMesh(mesh,new Material(0xFFFFFFFF));
        var group=Shape.Group(shape).Scaled(2,1,.5f);
        var scene=new Scene {AmbientLight=0,Light=new(-Vector3.UnitZ)}; scene.Add(group);
        var frame=new DepthRenderer().Render(scene,new Camera(5,0,0),32,32);
        Close(frame.Pixels.Span[16*32+16],new Vector3(255*Vector3.Normalize(new Vector3(.5f,0,2)).Z),"normal transformed as a position");
    }
    private static void SingularNormalTransform()
    {
        var mesh = new Mesh([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2],
            normals: Enumerable.Repeat(Vector3.Normalize(new Vector3(1,0,1)),3));
        var shape = Shape.FromMesh(mesh,new Material(0xFFFFFFFF));
        var group = Shape.Group(Shape.Group(shape).Scaled(1,1,1e-30f)).Scaled(1,1,1e-30f);
        var scene = new Scene {AmbientLight=0,Light=new(-Vector3.UnitZ)}; scene.Add(group);
        var frame = new DepthRenderer().Render(scene,new Camera(5,0,0),32,32);
        Check(frame.Pixels.Span[16*32+16] == 0xFFFFFFFF,"singular inverse reused a local-space normal");
    }
    private static void Allocation()
    {
        var texture=new Texture2D(1,1,[0xFFFFFFFF]);
        var mesh=new Mesh([new(-1,-1,0),new(1,-1,0),new(0,1,0)],[0,1,2],[Vector2.Zero,Vector2.UnitX,Vector2.UnitY],Enumerable.Repeat(Vector3.UnitZ,3),[0xFFFF0000,0xFF00FF00,0xFF0000FF]);
        var shape=Shape.FromMesh(mesh,new Material(texture));var scene=new Scene();
        for(var i=0;i<100;i++) scene.Add(shape.At(0,0,-i*.01f));
        var renderer=new DepthRenderer();var target=new RenderTarget(32,32);var camera=new Camera(5,0,0);
        for(var i=0;i<10;i++)renderer.RenderInto(scene,camera,target);
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var i=0;i<10;i++)renderer.RenderInto(scene,camera,target);
        Check((GC.GetAllocatedBytesForCurrentThread()-before)/10<4096,"allocations grow per triangle");
    }
    private static void LegacyGuard()
    {
        var scene=new Scene {AmbientLight=.1f};scene.Add(Shape.Box());
        try {SceneRenderer.Render(scene,new Camera(),32,32);} catch(NotSupportedException){return;}
        throw new Exception("legacy renderer ignored custom light");
    }
}
