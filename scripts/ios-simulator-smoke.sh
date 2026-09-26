#!/usr/bin/env bash
set -euo pipefail

device="$(python3 - <<'PY'
import json, subprocess
devices = json.loads(subprocess.check_output(['xcrun', 'simctl', 'list', 'devices', 'available', '-j']))['devices']
matches = [(runtime, item['udid']) for runtime, items in devices.items() for item in items
           if item['name'] == 'iPhone 17 Pro' and item['state'] in ('Booted', 'Shutdown')]
if not matches:
    raise SystemExit('No available iPhone 17 Pro simulator')
print(sorted(matches, reverse=True)[0][1])
PY
)"

app="$(find samples/Simple3D.Demo/bin/Release/net10.0-ios/iossimulator-arm64 \
    -maxdepth 2 -type d -name Simple3D.Demo.app -print -quit)"
test -n "$app" || { echo 'Built demo app bundle not found' >&2; exit 1; }

xcrun simctl boot "$device" || { xcrun simctl list devices | grep -F "$device" | grep -q Booted; }
xcrun simctl bootstatus "$device" -b
xcrun simctl install "$device" "$app"
xcrun simctl launch "$device" dev.simple3d.gallery
sleep 8
mkdir -p artifacts
xcrun simctl io "$device" screenshot artifacts/ios-gallery.png
swift scripts/check-gallery-screenshot.swift artifacts/ios-gallery.png
