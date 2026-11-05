#!/usr/bin/env python3
"""Checks screenshots of the label sync probe (LabelSyncProbePage): the white label must keep a
constant offset from the magenta marker sphere it is anchored to while the camera orbits. A label
drawn for a different GPU frame than the one on screen shifts by the per-frame marker motion.

Usage: check-label-sync.py <max-spread-px> <screenshot.png>...
Pass half the probe's logged motion_px_per_frame as the limit: a label one frame off moves by that much.
"""
import sys
import numpy as np
from PIL import Image

def measure(path):
    rgb = np.asarray(Image.open(path).convert("RGB")).astype(int)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    marker = (r > 170) & (g < 90) & (b > 170)
    label = (r > 210) & (g > 210) & (b > 210)
    if marker.sum() < 30 or label.sum() < 10:
        return None
    my, mx = np.nonzero(marker)
    ly, lx = np.nonzero(label)
    # A marker cut by the screen edge has a biased centroid, and its label is clamped inside the view.
    h, w = marker.shape
    if mx.min() < 80 or my.min() < 80 or mx.max() > w - 160 or my.max() > h - 80:
        return None
    # Only text near the marker (ignores status bars and other white UI).
    cx, cy = mx.mean(), my.mean()
    near = (np.abs(lx - cx) < 400) & (np.abs(ly - cy) < 300)
    if near.sum() < 10:
        return None
    lx, ly = lx[near], ly[near]
    # Text origin: left edge and bottom of the glyph bounds (anchor is at the text baseline start).
    return (cx, cy), (lx.min() - cx, ly.max() - cy)

def main():
    limit = float(sys.argv[1])
    results = [(p, measure(p)) for p in sys.argv[2:]]
    found = [(p, m) for p, m in results if m]
    if len(found) < 5:
        print(f"LABEL_SYNC_FAIL only {len(found)} usable screenshots")
        return 1
    offsets = np.array([m[1] for _, m in found])
    markers = np.array([m[0] for _, m in found])
    spread = offsets.max(axis=0) - offsets.min(axis=0)
    motion = np.linalg.norm(np.diff(markers, axis=0), axis=1)
    print(f"screens={len(found)} marker moved {motion.min():.1f}-{motion.max():.1f} px between screenshots; "
          f"label offset x {offsets[:,0].min():.1f}..{offsets[:,0].max():.1f} y {offsets[:,1].min():.1f}..{offsets[:,1].max():.1f} "
          f"(spread {spread[0]:.1f} x {spread[1]:.1f} px)")
    if motion.max() < 3:
        print("LABEL_SYNC_FAIL marker did not move; the camera is not orbiting")
        return 1
    ok = spread.max() <= limit
    print("LABEL_SYNC_PASS" if ok else f"LABEL_SYNC_FAIL spread above {limit} px")
    return 0 if ok else 1

if __name__ == "__main__":
    sys.exit(main())
