# UniConnect Mobile — Plan

> Companion to [PROJECT_OVERVIEW.md](PROJECT_OVERVIEW.md) and [test/TEST_PLAN.md](test/TEST_PLAN.md).
>
> **Audience:** students only. Instructor, DepartmentStaff, Company and Admin roles stay on the web.
> **Scope:** two modules — **Smart Attendance** and **Internships & Career Matching**.
> **Platform:** .NET MAUI, Android first.
> **Method:** AI-assisted development (Claude), reflected in the estimates in §7.

---

## 1. Scope

### In

| Module | Student-facing actions being migrated |
|---|---|
| **Attendance** | `ScanSubmit` · `ManualEntry` · `Submit` · `Result` · `MyAttendance` |
| **Internships** | `Index` (browse + filters + matching score) · `Details` · `Apply` · `MyApplications` · `Withdraw` |
| **Career Profile** | `Index` · `Edit` · `UploadCv` · `DeleteCv` · `SaveSkills` · `RemoveSkill` |

**17 student actions.** Everything else in those controllers is out.

### Out — and why that is a decision, not an omission

| Excluded | Reason |
|---|---|
| `InstructorAttendanceController` (12 actions) | Instructors work at a desk. Session creation needs a map pin, a QR projected on a screen, and a live roster — all desktop tasks |
| `CompanyController` (11 actions) | Career-services staff, not students |
| All Admin controllers | Desktop-first by nature |
| Study Groups · Rides · Tickets · Clubs | Phase 2 candidates, not in this plan |
| **Registration** | Students sign up on the web, then log in on mobile. Registration requires email confirmation and adapter-verified University ID — no value in duplicating it |

Excluding non-student roles removes roughly **60% of the controller surface** while losing nothing a student would use on a phone.

---

## 2. Why these two modules

They are chosen for opposite reasons, which is what makes the pair defensible.

**Attendance is the module that *needs* to be native.** From `Controllers/AttendanceController.cs`:

> - *"Mock location provider detected" requires native OS APIs with no browser equivalent — there
>   is no way for a website to ask "is this GPS reading coming from a spoofing app."*
> - *The device fingerprint is a persisted-per-BROWSER random ID (localStorage), not a hardware
>   identifier — clearing browser data or using a different browser on the same phone produces a
>   "new" device.*

The mobile client closes both. That is a measurable contribution, not a port — see §8.

**Internships is the module students *want* on a phone.** Browsing opportunities, checking a
matching score and applying are things people do in spare moments. It is also the better UI
showcase: filters, scored cards, a profile editor, file upload.

Together they cover both arguments for mobile — capability and convenience.

---

## 3. What has to be built server-side

**UniConnect has no API for its own features.** All ~141 controller actions return rendered Razor
or a redirect. The only HTTP API is `/external-api/v1` — the simulated *registrar*, data flowing
in, not out.

### Already in your favour

- **Attribute routing works today.** `Program.cs` never calls `MapControllers()`, only
  `MapControllerRoute` — but attribute-routed actions register regardless. Verified:
  `GET /external-api/v1/health` returns **401, not 404**. New `[ApiController]` classes need no
  routing changes.
- **`MapIdentityApi<ApplicationUser>()` gives `/login` and `/refresh` in ~one line**, and
  role-based `[Authorize]` keeps working because the bearer handler rebuilds the principal
  server-side. Saves ~2 days over hand-rolled JWT.
- **The business logic is already client-agnostic.** `MatchingScoreService`, `TextSimilarity`,
  `AttendanceSummaryService`, and the `IUniversityProvider` adapter need **no changes** — the API
  endpoints are thin wrappers.
- **214 tests already cover the rules** these endpoints expose.

### Endpoints

| # | Method | Route | Wraps |
|---|---|---|---|
| 1 | `POST` | `/api/v1/auth/login` | *free — `MapIdentityApi`* |
| 2 | `POST` | `/api/v1/auth/refresh` | *free — `MapIdentityApi`* |
| 3 | `GET` | `/api/v1/me` | profile, roles, enabled services |
| 4 | `GET` | `/api/v1/attendance/session/{token}` | `ScanSubmit` |
| 5 | `POST` | `/api/v1/attendance/submit` | `Submit` |
| 6 | `GET` | `/api/v1/attendance/history` | `MyAttendance` |
| 7 | `GET` | `/api/v1/internships` | `Index` — filters + matching scores |
| 8 | `GET` | `/api/v1/internships/{id}` | `Details` — keep the cross-university guard |
| 9 | `POST` | `/api/v1/internships/{id}/apply` | `Apply` |
| 10 | `GET` | `/api/v1/applications` | `MyApplications` |
| 11 | `POST` | `/api/v1/applications/{id}/withdraw` | `Withdraw` |
| 12 | `GET` | `/api/v1/career-profile` | `Index` |
| 13 | `PUT` | `/api/v1/career-profile` | `Edit` |
| 14 | `POST` | `/api/v1/career-profile/skills` | `SaveSkills` — batch, mirrors `SkillsBatchVM` |
| 15 | `POST` | `/api/v1/career-profile/cv` | `UploadCv` — multipart, ≤5 MB, format-validated |
| 16 | `DELETE` | `/api/v1/career-profile/cv` | `DeleteCv` |

