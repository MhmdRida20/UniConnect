"""
Visual layer for the deck: generated backgrounds, and the OOXML effects that
python-pptx has no API for (soft shadows, slide transitions, entrance builds).

The background motif is a node-and-edge constellation. That is not decoration
picked for looking technical -- the whole argument of the project is that
UniConnect connects things that were previously siloed, so the visual signature
is a network. It sits at low opacity behind everything and never competes with
the content.

Anything in here that writes raw XML is doing so because the feature does not
exist in python-pptx. Each such function notes what PowerPoint expects, because
a malformed element makes PowerPoint offer to "repair" the file, which silently
strips it.
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from pptx.oxml.ns import qn
from pptx.util import Pt

# --------------------------------------------------------------- palette ---
GREEN      = "16a34a"
GREEN_DK   = "15803d"
GREEN_DKR  = "166534"
GREEN_DEEP = "14532d"
GREEN_MID  = "108548"   # the mobile client's gradient start
GREEN_LOW  = "0a5d32"   # and its end
GREEN_LT   = "22c55e"
INK        = "0f172a"
BG         = "f6f9f7"


def _rgb(h):
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


# ----------------------------------------------------------- backgrounds ---
def _linear(w, h, c1, c2, angle_deg):
    """Float RGB array, c1 -> c2 along `angle_deg`."""
    ang = math.radians(angle_deg)
    x = np.linspace(0.0, 1.0, w)[None, :]
    y = np.linspace(0.0, 1.0, h)[:, None]
    t = x * math.cos(ang) + y * math.sin(ang)
    t = (t - t.min()) / (t.max() - t.min())
    a, b = np.array(_rgb(c1), float), np.array(_rgb(c2), float)
    return a[None, None, :] + (b - a)[None, None, :] * t[:, :, None]


def _glow(arr, cx, cy, radius, colour, strength):
    """Soft radial light, blended toward `colour`."""
    h, w = arr.shape[:2]
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / radius
    m = np.clip(1.0 - d, 0.0, 1.0) ** 2 * strength
    c = np.array(_rgb(colour), float)
    arr += (c[None, None, :] - arr) * m[:, :, None]


def _constellation(size, seed, count, colour, alpha, link_dist,
                   node_r=3.2, region=None, scale=2):
    """
    Node-and-edge layer on transparent RGBA.

    Drawn at `scale` and downsampled, because ImageDraw has no antialiasing and
    hairlines at 1x look like torn paper on a projector.
    """
    w, h = size
    W, H = w * scale, h * scale
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    rnd = random.Random(seed)

    x0, y0, x1, y1 = region or (0, 0, w, h)
    pts = []
    # rejection-sampled so nodes never clump into a blob
    guard = 0
    while len(pts) < count and guard < count * 200:
        guard += 1
        px = rnd.uniform(x0, x1)
        py = rnd.uniform(y0, y1)
        if all((px - qx) ** 2 + (py - qy) ** 2 > (link_dist * 0.42) ** 2
               for qx, qy in pts):
            pts.append((px, py))

    rc = _rgb(colour)
    for i, (ax, ay) in enumerate(pts):
        for bx, by in pts[i + 1:]:
            dist = math.hypot(ax - bx, ay - by)
            if dist < link_dist:
                fade = (1.0 - dist / link_dist) ** 1.5
                d.line([ax * scale, ay * scale, bx * scale, by * scale],
                       fill=rc + (int(alpha * fade * 0.85),), width=max(1, scale))
    for px, py in pts:
        r = node_r * scale
        d.ellipse([px * scale - r, py * scale - r, px * scale + r, py * scale + r],
                  fill=rc + (int(alpha * 1.5),))

    return layer.resize((w, h), Image.LANCZOS)


def make_backgrounds(out_dir, w=2000, h=1125):
    """bg_dark: title and closing.  bg_light: every content slide."""
    os.makedirs(out_dir, exist_ok=True)

    # ---- dark ------------------------------------------------------------
    arr = _linear(w, h, GREEN_DKR, "0b3d21", 34)
    _glow(arr, w * 0.20, h * 0.30, w * 0.52, GREEN_MID, 0.42)
    _glow(arr, w * 0.86, h * 0.82, w * 0.46, GREEN_DK, 0.30)
    _glow(arr, w * 0.62, h * 0.10, w * 0.34, GREEN_LT, 0.10)
    # vignette, so the type at the edges keeps its contrast
    yy, xx = np.mgrid[0:h, 0:w]
    v = np.sqrt(((xx / w) - 0.5) ** 2 + ((yy / h) - 0.5) ** 2) / 0.72
    arr *= (1.0 - np.clip(v, 0, 1) ** 2 * 0.30)[:, :, None]

    dark = Image.fromarray(np.clip(arr, 0, 255).astype("uint8"))
    dark = Image.alpha_composite(
        dark.convert("RGBA"),
        _constellation((w, h), seed=7, count=64, colour="d7f7e4",
                       alpha=58, link_dist=210))
    dark.convert("RGB").save(os.path.join(out_dir, "bg_dark.png"))

    # ---- light -----------------------------------------------------------
    arr = _linear(w, h, "ffffff", BG, 70)
    _glow(arr, w * 0.90, h * -0.10, w * 0.62, "dcfce7", 0.72)
    _glow(arr, w * 0.06, h * 1.06, w * 0.50, "e8f6ee", 0.60)

    light = Image.fromarray(np.clip(arr, 0, 255).astype("uint8")).convert("RGBA")
    # the motif is kept to the top-right corner, away from the title and body
    light = Image.alpha_composite(
        light,
        _constellation((w, h), seed=19, count=26, colour=GREEN,
                       alpha=30, link_dist=205, node_r=2.6,
                       region=(w * 0.60, -h * 0.04, w * 1.02, h * 0.40)))
    light.convert("RGB").save(os.path.join(out_dir, "bg_light.png"))

    print("backgrounds: bg_dark.png, bg_light.png")


def make_browser_frame(shot_path, out_path, radius=26, bar=64, pad=10):
    """
    Seats a web screenshot in a browser chrome so it reads as the product
    rather than as a loose rectangle pasted onto the slide.
    """
    with Image.open(shot_path) as im:
        shot = im.convert("RGBA")

    W = shot.width + pad * 2
    H = shot.height + bar + pad
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(frame)
    d.rounded_rectangle([0, 0, W - 1, H - 1], radius=radius,
                        fill=_rgb("103a24") + (255,))

    # traffic lights, in the deck's own greens rather than macOS red/amber/green
    for i, col in enumerate(("2f6b49", "2f6b49", "2f6b49")):
        cx = pad + 20 + i * 26
        cy = bar // 2
        d.ellipse([cx - 7, cy - 7, cx + 7, cy + 7], fill=_rgb(col) + (255,))

    # address pill
    d.rounded_rectangle([pad + 110, bar // 2 - 13, W - pad - 24, bar // 2 + 13],
                        radius=13, fill=_rgb("17492e") + (255,))

    mask = Image.new("L", shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, shot.width - 1, shot.height - 1], radius=12, fill=255)
    shot.putalpha(mask)
    frame.alpha_composite(shot, (pad, bar))
    frame.save(out_path)
    return out_path


def make_glow(out_path, size=900, colour=GREEN_LT, strength=0.85):
    """A soft radial glow on transparency, for sitting behind the brand mark."""
    yy, xx = np.mgrid[0:size, 0:size]
    c = size / 2.0
    d = np.sqrt((xx - c) ** 2 + (yy - c) ** 2) / c
    a = np.clip(1.0 - d, 0.0, 1.0) ** 2.6 * strength * 255.0
    rgb = np.array(_rgb(colour), float)
    img = np.zeros((size, size, 4), float)
    img[:, :, 0:3] = rgb[None, None, :]
    img[:, :, 3] = a
    Image.fromarray(img.astype("uint8"), "RGBA").save(out_path)
    return out_path


def make_phone_frame(shot_path, out_path, radius=48, pad=18):
    """Rounds the screenshot's corners and seats it in a dark device bezel."""
    with Image.open(shot_path) as im:
        shot = im.convert("RGBA")

    mask = Image.new("L", shot.size, 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, shot.width - 1, shot.height - 1],
                                           radius=radius, fill=255)
    shot.putalpha(mask)

    W, H = shot.width + pad * 2, shot.height + pad * 2
    frame = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    ImageDraw.Draw(frame).rounded_rectangle(
        [0, 0, W - 1, H - 1], radius=radius + pad, fill=_rgb(GREEN_DEEP) + (255,))
    frame.alpha_composite(shot, (pad, pad))
    frame.save(out_path)
    return out_path


