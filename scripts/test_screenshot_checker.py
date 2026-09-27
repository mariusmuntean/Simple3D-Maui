import importlib.util
import tempfile
import unittest
from pathlib import Path

from PIL import Image, ImageDraw


SPEC = importlib.util.spec_from_file_location("checker", Path(__file__).with_name("check-gallery-screenshot.py"))
checker = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(checker)


class ScreenshotCheckerTests(unittest.TestCase):
    def check(self, panel=True, shape=True, shape_outside=False):
        image = Image.new("RGB", (360, 720), (13, 19, 34))
        draw = ImageDraw.Draw(image)
        if panel:
            draw.rectangle((20, 150, 340, 520), fill=(24, 36, 59))
        if shape:
            draw.rectangle((100, 250, 250, 330) if not shape_outside else (100, 550, 250, 630),
                           fill=(90, 130, 245))
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.png"
            image.save(path)
            return checker.inspect(path)

    def test_gallery_drawing_passes(self):
        self.assertTrue(self.check())

    def test_launcher_or_blank_surface_fails(self):
        self.assertFalse(self.check(panel=False, shape=False))
        self.assertFalse(self.check(shape=False))

    def test_blue_decoration_outside_panel_does_not_count_as_drawing(self):
        self.assertFalse(self.check(shape_outside=True))


if __name__ == "__main__":
    unittest.main()
