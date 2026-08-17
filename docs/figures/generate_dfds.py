"""
Redraws the report's three data-flow diagrams.

    python docs/figures/generate_dfds.py

Each figure is written twice, as a 300 dpi PNG for pasting into Word and as an
SVG for anyone who wants to edit it further. Nothing here touches the .docx —
the files are written alongside this script for review.

Why redraw them at all: the originals were exported from a diagramming tool at
a size where the flow labels rendered at roughly 5pt, several arrows crossed
each other or passed through boxes, and the three diagrams used three different
visual vocabularies. The rules below are applied consistently across all three,
which is what makes a set of diagrams readable as a set:

  * process        rounded rectangle, brand green, white text, numbered
  * external entity  square-cornered rectangle, white, slate border
  * data store     Gane-Sarson open-ended store, amber, with a D-number
  * flow           slate arrow, label in a white box so it stays legible
                   where it crosses a line

Colours are the report's own palette (Section 4.3) so the diagrams match the
screenshots either side of them.
"""

import math
from pathlib import Path

import matplotlib
matplotlib.use("Agg")

import matplotlib.pyplot as plt
from matplotlib.patches import Circle, FancyArrowPatch, FancyBboxPatch, Rectangle

OUT = Path(__file__).resolve().parent

# ---- palette (matches the report's design system) -----------------------
GREEN = "#16a34a"          # --uc-primary: processes
GREEN_DARK = "#166534"     # process outline
SLATE = "#334155"          # entity outline and body text
SLATE_SOFT = "#475569"     # flow arrows
AMBER = "#b45309"          # data stores
AMBER_FILL = "#fffbeb"
WHITE = "#ffffff"

FONT = ["Segoe UI", "DejaVu Sans", "Arial"]
plt.rcParams["font.family"] = FONT


# ---- primitives ---------------------------------------------------------

def process(ax, x, y, w, h, number, name):
    """A numbered transformation. Rounded, filled, the visual anchor."""
    ax.add_patch(FancyBboxPatch(
        (x - w / 2, y - h / 2), w, h,
        boxstyle="round,pad=0,rounding_size=0.18",
        linewidth=1.6, edgecolor=GREEN_DARK, facecolor=GREEN, zorder=3))
    ax.text(x, y + h / 2 - 0.26, number, ha="center", va="center",
            fontsize=9, color="#dcfce7", fontweight="bold", zorder=4)
    ax.text(x, y - 0.06, name, ha="center", va="center",
            fontsize=10.5, color=WHITE, fontweight="bold",
            linespacing=1.45, zorder=4)


def entity(ax, x, y, w, h, name):
    """Something outside the system boundary. Square corners, unfilled."""
    ax.add_patch(Rectangle(
        (x - w / 2, y - h / 2), w, h,
        linewidth=1.5, edgecolor=SLATE, facecolor=WHITE, zorder=3))
    ax.text(x, y, name, ha="center", va="center",
            fontsize=10, color=SLATE, linespacing=1.45, zorder=4)


def store(ax, x, y, w, h, ident, name):
    """
    Gane-Sarson data store: open at the right-hand end, with the store's
    identifier in a cell at the left. Drawn from lines rather than as a patch
    because the open end is the whole point of the notation.
    """
    left, right = x - w / 2, x + w / 2
    top, bottom = y + h / 2, y - h / 2
    divider = left + 0.62

    ax.add_patch(Rectangle((left, bottom), w, h, linewidth=0,
                           facecolor=AMBER_FILL, zorder=2))
    for y0 in (top, bottom):
        ax.plot([left, right], [y0, y0], color=AMBER, linewidth=1.6, zorder=3)
    ax.plot([left, left], [bottom, top], color=AMBER, linewidth=1.6, zorder=3)
    ax.plot([divider, divider], [bottom, top], color=AMBER, linewidth=1.2, zorder=3)

    ax.text((left + divider) / 2, y, ident, ha="center", va="center",
            fontsize=9.5, color=AMBER, fontweight="bold", zorder=4)
    ax.text((divider + right) / 2, y, name, ha="center", va="center",
            fontsize=9.5, color=SLATE, linespacing=1.4, zorder=4)


