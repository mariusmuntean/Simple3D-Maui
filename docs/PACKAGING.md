# Packaging and consumer validation

The preview version is `0.1.0-preview.1` on NuGet.org. The MAUI package depends on Core, so applications install `Simple3D.Maui` alone.

```bash
dotnet add YourApp.csproj package Simple3D.Maui --version 0.1.0-preview.1
```

Core-only applications install `Simple3D.Core` instead. See [Getting started](site/getting-started.md) for platform requirements, SkiaSharp registration and the first scene. The local-feed instructions below support development builds.

## Build packages

Use a clean committed checkout and .NET 10 with Android, iOS and Mac Catalyst workloads. Pack on macOS to include all three MAUI frameworks. Linux produces Android assets only and must not supply the public multi-platform package.

```bash
dotnet pack src/Simple3D.Core -c Release -p:ContinuousIntegrationBuild=true -o artifacts/packages
dotnet pack src/Simple3D.Maui -c Release -p:ContinuousIntegrationBuild=true -p:EnableCodeSigning=false -o artifacts/packages
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
    <add key="preview" value="/absolute/path/to/Simple3D-Maui/artifacts/packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

```bash
dotnet add YourApp.csproj package Simple3D.Maui --version 0.1.0-preview.1 --no-restore
dotnet restore YourApp.csproj
```

Keep the feed configured for subsequent builds and IDE restores. Core-only apps use `Simple3D.Core` in the same commands. Continue with [SkiaSharp registration and the first scene](site/getting-started.md). If you rebuild the same preview version, use a fresh package cache for validation; NuGet may otherwise reuse an earlier local build.

## Verify outside the checkout

```bash
BUILD_PACKAGE_MAUI=true bash scripts/test-package-consumer.sh
```

The script creates consumers outside this repository and restores into an isolated package cache through a local feed and NuGet.org. It uses only `PackageReference`. The Core consumer checks pixels, picking and retained snapshots, then inspects metadata, XML docs, symbols and Source Link in both packages. The Source Link check downloads source and compares its SHA-256 checksum with the PDB, so push the packed source commit before running it. The optional MAUI step builds Release for Mac Catalyst, iOS simulator and Android; it requires macOS and native workloads.

The script prints and retains the consumer path for native inspection. Run its `PackageDemo.app` to check presentation and input. Its loaded view checks native pixels and picking and prints `PACKAGE_MAUI_PASS`. Stop the app afterward and remove the temporary consumer when no longer needed.

The fixture copies gallery platform bootstrap and icon resources. It neither references library projects nor compiles library source. Core arrives transitively through the MAUI package.

## Publish a preview

The manual-only [`publish-preview.yml`](../.github/workflows/publish-preview.yml) publishes the validated `0.1.0-preview.1` artifacts attached to the GitHub preview release, including matching symbols. It runs on `main`, checks four committed SHA-256 hashes before authentication, and uses a standard Ubuntu runner. Public repositories do not consume private-repository Actions minutes. It does not rebuild packages or run the native validation matrix.

Configure a NuGet.org trusted publishing policy for owner `marius.muntean`, GitHub owner `mariusmuntean`, repository `Simple3D-Maui`, workflow `publish-preview.yml`, and no environment. Allow pushing new packages and versions for the exact IDs `Simple3D.Core` and `Simple3D.Maui`; do not grant unlist/relist access. The workflow uses a short-lived key from [NuGet trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no long-lived API key belongs in GitHub secrets. Dispatch it only after the release artifacts and their source commit have passed the checks below. For another version, update the filenames, release tag and hashes after validating that version; this workflow deliberately publishes only this preview.

1. Commit the intended source, run portable tests, push the commit, then run package validation and independent consumers.
2. Launch a packaged native consumer; record device and configuration.
3. Inspect dependencies and all three MAUI framework assets. Publish matching symbols.
4. Tag the verified source commit and publish a GitHub prerelease with the validated package artifacts. Keep its notes clear about NuGet availability pending public restore. The Action's read-only token cannot download a draft release.
5. Publish `.nupkg` and `.snupkg` files with the maintainer's NuGet.org credentials.
6. Restore a fresh consumer from NuGet.org, then update installation instructions and badges.

Do not claim public NuGet availability before step 6. Source Link enables debugging when the IDE supports external sources and NuGet symbols; a verified PDB mapping does not establish Rider breakpoint behavior.