**14 hand-written endpoints.**

### Cross-cutting changes

- `RequireServiceAttribute` returns `RedirectToActionResult("NotAvailable", "Home")` — an API
  caller needs **403 + JSON**. Both modules are service-gated, so this is required, not optional.
- Cookie auth redirects 401s to the login page. `/api` paths must return a bare **401**.
- `MatchingScoreService.BuildCorpusAsync()` must be called **once per request**, then reused across
  every internship in the list — not once per listing. The web controller already does this;
  don't lose it in the port.
- Two columns on `AttendanceRecord` for the native signals:
  ```csharp
  public string? SubmissionSource { get; set; }        // "Web" | "Mobile"
  public double? LocationAccuracyMeters { get; set; }  // null from web — itself a finding
  ```

Bearer auth scoped to `/api` coexists with cookie auth. **The web app is not touched.**

---

## 4. Mobile screens

| Area | Screens |
|---|---|
| Auth | Login |
| Attendance | Scanner · Confirm & submit · Result · My attendance |
| Internships | Browse (filters + scored cards) · Detail · Apply · My applications |
| Career profile | Profile & edit · Skills editor · CV upload |
| Shell | Tab navigation · Settings / logout |

**13 screens.**

Two details not to miss:
- **`InternshipPostingMode` has two values.** `FullApplication` applies in-app; `ListingOnly` must
  hand off to the employer's external URL or email. Handle both or half the listings break.
- **The matching score is the feature.** Show the number prominently on each card — it is what
  makes this more than a job list.

---

## 5. Fixed technical choices

| Decision | Choice | Reason |
|---|---|---|
| Framework | MAUI **native (XAML)** | QR scanning needs native treatment either way; avoids a hybrid seam through the key flow |
| Platform | **Android only** | iOS needs a Mac to build and sign — and has no mock-location API at all |
| QR | `BarcodeScanning.Native.Maui` | MLKit-backed; faster and better maintained than `ZXing.Net.Maui` |
| Tokens | MAUI `SecureStorage` | Keychain / Keystore — never `Preferences` |
| Files | `FilePicker` + multipart | For CV upload |
| Distribution | **Sideloaded APK** | Play Store review adds nothing to the mark |

---

## 6. Using Claude effectively on this

The estimates in §7 assume AI assistance. They only hold if it is used well.

**Where it is worth 3–4x**
- API controllers wrapping existing services — **give it the existing controller and service file
  as context and ask it to produce the API equivalent.** The business rules already exist; it
  should be translating, not inventing.
- DTOs and mapping from your entities.
- XAML layouts — verbose, formulaic, ideal.
- Tests alongside each endpoint.
- EF migrations and schema changes.

**Where it is worth ~1x — budget full time for these**
- **Tooling setup**: MAUI workload, Android SDK, emulator, USB debugging, signing.
- **Device debugging**: black camera preview, permission dialogs, vendor quirks.
- **Certificates and networking**: Android 7+ will not trust user-installed CAs for app traffic
  by default. Expect to fight this.
- **Physical testing**: walking out of the GPS radius, scanning in real lighting.

**Rules that keep the speed real**
1. **Point at the existing implementation, always.** "Port `InternshipsController.Index` to an API
   endpoint, preserving the filter and scoring behaviour" beats "write an internships API."
2. **Never let it invent business rules.** The FR-21 validation chain, the 5-factor scoring
   weights, the 5 MB CV cap — all already written. Deviations are bugs.
3. **Ask for tests with the code**, not afterwards. You have 214 tests and the harness for them.
4. **Review everything you will have to defend.** You will be asked how the matching score works
   in the viva. Code you cannot explain is worse than code you did not write.
5. **One vertical slice before breadth** — login → scan → submit → result end-to-end before a
   second screen.

---

## 7. Estimates

Person-days at 8h, solo. **"Traditional"** = hand-written. **"AI-assisted"** = with Claude, used
as in §6.

