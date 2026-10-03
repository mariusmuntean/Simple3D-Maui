# Packaging and consumer validation

The preview version is `0.1.0-preview.1`. Packages are not yet published to NuGet.org. The MAUI package depends on Core, so applications install `Simple3D.Maui` alone.

## Build packages

Use a clean committed checkout and .NET 10 with Android, iOS and Mac Catalyst workloads. Pack on macOS to include all three MAUI frameworks. Linux produces Android assets only and must not supply the public multi-platform package.

```bash
dotnet pack src/Simple3D.Core -c Release -p:ContinuousIntegrationBuild=true -o artifacts/packages
dotnet pack src/Simple3D.Maui -c Release -p:ContinuousIntegrationBuild=true -p:EnableCodeSigning=false -o artifacts/packages
```

Each package includes license metadata, repository commit, README, icon and XML documentation. Matching `.snupkg` files contain portable PDBs. .NET 10 supplies [Source Link for GitHub](https://github.com/dotnet/sourcelink). Deterministic compilation and CI path normalization remove machine-specific source paths; [SDK package validation](https://learn.microsoft.com/dotnet/fundamentals/package-validation/overview) checks asset compatibility after packing.

Source Link points to the exact Git commit. Push that commit before publishing packages. Building changed tracked source against an older commit gives misleading source links.

## Verify outside the checkout

```bash
BUILD_PACKAGE_MAUI=true bash scripts/test-package-consumer.sh
```

The script creates consumers outside this repository and restores into an isolated package cache through a local feed and NuGet.org. It uses only `PackageReference`. The Core consumer checks pixels, picking and retained snapshots, then inspects metadata, XML docs, symbols and Source Link in both packages. The optional MAUI step builds Release for Mac Catalyst, iOS simulator and Android; it requires macOS and native workloads.

The script prints and retains the consumer path for native inspection. Run its `PackageDemo.app` to check presentation and input. Its loaded view checks native pixels and picking and prints `PACKAGE_MAUI_PASS`. Stop the app afterward and remove the temporary consumer when no longer needed.

The fixture copies gallery platform bootstrap and icon resources. It neither references library projects nor compiles library source. Core arrives transitively through the MAUI package.

## Publish a preview

1. Run portable tests, package validation and independent consumers.
2. Launch a packaged native consumer; record device and configuration.
3. Inspect dependencies and all three MAUI framework assets. Publish matching symbols.
4. Push the exact source commit; tag the verified preview and write release notes.
5. Publish `.nupkg` and `.snupkg` files with the maintainer's NuGet.org credentials.
6. Restore a fresh consumer from NuGet.org, then update installation instructions and badges.

Do not claim public NuGet availability before step 6. Source Link enables debugging when the IDE supports external sources and NuGet symbols; a verified PDB mapping does not establish Rider breakpoint behavior.
