import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import time
import unittest


SCRIPT = Path(__file__).with_name("ios-simulator-smoke.sh")

# Run the real shell script with only native tool boundaries replaced. These
# tests must also run on Linux CI, without starting a real simulator.
TOOL = '''#!/usr/bin/env python3
import json, os, pathlib, sys, time
name = pathlib.Path(sys.argv[0]).name
args = sys.argv[1:]
with open(os.environ["SMOKE_LOG"], "a") as log:
    log.write(json.dumps([name, *args]) + "\\n")
if name == "xcrun" and args == ["simctl", "list", "devices", "available", "-j"]:
    print(json.dumps({"devices": {"com.apple.CoreSimulator.SimRuntime.iOS-26-5": [
        {"name": "iPhone 17 Pro", "udid": "user-device", "state": "Booted", "isAvailable": True},
        *([] if os.environ.get("SMOKE_NO_IDLE") else [
            {"name": "iPhone 17 Pro", "udid": "test-device", "state": "Shutdown", "isAvailable": True}]),
        *([{"name": "iPhone 18 Pro", "udid": "fallback-device", "state": "Shutdown", "isAvailable": True}]
          if os.environ.get("SMOKE_FALLBACK") else [])
    ]}}))
if name == "xcrun" and args[1:2] == [os.environ.get("SMOKE_FAIL")]:
    sys.exit(23)
if name == "xcrun" and args[1:2] == ["launch"] and "--console" in args and not os.environ.get("SMOKE_NO_RENDER_MARKER"):
    print("NATIVE_RENDER_PROBE_PASS")
if name == "xcrun" and args[1:2] == ["bootstatus"] and os.environ.get("SMOKE_BLOCK"):
    pathlib.Path(os.environ["SMOKE_LOG"] + ".ready").touch()
    time.sleep(10)
'''


class IosSmokeCleanupTests(unittest.TestCase):
    def run_smoke(self, terminate=False, render_probe=False, **settings):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / "tools"
            tools.mkdir()
            for name in ("xcrun", "codesign", "swift", "sleep"):
                tool = tools / name
                tool.write_text(TOOL)
                tool.chmod(0o755)
            app = root / "samples/Simple3D.Demo/bin/Debug/net10.0-ios/iossimulator-arm64/Simple3D.Demo.app"
            app.mkdir(parents=True)
            log = root / "commands.jsonl"
            env = dict(os.environ, PATH=f"{tools}{os.pathsep}{os.environ['PATH']}",
                       SMOKE_LOG=str(log), **settings)
            command = ["bash", str(SCRIPT), *(["--render-probe"] if render_probe else [])]
            if terminate:
                env["SMOKE_BLOCK"] = "1"
                process = subprocess.Popen(command, cwd=root, env=env,
                                           text=True, stdout=subprocess.PIPE,
                                           stderr=subprocess.PIPE, start_new_session=True)
                try:
                    deadline = time.monotonic() + 5
                    while not Path(str(log) + ".ready").exists():
                        if time.monotonic() >= deadline:
                            self.fail("Smoke script did not reach boot wait")
                        time.sleep(.02)
                    os.killpg(process.pid, signal.SIGTERM)
                    stdout, stderr = process.communicate(timeout=5)
                    result = subprocess.CompletedProcess(process.args, process.returncode, stdout, stderr)
                finally:
                    if process.poll() is None:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait()
            else:
                result = subprocess.run(command, cwd=root, env=env,
                                        text=True, capture_output=True, timeout=10)
            commands = [json.loads(line) for line in log.read_text().splitlines()]
            return result, commands

    def assert_cleanup(self, commands, device="test-device"):
        self.assertEqual(commands[-2:], [
            ["xcrun", "simctl", "terminate", device, "dev.simple3d.gallery"],
            ["xcrun", "simctl", "shutdown", device],
        ])

    def test_success_releases_test_device(self):
        result, commands = self.run_smoke()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(["xcrun", "simctl", "boot", "test-device"], commands)
        self.assert_cleanup(commands)

    def test_native_render_probe_releases_device(self):
        result, commands = self.run_smoke(render_probe=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(["xcrun", "simctl", "launch", "--console", "test-device", "dev.simple3d.gallery"], commands)
        self.assert_cleanup(commands)

    def test_native_render_probe_rejects_missing_success_marker(self):
        result, commands = self.run_smoke(render_probe=True, SMOKE_NO_RENDER_MARKER="1")
        self.assertNotEqual(result.returncode, 0)
        self.assert_cleanup(commands)

    def test_install_failure_releases_device_and_preserves_error(self):
        result, commands = self.run_smoke(SMOKE_FAIL="install")
        self.assertEqual(result.returncode, 23, result.stderr)
        self.assert_cleanup(commands)

    def test_boot_failure_still_attempts_shutdown(self):
        result, commands = self.run_smoke(SMOKE_FAIL="boot")
        self.assertNotEqual(result.returncode, 0)
        self.assert_cleanup(commands)

    def test_existing_user_session_is_not_selected(self):
        result, commands = self.run_smoke(SMOKE_NO_IDLE="1")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(any(command[1:2] == ["simctl"] and command[2:3] in
                             (["boot"], ["terminate"], ["shutdown"]) for command in commands))

    def test_uses_idle_newer_phone_when_preferred_model_is_unavailable(self):
        result, commands = self.run_smoke(SMOKE_NO_IDLE="1", SMOKE_FALLBACK="1")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(["xcrun", "simctl", "boot", "fallback-device"], commands)
        self.assert_cleanup(commands, "fallback-device")

    def test_shutdown_failure_is_not_reported_as_success(self):
        result, commands = self.run_smoke(SMOKE_FAIL="shutdown")
        self.assertNotEqual(result.returncode, 0)
        self.assert_cleanup(commands)

    def test_termination_during_boot_wait_releases_device(self):
        result, commands = self.run_smoke(terminate=True)
        self.assertEqual(result.returncode, 143, result.stderr)
        self.assert_cleanup(commands)


if __name__ == "__main__":
    unittest.main()
