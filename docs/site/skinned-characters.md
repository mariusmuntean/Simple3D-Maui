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
| Node hierarchy (TRS or matrix), skins with inverse bind matrices | Morph targets, animations, cameras, lights |
| VRM 0.x / 1.0 humanoid bone names (`GltfModel.HumanoidBones`) | External or data URIs, sparse accessors, Draco/meshopt (rejected) |

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

## Rendering cost

| Renderer | What a new pose costs |
| --- | --- |
| Metal / OpenGL ES 3 | The bind-pose mesh uploads once and is shared by every pose and instance. Each frame uploads one joint palette (64 bytes per joint) per skinned instance; vertex shaders blend the joints. |
| Software | The posed mesh is skinned on the CPU once per pose, then rasterized as usual. |

Picking and `Scene.GetBounds` use the posed geometry and skin on the CPU on demand. Lighting skins normals by the blended joint matrix, which is exact for rotations and uniform scale.

Measured with the 3,330-triangle, 65-joint Mirror character on an Apple M-series Mac (Release, CPU time only): tracking, retargeting and posing take 0.02 ms per frame, preparing a 720×1280 GPU frame 0.07 ms, and a 360×640 software frame including CPU skinning 16 ms. Native probes check skinned GPU output against the software renderer, up to 128 joints, and that a pose change uploads no vertex data.
