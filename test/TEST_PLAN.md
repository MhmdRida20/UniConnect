# UniConnect — Test Strategy & Codebase Study

**Status:** Phases 1–2 built and passing (195 tests). Phases 3–4 not started.
**Date:** 2026-08-03
**Target:** `test/` (this folder)

> **Running them**
> ```
> dotnet test test/UniConnect.Tests          # everything, ~26s
> dotnet test test/UniConnect.Tests --filter "FullyQualifiedName~Unit"
> dotnet test test/UniConnect.Tests --filter "FullyQualifiedName~Rules"
> ```
> The four tests under `Concurrency/` need SQL Server LocalDB; they skip
> themselves with a reason on a machine that doesn't have it, rather than
> failing the run.

## What was built

| Area | File | Tests |
|---|---|---|
| TF-IDF engine | `Unit/TextSimilarityTests.cs` | 22 |
| Matching score (FR-41) | `Unit/MatchingScoreServiceTests.cs` | 10 |
| Instructor dashboard aggregation | `Unit/AttendanceSummaryServiceTests.cs` | 18 |
| Service catalog | `Unit/ServiceCatalogServiceTests.cs` | 6 |
| Form validation rules | `Unit/ViewModelValidationTests.cs` | 22 |
| Attendance submission (FR-21/23) | `Rules/AttendanceSubmissionTests.cs` | 23 |
| Internship apply/withdraw (FR-42) | `Rules/InternshipApplicationTests.cs` | 21 |
| Study group create/join/approve | `Rules/StudyGroupMembershipTests.cs` | 19 |
| Ride visibility + rate limiting | `Rules/RideVisibilityTests.cs` | 17 |
| Ticket workflow + department scoping | `Rules/TicketWorkflowTests.cs` | 18 |
| Club officer departure | `Rules/ClubOfficerDepartureTests.cs` | 10 |
| Per-university service gating | `Rules/RequireServiceFilterTests.cs` | 5 |
| Simultaneous-action edge cases | `Concurrency/SimultaneousActionTests.cs` | 4 (LocalDB) |

**Production changes made:** one, and it's a build-file change rather than
behaviour — `UniConnect.csproj` now excludes `test\**` from its compile globs.
The web project sits at the repo root, so its default `**/*.cs` glob was
pulling the test project's sources (and its generated `AssemblyInfo`) into the
web assembly, which broke the build outright.

**Deviations from the plan below:** none of substance. Two things the plan
predicted turned out slightly differently and are noted in place — see
§2.2④ on which guard actually catches the study-group race, and §9 for what
the exercise turned up.

---

## 0. Summary

UniConnect has **no tests today** and no CI (`.github/workflows/` exists but is empty). The
codebase is ~13,800 lines across 141 controller actions, 16 services, 6 hosted services and
5 customised Identity pages.

The good news from the survey: the architecture is unusually test-friendly for a project
this size. The academic-data boundary is a single narrow interface (`IUniversityProvider`,
8 methods), the service catalog and geocoder are interfaces too, and the two most
complex background jobs already have their logic split into separately-registered
"Runner" classes that can be called directly.

The bad news is concentrated in four places, all documented in §2.2. None of them are
fatal; two of them need a decision from you before I write anything.

**My recommendation:** one xUnit project, SQLite-backed, built in four phases, starting
with the pure-logic and business-rule tests that need zero production changes.

---

## 1. Inventory — what exists

### 1.1 Application layout

| Area | Files | Notes |
|---|---|---|
| `Controllers/` | 20 (+1 external API) | 141 public actions total |
| `Services/` | 16 | 6 are `BackgroundService`, 2 are extractable "Runner" classes |
| `Models/` | 27 | EF entities + enums |
| `ViewModels/` | 17 | Includes validation attributes worth testing |
| `Adapters/` | 3 | `IUniversityProvider` + one HTTP implementation + resolver |
| `Filters/` | 1 | `RequireServiceAttribute` — per-university service gating |
| `Middleware/` | 3 | Suspension, session anomaly, audit-on-authz-failure |
| `Hubs/` | 6 | Group management only; no business logic (per design) |
| `ExternalApi/` | 1 | Simulated registrar dataset store |
| `Areas/Identity/` | 5 | Login + 3 registration flows (custom University-ID verification) |
| `Data/` | 2 + migrations | `ApplicationDbContext` (503 lines), `DbSeeder` (517 lines) |

