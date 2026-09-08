"""Bundle this artwork and its original assets as one editable local HTML file."""
from pathlib import Path
import argparse
import base64
import re

root = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--output", required=True, type=Path)
args = parser.parse_args()

def data_url(path, mime):
    return "data:" + mime + ";base64," + base64.b64encode(path.read_bytes()).decode("ascii")

html = (root / "index.html").read_text(encoding="utf-8")
css = (root / "style.css").read_text(encoding="utf-8")
html = html.replace('<link rel="stylesheet" href="./style.css">', "<style>\n" + css + "\n</style>")
original = data_url(root.parents[1] / "assets" / "emerald-veil-background.jpg", "image/jpeg")
html = html.replace("../../assets/emerald-veil-background.jpg", original)
scripts = []
for filename, name in [("scene.js", "RainScene"), ("scene-gl.js", "RainSceneGL")]:
    code = (root / filename).read_text(encoding="utf-8")
    code = re.sub(r"\bexport\s+(?=class|const|function)", "", code)
    code = re.sub(r"\bexport\s*\{[^}]*\};?", "", code)
    scripts.append("const " + name + " = (() => {\n" + code + "\nreturn " + name + ";\n})();")
main = (root / "main.js").read_text(encoding="utf-8")
main = re.sub(r"^import .*?;\s*$", "", main, flags=re.MULTILINE)
depth = data_url(root / "assets" / "depth.png", "image/png")
main = main.replace("new URL('./assets/depth.png', import.meta.url).href", repr(depth))
scripts.append(main)
script = "\n".join(scripts).replace("</script", "<\\/script")
html = html.replace('<script type="module" src="./main.js"></script>', '<script type="module">\n' + script + '\n</script>')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(html, encoding="utf-8")
print(str(args.output))
