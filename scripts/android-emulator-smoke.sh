#!/usr/bin/env bash
set -euo pipefail

# The emulator runner owns the device lifecycle. This script owns only the
# gallery launch, including a start command that fails after creating a process.
gallery_started=false
cleanup() {
    local status=$?
    trap - EXIT INT TERM
    if "$gallery_started"; then
        if ! adb shell am force-stop dev.simple3d.gallery; then
            echo 'Failed to stop Android gallery' >&2
            if [ "$status" -eq 0 ]; then status=1; fi
        fi
    fi
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

abi="$(adb shell getprop ro.product.cpu.abi | tr -d '\r')"
case "$abi" in
    arm64-v8a) rid=android-arm64 ;;
    x86_64) rid=android-x64 ;;
    *) echo "Unsupported emulator ABI: $abi" >&2; exit 1 ;;
esac
configuration="${BUILD_CONFIGURATION:-Debug}"
case "$configuration" in Debug|Release) ;; *) echo 'Invalid BUILD_CONFIGURATION' >&2; exit 2 ;; esac
app="$(find "samples/Simple3D.Demo/bin/$configuration/net10.0-android/$rid" \
    -maxdepth 1 -type f -name '*-Signed.apk' -print -quit)"
test -n "$app" || { echo 'Built Android APK not found' >&2; exit 1; }
adb install -r "$app"
adb shell pm path dev.simple3d.gallery
adb logcat -c
activity="$(adb shell cmd package resolve-activity --brief \
    -a android.intent.action.MAIN -c android.intent.category.LAUNCHER dev.simple3d.gallery | tr -d '\r' | tail -1)"
case "$activity" in
    dev.simple3d.gallery/*) ;;
    *) echo "Gallery launcher activity not found: $activity" >&2; exit 1 ;;
esac
gallery_started=true
adb shell am start -W -n "$activity"
mkdir -p artifacts
for attempt in 1 2 3; do
    sleep 10
    adb exec-out screencap -p > artifacts/android-gallery.png
    if python3 scripts/check-gallery-screenshot.py artifacts/android-gallery.png; then
        exit 0
    fi
    echo "Android gallery is not visible after attempt $attempt" >&2
done

echo 'Recent Android app logs:' >&2
adb shell dumpsys activity activities | grep -Ei 'mResumed|topResumed|simple3d' | tail -30 || true
adb logcat -d -v brief | grep -Ei 'simple3d|dotnet|maui|fatal exception|AndroidRuntime|mono' | tail -180 || true
exit 1