### 1.2 Largest / most rule-dense files

```
748  Controllers/ClubsController.cs            16 actions
682  Controllers/RidesController.cs            15 actions
623  Controllers/StudyGroupsController.cs      12 actions
525  Controllers/AdminUniversitiesController.cs 11 actions
517  Data/DbSeeder.cs
503  Controllers/InstructorAttendanceController.cs  12 actions
490  Controllers/CompanyController.cs          11 actions
438  ExternalApi/ExternalUniversityDataStore.cs
411  Controllers/AdminReportsController.cs      8 report types
249  Services/AttendanceSummaryService.cs
232  Services/UniversityApiSyncRunner.cs
228  Services/MatchingScoreService.cs
135  Services/TextSimilarity.cs
```

### 1.3 Toolchain available

- SDKs installed: **8.0.303** and 9.0.315. Runtimes include 8.0.28. Project targets `net8.0`.
- `dotnet new xunit` template is available.
- `Microsoft.EntityFrameworkCore.Sqlite` **is already a package reference** in the main
  project (currently unused) — so a SQLite test database adds no new dependency surface.

---

## 2. Testability study

### 2.1 Seams that already work

These need no production change at all:

| Seam | Why it works |
|---|---|
| `IUniversityProvider` | 8 read-only methods returning plain records. A hand-written fake is ~40 lines and makes every enrollment/roster/course scenario trivially controllable. This is the single highest-leverage seam in the codebase. |
| `IUniversityProviderResolver` | One method. Wrap the fake above. |
| `IServiceCatalogService` | Two methods. Lets `RequireServiceAttribute` and nav-gating be tested directly. |
| `IGeocodingService` | Interface, so ride creation can be tested without hitting Nominatim. |
| `RealApiUniversityProvider` | Uses the named `HttpClient` `"UniversityApi"` via `IHttpClientFactory` → a stub `HttpMessageHandler` can simulate 200 / 404 / 503 / timeout without a network. |
| `UniversityApiSyncRunner`, `EnrollmentRevalidationRunner` | Registered as scoped services *separately* from their `BackgroundService` wrappers. Call them directly; no timers involved. |
| `TextSimilarity` | Pure static functions, zero dependencies. |
| `AttendanceSummaryService` | Constructor-injected, all deps replaceable. Public methods return a plain VM. |
| `ApplicationDbContext` | No SQL-Server-specific column types, computed columns, or default-value SQL in `OnModelCreating`. Maps cleanly onto SQLite. |

### 2.2 Obstacles — the four real ones

**① `Program.cs` is not reachable by `WebApplicationFactory`.**

Top-level statements generate an *internal* `Program` class, so `WebApplicationFactory<Program>`
won't compile. Standard fix is one line at the end of `Program.cs`:

```csharp
public partial class Program { }
```

Separately, `Program.cs` lines 159–168 run `db.Database.MigrateAsync()` and the full
`DbSeeder` at startup — and the seeder calls the external API over HTTP. Any integration
test would hit LocalDB and make live HTTP calls unless the factory overrides both.
**This affects Phase 3 only**; Phases 1–2 don't touch it.

**② No clock abstraction — `DateTime.Now` / `DateTime.UtcNow` are called inline.**

13 `DateTime.Now` and 59 `DateTime.UtcNow` call sites, and three files mix both
(`ClubsController`, `InstructorAttendanceController`, `RidesController`). Every
time-sensitive rule is affected: the Present-vs-Late grace boundary, QR expiry, the
session window, ticket staleness, club/group inactivity.

Tests *can* work around this by constructing entities relative to `DateTime.Now`
(e.g. "a session that started 3 minutes ago"), and that's what I'd do initially — it
needs no production change. But it makes boundary tests slightly fuzzy and it can't
test "what happens at exactly the grace-period edge".

.NET 8 ships `TimeProvider` for precisely this. Adopting it would be a clean, mechanical
refactor — but it touches production code across ~15 files, so **it is your call**, not mine.
See §8, Decision B.

> Worth flagging while I'm here: `CloseExpiredAttendanceSessionsService.cs:67` compares
> `DateTime.UtcNow` against `session.EndTime`, but `EndTime` is stored as **local** time
> (`AttendanceController.cs:199` documents this deliberately). On a machine offset from
> UTC these disagree by the offset, so sessions close early or late. I have **not**
> changed it — but it is exactly the kind of thing this suite should pin down, and it's
> the first test I'd write in Phase 2.

