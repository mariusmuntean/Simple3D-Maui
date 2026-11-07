# Packaging and consumer validation

The next release is `0.3.0`. The MAUI package depends on Core, so applications install `Simple3D.Maui` alone.

```bash
dotnet add YourApp.csproj package Simple3D.Maui --version 0.3.0
```

Core-only applications install `Simple3D.Core` instead. See [Getting started](site/getting-started.md) for platform requirements, SkiaSharp registration and the first scene. The local-feed instructions below support development builds.

## Build packages

Use a clean committed checkout and .NET 10 with Android, iOS and Mac Catalyst workloads. Pack on macOS to include all three MAUI frameworks. Linux produces Android assets only and must not supply the public multi-platform package.

```bash
dotnet pack src/Simple3D.Core -c Release -p:Version=0.3.0 -p:ContinuousIntegrationBuild=true -o artifacts/packages
dotnet pack src/Simple3D.Maui -c Release -p:Version=0.3.0 -p:ContinuousIntegrationBuild=true -o artifacts/packages
```

Each package includes license metadata, repository commit, README, icon and XML documentation. Matching `.snupkg` files contain portable PDBs. .NET 10 supplies [Source Link for GitHub](https://github.com/dotnet/sourcelink). Deterministic compilation and CI path normalization remove machine-specific source paths; [SDK package validation](https://learn.microsoft.com/dotnet/fundamentals/package-validation/overview) checks asset compatibility after packing.

Source Link points to the exact Git commit. Push that commit before publishing packages. Building changed tracked source against an older commit gives misleading source links.

## Install from the local feed

In an existing .NET 10 MAUI app, keep only supported targets (Android, iOS and Mac Catalyst), then add the package without restoring. Put this `NuGet.Config` beside the app's project or solution, replacing the local path:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="/absolute/path/to/Simple3D-Maui/artifacts/packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

```bash
dotnet add YourApp.csproj package Simple3D.Maui --version 0.3.0 --no-restore
dotnet restore YourApp.csproj
```

Keep the feed configured for subsequent builds and IDE restores. Core-only apps use `Simple3D.Core` in the same commands. Continue with [SkiaSharp registration and the first scene](site/getting-started.md). If you rebuild the same package version, use a fresh package cache for validation; NuGet may otherwise reuse an earlier local build.

## Verify outside the checkout

```bash
BUILD_PACKAGE_MAUI=true bash scripts/test-package-consumer.sh
```

The script creates consumers outside this repository and restores into an isolated package cache through a local feed and NuGet.org. It uses only `PackageReference`. The Core consumer checks pixels, picking and retained snapshots, then inspects metadata, XML docs, symbols and Source Link in both packages. The Source Link check downloads source and compares its SHA-256 checksum with the PDB, so push the packed source commit before running it. The optional MAUI step builds Release for Mac Catalyst, iOS simulator and Android; it requires macOS and native workloads.

The script prints and retains the consumer path for native inspection. Run its `PackageDemo.app` to check presentation and input. Its loaded view checks native pixels and picking and prints `PACKAGE_MAUI_PASS`. Stop the app afterward and remove the temporary consumer when no longer needed.

The fixture copies gallery platform bootstrap and icon resources. It neither references library projects nor compiles library source. Core arrives transitively through the MAUI package.

## Release

Push a committed changelog heading `## X.Y.Z · YYYY-MM-DD` with a real release date, then push the matching tag (for example, `git tag v0.3.0 && git push origin v0.3.0`). The [`release.yml`](../.github/workflows/release.yml) workflow takes the package version from the tag, runs tests, packs and validates both packages on macOS, attests the packages, and creates a GitHub Release with changelog notes and package/symbol assets. Versions containing `-` create prereleases.

One-time setup: create the GitHub environment `release` with a required reviewer. Configure a [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing) policy for NuGet owner `marius.muntean`, GitHub owner `mariusmuntean`, repository `Simple3D-Maui`, workflow `release.yml`, and environment `release`, allowing the exact package IDs `Simple3D.Core` and `Simple3D.Maui`. No long-lived API key is needed.

Approve the `release` environment deployment to publish Core, then MAUI, with matching symbols. After publication succeeds, the workflow builds DocFX and pushes the site to `gh-pages`. Restore a fresh consumer from NuGet.org before claiming public availability; a GitHub Release or successful local validation alone does not establish it.
