"""
Renders icons from the project's own Hugeicons sprite into transparent PNGs for
the presentation deck.

The deck is supposed to look like the product, so the slide icons are the
literal icons the web portal uses rather than lookalikes from a stock set. The
sprite lives in Views/Shared/_Icons.cshtml as <symbol> elements on a 24x24
viewBox, stroked at 1.5 with round caps and joins and never filled.

Only M, C, L, H, V and Z appear in the sprite's path data, all absolute, so the
parser below handles exactly those and raises on anything else rather than
silently drawing the wrong shape. If someone adds an icon using arcs or relative
commands, this will fail loudly -- which is the intent.

Run directly, or let build_presentation.py call it.
"""
import os
import re

import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
from matplotlib.patches import PathPatch
from matplotlib.path import Path

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SPRITE = os.path.join(ROOT, "Views", "Shared", "_Icons.cshtml")
OUT = os.path.join(HERE, "assets", "icons")

VIEWBOX = 24.0
NUM = re.compile(r"-?\d*\.?\d+(?:e-?\d+)?")


def parse_path(d):
    """SVG path data -> (vertices, codes) in SVG coordinates."""
    verts, codes = [], []
    cur = (0.0, 0.0)
    start = (0.0, 0.0)
    for cmd, body in re.findall(r"([MCLHVZmclhvz])([^MCLHVZmclhvz]*)", d):
        nums = [float(n) for n in NUM.findall(body)]
        if cmd == "M":
            for i in range(0, len(nums), 2):
                pt = (nums[i], nums[i + 1])
                # a second coordinate pair after M is an implicit lineto
                verts.append(pt)
                codes.append(Path.MOVETO if i == 0 else Path.LINETO)
                cur = pt
                if i == 0:
                    start = pt
        elif cmd == "L":
            for i in range(0, len(nums), 2):
                cur = (nums[i], nums[i + 1])
                verts.append(cur)
                codes.append(Path.LINETO)
        elif cmd == "H":
            for x in nums:
                cur = (x, cur[1])
                verts.append(cur)
                codes.append(Path.LINETO)
        elif cmd == "V":
            for y in nums:
                cur = (cur[0], y)
                verts.append(cur)
                codes.append(Path.LINETO)
        elif cmd == "C":
            for i in range(0, len(nums), 6):
                verts.extend([(nums[i], nums[i + 1]),
                              (nums[i + 2], nums[i + 3]),
                              (nums[i + 4], nums[i + 5])])
                codes.extend([Path.CURVE4] * 3)
                cur = (nums[i + 4], nums[i + 5])
        elif cmd in "Zz":
            verts.append(start)
            codes.append(Path.CLOSEPOLY)
            cur = start
        else:
            raise ValueError(f"unhandled path command {cmd!r}")
    return verts, codes


def load_sprite():
    src = open(SPRITE, encoding="utf-8").read()
    icons = {}
    for m in re.finditer(r'<symbol id="(i-[a-z0-9-]+)"[^>]*>(.*?)</symbol>', src, re.S):
        name, body = m.group(1), m.group(2)
        shapes = []
        for pm in re.finditer(r'<path[^>]*\sd="([^"]+)"', body):
            shapes.append(("path", pm.group(1)))
        for cm in re.finditer(r'<circle[^>]*cx="([\d.]+)"[^>]*cy="([\d.]+)"[^>]*r="([\d.]+)"', body):
            shapes.append(("circle", tuple(float(g) for g in cm.groups())))
        if shapes:
            icons[name] = shapes
    return icons


def render(name, shapes, colour, px=256, stroke=1.5):
    dpi = 100.0
    fig = plt.figure(figsize=(px / dpi, px / dpi), dpi=dpi)
    fig.patch.set_alpha(0.0)
    ax = fig.add_axes([0, 0, 1, 1])
    ax.set_xlim(0, VIEWBOX)
    ax.set_ylim(0, VIEWBOX)
    ax.set_aspect("equal")
    ax.axis("off")
    ax.patch.set_alpha(0.0)

    # points per SVG unit, so stroke-width 1.5 keeps the weight it has in the browser
    lw = stroke * (72.0 * (px / dpi) / VIEWBOX)

    for kind, data in shapes:
        if kind == "path":
            verts, codes = parse_path(data)
            # SVG's y axis points down; matplotlib's points up
            verts = [(x, VIEWBOX - y) for x, y in verts]
            ax.add_patch(PathPatch(Path(verts, codes), fill=False, edgecolor=colour,
                                   linewidth=lw, capstyle="round", joinstyle="round"))
        else:
            cx, cy, r = data
            ax.add_patch(plt.Circle((cx, VIEWBOX - cy), r, fill=False,
                                    edgecolor=colour, linewidth=lw))

    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, f"{name}_{colour.lstrip('#')}.png")
    fig.savefig(path, transparent=True, dpi=dpi)
    plt.close(fig)
    return path


# Every icon the deck uses, with the colours it needs it in.
WANTED = {
    "i-graduation":   ["ffffff"],
    "i-qr":           ["16a34a"],
    "i-users":        ["16a34a"],
    "i-car":          ["16a34a"],
    "i-briefcase":    ["16a34a"],
    "i-flag":         ["16a34a"],
    "i-ticket":       ["16a34a"],
    "i-bell":         ["16a34a", "64748b"],
    "i-chart":        ["64748b"],
    "i-file":         ["16a34a", "64748b"],
    "i-shield":       ["16a34a"],
    "i-lock":         ["16a34a"],
    "i-location":     ["16a34a"],
    "i-clock":        ["16a34a"],
    "i-check-circle": ["16a34a"],
    "i-check-badge":  ["16a34a", "ffffff"],
    "i-cloud-sync":   ["16a34a"],
    "i-target":       ["16a34a"],
    "i-idea":         ["16a34a"],
    "i-alert":        ["d97706"],
    "i-map":          ["16a34a"],
    "i-book":         ["16a34a"],
    "i-id":           ["16a34a"],
    "i-close":        ["16a34a"],
    "i-copy":         ["16a34a"],
    "i-dashboard":    ["16a34a"],
}

if __name__ == "__main__":
    sprite = load_sprite()
    missing = [n for n in WANTED if n not in sprite]
    if missing:
        raise SystemExit(f"not in sprite: {missing}")

    count = 0
    for name, colours in WANTED.items():
        for c in colours:
            render(name, sprite[name], "#" + c)
            count += 1
    print(f"rendered {count} icon files from {len(WANTED)} sprite symbols")
