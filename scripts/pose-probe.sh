#!/usr/bin/env bash
set -euo pipefail

# Builds nothing. Device names are deliberately isolated from the renderer smoke tests.
platform="${1:-}"
video="${2:-}"
case "$platform" in ios|maccatalyst|android) ;; *) echo 'Usage: pose-probe.sh <ios|maccatalyst|android> <video.mp4>' >&2; exit 2 ;; esac
[ -f "$video" ] || { echo "Video not found: $video" >&2; exit 2; }
video="$(python3 -c 'import os,sys; print(os.path.abspath(sys.argv[1]))' "$video")"
cd "$(dirname "$0")/.."
mkdir -p artifacts
log="artifacts/$platform-pose-probe.log"
: > "$log"
# Override with POSE_IOS_DEVICE (simulator UDID) or POSE_ANDROID_SERIAL / POSE_ANDROID_AVD.
device="${POSE_IOS_DEVICE:-2BAE44EE-B0AA-4B3F-B0FC-7776FD52A89F}"
serial="${POSE_ANDROID_SERIAL:-emulator-5580}"
avd_name="${POSE_ANDROID_AVD:-Pixel_5_API_33_Android_13}"
sdk="${ANDROID_HOME:-$HOME/Library/Android/sdk}"
adb="$sdk/platform-tools/adb"
booted=false
launched=false
runner_pid=
emulator_pid=
remote_video=
cleanup() {
    local status=$?
    trap - EXIT INT TERM
    if [ -n "$runner_pid" ]; then kill "$runner_pid" 2>/dev/null || true; wait "$runner_pid" 2>/dev/null || true; fi
    case "$platform" in
        ios)
            if "$launched"; then xcrun simctl terminate "$device" dev.simple3d.gallery 2>/dev/null || true; fi
            [ -z "$remote_video" ] || rm -f "$remote_video"
            if "$booted"; then xcrun simctl shutdown "$device" >&2 || status=1; fi ;;
        android)
            if "$launched"; then "$adb" -s "$serial" shell am force-stop dev.simple3d.gallery >&2 || true; fi
            if [ -n "$remote_video" ]; then "$adb" -s "$serial" shell run-as dev.simple3d.gallery rm -f "$remote_video" >&2 || true; fi
            if "$booted"; then "$adb" -s "$serial" emu kill >&2 || true; fi
            if [ -n "$emulator_pid" ]; then kill "$emulator_pid" 2>/dev/null || true; wait "$emulator_pid" 2>/dev/null || true; fi ;;
    esac
    exit "$status"
}
trap cleanup EXIT
trap 'exit 130' INT
trap 'exit 143' TERM

case "$platform" in
    maccatalyst)
        app=samples/Simple3D.Demo/bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/Simple3D.Demo.app
        executable="$app/Contents/MacOS/Simple3D.Demo"
        [ -x "$executable" ] || { echo "Built Debug executable not found: $executable" >&2; exit 1; }
        SIMPLE3D_POSE_PROBE="$video" "$executable" > "$log" 2>&1 & runner_pid=$! ;;
    ios)
        app=samples/Simple3D.Demo/bin/Debug/net10.0-ios/iossimulator-arm64/Simple3D.Demo.app
        [ -d "$app" ] || { echo "Built Debug app not found: $app" >&2; exit 1; }
        state="$(xcrun simctl list devices -j | python3 -c 'import json,sys; print(next(d["state"] for ds in json.load(sys.stdin)["devices"].values() for d in ds if d["udid"]==sys.argv[1]))' "$device")"
        if [ "$state" = Shutdown ]; then xcrun simctl boot "$device" >&2; booted=true; fi
        xcrun simctl bootstatus "$device" -b >&2
        xcrun simctl install "$device" "$app" >&2
        container="$(xcrun simctl get_app_container "$device" dev.simple3d.gallery data)"
        remote_video="$container/tmp/pose-probe-$$.mp4"
        cp "$video" "$remote_video"
        launched=true
        SIMCTL_CHILD_SIMPLE3D_POSE_PROBE="$remote_video" xcrun simctl launch --terminate-running-process --console "$device" dev.simple3d.gallery > "$log" 2>&1 & runner_pid=$! ;;
    android)
        if ! "$adb" -s "$serial" get-state >/dev/null 2>&1; then
            "$sdk/emulator/emulator" -avd "$avd_name" -no-window -no-audio -port "${serial#emulator-}" > artifacts/pose-emulator.log 2>&1 & emulator_pid=$!
            booted=true
        fi
        ready=false
        for attempt in $(seq 1 120); do
            if [ "$("$adb" -s "$serial" shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" = 1 ]; then ready=true; break; fi
            sleep 1
        done
        "$ready" || { echo 'Android emulator did not boot' >&2; exit 1; }
        avd="$("$adb" -s "$serial" emu avd name | tr -d '\r' | head -1)"
        [ "$avd" = "$avd_name" ] || { echo "Refusing unexpected AVD on $serial: $avd" >&2; exit 1; }
        abi="$("$adb" -s "$serial" shell getprop ro.product.cpu.abi | tr -d '\r')"
        case "$abi" in arm64-v8a) rid=android-arm64 ;; x86_64) rid=android-x64 ;; *) echo "Unsupported ABI $abi" >&2; exit 1 ;; esac
        app="$(find samples/Simple3D.Demo/bin/Debug/net10.0-android -type f -name '*-Signed.apk' -path "*/$rid/*" -print -quit)"
        if [ -z "$app" ]; then app="$(find samples/Simple3D.Demo/bin/Debug/net10.0-android -maxdepth 1 -name '*-Signed.apk' -print -quit)"; fi
        [ -n "$app" ] || { echo 'Built Debug APK not found' >&2; exit 1; }
        "$adb" -s "$serial" install -r "$app" >&2
        "$adb" -s "$serial" shell run-as dev.simple3d.gallery mkdir -p files >&2
        remote_video="files/pose-probe-$$.mp4"
        "$adb" -s "$serial" shell run-as dev.simple3d.gallery sh -c "'cat > $remote_video'" < "$video"
        activity="$("$adb" -s "$serial" shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER dev.simple3d.gallery | tr -d '\r' | tail -1)"
        case "$activity" in dev.simple3d.gallery/*) ;; *) echo "No launcher: $activity" >&2; exit 1 ;; esac
        "$adb" -s "$serial" shell am force-stop dev.simple3d.gallery
        "$adb" -s "$serial" logcat -c
        "$adb" -s "$serial" logcat -v raw Simple3D:I '*:S' > "$log" 2>&1 & runner_pid=$!
        launched=true
        "$adb" -s "$serial" shell am start -W -n "$activity" --es simple3d_pose_probe "/data/user/0/dev.simple3d.gallery/$remote_video" >&2 ;;
esac

for attempt in $(seq 1 150); do
    if grep -q 'POSE_PROBE_PASS ' "$log"; then
        grep -oE 'POSE_(FRAME|PROBE_).*' "$log"
        exit 0
    fi
    if grep -q 'POSE_PROBE_FAIL ' "$log"; then cat "$log"; exit 1; fi
    if ! kill -0 "$runner_pid" 2>/dev/null; then cat "$log"; echo 'Probe exited without a PASS marker' >&2; exit 1; fi
    sleep 2
done
cat "$log"
echo 'Pose probe timed out after 300 seconds' >&2
exit 1
