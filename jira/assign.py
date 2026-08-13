"""
Assigns every backlog item to a team member.

Deliberately a pure function of (epic, summary) and nothing else, so the same
decision comes out whether it is applied to the CSV files or to the tables in
JIRA_BACKLOG.md. Labels and story points are NOT inputs: their wording differs
slightly between the two, and a rule that read them could disagree with itself.
"""

RIDA = "Mhmd_Rida"
SABBAGH = "Mohamad_Sabbagh"
BOTH = "Mhmd_Rida;Mohamad_Sabbagh"

# What Jira actually matches on. Display names are unreliable (two accounts in
# this site are both called some form of "Mohammad ... Rida"), so the CSVs carry
# the university address of each member's Jira account.
EMAIL = {
    RIDA: "mhr824@usal.edu.lb",
    SABBAGH: "mha206@usal.edu.lb",
}

# The foundation was built together before the work split; these are the
# project's first two parts.
PAIR_EPICS = {"Platform Foundation", "Authentication"}

# Whole-epic ownership where the split is unambiguous.
EPIC_OWNER = {
    "Design System": RIDA,      # front-end
    "Testing": SABBAGH,         # back-end rules coverage
}

# Epics whose genuinely-mixed tasks go to Rida; mixed tasks everywhere else go
# to Sabbagh. Mobile App is his on the evidence — git shows 102 commits touching
# mobile/ from him and none from his teammate. The remaining two were chosen to
# bring the totals to an even split, which is the only reason they are here.
MIXED_TO_RIDA = {"Mobile App", "Ride Sharing", "Hardening"}

FRONTEND = (
    "screen", "page", "ui", "ux", "design", "layout", "redesign", "dropdown",
    "banner", "component", "stylesheet", "icon", "responsive", "animation",
    "calendar", "badge", "viewer", "dashboard", "portal", "form", "browse",
    "map display", "tracking page", "progress-bar", "stepper", "centre",
    "full-screen", "title", "picker", "grid", "avatar", "chat screen",
)

BACKEND = (
    "entity", "entities", "service", "api", "endpoint", "algorithm", "rule",
    "rules", "validation", "validate", "background", "job", "concurrency",
    "database", "migration", "sync", "adapter", "provider", "middleware",
    "security", "token", "auth", "hub", "seeding", "audit", "scoping",
    "workflow", "matching", "similarity", "eligibility", "cap", "model",
    "signalr", "storage", "scheme", "credential", "export",
)


# Jira accepts exactly one assignee per issue, so the 26 pair tasks still need a
# nominal owner. The same wording rule picks it, which leaves 59 points of
# genuinely undecidable work; giving these two to Sabbagh and the rest to Rida
# lands the overall totals on 431 each. Both members stay credited in the
# description and by the "pair" label — this only decides whose queue it sits in.
PAIR_TO_SABBAGH = {
    "Add RequireService filter to gate disabled modules per university",
    "Implement email confirmation with SMTP delivery",
}


def _leaning(summary: str) -> tuple[bool, bool]:
    s = summary.lower()
    return (any(k in s for k in FRONTEND), any(k in s for k in BACKEND))


def primary(epic: str, summary: str) -> str:
    """The single name that goes in the CSV's Assignee column."""
    if epic not in PAIR_EPICS:
        return assign(epic, summary)

    front, back = _leaning(summary)
    if front and not back:
        return RIDA
    if back and not front:
        return SABBAGH
    return SABBAGH if summary in PAIR_TO_SABBAGH else RIDA


def assign(epic: str, summary: str) -> str:
    """Who did the work — BOTH for the shared foundation phase."""
    if epic in PAIR_EPICS:
        return BOTH

    front, back = _leaning(summary)

    # Unambiguous wording wins over the epic's default: a screen is front-end
    # work even inside a back-end-leaning epic.
    if front and not back:
        return RIDA
    if back and not front:
        return SABBAGH

    # Genuinely mixed — the epic decides.
    if front and back:
        return RIDA if epic in MIXED_TO_RIDA else SABBAGH

    # Neither matched.
    return EPIC_OWNER.get(epic, SABBAGH)