**③ The densest business rules live in `private` controller methods.**

The clearest case is `AttendanceController.TrySubmitAttendanceAsync` (lines 128–232) —
that one method contains the *entire* FR-21/FR-23 rule set: enrollment check, time
window, token expiry, duplicate submission, GPS radius, Present-vs-Late, and
same-device-different-student flagging. `HaversineDistanceMeters` is likewise private static.

Three options:
- **(a)** Test through the public `Submit` action. Needs a real `UserManager`, a stubbed
  `IHubContext`, and a `ControllerContext` with `TempData`. Verbose but *honest* — it
  tests the actual entry point students use, and needs **no production change**.
- **(b)** Extract to a service. Cleaner tests, but it's a refactor of working code.
- **(c)** `[InternalsVisibleTo]` + make the method internal. Middle ground, mild smell.

**I recommend (a)** — build the ceremony once in a shared test fixture and it costs
nothing per test after that.

**④ SQLite cannot exercise optimistic concurrency.**

`Ride.RowVersion` and `StudyGroup.RowVersion` are `[Timestamp] byte[]`. SQL Server
generates these server-side; SQLite has no equivalent, so the column is created but never
populated and the concurrency check never fires.

This means two documented edge cases **cannot be tested on SQLite**:
- "Double seat reservation — two students request the last seat simultaneously"
- "Two near-simultaneous study-group approvals both squeeze past the capacity check"

Those two need a real LocalDB database. See §8, Decision C.

### 2.3 Other friction (manageable)

- **`UserManager<ApplicationUser>` is a concrete class.** Mocking it is notoriously ugly.
  Better: construct a *real* `UserManager` over the SQLite context in a fixture helper.
  Roughly 20 lines, written once.
- **`AuditLogService` and `NotificationService` are concrete classes**, not interfaces.
  Both are constructible with a test `DbContext` + stub `IHubContext`, so this is fine —
  and it lets tests assert on the audit rows and notifications that were actually written,
  which is arguably better than asserting on a mock.
- **Migrations are SQL-Server-specific.** Tests must use `EnsureCreated()`, never `Migrate()`.
- **`RequireConfirmedAccount = true`.** Seeded accounts set `EmailConfirmed = true`, so
  login works in tests; newly-created test users must set it explicitly.
- **4 of 6 hosted services keep their logic in a private method inside the `BackgroundService`**
  (`CloseExpiredAttendanceSessions`, `InactiveStudyGroup`, `InactiveClub`, `TicketStaleness`).
  Testable by building a small `ServiceProvider` and invoking the class, but clumsier than
  the two Runner-based ones. Same three options as ③.

---

## 3. Proposed structure

### 3.1 One project or two?

**Recommendation: one project**, `test/UniConnect.Tests`, with folders and xUnit traits.

A split into unit/integration projects is the textbook answer, but it doubles the
configuration for a suite this size and the fast tests (Phases 1–2) are the ones you'll
run constantly. If Phase 3 grows slow enough to be annoying, splitting it out later is a
30-minute job. Meanwhile `dotnet test --filter Category!=Integration` gives the same
benefit today.

```
test/
├── TEST_PLAN.md                     ← this file
├── UniConnect.Tests/
│   ├── UniConnect.Tests.csproj
│   ├── Infrastructure/
│   │   ├── SqliteDbFixture.cs        # in-memory SQLite + EnsureCreated
│   │   ├── TestDataBuilder.cs        # fluent seed: university, users, courses…
│   │   ├── FakeUniversityProvider.cs # the key seam
│   │   ├── FakeServiceCatalog.cs
│   │   ├── StubHubContext.cs
│   │   ├── StubHttpMessageHandler.cs
│   │   └── ControllerTestHelpers.cs  # UserManager, ControllerContext, TempData
│   ├── Unit/
│   │   ├── TextSimilarityTests.cs
│   │   ├── MatchingScoreServiceTests.cs
│   │   ├── AttendanceSummaryServiceTests.cs
│   │   ├── ServiceCatalogServiceTests.cs
│   │   ├── ViewModelValidationTests.cs
│   │   └── RealApiUniversityProviderTests.cs
│   ├── Rules/                        # controller-level business rules, real DB
│   │   ├── AttendanceSubmissionTests.cs
│   │   ├── StudyGroupMembershipTests.cs
│   │   ├── RideSeatTests.cs
│   │   ├── ClubOfficerDepartureTests.cs
│   │   ├── InternshipApplicationTests.cs
│   │   ├── TicketWorkflowTests.cs
│   │   └── CrossUniversityIsolationTests.cs
│   ├── Jobs/
│   │   ├── EnrollmentRevalidationTests.cs
│   │   ├── UniversityApiSyncTests.cs
│   │   └── AttendanceSessionCloseTests.cs
│   └── Integration/                  # [Trait("Category","Integration")]
│       ├── UniConnectWebFactory.cs
│       ├── AuthorizationMatrixTests.cs
│       ├── ExternalApiEndpointTests.cs
│       └── SmokeRouteTests.cs
```

