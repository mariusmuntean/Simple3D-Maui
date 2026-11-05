"""Builds the Mirror showcase GIF: a real person (public-domain NASA and CDC videos) next to the character that copies them.

    dotnet run --project tests/Simple3D.Maui.Tests -c Release -- --render-showcase out
    python3 scripts/mirror-showcase.py out docs/site/images/mirror.gif [video-cache-dir]

The videos are downloaded once into the cache directory and trimmed to the segments the landmark fixtures were extracted
from (tests/Fixtures/SOURCES.md), so fixture time t is video time t. Requires Pillow and ffmpeg.
"""
import gzip, json, os, subprocess, sys, tempfile, urllib.request
from PIL import Image, ImageDraw, ImageFont

# fixture: (source URL, start s, end s, crop "w:h:x:y" or None, credit)
SOURCES = {
    "face-nasa-menon": ("https://images-assets.nasa.gov/video/jsc2025m000153_NASA_Astronaut_Candidate_Anna_Menon_Resource_Reel/"
                        "jsc2025m000153_NASA_Astronaut_Candidate_Anna_Menon_Resource_Reel~orig.mp4", 83.2, 125.2, "600:600:660:0", "Video: NASA/JSC"),
    "upper-cdc-press": ("https://upload.wikimedia.org/wikipedia/commons/b/b9/Muscle_Strengthening_at_Home_-_Overhead_Press.webm",
                        0.2, 37.8, None, "Video: CDC"),
    "body-cdc-squat": ("https://upload.wikimedia.org/wikipedia/commons/a/ae/Muscle_Strengthening_at_Home_-_Half_squat.webm",
                       16.6, 35.9, None, "Video: CDC"),
}
CAPTIONS = {"face-nasa-menon": "Face: mouth, each eye, smile, head", "upper-cdc-press": "Waist up: arms and torso",
            "body-cdc-squat": "Whole body: squats"}
NAMES = {"cat.vrm": "Chubby Tubby Cat", "juanita.vrm": "Juanita", "cosmicbot.vrm": "CosmicBot", "froggy.vrm": "Froggy"}
BACKGROUND = (14, 26, 43)

def font(size):
    for path in ["/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/Helvetica.ttc", "/Library/Fonts/Arial.ttf"]:
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()

def fixture(name):
    with gzip.open(os.path.join(os.path.dirname(__file__), "..", "tests", "Fixtures", name + ".jsonl.gz"), "rt") as f:
        return [json.loads(line) for line in f]

def video(cache, name):
    """The trimmed (and cropped) segment of the source video, downloaded and cut once."""
    url, start, end, crop, _ = SOURCES[name]
    os.makedirs(cache, exist_ok=True)
    trimmed = os.path.join(cache, name + ".mp4")
    if not os.path.exists(trimmed):
        source = os.path.join(cache, name + ".source" + os.path.splitext(url)[1])
        if not os.path.exists(source):
            request = urllib.request.Request(url, headers={"User-Agent": "Simple3D-showcase/1.0"})
            with urllib.request.urlopen(request) as response, open(source, "wb") as out:
                out.write(response.read())
        filters = ["-vf", "crop=" + crop] if crop else []
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-ss", str(start), "-to", str(end), "-i", source, *filters,
                        "-an", "-c:v", "libx264", "-crf", "16", trimmed], check=True)
    return trimmed

def frames(path, size):
    """Every frame of a video as RGB images with their times, decoded once."""
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=width,height,r_frame_rate",
                            "-of", "csv=p=0", path], capture_output=True, text=True, check=True).stdout.strip().split(",")
    w, h = int(probe[0]), int(probe[1]); num, den = map(float, probe[2].split("/"))
    raw = subprocess.run(["ffmpeg", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", "-"], capture_output=True, check=True).stdout
    n = len(raw) // (w * h * 3)
    return [(i * den / num, Image.frombytes("RGB", (w, h), raw[i * w * h * 3:(i + 1) * w * h * 3])) for i in range(n)]

def person_center(recorded, start, end):
    """Horizontal center of the person over the scene, as a share of image width (0.5 without body landmarks)."""
    xs = [x for f in recorded if start <= f["t"] <= end and f.get("lm") for x, y, c in f["lm"] if c > .3]
    return sum(xs) / len(xs) if xs else .5

def cover(image, size, center_x):
    """Scales the image to fill the panel, keeping the person centered horizontally."""
    w, h = size
    scale = max(w / image.width, h / image.height)
    resized = image.resize((round(image.width * scale), round(image.height * scale)), Image.LANCZOS)
    left = min(max(0, round(center_x * resized.width - w / 2)), resized.width - w)
    top = (resized.height - h) // 2
    return resized.crop((left, top, left + w, top + h))

def main(render_dir, gif, cache):
    manifest = [line.split() for line in open(os.path.join(render_dir, "manifest.txt"))]
    spans = {}
    for _, name, t, _ in manifest:
        lo, hi = spans.get(name, (1e9, -1e9)); spans[name] = (min(lo, float(t)), max(hi, float(t)))
    centers = {name: person_center(fixture(name), *span) for name, span in spans.items()}
    decoded = {name: frames(video(cache, name), None) for name in spans}
    title, caption, small = font(30), font(24), font(17)
    with tempfile.TemporaryDirectory() as tmp:
        for index, name, t, model in manifest:
            render = Image.open(os.path.join(render_dir, f"frame-{int(index):04d}.png")).convert("RGB")
            _, still = min(decoded[name], key=lambda f: abs(f[0] - float(t)))
            person = cover(still, render.size, centers[name])
            out = Image.new("RGB", (render.width * 2, render.height), BACKGROUND)
            out.paste(person, (0, 0)); out.paste(render, (render.width, 0))
            d = ImageDraw.Draw(out)
            d.rounded_rectangle([14, 14, 304, 62], 14, fill=(8, 12, 22))
            d.text((28, 22), "Simple3D  ·  Mirror", font=title, fill=(255, 255, 255))
            d.text((render.width + 24, 24), NAMES.get(model, model), font=caption, fill=(169, 184, 212))
            d.text((16, out.height - 98), SOURCES[name][4], font=small, fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
            text = CAPTIONS.get(name, "")
            tw = d.textlength(text, font=caption)
            d.rounded_rectangle([out.width / 2 - tw / 2 - 18, out.height - 64, out.width / 2 + tw / 2 + 18, out.height - 22], 16, fill=(8, 12, 22))
            d.text((out.width / 2 - tw / 2, out.height - 58), text, font=caption, fill=(255, 255, 255))
            out.save(os.path.join(tmp, f"{int(index):04d}.png"))
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-framerate", "15", "-i", os.path.join(tmp, "%04d.png"), "-filter_complex",
                        "fps=10,scale=600:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=96:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle",
                        gif], check=True)
    print(f"{gif}: {os.path.getsize(gif) // 1024} KB")

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else os.path.join(tempfile.gettempdir(), "simple3d-showcase"))