# ------------------------------------------------------- OOXML: shadows ---
_A = "http://schemas.openxmlformats.org/drawingml/2006/main"


def fill_alpha(shape, alpha):
    """
    Makes an already-solid fill translucent.

    python-pptx can set a fill colour but not its alpha, so the a:alpha child is
    appended to the existing a:srgbClr. Used for panels that should read as
    glass over the background rather than as a flat block sitting on top of it.
    """
    from pptx.oxml import parse_xml

    solid = shape._element.spPr.find(qn("a:solidFill"))
    if solid is None:
        return shape
    srgb = solid.find(qn("a:srgbClr"))
    if srgb is None:
        return shape
    for old in srgb.findall(qn("a:alpha")):
        srgb.remove(old)
    srgb.append(parse_xml(f'<a:alpha xmlns:a="{_A}" val="{int(alpha * 100000)}"/>'))
    return shape


def soft_shadow(shape, blur=10, dist=3, alpha=0.09, colour=INK):
    """
    A low, soft drop shadow.

    python-pptx exposes shadow.inherit only, so the effect list is written
    directly. In CT_ShapeProperties, a:effectLst must follow the fill and line,
    which is where append lands it.
    """
    from pptx.oxml import parse_xml

    spPr = shape._element.spPr
    for old in spPr.findall(qn("a:effectLst")):
        spPr.remove(old)
    spPr.append(parse_xml(
        f'<a:effectLst xmlns:a="{_A}">'
        f'<a:outerShdw blurRad="{int(blur * 12700)}" dist="{int(dist * 12700)}" '
        f'dir="5400000" rotWithShape="0">'
        f'<a:srgbClr val="{colour.upper()}"><a:alpha val="{int(alpha * 100000)}"/></a:srgbClr>'
        f'</a:outerShdw></a:effectLst>'))
    return shape