Also proposed: `UniConnect.sln` gains the test project, and `.github/workflows/ci.yml`
gets a `dotnet build && dotnet test` job (the workflows folder is already there and empty).

### 3.2 Tooling

| Choice | Why |
|---|---|
| **xUnit** | Template installed; the de-facto standard for ASP.NET Core; best async story. |
| **EF Core SQLite (in-memory)** | Already referenced. Crucially, it is a *relational* provider — it enforces the unique indexes this codebase leans on for correctness (`AttendanceRecord(SessionId,UserId)`, `InternshipApplication(InternshipId,UserId)`, `ClubMember(ClubId,UserId)`, `Enrollment(UniversityId,CourseCode)`). The `InMemory` provider enforces none of those and would give false passes. |
| **Hand-written fakes** | For `IUniversityProvider` and friends. These interfaces are small, and a real fake reads better in a test than mock setup chains. |
| **`Microsoft.AspNetCore.Mvc.Testing`** | Phase 3 only, for `WebApplicationFactory`. |
| **No mocking library initially** | Add NSubstitute later only if a specific test genuinely needs it. (Avoiding Moq — its SponsorLink episode makes it a poor dependency to hand a marker.) |
| **Plain `Assert`** | Skipping FluentAssertions: v8 moved to a paid licence for commercial use, and that's a needless complication for an FYP. |

---

## 4. What to test — prioritised

Ranked by *risk × how hard it would be to notice the bug manually*.

### Tier 1 — pure logic, zero setup (highest value per line)

| Target | What to pin down |
|---|---|
| `TextSimilarity` | Tokenizer keeps `C#`, `.NET`, `Node.js` intact; word tokenizer drops 1-char tokens; smoothed IDF stays positive on a 1-document corpus; cosine returns 0 on empty input rather than dividing by zero; result always within [0,1]. |
| `MatchingScoreService` | The five weights sum to 100. Empty required-skills ⇒ full credit, not zero. Missing student major ⇒ neutral. **Adapter failure ⇒ course weight redistributes and still sums to 100** (the FR-41 edge case). Identical skill sets ⇒ 100. Disjoint ⇒ low. Score clamped to 0–100. |
| `AttendanceSummaryService` | Active sessions excluded from the denominator. Cancelled excluded. Excused leaves the denominator. Unregistered roster students get `NotRegistered` and are excluded from `OverallRate`. Standing thresholds at exactly 85 and 75. Ordering: worst-first, unregistered last. **`BuildCourseSummaryAsync` returns null for a course the instructor doesn't teach** — this one is load-bearing security, per the method's own comment. |
| `ServiceCatalogService` | A service that is enabled but `IsImplemented = false` is *not* reported enabled. |
| ViewModels | `[Required]`, ranges (`MinMembers`/`MaxMembers` 2–50), and the `[DisplayFormat]` round-trip on the four datetime VMs. |

### Tier 2 — business rules against a real (SQLite) database

