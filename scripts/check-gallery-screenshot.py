#!/usr/bin/env python3
"""Reject emulator captures that missed the gallery or its rendered shapes."""

import sys

from PIL import Image


def near(pixel, target):
    return all(abs(actual - expected) <= 8 for actual, expected in zip(pixel, target))


def inspect(path):
    with Image.open(path) as image:
        image = image.convert("RGB")
        counts = [0, 0, 0]
        for y in range(0, image.height, 6):
            for x in range(0, image.width, 6):
                red, green, blue = image.getpixel((x, y))
                counts[0] += near((red, green, blue), (13, 19, 34))
                counts[1] += near((red, green, blue), (24, 36, 59))
                low, high = min(red, blue), max(red, blue)
                counts[2] += (low >= 60 and high >= 120 and high > low + 35
                              and high > green + 25)
    print(f"Gallery pixels: background={counts[0]}, panel={counts[1]}, blue shape={counts[2]}")
    return all(count > minimum for count, minimum in zip(counts, (1000, 1000, 100)))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: check-gallery-screenshot.py SCREENSHOT.png")
    sys.exit(0 if inspect(sys.argv[1]) else 1)
