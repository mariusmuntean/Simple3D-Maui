"""Execute the real Android smoke script with a controlled adb boundary."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/android-emulator-smoke.sh"

ADB = r'''#!/usr/bin/env python3
import os, sys
from pathlib import Path
args = sys.argv[1:]
with open(os.environ["TEST_ADB_LOG"], "a") as log:
    log.write(" ".join(args) + "\n")
scenario = os.environ["TEST_SCENARIO"]
if args == ["shell", "getprop", "ro.product.cpu.abi"]: print("x86_64")
elif args[:1] == ["install"] and scenario == "install-failure": sys.exit(17)
elif args[:3] == ["shell", "pm", "path"]: print("package:/data/app/gallery.apk")
elif args[:4] == ["shell", "cmd", "package", "resolve-activity"]: print("dev.simple3d.gallery/.MainActivity")
elif args[:3] == ["shell", "am", "start"]:
    Path(os.environ["TEST_RUNNING"]).touch()
    if scenario in ("launch-failure", "launch-and-cleanup-failure"): sys.exit(23)
elif args[:3] == ["shell", "am", "force-stop"]:
    if scenario in ("cleanup-failure", "launch-and-cleanup-failure"): sys.exit(7)
    Path(os.environ["TEST_RUNNING"]).unlink(missing_ok=True)
elif args[:2] == ["exec-out", "screencap"]: sys.stdout.buffer.write(b"capture")
'''
SLEEP = '''#!/usr/bin/env python3
import os, signal
if os.environ["TEST_SCENARIO"] == "termination":
    os.kill(os.getppid(), signal.SIGTERM)
'''
CHECK = '''import os, sys
sys.exit(1 if os.environ["TEST_SCENARIO"] == "screenshot-failure" else 0)
'''


class AndroidCleanupTests(unittest.TestCase):
    def run_smoke(self, scenario, configuration="Debug"):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tools = root / "tools"
            tools.mkdir()
            for name, content in (("adb", ADB), ("sleep", SLEEP)):
                tool = tools / name
                tool.write_text(content)
                tool.chmod(0o755)
            apk = root / f"samples/Simple3D.Demo/bin/{configuration}/net10.0-android/android-x64/gallery-Signed.apk"
            apk.parent.mkdir(parents=True)
            apk.touch()
            (root / "scripts").mkdir()
            (root / "scripts/check-gallery-screenshot.py").write_text(CHECK)
            log, running = root / "adb.log", root / "running"
            env = dict(os.environ, PATH=str(tools) + os.pathsep + os.environ["PATH"],
                       TEST_SCENARIO=scenario, TEST_ADB_LOG=str(log), TEST_RUNNING=str(running), BUILD_CONFIGURATION=configuration)
            result = subprocess.run(["bash", str(SCRIPT)], cwd=root, env=env,
                                    capture_output=True, text=True, timeout=10)
            return result, log.read_text().splitlines(), running.exists()

    def test_release_launch_stops_gallery(self):
        result, commands, running = self.run_smoke("success", configuration="Release")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(running)
        self.assertEqual(commands[-1], "shell am force-stop dev.simple3d.gallery")

    def test_success_stops_only_the_gallery(self):
        result, commands, running = self.run_smoke("success")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertFalse(running)
        self.assertEqual(commands[-1], "shell am force-stop dev.simple3d.gallery")
        self.assertEqual(commands.count("shell am force-stop dev.simple3d.gallery"), 1)
        self.assertNotIn("emu kill", commands)

    def test_screenshot_failure_stops_gallery(self):
        result, commands, running = self.run_smoke("screenshot-failure")
        self.assertEqual(result.returncode, 1, result.stderr)
        self.assertFalse(running)
        self.assertEqual(commands[-1], "shell am force-stop dev.simple3d.gallery")

    def test_launch_failure_preserves_exit_status_and_stops_gallery(self):
        result, _, running = self.run_smoke("launch-failure")
        self.assertEqual(result.returncode, 23, result.stderr)
        self.assertFalse(running)

    def test_install_failure_does_not_stop_an_unlaunched_app(self):
        result, commands, running = self.run_smoke("install-failure")
        self.assertEqual(result.returncode, 17, result.stderr)
        self.assertFalse(running)
        self.assertNotIn("shell am force-stop dev.simple3d.gallery", commands)

    def test_termination_stops_gallery(self):
        result, _, running = self.run_smoke("termination")
        self.assertEqual(result.returncode, 143, result.stderr)
        self.assertFalse(running)

    def test_cleanup_failure_makes_success_fail(self):
        result, _, running = self.run_smoke("cleanup-failure")
        self.assertNotEqual(result.returncode, 0)
        self.assertTrue(running)
        self.assertIn("stop", result.stderr.lower())

    def test_cleanup_failure_preserves_original_failure(self):
        result, commands, _ = self.run_smoke("launch-and-cleanup-failure")
        self.assertEqual(result.returncode, 23, result.stderr)
        self.assertEqual(commands[-1], "shell am force-stop dev.simple3d.gallery")


if __name__ == "__main__":
    unittest.main()
