#!/usr/bin/env bash
set -euo pipefail

app="$(find samples/Simple3D.Demo/bin/Debug/net10.0-android/android-arm64 \
    -type f -name '*.apk' -print -quit)"
test -n "$app" || { echo 'Built Android APK not found' >&2; exit 1; }
adb install -r "$app"
adb shell monkey -p dev.simple3d.gallery -c android.intent.category.LAUNCHER 1
mkdir -p artifacts
for attempt in 1 2 3; do
    sleep 10
    adb exec-out screencap -p > artifacts/android-gallery.png
    if swift scripts/check-gallery-screenshot.swift artifacts/android-gallery.png; then
        exit 0
    fi
    echo "Android gallery is not visible after attempt $attempt" >&2
done

echo 'Recent Android app logs:' >&2
adb logcat -d -t 500 | grep -Ei 'simple3d|dotnet|maui|fatal exception' | tail -100 || true
exit 1
