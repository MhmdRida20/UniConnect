# Getting UniConnect Back to Green

> Written 10 Aug 2026. Purpose: take the repo from its current partially-reverted state to a
> verified, committed baseline — **before** any mobile work starts.
>
> Every step has a command that proves it worked. "Functional without errors" is defined as the
> seven gates in §1, not as a feeling.

---

## 1. Definition — what "no errors" actually means

Seven gates. The project is green when all seven pass.

| # | Gate | Command | Now |
|---|---|---|---|
| 1 | Web project compiles | `dotnet build UniConnect.csproj --nologo` | ✅ 0/0 |
| 2 | Test project compiles | `dotnet build test/UniConnect.Tests/UniConnect.Tests.csproj --nologo` | ❌ 7 errors |
| 3 | Solution compiles | `dotnet build UniConnect.sln --nologo` | ❌ missing project |
| 4 | All tests pass | `dotnet test test/UniConnect.Tests/UniConnect.Tests.csproj` | ❌ can't run |
| 5 | App starts and serves | launch, `GET /` → 200, no unhandled exception | ✅ |
| 6 | The 4 fixed bugs are actually fixed | §6 runtime checks | ❌ reverted |
| 7 | Everything committed | `git status --short` is empty | ❌ 8 untracked |

**Gate 1 already passes — the website is not broken.** It builds clean and runs. What is missing
is seven fixes, a solution reference, and the ability to run the test suite.

---

## 2. Phase 0 — protect unversioned work (do this first)

A revert already destroyed work once today. Eight files are untracked, meaning git is not
protecting them. Unlike the reverted code, **the pre-compaction test files cannot be reconstructed
from the conversation.**

```bash
cd c:/Users/Mohamed/source/repos/UniConnect
git add MOBILE_APP_PLAN.md MOBILE_STUDYGROUPS_PLAN.md RECOVERY_PLAN.md test/
git commit -m "Add mobile plans, recovery plan, and regression tests"
```

Verify:
```bash
git status --short        # should list nothing untracked under test/
```

> Do this **before** Visual Studio touches the folder again. It takes ten seconds and removes the
> only irreversible risk on the table.

---

## 3. Phase 1 — restore the seven reverted changes

All seven are in files that still exist and still compile. The files are fine; the fixes inside
them were discarded.

### 3.1 `Services/CloseExpiredAttendanceSessionsService.cs` — split out a testable method

The `DateTime.Now` timezone fix **survived** (it is in commit `88bc72a`). What was lost is the
public overload the tests call.

Change the private method into a thin wrapper plus a public overload:

```csharp
private async Task CloseExpiredSessionsAsync(CancellationToken ct)
{
    using var scope = _services.CreateScope();
    await CloseExpiredSessionsAsync(
        scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
        scope.ServiceProvider.GetRequiredService<IHubContext<AttendanceHub>>(),
        ct);
}

public async Task CloseExpiredSessionsAsync(
    ApplicationDbContext db, IHubContext<AttendanceHub> hub, CancellationToken ct = default)
{
    // ... existing body, unchanged ...
}
```

*Why:* the timing rules stay silently wrong for a long time when untested, and reaching them
through `ExecuteAsync` means waiting on the loop's 10-second startup delay.

### 3.2 `Controllers/AdminReportsController.cs` — the Arabic CSV fix

In `Export`, replace the plain byte conversion:

```csharp
// Excel reads a BOM-less UTF-8 CSV as the system codepage, which mangles any
// non-ASCII text — Arabic addresses/names in these reports included.
var bytes = Encoding.UTF8.GetPreamble()
    .Concat(Encoding.UTF8.GetBytes(sb.ToString()))
    .ToArray();
```

*Highest-priority restore* — user-reported, user-visible, and verified working earlier today.

### 3.3 `Services/UniversityApiSyncRunner.cs` — the `UriFormatException` crash

Replace the bare `new Uri(...)` with a guarded parse that records a sync failure instead of
throwing. Every other failure mode in this method is caught and recorded; this one escaped because
the constructor threw *before* the `try` block was entered, taking the whole request down.

