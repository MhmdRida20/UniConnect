# Final report — what is done, and what still needs adding

Working notes for `UniConnect_Final_Report.docx`. Tick things off here as you go.

Last reviewed: 15 August 2026.

---

## How the contents tables work — read this before editing

**Ctrl+A then F9 does nothing in this document.** The Table of Contents and the
Table of Figures are not Word fields; they are hand-typed tables. An earlier
version had a real TOC field, but it was flattened to plain text at some point,
leaving dead `_Toc…` hyperlinks behind. Those have been removed.

Both tables are now correct, and every row is an internal hyperlink:
**Ctrl+click a line and Word jumps to that page.** Links point at bookmarks
placed on each heading and figure caption.

The consequence is that **nothing updates itself**. If you add, remove or
reorder a section, or if anything reflows onto a different page, you must fix
both tables by hand:

1. Add a bookmark to the new heading (Insert → Links → Bookmark).
2. Add the row, then Insert → Link → Place in This Document → pick the bookmark.
3. Correct the page numbers in every row the change affected.

If you would rather have it maintain itself, replace the table with a real TOC
field (References → Table of Contents). It updates on F9 and is clickable by
default, but it will not keep the bordered look the two tables currently share.

---

## What is in this folder

| File | What it is |
| --- | --- |
| `figures/generate_dfds.py` | Redraws the three data-flow diagrams. Edit this, not the images. |
| `figures/figure2-context-dfd.png` / `.svg` | Figure 2 — Context DFD |
| `figures/figure3-level0-dfd.png` / `.svg` | Figure 3 — Level 0 DFD |
| `figures/figure4-attendance-dfd.png` / `.svg` | Figure 4 — Level 2 DFD, Smart Attendance |

All three are already embedded in the document at 6.30 in wide. Re-run
`python docs/figures/generate_dfds.py` after any edit, then re-insert.

Figure 1 (the entity model) was **not** redrawn.

---

## 1. Front matter to add

### 1a. Declaration of originality — check whether it is required

Most departments require a signed statement that the work is your own and that
all sources are cited. **Ask the supervisor whether USAL has required wording**
and use theirs verbatim rather than anything drafted here. This is the one piece
of missing front matter that can cost marks purely by being absent.

### 1b. Acknowledgements — ADDED, but needs your edit

Now in the document, on page 3, between the Abstract and the Table of Contents.
Two things still to fix:

- **Name the partner university** if you are permitted to; it currently says
  "our partner university". If you do name them here, reconsider why they are
  anonymous in the body of the report.
- **Add anyone specific who actually helped.** A generic acknowledgement reads
  as filler; a specific one does not. The draft thanks the supervisor, the
  faculty, the partner university team and your families — nothing beyond what
  could be written without knowing you.

### 1c. List of Abbreviations — ADDED

Now in the document, on pages 7–8, after the Table of Figures. The 25 entries
were taken from the abbreviations actually used in the text:

| Abbreviation | Expansion |
| --- | --- |
| API | Application Programming Interface |
| ASP.NET | Active Server Pages .NET |
| CV | Curriculum Vitae |
| DFD | Data Flow Diagram |
| DPIA | Data Protection Impact Assessment |
| EF Core | Entity Framework Core |
| GPS | Global Positioning System |
| HTTP | Hypertext Transfer Protocol |
| JSON | JavaScript Object Notation |
| JWT | JSON Web Token |
| LMS | Learning Management System |
| MAUI | Multi-platform App UI |
| MVC | Model–View–Controller |
| QR | Quick Response (code) |
| RFC | Request for Comments |
| RSVP | Répondez s'il vous plaît |
| SIS | Student Information System |
| SMS | Short Message Service |
| SQL | Structured Query Language |
| SQLite | Embedded SQL database engine |
| SignalR | ASP.NET Core real-time messaging library |
| TF-IDF | Term Frequency–Inverse Document Frequency |
| TOTP | Time-based One-Time Password |
| URI | Uniform Resource Identifier |
| UTC | Coordinated Universal Time |

### 1d. List of Tables

The report has three tables and a Table of Figures, but no List of Tables. One
Word field, matching the existing style.

---

## 2. Appendices — the ones with real marks attached

Section 3.2 cites **FR-01 through FR-92** and Section 3.3 says **twenty use
cases** were documented, but the report shows only five. Neither document exists
anywhere in this repository.

- **Appendix A — Functional requirements.** The full FR-01…FR-92 list.
- **Appendix B — Use cases.** All twenty, in the same format as the five shown.
- **Appendix C — Requirements traceability.** A table of FR → met / partial /
  not implemented, with the "not implemented" rows pointing at Section 9.

Appendix C is the single biggest improvement available. Right now the report
asserts 92 requirements and never reports how many were met.

**If those two documents no longer exist, say so in Section 1.5 rather than
citing numbers that cannot be produced.** An examiner may ask to see them.

