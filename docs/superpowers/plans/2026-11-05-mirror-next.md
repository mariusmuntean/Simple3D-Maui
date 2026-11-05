# Mirror: remaining work (handoff, 2026-11-05)

Branch `feat/mirror-pose-character`; everything is committed and nothing is pushed. Read `docs/HANDOFF.md`, the Mirror sections of `docs/DEVELOPMENT.md` and `docs/superpowers/specs/2026-11-05-mirror-design.md` first.

## Where things stand

- **Gallery page:** `samples/Simple3D.Demo/Mirror/MirrorPage.cs`. One chosen VRM character copies body and face from the camera. The ☺ picker offers cat, Juanita, CosmicBot and Froggy (`MirrorModels.cs`), with an animated swap.
- **Portable logic** in `samples/Simple3D.Shared/Mirror`:
  - `PoseTracker`: visibility hysteresis, an outlier gate and per-person brow/smile calibration.
  - `HumanoidRetargeter`: spine bend, facing, a face-only mode, and limbs that tilt out of the image plane to clear the torso.
  - `MirrorSession`: eases the shown pose toward each tracked pose at the display rate through `Present(dt)`.
  - `FaceGeometry`.
- **Platform code** in `samples/Simple3D.Demo/Mirror/{Apple,Android}`: Vision body and face on Apple; ML Kit accurate pose plus face detection on Android.
- **Library additions:** skinned meshes; morph targets (CPU, Metal, GLES); `GltfReader` with VRM expressions (`GltfModel.ApplyExpressions`); `FrameClock`, which ticks at the display rate (capped at 240).
- **Tests:** Core 80/80 plus the Mirror suites. These include smoothness at 120 Hz (p99 6.8°, max 16°) and the face end-to-end test on the `face-46379`, `face-11519` and `face-43829` fixtures: mouth 1.00 vs 0.05, blinks r=0.93, a right-eye wink, and head tilt, turn and nod each with r ≥ 0.98. MAUI tests 57/57.

## Remaining

1. **Landscape mode.** Arms leave the frame in portrait, so the page needs to work in landscape:
   - a responsive layout: camera inset, pills, buttons;
   - stage camera framing that adapts to wide viewports;
   - orientation-aware capture. Apple `CameraPoseSource` pins `VideoOrientation = Portrait`; it must follow the interface orientation for both the analysis output and the preview layer. On Android, the CameraX target rotation must follow the display (`MainActivity` handles orientation itself).
   - Verify that landmarks stay upright in landscape.
2. **Library vs sample review.** Move code to its right place without putting sample-only code in the library. Candidates: the facing detection in `MirrorCharacter.FromVrm`, `OneEuroFilter`. Make the scenes easy to reproduce (`docs/site/examples.md`, `docs/site/skinned-characters.md`).
3. **Validate before deploying.**
   - All test suites.
   - The iOS simulator and Android GPU probes (see `DEVELOPMENT.md`; build iOS with `-p:ValidateXcodeVersion=false`; Android with `-r android-arm64` on AVD `Simple3D_Test_API35`, port 5556).
   - The Mirror page end to end: `scripts/mirror-video-check.sh` on the iOS simulator, and on Android with the `simple3d_mirror_video` intent extra.
   - Mac Catalyst probes need an unlocked screen.
   - Delete stale `obj/**/AppManifest.plist` files after editing `Info.plist`.
4. **Deploy to the iPhone 17 Pro** (`00008150-001545143C82401C`).
   - Build with `-r ios-arm64 -p:ValidateXcodeVersion=false -p:CodesignKey="Apple Development: Marius Muntean (7TWY7D6QGD)" -p:CodesignProvision="iOS Team Provisioning Profile: *"`.
   - Install with `xcrun devicectl device install app` and start with `device process launch --terminate-existing`.
   - If the app aborts in `load_aot_module`, delete `obj` and `bin` for `net10.0-ios/ios-arm64` and rebuild.
5. **Update the changelog, README and docs.**

## Known gaps

- No CC0 model has a `surprised` expression.
- The iOS simulator can't run Vision body or face landmarks; replay recorded landmarks there.
- Mixkit test clips are downloaded at test time and never committed.
