using System.Numerics;

namespace Simple3D.Core;

internal static class Meshes
{
    internal static readonly Triangle3[] Box = MakeBox();
    internal static readonly Triangle3[] Sphere = MakeSphere();
    internal static readonly Triangle3[] Cylinder = MakeCylinder();
    internal static readonly Triangle3[] Pyramid = MakePyramid();

    private static void Add(List<Triangle3> faces, Vector3 a, Vector3 b, Vector3 c)
    {
        // Orient faces away from the center of a unit shape.
        var normal = Vector3.Cross(b - a, c - a);
        if (Vector3.Dot(normal, (a + b + c) / 3) < 0) (b, c) = (c, b);
        faces.Add(new(a, b, c));
    }

    private static Triangle3[] MakeBox()
    {
        var faces = new List<Triangle3>(12);
        var p = new[] {
            new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f),
            new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
            new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f),
            new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
        foreach (var (a,b,c,d) in new[] { (0,1,2,3),(4,5,6,7),(0,4,7,3),(1,5,6,2),(3,2,6,7),(0,1,5,4) })
        {
            Add(faces,p[a],p[b],p[c]); Add(faces,p[a],p[c],p[d]);
        }
        return faces.ToArray();
    }

    private static Triangle3[] MakeSphere()
    {
        const int segments = 16, rings = 10;
        var faces = new List<Triangle3>(segments * rings * 2);
        Vector3 Point(int ring, int segment)
        {
            var phi = MathF.PI * ring / rings;
            var theta = 2 * MathF.PI * segment / segments;
            return new(MathF.Sin(phi)*MathF.Cos(theta)*.5f, MathF.Cos(phi)*.5f, MathF.Sin(phi)*MathF.Sin(theta)*.5f);
        }
        for (var ring = 0; ring < rings; ring++)
            for (var segment = 0; segment < segments; segment++)
            {
                var a = Point(ring,segment); var b = Point(ring+1,segment);
                var c = Point(ring+1,segment+1); var d = Point(ring,segment+1);
                if (ring > 0) Add(faces,a,b,d);
                if (ring < rings-1) Add(faces,b,c,d);
            }
        return faces.ToArray();
    }

    private static Triangle3[] MakeCylinder()
    {
        const int segments = 20;
        var faces = new List<Triangle3>(segments * 4);
        for (var i = 0; i < segments; i++)
        {
            var a = 2*MathF.PI*i/segments; var b = 2*MathF.PI*(i+1)/segments;
            var x = new Vector3(MathF.Cos(a)*.5f, -.5f, MathF.Sin(a)*.5f);
            var y = new Vector3(MathF.Cos(b)*.5f, -.5f, MathF.Sin(b)*.5f);
            var topX = x + Vector3.UnitY; var topY = y + Vector3.UnitY;
            Add(faces,x,y,topX); Add(faces,y,topY,topX);
            Add(faces,new(0,-.5f,0),y,x); Add(faces,new(0,.5f,0),topX,topY);
        }
        return faces.ToArray();
    }

    private static Triangle3[] MakePyramid()
    {
        var faces = new List<Triangle3>(6);
        var a = new Vector3(-.5f,-.5f,-.5f); var b = new Vector3(.5f,-.5f,-.5f);
        var c = new Vector3(.5f,-.5f,.5f); var d = new Vector3(-.5f,-.5f,.5f);
        var top = new Vector3(0,.5f,0);
        Add(faces,a,b,c); Add(faces,a,c,d);
        Add(faces,a,b,top); Add(faces,b,c,top); Add(faces,c,d,top); Add(faces,d,a,top);
        return faces.ToArray();
    }
}
