#!/usr/bin/env bash
# Restore independent consumers from packed libraries, never project references.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
packages="${1:-$root/artifacts/packages}"
packages="$(cd "$packages" && pwd -P)"
consumer="${PACKAGE_CONSUMER_DIRECTORY:-$(mktemp -d "${TMPDIR:-/tmp}/simple3d-consumer.XXXXXX")}"
mkdir -p "$consumer"
consumer="$(cd "$consumer" && pwd -P)"
cp -R "$root/tests/PackageConsumer/Core" "$root/tests/PackageConsumer/Maui" "$consumer/"
cp -R "$root/samples/Simple3D.Demo/Platforms" "$root/samples/Simple3D.Demo/Resources" "$consumer/Maui/"
cat > "$consumer/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear/><add key="preview" value="$packages"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <config><add key="globalPackagesFolder" value="$consumer/packages"/></config>
</configuration>
EOF
"$dotnet_command" restore "$consumer/Core" --configfile "$consumer/NuGet.Config"
"$dotnet_command" run --project "$consumer/Core" -c Release --no-restore -- "$packages"
echo "Independent consumer: $consumer/Maui/PackageDemo.csproj"
if [[ "${BUILD_PACKAGE_MAUI:-false}" == true ]]; then
    "$dotnet_command" restore "$consumer/Maui" --configfile "$consumer/NuGet.Config" -p:Configuration=Release
    "$dotnet_command" build "$consumer/Maui" -c Release -f net10.0-maccatalyst -r maccatalyst-arm64 -p:EnableCodeSigning=true -p:CodesignKey=-
    "$dotnet_command" build "$consumer/Maui" -c Release -f net10.0-ios -r iossimulator-arm64 -p:EnableCodeSigning=true -p:CodesignKey=-
    "$dotnet_command" build "$consumer/Maui" -c Release -f net10.0-android -r android-arm64
fi
