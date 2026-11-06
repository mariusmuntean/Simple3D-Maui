#!/usr/bin/env bash
# Checks that this machine uses the toolchain the repository pins: the .NET SDK and workload set in global.json, and
# (on macOS) the Xcode in .xcode-version. Prints how to fix each mismatch; exits non-zero if anything is off.
set -uo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
dotnet_command="${DOTNET_COMMAND:-dotnet}"
failed=0
fail() { echo "✗ $1"; echo "  fix: $2"; failed=1; }
ok() { echo "✓ $1"; }

json() { python3 -c "import json,sys; print(json.load(open('global.json'))['sdk']['$1'])"; }
sdk="$(json version)"
workloads="$(json workloadVersion)"

if ! command -v "$dotnet_command" >/dev/null; then
    fail ".NET SDK not found" "install SDK $sdk: curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version $sdk"
else
    actual="$("$dotnet_command" --version 2>/dev/null | awk 'NF { print $1; exit }')"
    # global.json rolls forward to later patches of the same feature band (10.0.4xx).
    if [[ "${actual%??}" == "${sdk%??}" && "${actual: -2}" -ge "${sdk: -2}" ]] 2>/dev/null; then
        ok ".NET SDK $actual (pinned $sdk, latest patch allowed)"
    else
        fail ".NET SDK ${actual:-missing}, expected $sdk or a later ${sdk%??}xx patch" \
            "curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version $sdk, and put that dotnet first on PATH"
    fi
    actual_workloads="$("$dotnet_command" workload --version 2>/dev/null | awk 'NF { print $1; exit }')"
    if [[ "$actual_workloads" == "$workloads" ]] && "$dotnet_command" workload list 2>/dev/null | grep -q '^maui'; then
        ok "workload set $actual_workloads with MAUI"
    else
        fail "workload set ${actual_workloads:-missing} or MAUI missing, expected $workloads" "dotnet workload install maui (run inside this repository)"
    fi
fi

if [[ "$(uname)" == Darwin ]]; then
    xcode="$(tr -d '[:space:]' < .xcode-version)"
    developer="${XcodeLocation:-$(xcode-select -p 2>/dev/null)}"
    actual="$(DEVELOPER_DIR="$developer" xcodebuild -version 2>/dev/null | awk 'NR == 1 { print $2 }')"
    if [[ "$actual" == "$xcode" || "$actual" == "$xcode.0" ]]; then
        ok "Xcode $actual at ${developer%/Contents/Developer}"
    else
        fail "Xcode ${actual:-missing}, expected $xcode" "xcodes install $xcode && sudo xcodes select $xcode (or set XcodeLocation)"
    fi
    for override in MD_APPLE_SDK_ROOT; do
        [[ -n "${!override:-}" ]] && fail "$override is set and overrides xcode-select" "unset $override"
    done
    for settings in ~/Library/Preferences/maui/Settings.plist ~/Library/Preferences/Xamarin/Settings.plist; do
        if [[ -f "$settings" ]] && /usr/libexec/PlistBuddy -c 'Print :AppleSdkRoot' "$settings" >/dev/null 2>&1; then
            fail "$settings sets AppleSdkRoot, which overrides xcode-select" "delete the AppleSdkRoot entry (deprecated by .NET for iOS)"
        fi
    done
fi

if [[ -z "${JAVA_HOME:-}" ]] && ! /usr/libexec/java_home -v 17+ >/dev/null 2>&1 && ! command -v java >/dev/null; then
    echo "! no JDK found; Android builds need JDK 17 or later (see docs/DEVELOPMENT.md)"
fi
exit "$failed"