| Phase | Work | Traditional | **AI-assisted** |
|---|---|---|---|
| **0** | API layer — auth, 14 endpoints, DTOs, `/api` 401+403 branches, schema columns, Swagger | 20 | **7** |
| **1** | MAUI foundation — tooling, device deploy, DI, HTTP + token handler, `SecureStorage`, shell, theming, login | 11 | **6** |
| **2** | Attendance — QR scanner, GPS + permissions, submit/confirm/result, history, native signals | 13 | **8** |
| **3** | Internships — browse + filters + scores, detail (both posting modes), apply, my applications, profile, skills editor, CV upload | 21 | **9** |
| **4** | Hardening — error/offline states, device testing, APK, docs | 10 | **8** |
| | **Total** | **75** | **≈ 38** |

**≈ 38 person-days ≈ 7–8 weeks at a normal pace, or ~4 weeks working full-time.**

### Why the overall speedup is ~2x, not 5x

Phase 0 gets **3x** — it is near-ideal AI work, wrapping services that already exist.
Phase 4 gets barely **1.2x** — you cannot delegate walking outside to test a GPS radius.

The AI-resistant categories are disproportionately large in mobile work, which is exactly why
mobile projects overrun when teams budget from the 5x figure they saw on a backend project.

### If the deadline is one month

38 days does not fit 20 working days. Cut in this order:

1. **Career profile management** (screens 11–13, endpoints 12–16) — students edit their profile
   and upload their CV on the web; mobile browses and applies. **Saves ~6 days.**
2. **Manual token entry** — the scanner is the point. **Saves ~1 day.**
3. **GPS accuracy validation** — keep device ID and mock location, which are the documented gaps.

That lands at **~29 days**: attendance complete, internships browse/detail/apply/withdraw
complete, profile read-only. A coherent product, not a truncated one.

---

## 8. The evaluation chapter — where the marks are

Do not just demo the app. Run controlled experiments and publish a table:

| Attack | Web client | Mobile client |
|---|---|---|
| GPS spoofing app, submit from home | ✅ accepted | ❌ rejected — mock provider detected |
| One phone, three students, site data cleared between each | ✅ 3 accepted, unflagged | ❌ flagged — same hardware ID |
| Indoor submission, ±400m fix | ✅ accepted (by luck) | ❌ rejected — accuracy below threshold |

`SubmissionSource` makes this queryable: run both clients against the **same session** and report
the difference from the database, not from anecdote. The existing instructor dashboard already
surfaces `IsSuspicious`/`SuspiciousReason`, so no new UI is needed to show the result.

**Record each experiment as its signal lands — not in a batch at the end.**

### A finding to report either way

`wwwroot/js/pages/attendance-submit.js:46` reads only `latitude`/`longitude`;
`pos.coords.accuracy` is never sent, and `Submit(token, lat, lng, deviceFingerprint)` has no
parameter for it. **GPS accuracy is never captured or validated in the current system**, so a
student indoors with a ±400m fix passes or fails the 100m radius check essentially at random.
Report it whether or not you build the fix — finding it is a result.

---

## 9. Order of work

1. **Phase 0 complete before opening the MAUI templates.** You should be able to log in, scan,
   submit and list internships from Postman first. Debugging auth through an unfamiliar UI
   framework is miserable.
2. **Day one of Phase 1 is proving deployment to a real Android phone** — not the emulator. If
   that takes three days, learn it on day one.
3. **Attendance before Internships.** It is the smaller module and carries the evaluation story.
4. **One vertical slice end-to-end** before breadth.

## 10. Limitations to state

- `ANDROID_ID` resets on factory reset and differs per signing key.
- `IsFromMockProvider` is defeatable on rooted devices — you raise the attack cost, not eliminate it.
- iOS has no mock-location API, so this defence is Android-only by platform constraint.
- Prototype, not a product: sideloaded, no offline handling, no push notifications.
- Students-only by design — instructor and admin workflows remain web-based.

Future work: BLE proximity beacons, Play Integrity API, push notifications, iOS parity, and the
remaining four student modules.

## 11. Risks

| Risk | Mitigation |
|---|---|
| MAUI tooling eats days | Phase 1 day one is nothing but proving a real-device deploy |
| Android cert/LAN issues block API calls | Budget a day; `appsettings.json` already carries a LAN IP for `Attendance:PublicBaseUrl` for exactly this reason |
| AI-generated code you cannot explain | §6 rule 4 — review anything the viva will touch |
| Matching score re-implemented on the client | It stays server-side. The API returns the number; the app renders it |
| Scope creep to other modules | Four modules are explicitly deferred. Depth over breadth |
| Report squeezed | Protect the final week for writing |