| Target | What to pin down |
|---|---|
| **Attendance submission** (FR-21/23) | Invalid token; inactive session; before start; after `QrExpiresAt`; not enrolled; duplicate submission; missing GPS; outside radius; inside radius ⇒ Present; after grace ⇒ Late; same device + different student ⇒ `IsSuspicious` **but still recorded**; audit rows written for both submission and suspicion. |
| **Cross-university isolation** | The documented guard on `InternshipsController.Details`, plus the same question asked of rides, clubs, groups, tickets and attendance. A student from university A must not reach university B's data by direct URL. This is the single most valuable *security* test group here. |
| **`RequireServiceAttribute`** | Disabled service ⇒ redirect to `Home/NotAvailable`; unauthenticated ⇒ challenge; enabled ⇒ passes through. |
| **Study groups** | Creator auto-approved; `MaxMembers` respected; unique membership; join ⇒ Pending; leadership transfer; leave. |
| **Clubs — officer departure** | President leaves ⇒ VP promoted; no VP ⇒ longest-standing approved member; nobody left ⇒ club archived. Three distinct branches, easy to get wrong, invisible until it happens. |
| **Rides** | Only active / seats-remaining / not-departed / same-university / other-people's rides are listed. Accept reserves a seat. Cancellation. |
| **Internship applications** | Duplicate application blocked (unique index). Withdraw. Deactivation notifies applicants. `ListingOnly` vs `FullApplication` paths. |
| **Tickets** | Status transitions recorded with `PreviousStatus`/`NewStatus`; staff scoped to their own department; reassign; reject with reason. |
| **`AdminUsers` role change** | Regression test for the bug fixed earlier this session — assign role, then assert both the DB *and* the re-rendered dropdown reflect it. |
| **Suspension middleware** | Suspended user is signed out on next request. |

### Tier 3 — jobs and adapters

| Target | What to pin down |
|---|---|
| `EnrollmentRevalidationRunner` | Student drops a course ⇒ removed/flagged from the group + notified. |
| `UniversityApiSyncRunner` | Successful sync mirrors data; 503 leaves existing data intact rather than wiping it; failure is logged, not thrown. |
| `CloseExpiredAttendanceSessions` | Writes exactly one `Absent` per enrolled non-submitter; doesn't duplicate on a second pass; **and the UTC/local comparison noted in §2.2②**. |
| `RealApiUniversityProvider` | Correct `X-Api-Key` header sent; 404 ⇒ `null` not an exception; 503 ⇒ surfaces so `MatchingScoreService` can degrade; malformed JSON handled. |
| `TicketStalenessService` / inactivity jobs | Threshold boundaries; idempotency across repeated runs. |

### Tier 4 — integration (`WebApplicationFactory`)

| Target | What to pin down |
|---|---|
| **Authorization matrix** | Every `[Authorize(Roles=…)]` controller × every role × anonymous. ~20 controllers × 6 identities, driven from one `[Theory]` table. Catches a missing or over-broad attribute instantly, and it's the kind of thing that silently regresses. |
| **External API endpoints** | Missing `X-Api-Key` ⇒ 401; wrong key ⇒ 401; valid ⇒ 200; unknown student ⇒ 404. Note `SimulatedFailureRatePercent` must be pinned to 0 or this flakes. |
| **Smoke routes** | Every GET action returns 200/302 rather than 500 — cheap protection against Razor-time errors that compile fine but throw on render. |

---

## 5. Explicitly out of scope

- **Razor view markup / CSS.** Already covered by the browser-driven checks used through
  this session's UI work. Unit-testing markup is low value and high churn.
- **SignalR hubs.** By design they only manage group membership; the logic they'd be
  guarding lives in controllers, which Tier 2 covers.
- **`DbSeeder`.** Idempotency is worth one integration test; asserting the full seed
  contents would just restate the seeder.
- **Client-side JS** (`uc-select`, `uc-datepicker`, `uc-inputs`). Worth testing eventually
  — a Vitest/jsdom setup would do it — but that's a separate toolchain and a separate
  decision. Flagged, not included.
- **Real SMTP and real Nominatim.** Stubbed at the interface.

---

## 6. Production changes required

Kept deliberately minimal and listed so nothing is smuggled in:

| Phase | Change | Size |
|---|---|---|
| 1–2 | *(none)* | — |
| 3 | `public partial class Program { }` at the end of `Program.cs` | 1 line |
| 3 | Guard the startup migrate/seed block so a test factory can skip it (e.g. an environment check) | ~3 lines |
| Optional | `TimeProvider` adoption — Decision B | ~15 files |
| Optional | Extract `TrySubmitAttendanceAsync` to a service — Decision D | 1 file moved |

---

## 7. Phasing

| Phase | Contents | Est. tests | Production changes |
|---|---|---|---|
| **1** | Project scaffold, `Infrastructure/` fixtures, all Tier 1 | ~55 | none |
| **2** | Tier 2 business rules + cross-university isolation | ~70 | none |
| **3** | `WebApplicationFactory`, authorization matrix, external API, smoke routes | ~45 | 2 small (§6) |
| **4** | Tier 3 jobs and adapters, plus CI workflow | ~30 | none |

Phase 1 is the one that proves the fixtures work. I'd want you to look at it before I
build on top of it.

