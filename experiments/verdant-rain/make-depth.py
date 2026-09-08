"""Build an editable, image-guided occlusion map. Source colours are never changed."""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from scipy import ndimage

ROOT = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, default=ROOT.parents[1] / "assets" / "emerald-veil-background.jpg")
parser.add_argument("--overlay", type=Path)
args = parser.parse_args()
size = (1920, 1080)
image = Image.open(args.source).convert("RGB").resize(size, Image.Resampling.LANCZOS)
rgb = np.asarray(image, dtype=np.float32)
r, g, b = rgb.transpose(2, 0, 1)
height, width = g.shape
yy = np.linspace(0, 1, height, dtype=np.float32)[:, None]
depth = np.broadcast_to(0.88 - 0.12 * yy, (height, width)).copy()
regions = []
masks = []

def feather(mask, radius):
    return np.asarray(Image.fromarray(np.uint8(mask * 255)).filter(ImageFilter.GaussianBlur(radius)), dtype=np.float32) / 255

def moss_shelf(name, xmin, xmax, ymin, ymax, fallback):
    green = (g > 137) & (g - b > 31) & (g - r > 20)
    supported = ndimage.uniform_filter(green.astype(np.float32), size=(7, 9)) > 0.38
    x0, x1 = round(xmin * width), min(width, round(xmax * width))
    y0, y1 = round(ymin * height), min(height, round(ymax * height))
    area = supported[y0:y1, x0:x1]
    valid = area.any(axis=0)
    border = area.argmax(axis=0).astype(float) + y0
    if valid.any():
        columns = np.arange(x1 - x0)
        border = np.interp(columns, columns[valid], border[valid])
    else:
        border[:] = fallback * height
    border = ndimage.median_filter(border, size=33)
    indices = list(range(0, len(border), 12))
    if indices[-1] != len(border) - 1:
        indices.append(len(border) - 1)
    points = [(x0 + x, float(border[x]) - 3) for x in indices]
    points += [(x1 - 1, height - 1), (x0, height - 1)]
    outline = Image.new("L", size)
    ImageDraw.Draw(outline).polygon(points, fill=255)
    mask = feather(np.asarray(outline) / 255.0, 3.0)
    regions.append({"id": name, "depth": 0.16, "feather_px": 3, "contour": [[round(x / width, 5), round(y / height, 5)] for x, y in points]})
    masks.append((name, mask))

moss_shelf("bottom_rock", .233, .84, .805, .955, .91)
moss_shelf("left_rock", 0, .254, .49, .64, .55)
moss_shelf("right_rock", .785, 1, .735, .88, .80)
for name, mask in masks:
    depth = depth * (1 - mask) + .16 * mask

maximum, minimum = rgb.max(axis=2), rgb.min(axis=2)
neutral = (maximum - minimum < maximum * .25 + 12) & (g < np.maximum(r, b) * 1.2 + 8)
roi = np.zeros((height, width), dtype=bool)
roi[:round(height * .35), round(width * .785):] = True
candidate = ndimage.binary_closing(neutral & roi, iterations=3)
labels, count = ndimage.label(candidate)
sizes = np.bincount(labels.ravel())
sizes[0] = 0
if not count or sizes.max() < 5000:
    raise RuntimeError("The cat mask could not be identified; inspect the source.")
cat = ndimage.binary_fill_holes(labels == sizes.argmax()).astype(np.float32)
# Restore the cropped-out outer rim where the source cat continues off screen.
cat[:4] = cat[4]
cat[:, -4:] = cat[:, -5:-4]
cat = feather(cat, 1.7)
depth = depth * (1 - cat) + .08 * cat
masks.append(("cat", cat))
regions.append({"id": "cat", "depth": .08, "feather_px": 1.7, "roi": [.785, 0, 1, .35], "method": "largest connected neutral-colour silhouette with filled eye and ear interior"})

destination = ROOT / "assets"
destination.mkdir(exist_ok=True)
Image.fromarray(np.uint8(np.clip(depth, 0, 1) * 255)).save(destination / "depth.png")
Image.fromarray(np.uint8(cat * 255)).save(destination / "cat-mask.png")
(destination / "regions.json").write_text(json.dumps({
    "source": "../../../assets/emerald-veil-background.jpg",
    "source_size": [3840, 2160],
    "map_size": list(size),
    "convention": "near=0, far=1",
    "background": {"top": .88, "bottom": .76},
    "regions": regions,
    "limitation": "Auxiliary 2.5D occlusion only; the forest is not a complete 3D reconstruction."
}, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
if args.overlay:
    display = rgb.copy()
    colours = [[255, 168, 35], [255, 218, 65], [65, 210, 255], [246, 50, 165]]
    for (_, mask), colour in zip(masks, colours):
        alpha = mask[:, :, None] * .42
        display = display * (1 - alpha) + np.array(colour) * alpha
    Image.fromarray(np.uint8(display)).save(args.overlay)
print(json.dumps({"depth": str(destination / "depth.png"), "cat_pixels": int((cat > .5).sum()), "size": list(size)}))
