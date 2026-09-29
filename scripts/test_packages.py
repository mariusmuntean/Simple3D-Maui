"""Pack and consume Core without project references or a shared package cache."""
from pathlib import Path
import subprocess
import tempfile
import unittest
from zipfile import ZipFile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
NS = {"p": "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"}


class PackageTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.directory = tempfile.TemporaryDirectory()
        cls.addClassCleanup(cls.directory.cleanup)
        cls.feed = Path(cls.directory.name) / "feed"
        subprocess.run(["dotnet", "pack", "src/Simple3D.Core", "-c", "Release",
                        "-p:Version=0.0.0-validation", "-o", str(cls.feed)],
                       cwd=ROOT, check=True, capture_output=True, text=True, timeout=120)
        cls.package = cls.feed / "Simple3D.Core.0.0.0-validation.nupkg"

    def test_package_has_real_description_and_source_links(self):
        with ZipFile(self.package) as archive:
            document = ET.fromstring(archive.read("Simple3D.Core.nuspec"))
        metadata = document.find("p:metadata", NS)
        description = metadata.findtext("p:description", namespaces=NS)
        self.assertNotEqual(description, "Package Description")
        self.assertTrue(description and len(description) > 30)
        repository = metadata.find("p:repository", NS)
        self.assertEqual(repository.get("url"), "https://github.com/mariusmuntean/Simple3D-Maui")
        self.assertEqual(metadata.findtext("p:projectUrl", namespaces=NS), repository.get("url"))

    def test_package_contains_documentation_and_icon(self):
        with ZipFile(self.package) as archive:
            names = set(archive.namelist())
            self.assertTrue({"README.md", "package-icon.png", "lib/net10.0/Simple3D.Core.dll",
                             "lib/net10.0/Simple3D.Core.xml"}.issubset(names))
            self.assertTrue(archive.read("package-icon.png").startswith(b"\x89PNG\r\n\x1a\n"))
            self.assertIn(b"DepthRenderer", archive.read("lib/net10.0/Simple3D.Core.xml"))

    def test_packaged_public_api_runs_without_project_references(self):
        result = subprocess.run(
            ["dotnet", "run", "--project", "tests/Simple3D.Package.Tests", "-c", "Release",
             f"-p:PackageFeed={self.feed}",
             f"-p:RestorePackagesPath={Path(self.directory.name) / 'cache'}"],
            cwd=ROOT, capture_output=True, text=True, timeout=120)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn("Packaged Core consumer passed.", result.stdout)


if __name__ == "__main__":
    unittest.main()
