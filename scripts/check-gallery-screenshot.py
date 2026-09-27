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
        bounds = [image.width, image.height, -1, -1]
        shape_pixels = []
        for y in range(0, image.height, 6):
            for x in range(0, image.width, 6):
                red, green, blue = image.getpixel((x, y))
                counts[0] += near((red, green, blue), (13, 19, 34))
                if near((red, green, blue), (24, 36, 59)):
                    counts[1] += 1
                    bounds = [min(bounds[0], x), min(bounds[1], y),
                              max(bounds[2], x), max(bounds[3], y)]
                low, high = min(red, blue), max(red, blue)
                if low >= 60 and high >= 120 and high > low + 35 and high > green + 25:
                    shape_pixels.append((x, y))
        counts[2] = sum(bounds[0] <= x <= bounds[2] and bounds[1] <= y <= bounds[3]
                        for x, y in shape_pixels)
    print(f"Gallery pixels: background={counts[0]}, panel={counts[1]}, blue shape={counts[2]}")
    return all(count > minimum for count, minimum in zip(counts, (1000, 1000, 100)))


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: check-gallery-screenshot.py SCREENSHOT.png")
    sys.exit(0 if inspect(sys.argv[1]) else 1)
