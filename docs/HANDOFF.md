# Contributor handoff

Simple3D is a public MIT-licensed .NET 10 library with portable Core and a MAUI control for Android, iOS and Mac Catalyst. The gallery has five teaching scenes; tests retain additional regression workloads. Start changes from updated `main`; `gh-pages` contains generated documentation and is not a development branch.

Read [architecture](site/architecture.md), [development](DEVELOPMENT.md) and [package validation](PACKAGING.md) before changing rendering, platform configuration or distribution.

## Invariants to preserve

- Keep Core independent of MAUI and SkiaSharp.
- Keep owned snapshots valid across later renders; invalidate reusable picking after failed renders.
- Preserve depth-aware picking, immutable mesh reuse and stable logical child order during animation.
- Bound traversal and raster work. CPU timings do not establish displayed FPS.
- Validate portable tests and actual native builds separately. Stop test apps and devices you started.

## Distribution

Documentation lives at https://mariusmuntean.github.io/Simple3D-Maui/. Build locally and publish through `scripts/publish-docs.sh`. Native hosted validation is manual to control Actions costs.

Preview packages use version `0.1.0-preview.1`. Local packing and consumer validation do not imply NuGet.org publication. Follow the package checklist before publishing, and record tested platforms and configurations in release notes.