```csharp
if (!Uri.TryCreate(university.ApiBaseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
    || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
{
    university.LastSyncStatus = "Failed";
    university.LastSyncError = $"'{university.ApiBaseUrl}' isn't a valid http(s) address.";
    university.LastSyncAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return;
}
var client = _httpClientFactory.CreateClient("UniversityApi");
client.BaseAddress = baseAddress;
```

### 3.4 `Controllers/AdminUniversitiesController.cs` — validate the URL on the form

In `Create`, after the empty check:

```csharp
else if (!Uri.TryCreate(vm.ApiBaseUrl.Trim(), UriKind.Absolute, out var parsed)
         || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
    ModelState.AddModelError(nameof(vm.ApiBaseUrl),
        "Enter the full address including https://, e.g. https://registrar.uni.edu/api/v1.");
```

*Why both 3.3 and 3.4:* `Create` saves the University row **before** it provisions the accounts and
service catalog. Anything that throws in between leaves an institution that exists, cannot be
used, and blocks its own code from being reused.

### 3.5 `Controllers/InstructorAttendanceController.cs` — split the pending count

```csharp
ViewBag.PendingCount = rows.Count(r => r.UserId is not null && r.Record is null);
ViewBag.NotRegisteredCount = rows.Count(r => r.UserId is null);
```

### 3.6 `Views/InstructorAttendance/Details.cshtml` — surface it

A conditional "No account" tile in the counts bar, and in the roster row:

```razor
@if (row.UserId is null)      { <span class="uc-pill uc-pill-grey">No account</span> }
else if (row.Record is null)  { <span class="uc-pill uc-pill-grey">Pending</span> }
```

### 3.7 `wwwroot/css/pages/attendance.css` — let the counts bar flex

```css
grid-template-columns: repeat(auto-fit, minmax(92px, 1fr));
```

The old fixed `repeat(5, 1fr)` breaks when the sixth tile appears, and was cramped on a phone
regardless.

**Checkpoint:** `dotnet build UniConnect.csproj --nologo` → **Gate 1** still 0/0.

---

## 4. Phase 2 — fix the test project

### 4.1 `UniversityApiSyncRunnerTests.cs` — a genuine test bug

This one is **not** caused by the revert. The test constructs:

```csharp
new(_test.Db, _http, NullLogger<UniversityApiSyncRunner>.Instance)   // 3 args
```

but the constructor takes four, and `providerResolver` is genuinely used (line 286 calls
`_providerResolver.GetProviderAsync`). So the production code is right and the test is wrong.

Add a fake resolver to `test/UniConnect.Tests/Infrastructure/`, then pass it:

```csharp
new(_test.Db, _http, new FakeProviderResolver(), NullLogger<UniversityApiSyncRunner>.Instance)
```

> Worth noting: I previously described this as revert damage. It isn't — it is a test that was
> written against the wrong signature.

### 4.2 `CloseExpiredAttendanceSessionsTests.cs` (6 errors)

Fixed automatically by §3.1. No test change needed.

### 4.3 `InstructorRosterTests.cs`

Reads `ViewBag.NotRegisteredCount`. Fixed automatically by §3.5.

**Checkpoint — Gate 2:**
```bash
dotnet build test/UniConnect.Tests/UniConnect.Tests.csproj --nologo
```

---

## 5. Phase 3 — fix the solution reference

`UniConnect.sln` references a project that does not exist:

```
C:\Users\Mohamed\source\repos\UniConnect.Mobile\UniConnect.Mobile.csproj
```

Note the path is a **sibling of the repo, outside it** — a project there is not version-controlled
with the rest of your work.

Pick one:

| Option | When | Action |
|---|---|---|
| **A — create it there** | You are mid-MAUI-setup and that path is intended | Finish creating the project at that exact path. Resolves itself. |
| **B — move it inside** *(recommended)* | You want it in the repo, next to `test/` | Create at `UniConnect/mobile/UniConnect.Mobile/`, then fix the `.sln` path |
| **C — remove for now** | You want a green build today | `dotnet sln UniConnect.sln remove ../UniConnect.Mobile/UniConnect.Mobile.csproj` and re-add later |

