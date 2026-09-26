#!/usr/bin/env bash
set -euo pipefail

app="$(find samples/Simple3D.Demo/bin/Debug/net10.0-android/android-x64 \
    -type f -name '*.apk' -print -quit)"
test -n "$app" || { echo 'Built Android APK not found' >&2; exit 1; }
adb install -r "$app"
adb shell pm path dev.simple3d.gallery
adb logcat -c
adb shell am start -W -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p dev.simple3d.gallery
mkdir -p artifacts
for attempt in 1 2 3; do
    sleep 10
    adb exec-out screencap -p > artifacts/android-gallery.png
    if python scripts/check-gallery-screenshot.py artifacts/android-gallery.png; then
        exit 0
    fi
    echo "Android gallery is not visible after attempt $attempt" >&2
done

echo 'Recent Android app logs:' >&2
adb shell dumpsys activity activities | grep -Ei 'mResumed|topResumed|simple3d' | tail -30 || true
adb logcat -d -v brief | grep -Ei 'simple3d|dotnet|maui|fatal exception|AndroidRuntime|mono' | tail -180 || true
exit 1