# --------------------------------------------------- OOXML: transitions ---
_P = "http://schemas.openxmlformats.org/presentationml/2006/main"


def set_transition(slide, kind="fade", speed="med"):
    """
    Slide transition. p:transition belongs after p:clrMapOvr in p:sld, and
    PowerPoint drops the whole element if it appears anywhere else.
    """
    from pptx.oxml import parse_xml

    sld = slide._element
    for old in sld.findall(qn("p:transition")):
        sld.remove(old)

    el = parse_xml(f'<p:transition xmlns:p="{_P}" spd="{speed}"><p:{kind}/></p:transition>')
    anchor = sld.find(qn("p:clrMapOvr"))
    if anchor is not None:
        anchor.addnext(el)
    else:
        sld.find(qn("p:cSld")).addnext(el)
    return el


# ---------------------------------------------------- OOXML: animations ---
def animate(slide, waves, dur=420, gap=170, first_delay=120,
            filters=None, stagger=0):
    """
    Reveal shapes one wave at a time, automatically.

    `waves` is a list of shape lists; everything inside a wave appears together,
    and each wave follows the previous one without a click. Clicks are avoided
    deliberately -- a presenter working through a ten-minute defence should not
    be counting clicks to keep the slide in sync with the sentence.

    `filters` optionally names the reveal per wave. "fade" is the default;
    "wipe(right)" reveals left-to-right, which is what a bar growing from its
    baseline actually needs -- animScale would grow it from its centre outward.
    `stagger` delays each shape within a wave by that many ms, so a group of
    bars sweeps rather than snapping in together.

    The tree is p:timing > p:tnLst > root p:par > p:seq(mainSeq) > one p:par per
    wave, each holding a p:par per shape carrying a p:set (make visible) and a
    p:animEffect.
    """
    from pptx.oxml import parse_xml

    waves = [[s for s in w if s is not None] for w in waves]
    keep = [i for i, w in enumerate(waves) if w]
    if filters is None:
        filters = ["fade"] * len(waves)
    elif isinstance(filters, str):
        filters = [filters] * len(waves)
    filters = [filters[i] if i < len(filters) else "fade" for i in keep]
    waves = [waves[i] for i in keep]
    if not waves:
        return None

    sld = slide._element
    for old in sld.findall(qn("p:timing")):
        sld.remove(old)

    counter = [3]          # 1 = tmRoot, 2 = mainSeq

    def nid():
        counter[0] += 1
        return counter[0]

    wave_xml = []
    for w_i, wave in enumerate(waves):
        shape_xml = []
        filt = filters[w_i]
        # preset 10 is Appear/Fade; 22 is Wipe. Naming it lets PowerPoint show
        # the effect properly in the animation pane instead of "custom".
        #
        # The filter string alone leaves the direction unset -- PowerPoint reads
        # it back as "none" and picks its own. presetSubtype is what actually
        # carries it, named for the edge the reveal STARTS from, which is the
        # opposite of the direction the wipe travels.
        preset = 22 if filt.startswith("wipe") else 10
        subtype = {"wipe(right)": 2,    # from left
                   "wipe(left)": 4,     # from right
                   "wipe(up)": 1,       # from bottom
                   "wipe(down)": 8      # from top
                   }.get(filt, 0)
        for s_i, shp in enumerate(wave):
            spid = shp.shape_id
            shape_xml.append(
                f'<p:par><p:cTn id="{nid()}" presetID="{preset}" presetClass="entr" '
                f'presetSubtype="{subtype}" fill="hold" grpId="0" nodeType="afterEffect">'
                f'<p:stCondLst><p:cond delay="{s_i * stagger}"/></p:stCondLst><p:childTnLst>'
                f'<p:set><p:cBhvr><p:cTn id="{nid()}" dur="1" fill="hold">'
                f'<p:stCondLst><p:cond delay="0"/></p:stCondLst></p:cTn>'
                f'<p:tgtEl><p:spTgt spid="{spid}"/></p:tgtEl>'
                f'<p:attrNameLst><p:attrName>style.visibility</p:attrName></p:attrNameLst>'
                f'</p:cBhvr><p:to><p:strVal val="visible"/></p:to></p:set>'
                f'<p:animEffect transition="in" filter="{filt}">'
                f'<p:cBhvr><p:cTn id="{nid()}" dur="{dur}"/>'
                f'<p:tgtEl><p:spTgt spid="{spid}"/></p:tgtEl></p:cBhvr>'
                f'</p:animEffect></p:childTnLst></p:cTn></p:par>')

        delay = first_delay if w_i == 0 else gap
        wave_xml.append(
            f'<p:par><p:cTn id="{nid()}" fill="hold">'
            f'<p:stCondLst><p:cond delay="{delay}"/></p:stCondLst><p:childTnLst>'
            f'<p:par><p:cTn id="{nid()}" fill="hold">'
            f'<p:stCondLst><p:cond delay="0"/></p:stCondLst>'
            f'<p:childTnLst>{"".join(shape_xml)}</p:childTnLst>'
            f'</p:cTn></p:par></p:childTnLst></p:cTn></p:par>')

    timing = parse_xml(
        f'<p:timing xmlns:p="{_P}"><p:tnLst>'
        f'<p:par><p:cTn id="1" dur="indefinite" restart="never" nodeType="tmRoot">'
        f'<p:childTnLst><p:seq concurrent="1" nextAc="seek">'
        f'<p:cTn id="2" dur="indefinite" nodeType="mainSeq"><p:childTnLst>'
        f'{"".join(wave_xml)}'
        f'</p:childTnLst></p:cTn>'
        f'<p:prevCondLst><p:cond evt="onPrev" delay="0">'
        f'<p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:prevCondLst>'
        f'<p:nextCondLst><p:cond evt="onNext" delay="0">'
        f'<p:tgtEl><p:sldTgt/></p:tgtEl></p:cond></p:nextCondLst>'
        f'</p:seq></p:childTnLst></p:cTn></p:par>'
        f'</p:tnLst></p:timing>')
    sld.append(timing)
    return timing