---

## 3. Citations still missing

Section 2.1 discusses Canvas, Blackboard, WhatsApp, Discord, LinkedIn, Indeed
and "card-based attendance systems". The product sources are now cited [8]–[11],
but there are still **no academic sources** on the two topics the project makes
technical claims about:

- QR / proximity-based attendance verification
- Multi-tenant SaaS architecture (partially covered by [13])

Two or three genuine papers would close the last soft spot in the literature
review. None were invented for you — every reference currently in the
bibliography is real and verifiable.

---

## 4. Claims only you can confirm

These are in the report and cannot be checked from the code. Read each and
confirm it is true before submission.

- **Section 6** states that modules without mobile coverage "were exercised
  manually through the web portal". This was inferred. If no manual test pass
  happened, change the sentence.
- **The partner university claims** throughout — validation against their live
  production API, and that the two missing endpoints were "formally raised with
  them".
- **Sections 1.5 and 8** — the lost development account and two months of work.
  Confirm the supervisor is comfortable with that framing.
- **Section 7's personal-data list** — confirm nothing is missing, particularly
  anything the partner university's API returns that gets cached locally.
- **Sections 6 and 7 in full.** Roughly 900 words were added in a voice that is
  not yours. Read them and make them sound like you wrote them.

---

## 5. Verified figures — do not change these back

Every number below was re-derived from this repository on 15 August 2026. If you
edit the surrounding text, keep them.

| Claim | Value | Source |
| --- | --- | --- |
| Passing tests | **271** | `dotnet test` in `test/UniConnect.Tests` |
| Server application | **~16,900 lines** | hand-written C#, excl. migrations/designer/build output |
| MAUI client | **~5,800 lines** | same basis |
| Test project | **~6,000 lines** | same basis |
| Total hand-written C# | **~28,700 lines** | sum of the three |
| Controllers | **27** | classes deriving from `Controller` / `ControllerBase` |
| Controller actions | **172** | action-shaped public methods |
| Services | **21** | files in `Services/` |
| SignalR hubs | **6** | Attendance, Club, Notification, RideTracking, StudyGroup, Ticket |
| Background hosted services | **6** | `BackgroundService` / `IHostedService` implementations |
| EF Core migrations | **33** | excl. designer and snapshot files |
| Database tables | **37** | `DbSet<>` declarations in `ApplicationDbContext` |
| Catalogue services | **6** | constants in `ServiceCodes` |

The earlier draft understated every one of these.

---

## 6. Consistency check before submitting

- [ ] Ctrl+A, F9 to refresh the ToC and Table of Figures
- [ ] Confirm no blank pages (there were 22 pages and none blank at last check)
- [ ] **Figure parentheses are inconsistent.** Figure 3 had its bracketed detail
      removed; Figures 2 and 4 still carry theirs. Decide on one convention.
- [ ] **Figure 3 no longer names `IUniversityProvider`**, which is the interface
      the whole architectural argument rests on. It is still named in Sections
      4.2 and 5. Consider putting it back on a second line, without brackets.
- [ ] **Figure 3 no longer says how many service modules there are.** The count
      is still in the Abstract and Section 3.1.
- [ ] D-numbers are shared across figures: D1/D2 in Figures 2–3, D3/D4 in Figure
      4. If you revert the numbering in one figure, revert it in all three.
- [ ] Section 4.2's prose describes users reaching the service modules directly;
      the redrawn Figure 3 routes them through the Core Platform. Make the text
      and the figure agree, whichever way you choose.

---

## 7. Outside the report — still outstanding

### 7a. Revoke the Gmail app password (do this first)

`appsettings.json` is tracked and contains a live Gmail app password for the
SMTP account. It has been in the public history of
`https://github.com/MhmdRida20/UniConnect.git` since commit `64234bb`.

**Rotating it is not enough — it must be revoked**, at
<https://myaccount.google.com/apppasswords>. Anyone who has ever cloned the
repository still has the old value, and rewriting history does not change that.

Afterwards, move mail settings to user secrets or environment variables so the
replacement never enters the repository.

### 7b. Git history still carries files that should not be in it

Both are already ignored going forward, but remain in history:

- `UniConnect.zip` — 87.7 MB, which is most of the ~211 MB `.git` directory
- `wwwroot/uploads/` — eight CVs, five ticket attachments, one profile photo:
  other people's personal documents, on a public repository

Removing them needs `git filter-repo` (or BFG) and a force push, after which
your teammate must re-clone. Worth doing in one pass while only two people are
affected. Section 7 of the report says a real deployment should treat data
protection as a prerequisite — this is the same point applied to the repository.

### 7c. Junk tenants

Two test universities, `TEST` and `58TEST`, have `ApiBaseUrl = 'test'`. Clean
them up before any demo that shows the admin portal.
