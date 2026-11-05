"""Side-by-side review sheet: source video frame with landmarks, next to the rendered Mirror character.

    python3 scripts/mirror-contact-sheet.py video.mp4 landmarks.jsonl[.gz] render-dir sheet.png [columns]

render-dir holds frame-NNNN.png images from `tests/Simple3D.Maui.Tests -- --render-mirror`; NNNN is the
landmark line index. Requires ffmpeg and Pillow.
"""
import gzip, json, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw

BONES = [(1, 3), (3, 5), (2, 4), (4, 6), (1, 2), (7, 8), (1, 7), (2, 8), (7, 9), (9, 11), (8, 10), (10, 12)]
LEFT = {1, 3, 5, 7, 9, 11}

def main(video, landmarks, renders, sheet, columns=6):
    opener = gzip.open if landmarks.endswith(".gz") else open
    with opener(landmarks, "rt") as f:
        frames = [json.loads(line) for line in f]
    names = sorted(n for n in os.listdir(renders) if n.startswith("frame-") and n.endswith(".png"))
    tiles = []
    with tempfile.TemporaryDirectory() as tmp:
        for name in names:
            index = int(name[6:10])
            frame = frames[index]
            still = os.path.join(tmp, f"{index}.png")
            subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-ss", f"{frame['t']:.3f}", "-i", video, "-frames:v", "1", still], check=True)
            source = Image.open(still).convert("RGB")
            render = Image.open(os.path.join(renders, name)).convert("RGB")
            # Scale the source to the render height and draw the landmarks the character follows.
            scale = render.height / source.height
            source = source.resize((round(source.width * scale), render.height))
            draw = ImageDraw.Draw(source)
            if frame["lm"]:
                points = [(x * source.width, y * source.height, c) for x, y, c in frame["lm"]]
                for a, b in BONES:
                    if points[a][2] > .3 and points[b][2] > .3:
                        color = (255, 120, 60) if a in LEFT and b in LEFT else (60, 200, 255) if a not in LEFT and b not in LEFT else (240, 240, 240)
                        draw.line([points[a][:2], points[b][:2]], fill=color, width=4)
            draw.text((8, 8), f"{frame['t']:.2f}s", fill=(255, 255, 255))
            tile = Image.new("RGB", (source.width + render.width, render.height))
            tile.paste(source, (0, 0)); tile.paste(render, (source.width, 0))
            tiles.append(tile.resize((tile.width // 2, tile.height // 2)))
    rows = (len(tiles) + columns - 1) // columns
    out = Image.new("RGB", (tiles[0].width * columns, tiles[0].height * rows))
    for i, tile in enumerate(tiles):
        out.paste(tile, ((i % columns) * tile.width, (i // columns) * tile.height))
    out.save(sheet)
    print(f"{sheet}: {len(tiles)} tiles (orange = person's left side, blue = right)")

if __name__ == "__main__":
    main(*sys.argv[1:5], *(int(a) for a in sys.argv[5:6]))
