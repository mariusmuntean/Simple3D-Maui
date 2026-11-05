# Skinned characters and glTF

Load a rigged glTF or VRM character and pose it from code: from a camera, a data stream or your own animation. The [Mirror example](examples.md#mirror) drives a character from a person's body pose.

## Load a model

`GltfLoader` reads a binary glTF 2.0 file (`.glb`, and VRM, which is GLB with a humanoid bone map) and decodes its textures:

```csharp
await using var stream = await FileSystem.OpenAppPackageFileAsync("froggy.vrm");
var character = await GltfLoader.LoadAsync(stream);   // parsing and texture decoding run off the UI thread
scene.Add(character.Shape);
var skeleton = character.Model.Skeletons[0];
```

Core's `GltfReader.Read` does the same without decoding images, for headless rendering and tests: pass a decoder to `GltfModel.CreateShape`, or textured materials keep only their base color.

Coordinates stay as authored: right-handed, +Y up, counterclockwise front faces. VRM 0.x models face −Z and VRM 1.0 models face +Z; Simple3D's default camera looks along −Z, so turn a VRM 0.x model with `Rotated(0, MathF.PI, 0)` to face it.

| Supported | Ignored or rejected |
| --- | --- |
| Triangle meshes, indices, normals, UV0, vertex colors | Points, lines, strips (skipped) |
| Base color factor and texture; `doubleSided`; `KHR_materials_unlit` | PBR maps, alpha blending (drawn opaque), texture transforms |
| Node hierarchy (TRS or matrix), skins with inverse bind matrices | Animations, cameras, lights |
| Morph targets on skinned primitives (POSITION/NORMAL, dense or sparse, `targetNames`, default `weights`); VRM 0.x / 1.0 expressions | Morph targets on unskinned primitives, TANGENT deltas, expression material binds |
| VRM 0.x / 1.0 humanoid bone names (`GltfModel.HumanoidBones`) | External or data URIs, sparse accessors outside morph targets, Draco/meshopt (rejected) |

Input is bounded (64 MiB, `Mesh.MaximumElements`, node count and depth); malformed files throw `InvalidDataException`. Degenerate triangles are dropped.

## Pose a skinned shape

A `SkinnedMesh` deforms bind-pose geometry with a `Skeleton` of up to 128 joints, each vertex blending up to four. A `SkeletonPose` sets every joint's local rotation, plus an offset for the root joints:

```csharp
var rotations = skeleton.RestPose.Rotations.ToArray();
var elbow = model.HumanoidBones["leftLowerArm"];
rotations[elbow] = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, .8f) * rotations[elbow];
var waved = character.Shape.WithPose(SkeletonPose.Create(skeleton, rotations));
scene.Replace(character.Shape, waved);
```

Poses and shapes are immutable like the rest of the scene: `WithPose` returns a copy, updating every skinned descendant that uses the pose's skeleton, and `Scene.Replace` redraws. `SkeletonPose.WorldTransforms` gives joint transforms, for example to attach a prop to a hand.

## Expressions and morph targets

A `SkinnedMesh` carries up to 64 morph targets (blend shapes), blended before skinning. Set raw weights with `Shape.WithMorphWeights(mesh, weights)`, or drive VRM expressions by name:

```csharp
var face = new Dictionary<string, float> { ["joy"] = .8f, ["a"] = mouthOpen, ["blink_l"] = 1 };
scene.Replace(current, current = character.Model.ApplyExpressions(current, face));
```

`GltfModel.Expressions` is keyed by lowercase VRM 0.x preset (`joy`, `angry`, `sorrow`, `fun`, `a`…`o`, `blink`, `blink_l`, `blink_r`, `lookup`…) or the custom name; VRM 1.0 presets keep their names and also answer to the VRM 0.x one (`happy`→`joy`, `aa`→`a`, `blinkLeft`→`blink_l`…). `ApplyExpressions` sums intensity × bind weight per target, clamps to [0,1], keeps pose and transforms, ignores unknown names, and replaces every weight of the meshes it touches, so pass the whole face each frame. Which presets exist depends on the model.

## Rendering cost

| Renderer | What a new pose costs |
| --- | --- |
| Metal / OpenGL ES 3 | The bind-pose mesh uploads once and is shared by every pose and instance. Each frame uploads one joint palette (64 bytes per joint) per skinned instance; vertex shaders blend the joints. |
| Metal / OpenGL ES 3, morphs | Deltas (32 bytes per target per source vertex) upload once per mesh: a Metal buffer, or an RGBA32F texture on OpenGL ES. A weight change uploads 80 bytes of per-item constants; the vertex shader adds the 8 largest nonzero weights (more are dropped on the GPU; the software renderer blends all). |
| Software | The posed mesh is morphed and skinned on the CPU once per pose or weight change, then rasterized as usual. |

Picking and `Scene.GetBounds` use the posed geometry and skin on the CPU on demand. Lighting skins normals by the blended joint matrix, which is exact for rotations and uniform scale.

Measured with the 3,330-triangle, 65-joint Mirror character on an Apple M-series Mac (Release, CPU time only): tracking, retargeting and posing take 0.02 ms per frame, preparing a 720×1280 GPU frame 0.07 ms, and a 360×640 software frame including CPU skinning 16 ms. Native probes check skinned GPU output against the software renderer, up to 128 joints, morphs at several weights on a bent pose, and that a pose or weight change uploads no vertex or delta data.