def flow(ax, p0, p1, label=None, offset=0.0, rad=0.0, label_at=0.5,
         label_shift=(0.0, 0.0)):
    """
    An arrow from p0 to p1, optionally shifted sideways so that a request and
    its response can run parallel instead of on top of one another.
    """
    (x0, y0), (x1, y1) = p0, p1
    if offset:
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy) or 1.0
        nx, ny = -dy / length, dx / length      # unit normal
        x0, y0 = x0 + nx * offset, y0 + ny * offset
        x1, y1 = x1 + nx * offset, y1 + ny * offset

    ax.add_patch(FancyArrowPatch(
        (x0, y0), (x1, y1),
        arrowstyle="-|>", mutation_scale=13,
        linewidth=1.3, color=SLATE_SOFT,
        connectionstyle=f"arc3,rad={rad}",
        shrinkA=2, shrinkB=2, zorder=5))

    if not label:
        return

    lx = x0 + (x1 - x0) * label_at + label_shift[0]
    ly = y0 + (y1 - y0) * label_at + label_shift[1]
    if rad:
        # Nudge the label onto the curve rather than the chord it spans.
        dx, dy = x1 - x0, y1 - y0
        length = math.hypot(dx, dy) or 1.0
        lx += -dy / length * rad * length * 0.5
        ly += dx / length * rad * length * 0.5

    ax.text(lx, ly, label, ha="center", va="center",
            fontsize=8, color=SLATE, linespacing=1.35, zorder=6,
            bbox=dict(boxstyle="round,pad=0.28", facecolor=WHITE,
                      edgecolor="none", alpha=0.96))


def canvas(width, height):
    fig, ax = plt.subplots(figsize=(width, height))
    ax.set_xlim(0, width)
    ax.set_ylim(0, height)
    ax.set_aspect("equal")
    ax.axis("off")
    fig.subplots_adjust(left=0, right=1, top=1, bottom=0)
    return fig, ax


def legend(ax, x, y):
    """One shared key, so the shapes mean the same thing in all three."""
    items = [
        (GREEN, GREEN_DARK, "Process"),
        (WHITE, SLATE, "External entity"),
        (AMBER_FILL, AMBER, "Data store"),
    ]
    for i, (fill, edge, label) in enumerate(items):
        yy = y - i * 0.42
        ax.add_patch(Rectangle((x, yy - 0.12), 0.34, 0.24,
                               linewidth=1.3, edgecolor=edge, facecolor=fill))
        ax.text(x + 0.48, yy, label, ha="left", va="center",
                fontsize=8.5, color=SLATE)


def save(fig, name):
    for ext in ("png", "svg"):
        path = OUT / f"{name}.{ext}"
        fig.savefig(path, dpi=300, facecolor=WHITE,
                    bbox_inches="tight", pad_inches=0.18)
        print(f"  wrote {path.relative_to(OUT.parent.parent)}")
    plt.close(fig)


# ---- Figure 2 — Context diagram ----------------------------------------

