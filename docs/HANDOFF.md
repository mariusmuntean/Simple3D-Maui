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

Documentation lives at https://mariusmuntean.github.io/Simple3D-Maui/. The tag-driven release workflow builds and publishes it through `scripts/publish-docs.sh` after NuGet publication.

The next release is `0.3.0`; local builds default to `0.3.0-dev`, and release tags supply the package version. Follow the [release flow](PACKAGING.md#release), and record tested platforms and configurations in release notes. Local packing and consumer validation do not imply NuGet.org publication.

## Keep the docs in sync

Every change to the library's capabilities updates its docs page, the README's capability table and the changelog in the same commit. The README and the docs landing page lead with the Mirror showcase (`docs/site/images/mirror.gif`). Regenerate it with `dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-showcase out` and then `python3 scripts/mirror-showcase.py out docs/site/images/mirror.gif`.
