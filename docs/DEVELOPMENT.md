# Development and native validation

## Toolchain and Rider

Use .NET 10 with MAUI workloads. Check `dotnet --info` and `dotnet workload list`. In Rider, choose that CLI installation and its .NET SDK MSBuild under **Settings → Build, Execution, Deployment → Toolset and Build**. A second installation without workloads can produce missing references in the editor.

Use matching Xcode and Apple workload versions. The locally validated combination is SDK 10.0.401, workload set 10.0.401.1 and Xcode 27. Keep Xcode validation enabled. After a targeted terminal build, run `dotnet restore samples/Simple3D.Demo` before building all platforms in Rider; targeted restores can replace shared runtime assets.

The manual validation workflow pins that SDK and workload set, creating a job-local `global.json` to prevent a newer preinstalled SDK from taking precedence. Apple builds and documentation use the `xcode-27` runner with Xcode 27.0 selected explicitly. Update those versions together after local validation; installing the latest workload against an older Xcode can break an otherwise unchanged build.

Choose the `Simple3D.Demo` configuration with the appropriate platform icon. Android uses the checked-in manifest. If an iOS simulator launch returns `HE0042` / `NSPOSIXErrorDomain code 3`, stop the run and restart that simulator, then retry. Preserve other simulator sessions.

## Local checks

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release
dotnet run --project tests/Simple3D.Maui.Tests -c Release
python3 -m unittest discover -s scripts -p 'test_*.py'
dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --performance
```

The performance runner reports selected animation and pan CPU medians, managed allocations and Core pixel hashes. Compare under similar machine load. It excludes native presentation and does not measure displayed FPS.

Build each native target sequentially. Android Debug uses JIT. Apple Debug compiles rendering libraries and framework assemblies, retaining interpreter support for the demo UI. Clean native outputs when changing interpreter configuration or workloads.

```bash
dotnet build samples/Simple3D.Demo -f net10.0-android -c Debug -p:EmbedAssembliesIntoApk=true
dotnet build samples/Simple3D.Demo -f net10.0-ios -r iossimulator-arm64 -c Debug
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -r maccatalyst-arm64 -c Debug
bash scripts/ios-simulator-smoke.sh --render-probe
```

The Debug native probe checks pixels, picking and retained snapshots for each scene. Its completion marker is required. The iOS script selects an idle supported simulator, then stops its app and shuts down that simulator on completion or failure. Stop any Mac or Android test app after inspection.

Use the current `Simple3D.Demo.app` bundle. An obsolete `Simple3D Gallery.app` output can contain old code.

### Release on macOS

```bash
dotnet restore samples/Simple3D.Demo -p:Configuration=Release
dotnet clean samples/Simple3D.Demo -f net10.0-maccatalyst -c Release
dotnet build samples/Simple3D.Demo -f net10.0-maccatalyst -c Release
open samples/Simple3D.Demo/bin/Release/net10.0-maccatalyst/Simple3D.Demo.app
```

Clean after workload changes or an AOT-module mismatch at startup. A stale `Microsoft.Maui.Controls.Xaml` module caused the observed Release abort; clean single-architecture and universal builds both launched and rendered. Release keeps the default compiled runtime.

Run `DOTNET_COMMAND="$HOME/.dotnet/dotnet" bash scripts/mac-release-smoke.sh` to clean, build and test the Release renderer. The script enables the native probe for that build, requires its completion marker and stops its own process on failure or timeout. Ordinary Release builds exclude the probe; clean and rebuild without `EnableNativeRenderProbe` for distribution. This checks native rendering, not the gallery UI or displayed frame rate.

## Images and documentation

The Core test runner also exports gallery still frames, without running tests:

```bash
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images --all output
dotnet run --project tests/Simple3D.Core.Tests -c Release -- --render-images Equipment equipment.ppm
```

The output format is binary PPM. Install Pillow and run `python3 scripts/render-doc-images.py` to regenerate documentation PNGs. Both the exporter and app use the five factories in `samples/Simple3D.Shared/`. Retired gallery scenes remain in `tests/Shared/RegressionScenes.cs` to preserve renderer and allocation coverage; the performance runner includes that larger corpus.

```bash
dotnet tool install docfx --tool-path .tools --version 2.81.0
.tools/docfx metadata docs/site/docfx.json --warningsAsErrors
.tools/docfx build docs/site/docfx.json --warningsAsErrors
python3 -m http.server 8000 --directory _site
```

DocFX needs the workload-enabled `dotnet` first in `PATH`; set `DOTNET_ROOT` if you have multiple installations. The built `_site` contains user documentation, images and generated API reference. Contributor setup and package validation remain in the repository's `docs` directory.

After reviewing `_site`, run `bash scripts/publish-docs.sh`. It publishes generated files to `gh-pages` through a temporary checkout and a normal push. It preserves the source checkout and cleans up its temporary directory. GitHub Pages serves the branch root; `.nojekyll` keeps DocFX assets intact. This triggers only the Pages deployment, not the manual MAUI validation workflow.

To test a Release iOS simulator build, use `BUILD_CONFIGURATION=Release bash scripts/ios-simulator-smoke.sh --render-probe` after building with `-p:EnableNativeRenderProbe=true`. Remove that property and rebuild for distribution. If ordinary Clean leaves stale registrar or SDK compiler cache files, move only the affected generated `bin/Release` and `obj/Release` target directories aside and rebuild.
