"""
Cuts MAUI icon files out of the web app's Hugeicons sprite.

    python mobile/tools/gen_icons.py

Views/Shared/_Icons.cshtml holds every glyph the web renders, as <symbol>
elements coloured with `currentColor`. MAUI has no sprite equivalent and no way
to tint a MauiImage at runtime on every platform, so each icon becomes its own
file with the colour baked in — which is why one glyph in two colours is two
files.

Add to ICONS below and re-run; existing files are rewritten, so this is the
place to change an icon, never the .svg itself.
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
SPRITE = ROOT / "Views" / "Shared" / "_Icons.cshtml"
OUT = ROOT / "mobile" / "UniConnect.Mobile" / "Resources" / "Images"

# The palette the icon filenames refer to. Keep in step with the colour tokens
# in Resources/Styles/UniConnect.xaml.
COLOURS = {
    "white": "#ffffff",
    "green": "#108548",   # UcBrandStart — the brand green
    "teal": "#0d4f4f",    # UcTealDeep — Academic Vitality's deep teal
    "muted": "#64748b",   # UcMuted
    "slate": "#334155",   # UcTextSoft
    # UcPillAmberFg, not UcWarning: every amber icon is drawn on an amber-tinted
    # background, where the lighter warning colour loses contrast.
    "amber": "#92400e",
    "red": "#dc2626",     # UcDanger
}

# (sprite id, colour key) -> written as ic_<id with _ for ->_<colour>.svg
ICONS = [
    ("arrow-left", "white"),
    ("arrow-left", "green"),
    ("arrow-right", "green"),
    ("bell", "green"),
    ("bell", "teal"),
    ("sparkles", "teal"),
    ("car", "green"),
    ("car", "muted"),
    ("home", "green"),
    ("home", "muted"),
    ("user-circle", "green"),
    ("users", "muted"),
    ("book", "green"),
    ("briefcase", "green"),
    ("briefcase", "muted"),
    ("calendar", "muted"),
    ("calendar", "green"),
    ("check-badge", "green"),
    ("check-badge", "muted"),
    ("arrow-right", "muted"),
    ("clock", "muted"),
    ("users", "muted"),
    ("calendar", "white"),
    ("check-badge", "slate"),
    ("check-circle", "green"),
    ("check-circle", "white"),
    ("clock", "amber"),
    ("close", "red"),
    ("eye", "muted"),
    ("eye-off", "muted"),
    ("flag", "amber"),
    ("graduation", "white"),
    ("idea", "green"),
    ("info", "green"),
    ("location", "muted"),
    ("location", "teal"),
    ("location", "white"),
    ("lock", "green"),
    ("lock", "muted"),
    ("logout", "white"),
    ("logout", "red"),
    ("mail", "muted"),
    ("plus", "green"),
    ("plus", "white"),
    ("refresh", "green"),
    ("search", "muted"),
    ("send", "white"),
    ("shield", "green"),
    ("trash", "red"),
    ("user-add", "amber"),
    ("user-add", "white"),
    ("edit", "muted"),
    ("user-circle", "muted"),
    ("user-circle", "white"),
    ("user-multiple", "muted"),
    ("users", "green"),
    ("users", "white"),
    # Home dashboard: one per service card, plus the clubs stat.
    ("user-multiple", "green"),
    ("qr", "green"),
    ("ticket", "green"),
    ("flag", "green"),
]

TEMPLATE = ('<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" '
            'viewBox="0 0 24 24" fill="none">{body}</svg>')


def symbols():
    text = SPRITE.read_text(encoding="utf-8")
    found = {}
    for m in re.finditer(r'<symbol id="i-([a-z0-9-]+)"[^>]*>(.*?)</symbol>', text, re.S):
        found[m.group(1)] = m.group(2).strip()
    return found


def main():
    if not SPRITE.exists():
        sys.exit(f"Sprite not found at {SPRITE}")

    found = symbols()
    written = 0

    for name, colour in ICONS:
        if name not in found:
            sys.exit(f"No <symbol id='i-{name}'> in the sprite")
        if colour not in COLOURS:
            sys.exit(f"Unknown colour {colour!r} for {name!r}")

        # The sprite paints strokes and fills with currentColor, inherited from
        # CSS. Nothing inherits in a standalone file, so it is substituted here.
        body = found[name].replace("currentColor", COLOURS[colour])

        path = OUT / f"ic_{name.replace('-', '_')}_{colour}.svg"
        path.write_text(TEMPLATE.format(body=body), encoding="utf-8")
        written += 1

    print(f"{written} icons written to {OUT.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