---

## 8. Decisions I need from you

**A. Scope of the first pass.** Everything above is ~200 tests. Reasonable alternatives:
Phase 1 only (fast, proves the approach), Phases 1–2 (the real value — all business
rules), or all four.

**B. `TimeProvider` refactor?** Without it, time-boundary tests are approximate.
With it, they're exact — at the cost of touching ~15 production files. My lean: **skip it
for now**, write the approximate tests, revisit if they prove flaky.

**C. LocalDB for the concurrency tests?** The two "simultaneous" edge cases (§2.2④)
can't run on SQLite. Options: (i) skip them and document the gap, (ii) add a small
LocalDB-backed test collection just for those two, (iii) run the *whole* suite on
LocalDB — much slower and needs a live SQL Server on any CI machine. My lean: **(ii)**.

**D. Refactor `TrySubmitAttendanceAsync` out of the controller?** My lean: **no** —
test through the public action instead. Mentioned only because it would make those
particular tests noticeably shorter.

**E. CI workflow?** `.github/workflows/` is empty. Want a `dotnet build && dotnet test`
job on push, or leave CI alone for now?

---

## 9. What building Phases 1–2 turned up

Three things that weren't visible from reading the code:

**① The study-group race is caught by the capacity check, not the rowversion.**
`ApproveMember` re-counts approved members with a *fresh* database query, so by
the time a second approval runs, the first has already committed and the plain
check refuses it. The `[Timestamp]` token only comes into play for a tighter
interleaving where the count itself reads stale data. Both outcomes are
correct — the group never exceeds its capacity — so the test asserts the
outcome and accepts either message, with the token proved separately at the
DbContext level. The ride race *does* go through the rowversion, because
`AcceptRequest` reads seat count from an entity it loaded earlier.

**② StudyGroup has a hard FK to the local `Courses` table.** The architecture
notes say academic data is read only through `IUniversityProvider`, and that's
true for *reads* — but a study group can't be created for a course until the
sync job has mirrored it locally. SQLite's foreign-key enforcement surfaced
this immediately; EF's `InMemory` provider would have silently allowed it and
the tests would have been lying. Not a bug, but a real coupling worth knowing
about.

**③ Interests and location are not neutral when missing.** The matching service
documents "missing data is neutral, never a penalty", and that holds for
skills, courses and major — but a student with no career profile scores 0 on
both interests and location, costing 20 points off an otherwise perfect match.
That appears deliberate (it's what drives the "improve your profile" prompt),
so it's pinned down as a test rather than reported as a defect.

Still outstanding from §2.2②: the UTC/local mismatch in
`CloseExpiredAttendanceSessionsService.cs:67`. Phase 4 territory — the job's
logic is private inside the `BackgroundService`, so testing it needs the
decision in §2.2③ applied there too.

---

## Appendix — reference facts gathered

- Roles: `Student`, `Instructor`, `DepartmentStaff`, `Company`, `Admin`
  (`UniversityAdmin` appears in `[Authorize]` strings but is listed as not-yet-built in
  `PROJECT_OVERVIEW.md` §18 — worth a test that documents which it is).
- Service codes: `StudyGroups`, `RideSharing`, `Attendance`, `Tickets`, `Internships`, `Clubs`.
- Seeded logins (from `PROJECT_OVERVIEW.md` §5) — usable directly by integration tests:
  `admin@uniconnect.local` / `Admin@12345`, `instructor.habib@uni.edu` / `Instructor@12345`,
  `it.staff@uni.edu` / `Staff@12345`, `careers@uniconnectdemo.edu` / `Career@12345`.
  Valid student IDs `U2024001`–`U2024008`.
- Matching weights: Skills 35, Courses 25, Major 20, Interests 10, Location 10.
- Attendance standings: Good ≥ 85, Watch ≥ 75, else At-Risk.
- Unique indexes that tests can rely on for negative cases: `ApplicationUser.UniversityId`,
  `AttendanceSession.QrToken`, `AttendanceRecord(SessionId,UserId)`,
  `InternshipApplication(InternshipId,UserId)`, `ClubMember(ClubId,UserId)`,
  `EventRsvp(ClubEventId,UserId)`, `StudyGroupMember(GroupId,UserId)`,
  `Enrollment(UniversityId,CourseCode)`, `UniversityService(UniversityCode,ServiceCode)`,
  `Company.UniversityCode`, `CareerProfile.UserId`.
