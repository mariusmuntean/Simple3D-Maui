"""Builds the Mirror showcase GIF: the tracked person as a glowing skeleton (from recorded landmarks, so no video footage
is redistributed) next to the character that copies them.

    dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-showcase out
    python3 scripts/mirror-showcase.py out docs/site/images/mirror.gif

Requires Pillow and ffmpeg.
"""
import gzip, json, math, os, subprocess, sys, tempfile
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BONES = [(1, 3), (3, 5), (2, 4), (4, 6), (1, 2), (7, 8), (1, 7), (2, 8), (7, 9), (9, 11), (8, 10), (10, 12)]
LEFT = {1, 3, 5, 7, 9, 11}
CAPTIONS = {"mirror-landmarks": "Whole body: squats and presses", "face-46379": "Head and face: mouth, eyes, head tilt",
            "turning-landmarks": "Turning around: front, side and back"}
BACKGROUND = (14, 26, 43)

def load(fixture):
    with gzip.open(os.path.join(os.path.dirname(__file__), "..", "tests", "Fixtures", fixture + ".jsonl.gz"), "rt") as f:
        return [json.loads(line) for line in f]

def nearest(frames, t):
    return min(frames, key=lambda f: abs(f["t"] - t))

def font(size):
    for path in ["/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/Helvetica.ttc", "/Library/Fonts/Arial.ttf"]:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()

NAMES = {"cat.vrm": "Chubby Tubby Cat", "juanita.vrm": "Juanita", "cosmicbot.vrm": "CosmicBot", "froggy.vrm": "Froggy"}

def region(frames, start, end):
    """Bounds (in image-height units) of every confident landmark in a time range: the panel zooms to where the person moves."""
    xs, ys = [], []
    for f in frames:
        if start <= f["t"] <= end and f.get("lm"):
            aspect = f["w"] / f["h"]
            for x, y, c in f["lm"]:
                if c > .3: xs.append(x * aspect); ys.append(y)
    return (min(xs), min(ys), max(xs), max(ys)) if xs else (0, 0, 1, 1)

def skeleton(frame, size, bounds):
    w, h = size
    glow = Image.new("RGB", size, BACKGROUND)
    d = ImageDraw.Draw(glow)
    aspect = frame["w"] / frame["h"]
    x0, y0, x1, y1 = bounds
    # Zoom to the area the person moves in (with room for the head above the shoulders), centered in the panel.
    y0 -= .25 * (y1 - y0) + .05
    scale = min(w * .82 / max(x1 - x0, .05), h * .8 / max(y1 - y0, .05))
    ox, oy = w / 2 - (x0 + x1) / 2 * scale, h * .52 - (y0 + y1) / 2 * scale
    lm = frame.get("lm")
    def p(i): return (ox + lm[i][0] * aspect * scale, oy + lm[i][1] * scale)
    if lm:
        for a, b in BONES:
            if lm[a][2] > .3 and lm[b][2] > .3:
                color = (255, 140, 90) if a in LEFT and b in LEFT else (90, 200, 255) if a not in LEFT and b not in LEFT else (235, 235, 245)
                d.line([p(a), p(b)], fill=color, width=9)
        for i in range(1, 13):
            if lm[i][2] > .3:
                x, y = p(i); d.ellipse([x - 7, y - 7, x + 7, y + 7], fill=(255, 255, 255))
    face = frame.get("face")
    if face:
        yaw, pitch, roll, mouth, smile, brows, blink_l, blink_r = face
        if lm and lm[0][2] > .3 and lm[1][2] > .3 and lm[2][2] > .3:
            # Head sized from the shoulders: large in close-ups, small for a whole body.
            cx, cy = p(0); r = .42 * math.dist(p(1), p(2))
        else:
            cx, cy, r = w / 2, h * .42, .3 * min(w, h)
        d.ellipse([cx - r, cy - r * 1.15, cx + r, cy + r * 1.15], outline=(255, 214, 120), width=5)
        tilt = -roll
        def rot(x, y): return (cx + x * math.cos(tilt) - y * math.sin(tilt) + yaw * r * .6, cy + x * math.sin(tilt) + y * math.cos(tilt) - pitch * r * .5)
        for side, blink in ((-1, blink_r), (1, blink_l)):  # the person's right eye appears on the image's left
            ex, ey = rot(side * r * .38, -r * .2)
            open_ = max(.08, 1 - blink) * r * .16
            d.ellipse([ex - r * .16, ey - open_, ex + r * .16, ey + open_], fill=(255, 255, 255))
        mx, my = rot(0, r * .45)
        mw, mh = r * (.32 + .18 * smile), max(3, r * .5 * mouth)
        d.ellipse([mx - mw, my - mh, mx + mw, my + mh], outline=(255, 214, 120), width=4, fill=(60, 20, 30) if mouth > .15 else None)
    blurred = glow.filter(ImageFilter.GaussianBlur(10))
    return Image.blend(blurred, glow, .65)

def main(render_dir, gif):
    manifest = [line.split() for line in open(os.path.join(render_dir, "manifest.txt"))]
    fixtures = {f: load(f) for f in {m[1] for m in manifest}}
    times = {}
    for _, fixture, t, _ in manifest: times.setdefault(fixture, []).append(float(t))
    bounds = {f: region(fixtures[f], min(ts), max(ts)) for f, ts in times.items()}
    title, caption = font(30), font(24)
    with tempfile.TemporaryDirectory() as tmp:
        for index, fixture, t, model in manifest:
            render = Image.open(os.path.join(render_dir, f"frame-{int(index):04d}.png")).convert("RGB")
            panel = skeleton(nearest(fixtures[fixture], float(t)), render.size, bounds[fixture])
            out = Image.new("RGB", (render.width * 2, render.height), BACKGROUND)
            out.paste(panel, (0, 0)); out.paste(render, (render.width, 0))
            d = ImageDraw.Draw(out)
            d.text((24, 20), "Simple3D  ·  Mirror", font=title, fill=(255, 255, 255))
            d.text((render.width + 24, 24), NAMES.get(model, model), font=caption, fill=(169, 184, 212))
            text = CAPTIONS.get(fixture, "")
            tw = d.textlength(text, font=caption)
            d.rounded_rectangle([out.width / 2 - tw / 2 - 18, out.height - 64, out.width / 2 + tw / 2 + 18, out.height - 22], 16, fill=(8, 12, 22))
            d.text((out.width / 2 - tw / 2, out.height - 58), text, font=caption, fill=(255, 255, 255))
            out.save(os.path.join(tmp, f"{int(index):04d}.png"))
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", "15", "-i", os.path.join(tmp, "%04d.png"), "-filter_complex",
                        "fps=12,scale=640:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle",
                        gif], check=True)
    print(f"{gif}: {os.path.getsize(gif) // 1024} KB")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
