#!/usr/bin/env bash
# Runs the Mirror page on an iOS simulator with a video instead of the camera, takes timed screenshots and
# collects the page's MIRROR_* log lines. Builds nothing: build the Debug iOS simulator app first.
#   bash scripts/mirror-video-check.sh <simulator-udid> <video.mp4> <landmarks.jsonl> <output-dir> [seconds...]
# The simulator cannot run Apple's body-pose model, so the recorded landmarks (scripts/extract-pose-landmarks.swift)
# replace detection; video decoding, the page states, retargeting and rendering run for real.
set -euo pipefail
device="$1"; video="$2"; landmarks="$3"; output="$4"; shift 4
shots=("${@:-2 6 12 16 17.5 21 27}")
cd "$(dirname "$0")/.."
app=samples/Simple3D.Demo/bin/Debug/net10.0-ios/iossimulator-arm64/Simple3D.Demo.app
[ -d "$app" ] || { echo "Build the Debug iOS simulator app first: $app" >&2; exit 1; }
mkdir -p "$output"
booted=false
if xcrun simctl list devices | grep "$device" | grep -q Shutdown; then xcrun simctl boot "$device"; booted=true; fi
cleanup() {
    xcrun simctl terminate "$device" dev.simple3d.gallery 2>/dev/null || true
    if "$booted"; then xcrun simctl shutdown "$device" || true; fi
}
trap cleanup EXIT
xcrun simctl bootstatus "$device" -b >/dev/null
xcrun simctl install "$device" "$app"
container="$(xcrun simctl get_app_container "$device" dev.simple3d.gallery data)"
cp "$video" "$container/tmp/mirror.mp4"; cp "$landmarks" "$container/tmp/mirror.jsonl"
SIMCTL_CHILD_SIMPLE3D_MIRROR_VIDEO="$container/tmp/mirror.mp4" SIMCTL_CHILD_SIMPLE3D_MIRROR_LANDMARKS="$container/tmp/mirror.jsonl" \
    xcrun simctl launch --terminate-running-process --console "$device" dev.simple3d.gallery > "$output/console.log" 2>&1 &
python3 - "$device" "$output" ${shots[@]} <<'PY'
import subprocess, sys, time
device, output, *times = sys.argv[1:]
start = time.time()
for t in map(float, times):
    time.sleep(max(0, t - (time.time() - start)))
    subprocess.run(["xcrun", "simctl", "io", device, "screenshot", f"{output}/shot-{t:04.1f}.png"], capture_output=True)
PY
grep -oE 'MIRROR_[A-Z]+.*' "$output/console.log" || true