I would take **B**: the mobile app is part of this project's deliverable, and a sibling folder
will not be in your submission, your backups, or your git history.

**Checkpoint — Gate 3:**
```bash
dotnet build UniConnect.sln --nologo
```

---

## 6. Phase 4 — verify behaviour, not just compilation

A green build proves nothing about the four bugs. Run the app and check each.

**Gate 4 — the suite:**
```bash
dotnet test test/UniConnect.Tests/UniConnect.Tests.csproj --nologo
```
Expect **214+ passing, 0 failed**. The count rises with the restored tests.

**Gate 5 — the app starts:**
```bash
dotnet run --project UniConnect.csproj
```
`GET /` → 200, no unhandled exception in the console.

**Gate 6 — the four bugs, checked in a running app:**

| Bug | How to verify | Pass |
|---|---|---|
| Arabic CSV | Log in as admin → `/AdminReports/Export?type=Rides` → open in Excel | Arabic renders; first 3 bytes are `EF BB BF` |
| URI crash | `/AdminUniversities/Create`, enter `uni.edu/api` (no scheme) | Red field error — **not** a 500 page |
| "N PENDING" | Open a **closed** session's Details with an unregistered roster student | Shows "No account", not "Pending" |
| Session close time | Confirm `CloseExpiredAttendanceSessionsService` uses `DateTime.Now` | `grep -n "var now = DateTime" Services/CloseExpiredAttendanceSessionsService.cs` |

Byte check for the CSV:
```powershell
$b = [System.IO.File]::ReadAllBytes("path\to\downloaded.csv")
[BitConverter]::ToString($b[0..2])     # expect EF-BB-BF
```

---

## 7. Phase 5 — commit the baseline

```bash
git add -A
git commit -m "Restore CSV BOM, URI validation, roster counts; fix sync runner test"
```

**Gate 7:** `git status --short` is empty.

This is the known-good point to branch from for mobile work. Tag it if you like:
```bash
git tag pre-mobile-baseline
```

---

## 8. Still open after all this

These are **not** build errors and none block the mobile work. They are the honest remaining list.

| # | Item | Severity | Note |
|---|---|---|---|
| 1 | **Gmail app password in `appsettings.json` and git history** | Critical | Live until revoked at myaccount.google.com/apppasswords. Rotating does not remove it from history |
| 2 | GPS accuracy never captured or validated | Medium | `attendance-submit.js:46` sends only lat/lng. Real defect — see [MOBILE_APP_PLAN.md](MOBILE_APP_PLAN.md) §8 |
| 3 | Study-group FK to local `Courses` returns 500 | Medium | See [MOBILE_STUDYGROUPS_PLAN.md](MOBILE_STUDYGROUPS_PLAN.md) §3.2 |
| 4 | Chat broadcasts a pre-formatted date string | Low | Blocks mobile chat — [MOBILE_STUDYGROUPS_PLAN.md](MOBILE_STUDYGROUPS_PLAN.md) §3.1 |
| 5 | Test coverage phases 3–4 unwritten | Low | Authorization matrix, external API, background jobs — [test/TEST_PLAN.md](test/TEST_PLAN.md) |

Items 2–4 are best fixed **during** the mobile work, since each benefits both clients.

---

## 9. Order and time

| Phase | Work | Time |
|---|---|---|
| 0 | Commit untracked files | 1 min |
| 1 | Restore 7 changes | 15 min |
| 2 | Fix the two test issues | 10 min |
| 3 | Decide + fix the solution reference | 5 min |
| 4 | Run all gates, verify 4 bugs in a live app | 30 min |
| 5 | Commit baseline | 2 min |
| | **Total** | **≈ 1 hour** |

Do **not** reorder. Phase 0 first is the whole point — everything after it is recoverable, and
before it, it isn't.

## 10. How to keep this from recurring

1. **Commit before switching context.** Both losses today happened around tooling changes.
2. **Never "discard all changes" in Visual Studio** on a tree with uncommitted work — `git stash`
   instead. Stash is recoverable; discard is not.
3. **Run gates 1–4 before each commit.** Four commands, under a minute.
4. **Keep the mobile project inside the repo** (§5 option B), so one `git add -A` protects
   everything.