def figure2_context():
    fig, ax = canvas(12.4, 7.6)

    cx, cy, r = 6.2, 3.9, 1.28
    ax.add_patch(Circle((cx, cy), r, linewidth=1.8,
                        edgecolor=GREEN_DARK, facecolor=GREEN, zorder=3))
    ax.text(cx, cy + 0.42, "0", ha="center", va="center",
            fontsize=11, color="#dcfce7", fontweight="bold", zorder=4)
    ax.text(cx, cy - 0.16, "UniConnect\nSystem", ha="center", va="center",
            fontsize=12, color=WHITE, fontweight="bold",
            linespacing=1.4, zorder=4)

    entity(ax, 1.75, 3.9, 2.9, 1.30, "Student / Instructor /\nStaff\n(web + mobile)")
    entity(ax, 6.2, 6.85, 3.2, 1.00, "Administrator /\nCompany user (web portal)")
    entity(ax, 10.65, 3.9, 2.9, 1.00, "University's\nexternal API")
    # The store's contents are spelled out as the original did: "academic-data
    # cache" rather than "cache", because the distinction between a cache and a
    # system of record is the point Section 4.1 rests on.
    store(ax, 6.2, 1.00, 5.4, 1.05, "D1",
          "UniConnect database\n(accounts, service records,\nacademic-data cache)")

    # user <-> system
    flow(ax, (3.20, 3.9), (cx - r, 3.9), "Service requests\n(attendance, groups, rides, tickets)",
         offset=0.42, label_at=0.5, label_shift=(0, 0.30))
    flow(ax, (cx - r, 3.9), (3.20, 3.9), "Responses,\nnotifications",
         offset=0.42, label_at=0.5, label_shift=(0, -0.30))

    # admin <-> system
    flow(ax, (6.2, 6.35), (cx, cy + r), "Admin actions,\nreport requests",
         offset=0.55, label_shift=(0, 0.05))
    flow(ax, (cx, cy + r), (6.2, 6.35), "Dashboards,\naudit log",
         offset=0.55, label_shift=(0, 0.05))

    # system <-> university API
    flow(ax, (cx + r, 3.9), (9.20, 3.9), "Enrolment and\nidentity lookups",
         offset=0.42, label_shift=(0, 0.30))
    flow(ax, (9.20, 3.9), (cx + r, 3.9), "Academic data\n(read-only)",
         offset=0.42, label_shift=(0, -0.30))

    # system <-> own database
    flow(ax, (cx, cy - r), (6.2, 1.46), "Read / write\nservice data",
         offset=0.60, label_shift=(0, 0))
    flow(ax, (6.2, 1.46), (cx, cy - r), None, offset=0.60)

    legend(ax, 0.35, 7.30)
    ax.text(0.35, 0.35,
            "UniConnect never writes to the university's own systems: every academic-data flow is read-only.",
            fontsize=8.5, color=SLATE_SOFT, style="italic", ha="left", va="center")
    save(fig, "figure2-context-dfd")


# ---- Figure 3 — Level 0 -------------------------------------------------

def figure3_level0():
    """
    Laid out so that every flow is either horizontal or vertical, with one
    deliberate diagonal (services writing their own records). The background
    sync sits below the API rather than beside the adapter, which is what keeps
    its two flows from cutting through the adapter or the API box.
    """
    fig, ax = canvas(14.6, 9.4)

    # Node labels are kept to the bare name: the qualifying detail that used to
    # sit in parentheses belongs in the prose around the figure, not inside the
    # boxes, where it competes with the flow labels for the reader's attention.
    # The one exception is D1, where naming the contents is the point.
    entity(ax, 3.20, 7.90, 3.2, 0.80, "Web / mobile users")
    process(ax, 3.20, 5.80, 3.2, 1.00, "1.0", "Core Platform")
    process(ax, 8.20, 5.80, 3.4, 1.00, "3.0", "Service Modules")
    process(ax, 8.20, 3.50, 3.4, 1.00, "2.0", "Adapter Layer")
    entity(ax, 12.70, 3.50, 3.0, 1.00, "University's\nexternal API")
    process(ax, 12.70, 1.30, 3.0, 1.00, "4.0", "Background Sync")
    store(ax, 3.20, 1.30, 4.0, 1.05, "D1", "UniConnect database\n(accounts, service records)")
    store(ax, 8.20, 1.30, 4.0, 1.05, "D2", "Academic-data cache")

    # users <-> core platform
    flow(ax, (3.20, 7.50), (3.20, 6.30), "Login,\nservice requests",
         offset=-0.40, label_shift=(-0.62, 0))
    flow(ax, (3.20, 6.30), (3.20, 7.50), "Responses,\nnotifications",
         offset=-0.40, label_shift=(0.70, 0))

    # core platform <-> services
    flow(ax, (4.80, 5.80), (6.50, 5.80), "Role and\nservice context",
         offset=0.34, label_shift=(0, 0.30))
    flow(ax, (6.50, 5.80), (4.80, 5.80), "Service results",
         offset=0.34, label_shift=(0, -0.32))

    # services <-> adapter
    flow(ax, (8.20, 5.30), (8.20, 4.00), "Enrolment /\nidentity check",
         offset=-0.36, label_shift=(-0.78, 0))
    flow(ax, (8.20, 4.00), (8.20, 5.30), "Verified\nacademic data",
         offset=-0.36, label_shift=(0.82, 0))

    # adapter <-> university API
    flow(ax, (9.90, 3.50), (11.20, 3.50), "Live read-only query",
         offset=0.28, label_shift=(0, 0.30))
    flow(ax, (11.20, 3.50), (9.90, 3.50), "Students, courses,\nenrolments",
         offset=0.28, label_shift=(0, -0.34))

    # background sync
    flow(ax, (12.70, 1.80), (12.70, 3.00), "Periodic\nbulk refresh",
         label_shift=(-0.92, 0))
    flow(ax, (11.20, 1.30), (10.20, 1.30), "Writes\nrefreshed cache",
         label_shift=(0, 0.52))

    # stores. The Core Platform owns accounts and per-university configuration,
    # so it writes to D1 in its own right - not only through the services.
    flow(ax, (3.20, 5.30), (3.20, 1.83), "Accounts,\nconfiguration",
         label_at=0.45, label_shift=(-0.72, 0))

    # The diagonal leaves from the left of process 3.0 rather than its centre
    # so it clears the "Enrolment / identity check" label below.
    flow(ax, (6.85, 5.30), (4.60, 1.83), "Service records",
         label_at=0.55, label_shift=(0.20, 0.20))
    flow(ax, (8.20, 1.83), (8.20, 3.00), "Fallback read",
         label_shift=(0.72, 0))

    legend(ax, 12.05, 8.90)
    save(fig, "figure3-level0-dfd")


