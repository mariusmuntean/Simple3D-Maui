"""Exercise the published console examples and their image output."""
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[1]


class ExampleTests(unittest.TestCase):
    def test_all_scenes_write_genuine_rgb_images(self):
        with tempfile.TemporaryDirectory() as directory:
            subprocess.run(
                ["dotnet", "run", "--project", "samples/Simple3D.Examples", "-c", "Release", "--", "--all", directory],
                cwd=ROOT, check=True, capture_output=True, text=True,
            )
            for name in ("Equipment", "Packing", "Surface", "Assembly", "Molecule", "Telemetry", "City"):
                data = (pathlib.Path(directory) / f"{name}.ppm").read_bytes()
                self.assertTrue(data.startswith(b"P6\n800 600\n255\n"), name)
                pixels = data.split(b"\n", 3)[3]
                self.assertEqual(len(pixels), 800 * 600 * 3)
                self.assertGreater(len(set(pixels)), 20, name)


if __name__ == "__main__":
    unittest.main()
