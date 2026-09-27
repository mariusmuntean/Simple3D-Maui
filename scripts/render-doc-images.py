"""Render documentation illustrations from the executable Core examples."""
import pathlib
import subprocess
import tempfile

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[1]
DEST = ROOT / "docs" / "site" / "images"

with tempfile.TemporaryDirectory() as directory:
    subprocess.run(
        ["dotnet", "run", "--project", "samples/Simple3D.Examples", "-c", "Release", "--", "--all", directory],
        cwd=ROOT, check=True,
    )
    DEST.mkdir(parents=True, exist_ok=True)
    for name in ("Equipment", "Packing", "Surface"):
        with Image.open(pathlib.Path(directory) / f"{name}.ppm") as image:
            image.save(DEST / f"{name}.png", optimize=True)
