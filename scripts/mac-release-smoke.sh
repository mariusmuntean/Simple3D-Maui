#!/usr/bin/env bash
# Build and exercise the Release renderer in the real Mac Catalyst runtime.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet_command="${DOTNET_COMMAND:-dotnet}"
case "$(uname -m)" in
  arm64) runtime=maccatalyst-arm64 ;;
  x86_64) runtime=maccatalyst-x64 ;;
  *) echo "Unsupported Mac architecture" >&2; exit 2 ;;
esac
mkdir -p artifacts
app="samples/Simple3D.Demo/bin/Release/net10.0-maccatalyst/$runtime/Simple3D.Demo.app/Contents/MacOS/Simple3D.Demo"
log_path=artifacts/mac-release-render-probe.log
owned_pid=
cleanup() {
  if [[ -n "$owned_pid" ]] && kill -0 "$owned_pid" 2>/dev/null; then
    kill -TERM "$owned_pid" 2>/dev/null || true
    wait "$owned_pid" 2>/dev/null || true
  fi
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
"$dotnet_command" restore samples/Simple3D.Demo -p:Configuration=Release
"$dotnet_command" clean samples/Simple3D.Demo -f net10.0-maccatalyst -r "$runtime" -c Release
"$dotnet_command" build samples/Simple3D.Demo -f net10.0-maccatalyst -r "$runtime" -c Release -p:EnableNativeRenderProbe=true
SIMPLE3D_RENDER_PROBE=1 "$app" > "$log_path" 2>&1 &
owned_pid=$!
for ((attempt=0; attempt<120; attempt++)); do
  if ! kill -0 "$owned_pid" 2>/dev/null; then
    wait "$owned_pid"
    owned_pid=
    if ! grep -q NATIVE_RENDER_PROBE_PASS "$log_path"; then
      cat "$log_path" >&2
      exit 1
    fi
    cat "$log_path"
    exit 0
  fi
  sleep .25
done
echo "Release render probe timed out; see $log_path" >&2
exit 1
