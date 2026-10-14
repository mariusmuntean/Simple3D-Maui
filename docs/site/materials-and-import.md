# Materials, lighting, and model import

These APIs are available in the current source version; they are not included in the published `0.1.0-preview.1` package. Use a source build until a release includes them.

## Textures

`Texture2D` copies opaque `0xFFRRGGBB` pixels in row order, starting at the top-left. UV coordinates use the usual bottom-left origin: U increases rightward and V upward. Nearest sampling and clamped edges are the defaults. Bilinear filtering blends four texels; repeat addressing tiles on both axes, including negative coordinates.

```csharp
var texture = new Texture2D(2, 2,
    [0xFFFFC070, 0xFF749DFF, 0xFF749DFF, 0xFFFFC070],
    TextureFilter.Bilinear, TextureAddressMode.Repeat);
var box = Shape.Box().WithMaterial(new Material(texture));
scene.Add(box);
```

A texture material defaults to white, preserving the image colors. Its optional `color` multiplies the image, and `lit: false` displays it without lighting. Box and pyramid faces use planar UVs; spheres use longitude/latitude UVs; cylinders use wrapped side UVs and planar caps. Texture-ready primitive geometry is cached and shared. Use repeat addressing for tiled or seamless wrapped textures.

Custom meshes supply one `Vector2` texture coordinate per vertex. Duplicate vertices wherever a UV seam needs different coordinates. A textured material on a mesh without UVs throws immediately. Textured groups are rejected: apply materials to their leaves.

MAUI applications can decode PNG/JPEG and other Skia-supported images without adding a dependency:

```csharp
using var stream = await FileSystem.OpenAppPackageFileAsync("panel.png");
var texture = TextureLoader.Load(stream, TextureFilter.Bilinear);
var panel = Shape.Box().WithMaterial(new Material(texture, lit: false));
```

`TextureLoader.Load` is synchronous and leaves the stream open. Load assets once, outside animation/render callbacks; decode large images off the UI thread, then assign shapes on the UI thread. Only the first encoded image frame is loaded. Images must be fully opaque; transparent images are rejected. Dimensions are checked before decode and cannot exceed 4096 × 4096. Core itself has no image codec or file IO dependency.

## Directional and ambient light

```csharp
scene.Light = new DirectionalLight(
    new Vector3(.4f, -1, -.7f), // direction rays travel toward the scene
    color: 0xFFFFE9CF,
    intensity: .7f);
scene.AmbientLight = .3f;
```

The direction is world-space, finite, nonzero, and normalized on construction. Directional intensity and ambient brightness range from 0 to 1; final color channels saturate at 255. `scene.Light = null` disables directional lighting. Unlit materials ignore both settings. Changes notify `SceneView` automatically. There is one directional light and no shadow pass.

Defaults preserve the original appearance: white light traveling along `(.4, -.8, -1)`, intensity `.62`, ambient `.38`. Lighting is diffuse Lambert shading, with packed RGB arithmetic rather than a physically based, linear-light color pipeline.

## Smooth normals and vertex colors

```csharp
var mesh = new Mesh(
    vertices: [new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(0, 1, 0)],
    indices: [0, 1, 2],
    colors: [0xFFFFAA70, 0xFF70AAFF, 0xFF70DDC0])
    .WithCalculatedNormals();
scene.Add(Shape.FromMesh(mesh, new Material(0xFFFFFFFF)));
```

Optional `normals`, `colors`, and `textureCoordinates` are defensively copied and must each match the vertex count. Normals are normalized and must be finite and nonzero; colors must be opaque. Meshes without normals retain flat shading. Normals enable Gouraud shading: light and vertex color are combined at triangle corners, then interpolated across the surface. Smooth shading avoids increasing triangle count, but does not change silhouettes or add specular highlights.

`WithCalculatedNormals()` returns a new mesh with area-weighted normals averaged at shared indices. Split vertices preserve sharp edges and UV seams; the method does not weld duplicate positions. Unused vertices or normals that cancel use +Z. Imported explicit normals are preferable when a seam should remain smooth. Nonuniform shape and group scales use inverse-transpose normal transforms. If a composed transform has no inverse but leaves a valid triangle, lighting uses its world-space face normal. Two-sided surfaces flip their lighting normals on backfaces.

Colors multiply the material tint and texture. Use white material color to preserve vertex colors. UVs and shaded vertex colors use perspective-correct interpolation and are carried through near-plane clipping. Orthographic cameras interpolate linearly. The [Surface example](examples.md) demonstrates a height gradient on 512 triangles. Its **Shading: smooth / flat** control compares prepared meshes with and without normals; **Light: from right / left** changes the world-space directional light without rebuilding geometry or resetting the orbit camera.

## OBJ geometry

```csharp
using var reader = new StreamReader(modelStream);
var mesh = ObjReader.Read(reader);
var model = Shape.FromMesh(mesh, new Material(0xFFD4DCEF));
scene.Add(model);
```

`ObjReader.Parse(string)` is convenient for small embedded models. The reader supports positions, UVs, normals, positive/negative indices, triangles, and strictly convex planar polygons. It preserves independent position/UV/normal indices by splitting vertices only when attribute tuples differ. Every face corner in an input must use the same attribute layout. Explicit normals control smoothness; call `WithCalculatedNormals()` yourself when appropriate.

Import returns one mesh. Object/group names, material references, and smoothing directives are ignored. MTL files, textures, and other external resources are never opened; assign a material yourself. Lines, points, freeform surfaces, concave/nonplanar polygons, and collinear polygon corners are rejected. Export triangulated geometry with normals and UVs from your modeling tool for predictable results.

Malformed or unsupported input throws `FormatException` with a line number. Input is bounded to 16 Mi characters, 16 Ki characters per line, 256 corners per face, and the mesh's one-million-element limits. Supplied readers remain open. Import once, share the resulting immutable mesh, and keep IO out of animation callbacks.
