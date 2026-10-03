#!/usr/bin/env bash
set -euo pipefail

device="$(python3 - <<'PY'
import json, subprocess
devices = json.loads(subprocess.check_output(['xcrun', 'simctl', 'list', 'devices', 'available', '-j']))['devices']
for name in ('iPhone 17 Pro', 'iPhone 18 Pro', 'iPhone 17'):
    matches = [(runtime, item['udid']) for runtime, items in devices.items() for item in items
               if item['name'] == name and item['state'] == 'Shutdown']
    if matches:
        print(max(matches)[1])
        break
else:
    raise SystemExit('No idle supported iPhone simulator; existing sessions are left untouched')
PY
)"

app="$(find samples/Simple3D.Demo/bin/Debug/net10.0-ios/iossimulator-arm64 \
    -maxdepth 2 -type d -name Simple3D.Demo.app -print -quit)"
test -n "$app" || { echo 'Built demo app bundle not found' >&2; exit 1; }
codesign --verify --deep --strict --verbose=2 "$app"

cleanup() {
    local result=$?
    trap - EXIT INT TERM
    xcrun simctl terminate "$device" dev.simple3d.gallery 2>/dev/null || true
    if ! xcrun simctl shutdown "$device"; then
        echo "Could not shut down test simulator $device" >&2
        if [ "$result" -eq 0 ]; then result=1; fi
    fi
    exit "$result"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

xcrun simctl boot "$device"
xcrun simctl bootstatus "$device" -b
xcrun simctl install "$device" "$app"
mkdir -p artifacts
for scene in Equipment Packing Surface; do
    xcrun simctl terminate "$device" dev.simple3d.gallery 2>/dev/null || true
    SIMCTL_CHILD_SIMPLE3D_GALLERY_SCENE="$scene" xcrun simctl launch "$device" dev.simple3d.gallery
    visible=false
    for attempt in 1 2 3; do
        sleep 10
        xcrun simctl io "$device" screenshot "artifacts/ios-gallery-$scene.png"
        if swift scripts/check-gallery-screenshot.swift "artifacts/ios-gallery-$scene.png"; then
            visible=true
            break
        fi
        echo "$scene gallery is not visible after attempt $attempt" >&2
    done
    if [ "$visible" = false ]; then break; fi
done
if [ "$visible" = true ] && swift scripts/check-gallery-scenes.swift \
    artifacts/ios-gallery-{Equipment,Packing,Surface}.png; then
    exit 0
fi

echo 'Recent gallery simulator logs:' >&2
xcrun simctl spawn "$device" log show --last 5m --style compact \
    --predicate 'process CONTAINS "Simple3D"' 2>&1 | tail -100 || true
echo 'Recent gallery crash reports:' >&2
find "$HOME/Library/Logs/DiagnosticReports" -maxdepth 1 -iname '*Simple3D*' -mmin -15 \
    -print -exec head -100 {} \; 2>/dev/null || true
exit 1
