"""
Regenerates the two team-managed import files from the master table in
JIRA_BACKLOG.md, so the document and the CSVs cannot drift apart.

    python jira/build_csv.py

Reads the fenced CSV block in section 16 (the company-managed reference copy,
which is the one place every task is listed with its status and points) and
writes jira/1-epics.csv and jira/2-tasks.csv in team-managed field names.

Assignees come from assign.py, not from the master table, so the rule stays the
single source of truth for who owns what.
"""

import csv
import io
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
import assign as A  # noqa: E402

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "JIRA_BACKLOG.md"
OUT = ROOT / "jira"

# The short name used in the master table's Epic Link column -> the epic's full
# summary. Kept here rather than derived, because the two differ deliberately:
# "Ticketing" is easier to find-and-replace than the full title.
EPIC_TOKENS = {
    "Platform Foundation": "Platform Foundation & Multi-University Architecture",
    "Authentication": "Authentication Roles & Account Management",
    "Study Groups": "Study Groups",
    "Ride Sharing": "Ride Sharing",
    "Smart Attendance": "Smart Attendance",
    "Ticketing": "Complaints & Ticketing",
    "Clubs": "Clubs & Organizations",
    "Internships": "Internships & Career Matching",
    "Notifications": "Notifications & Real-Time Infrastructure",
    "Administration": "Administration Reporting & Audit",
    "Design System": "UI/UX & Design System",
    "Testing": "Automated Testing",
    "Mobile App": "Mobile Application",
    "Hardening": "Security Hardening & Defect Fixes",
}


def master_rows():
    text = DOC.read_text(encoding="utf-8")
    block = re.search(r"## 16\..*?```csv\n(.*?)```", text, re.S)
    if not block:
        sys.exit("Could not find the CSV block in section 16 of JIRA_BACKLOG.md")
    return list(csv.DictReader(io.StringIO(block.group(1))))


def enrich(r):
    """Everything the import files need that the master table does not hold."""
    epic = r["Epic Link"]
    worked_on = A.assign(epic, r["Summary"])
    owner = A.primary(epic, r["Summary"])

    description, labels = r["Description"], r["Labels"]
    if worked_on == A.BOTH:
        # Jira takes one assignee, so the pairing is recorded where it survives
        # the import instead of being lost with the ";" value.
        description = f"{description} — pair work by Mhmd Rida and Mohamad Sabbagh"
        labels = f"{labels};pair"

    return owner, description, labels


def write_all_in_one(epics, tasks):
    """
    Single-file import for the current Jira importer, which refuses a Parent
    column unless every row carries a "Work item ID" that parents can point at.
    Rows reference each other by that ID, so this needs no epic keys and no
    find-and-replace — but it creates the epics too, so the project must not
    already contain them.
    """
    epic_id = {r["Epic Name"]: i for i, r in enumerate(epics, start=1)}

    with open(OUT / "0-all-in-one.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["Work item ID", "Summary", "Work type", "Description", "Parent",
                    "Status", "Assignee", "Labels", "Story point estimate"])

        for r in epics:
            w.writerow([epic_id[r["Epic Name"]], r["Summary"], "Epic", r["Description"],
                        "", r["Status"], "", r["Labels"], ""])

        # Epics are written first so a parent always appears above its children.
        for i, r in enumerate(tasks, start=len(epics) + 1):
            owner, description, labels = enrich(r)
            w.writerow([i, r["Summary"], r["Issue Type"], description,
                        epic_id[r["Epic Link"]], r["Status"], A.EMAIL[owner],
                        labels, int(r["Story Points"] or 0)])


def main():
    rows = master_rows()
    epics = [r for r in rows if r["Issue Type"] == "Epic"]
    tasks = [r for r in rows if r["Issue Type"] != "Epic"]

    for r in tasks:
        if r["Epic Link"] not in EPIC_TOKENS:
            sys.exit(f"Unknown epic link {r['Epic Link']!r} on {r['Summary']!r}")

    write_all_in_one(epics, tasks)

    # ---- epics ----
    with open(OUT / "1-epics.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["Summary", "Work type", "Description", "Status", "Labels"])
        for r in epics:
            # Summary is the full title; Epic Name is the short token the tasks
            # point at, which is why the two columns differ.
            w.writerow([r["Summary"], "Epic", r["Description"], r["Status"], r["Labels"]])

    # ---- tasks ----
    balance, counts = Counter(), Counter()
    with open(OUT / "2-tasks.csv", "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["Summary", "Work type", "Description", "Parent",
                    "Status", "Assignee", "Labels", "Story point estimate"])

        for r in tasks:
            owner, description, labels = enrich(r)
            points = int(r["Story Points"] or 0)

            w.writerow([r["Summary"], r["Issue Type"], description,
                        f"EPIC>{r['Epic Link']}", r["Status"], A.EMAIL[owner],
                        labels, points])

            balance[owner] += points
            counts[owner] += 1

    total = sum(balance.values())
    print(f"{len(epics)} epics, {len(tasks)} tasks written")
    for who in (A.RIDA, A.SABBAGH):
        print(f"  {who:<16} {counts[who]:>3} tasks  {balance[who]:>4} pts  "
              f"({balance[who] / total:.1%})")


if __name__ == "__main__":
    main()
