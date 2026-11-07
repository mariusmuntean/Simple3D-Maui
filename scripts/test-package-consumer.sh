#!/usr/bin/env bash
# Restore independent consumers from packed libraries, never project references.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
packages="${1:-$root/artifacts/packages}"
packages="$(cd "$packages" && pwd -P)"
shopt -s nullglob
maui_packages=("$packages"/Simple3D.Maui.*.nupkg)
if [[ ${#maui_packages[@]} -ne 1 ]]; then
    echo "Expected exactly one Simple3D.Maui.<version>.nupkg in $packages" >&2
    exit 1
fi
version="${maui_packages[0]##*/Simple3D.Maui.}"
version="${version%.nupkg}"
consumer="${PACKAGE_CONSUMER_DIRECTORY:-$(mktemp -d "${TMPDIR:-/tmp}/simple3d-consumer.XXXXXX")}"
mkdir -p "$consumer"
consumer="$(cd "$consumer" && pwd -P)"
cp -R "$root/tests/PackageConsumer/Core" "$root/tests/PackageConsumer/Maui" "$consumer/"
cp -R "$root/samples/Simple3D.Demo/Platforms" "$root/samples/Simple3D.Demo/Resources" "$consumer/Maui/"
# The same SDK and workload set as the repository.
cp "$root/global.json" "$consumer/"
cat > "$consumer/NuGet.Config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear/><add key="preview" value="$packages"/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources>
  <packageSourceMapping>
    <packageSource key="preview"><package pattern="Simple3D.*"/></packageSource>
    <packageSource key="nuget.org"><package pattern="*"/></packageSource>
  </packageSourceMapping>
  <config><add key="globalPackagesFolder" value="$consumer/packages"/></config>
</configuration>
EOF
"$dotnet_command" restore -p:Simple3DVersion="$version" "$consumer/Core" --configfile "$consumer/NuGet.Config"
"$dotnet_command" run -p:Simple3DVersion="$version" --project "$consumer/Core" -c Release --no-restore -- "$packages" "$version"
echo "Independent consumer: $consumer/Maui/PackageDemo.csproj"
if [[ "${BUILD_PACKAGE_MAUI:-false}" == true ]]; then
    # MAUI_VERSION builds against an older Microsoft.Maui.Controls, e.g. the library's minimum; unset uses the workload's.
    maui=(); [[ -n "${MAUI_VERSION:-}" ]] && maui=("-p:ConsumerMauiVersion=$MAUI_VERSION")
    "$dotnet_command" restore -p:Simple3DVersion="$version" "$consumer/Maui" --configfile "$consumer/NuGet.Config" -p:Configuration=Release ${maui[@]+"${maui[@]}"}
    if [[ "$(uname)" == Darwin ]]; then
        "$dotnet_command" build -p:Simple3DVersion="$version" "$consumer/Maui" -c Release -f net10.0-maccatalyst -r maccatalyst-arm64 ${maui[@]+"${maui[@]}"}
        "$dotnet_command" build -p:Simple3DVersion="$version" "$consumer/Maui" -c Release -f net10.0-ios -r iossimulator-arm64 ${maui[@]+"${maui[@]}"}
    fi
    "$dotnet_command" build -p:Simple3DVersion="$version" "$consumer/Maui" -c Release -f net10.0-android -r android-arm64 ${maui[@]+"${maui[@]}"}
fi