# ---- Figure 4 — Level 2, Smart Attendance -------------------------------

def figure4_attendance():
    """
    Three stacked bands, one per sub-process, read top to bottom: the
    instructor opens a session, the student submits against it, the result is
    recorded and returned. The stores stop well short of the right margin so
    their open ends read as the notation rather than as clipping.
    """
    fig, ax = canvas(13.6, 9.0)

    entity(ax, 2.10, 7.55, 2.6, 0.90, "Instructor")
    process(ax, 6.30, 7.55, 3.5, 1.25, "3.1", "Create Session\n(time-bound QR token)")
    store(ax, 10.90, 7.55, 3.4, 0.90, "D3", "AttendanceSession")

    entity(ax, 2.10, 4.55, 2.6, 1.20, "Student\n(mobile or web)")
    process(ax, 6.30, 4.55, 3.5, 1.45, "3.2", "Validate Submission\n(token, window, GPS,\nenrolment, device)")
    entity(ax, 10.90, 4.55, 3.0, 1.05, "University's\nexternal API")

    process(ax, 6.30, 1.55, 3.5, 1.20, "3.3", "Aggregate Result\nand Notify")
    store(ax, 10.90, 1.55, 3.4, 0.90, "D4", "AttendanceRecord")

    # session creation
    flow(ax, (3.40, 7.55), (4.55, 7.55), "Course, time window,\nGPS radius",
         label_shift=(0, 0.52))
    flow(ax, (8.05, 7.55), (9.20, 7.55), "Session +\nQR token", label_shift=(0, 0.46))

    # the stored session is what a submission is validated against
    flow(ax, (10.90, 7.10), (7.40, 5.28), "Token, time window,\nGPS radius",
         label_at=0.50, label_shift=(0.55, 0.46))

    # student submission
    flow(ax, (3.40, 4.55), (4.55, 4.55), "QR scan or token,\nGPS, device fingerprint",
         label_shift=(0, 0.62))

    # enrolment check against the university
    flow(ax, (8.05, 4.55), (9.40, 4.55), "Enrolment check",
         offset=0.28, label_shift=(0, 0.30))
    flow(ax, (9.40, 4.55), (8.05, 4.55), "Enrolled /\nnot enrolled",
         offset=0.28, label_shift=(0, -0.34))

    # outcome
    flow(ax, (8.05, 4.00), (9.20, 2.00), "Present / Late /\nrejected with reason",
         label_at=0.50, label_shift=(1.15, 0.28))
    flow(ax, (9.20, 1.55), (8.05, 1.55), "New submission", label_shift=(0, 0.40))
    flow(ax, (4.55, 1.55), (2.45, 3.95), "Notification\nof result",
         label_at=0.52, label_shift=(-0.72, -0.14))

    legend(ax, 0.40, 2.05)
    save(fig, "figure4-attendance-dfd")


if __name__ == "__main__":
    print("writing diagrams:")
    figure2_context()
    figure3_level0()
    figure4_attendance()
    print("done")
