"""Render documentation illustrations from the executable Core examples."""
import pathlib
import subprocess
import tempfile

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]
DEST = ROOT / "docs" / "site" / "images"

with tempfile.TemporaryDirectory() as directory:
    subprocess.run(
        ["dotnet", "run", "--project", "tests/Simple3D.Core.Tests", "-c", "Release", "--", "--render-images", "--all", directory],
        cwd=ROOT, check=True,
    )
    DEST.mkdir(parents=True, exist_ok=True)
    for image_path in pathlib.Path(directory).glob("*.ppm"):
        with Image.open(image_path) as image:
            image.save(DEST / f"{image_path.stem}.png", optimize=True)
