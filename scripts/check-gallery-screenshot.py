#!/usr/bin/env python3
"""Reject emulator captures that missed the gallery or its rendered shapes."""

import sys

from PIL import Image


def near(pixel, target):
    return all(abs(actual - expected) <= 8 for actual, expected in zip(pixel, target))


def inspect(path):
    with Image.open(path) as image:
        image = image.convert("RGB")
        counts = [0, 0]
        bounds = [image.width, image.height, -1, -1]
        shape_pixels = []
        for y in range(0, image.height, 6):
            for x in range(0, image.width, 6):
                red, green, blue = image.getpixel((x, y))
                # The scene background (Theme.SceneBackground), behind every scene and thumbnail.
                if near((red, green, blue), (20, 12, 7)):
                    counts[0] += 1
                    bounds = [min(bounds[0], x), min(bounds[1], y),
                              max(bounds[2], x), max(bounds[3], y)]
                # Blue scene accents; the interface itself has no blue unless the renderer fell back to software.
                if blue >= 150 and blue > red + 60 and blue > green + 15:
                    shape_pixels.append((x, y))
        counts[1] = sum(bounds[0] <= x <= bounds[2] and bounds[1] <= y <= bounds[3]
                        for x, y in shape_pixels)
    # Thresholds scale with the screenshot: CI's emulator screen is a fraction of a phone's.
    samples = len(range(0, image.height, 6)) * len(range(0, image.width, 6))
    print(f"Gallery pixels: scene background={counts[0]}, blue shape={counts[1]} of {samples} samples")
    return counts[0] > samples * .05 and counts[1] > samples * .003


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: check-gallery-screenshot.py SCREENSHOT.png")
    sys.exit(0 if inspect(sys.argv[1]) else 1)
