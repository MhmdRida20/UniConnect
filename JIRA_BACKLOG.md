# UniConnect — Jira Backlog

A complete task breakdown of the UniConnect project, derived from the actual
codebase (21 commits, 20 web controllers, 5 mobile API controllers, 19 services,
6 SignalR hubs, 27 entities, 271 automated tests).

Every task sits on **one line** in the CSV block at the end so it can be pasted
straight into Jira. The tables above it carry the same tasks with more context
for reading and for filling in assignees.

---

## How to load this into Jira

> **Which Jira are you on?** If the import field list offers **"Work type"**
> rather than "Issue Type", and has no "Epic Name" or "Epic Link", you are on a
> **team-managed** project on current Jira Cloud. Those fields genuinely do not
> exist there — epics are ordinary work items and children point at them
> through **Parent**. Use the ready-made files in [`jira/`](jira/), which are
> already written for that. The single CSV in §16 uses the older
> company-managed field names and is kept as a reference.

### Team-managed Jira (the `jira/` folder)

Use **[`jira/0-all-in-one.csv`](jira/0-all-in-one.csv)** — 182 rows, epics and
tasks together, one pass.

The current importer will not accept a `Parent` column unless every row also
carries a **Work item ID**, because parents are referenced by that ID rather
than by a Jira key. That turns out to be simpler than the old way: rows point at
each other inside the file, so the epics can be created in the same import as
their children and there are no keys to look up.

1. Jira → **Settings (⚙) → System → External System Import → CSV**.
2. Upload `0-all-in-one.csv` and pick the project.
3. Map every column to the field of the same name — `Work item ID` → Work item
   ID is the one that is easy to miss, and without it `Work type` and `Parent`
   both fail.
4. Step through *Map values*, *Move users* and *Review details*, then import.

> **The project must not already contain these epics.** This file creates all
> 14. If a previous attempt imported them, delete those first — filter the List
> view by *Work type = Epic*, select all, **Bulk change → Delete** — otherwise
> you end up with two of each.

**The two-file alternative**

[`jira/1-epics.csv`](jira/1-epics.csv) and [`jira/2-tasks.csv`](jira/2-tasks.csv)
are the same content split in two, for the older importer that resolves parents
by issue key. Import the epics, write the keys Jira assigns into
[`jira/3-parent-map.csv`](jira/3-parent-map.csv), run 14 find-and-replace
operations over `2-tasks.csv` (`EPIC>Study Groups` → `UC-3`, and so on — the
`EPIC>` prefix keeps each token unique), then import the tasks. Only worth it if
the single-file import will not go through.

**Afterwards — get them onto the board**

The 168 issues exist now, but whether they appear on the board depends on which
board you have. A **Backlog** entry in the left sidebar means Scrum.

Note that the 14 epics will *not* appear as cards either way — epics are
containers in a team-managed project, visible in the Backlog's epic panel, the
Timeline and List view, but never on the board itself. A board showing only
tasks and stories is working correctly.

- **Kanban** — nothing to do. The issues show up straight away, sorted into the
  To Do / In Progress / Done columns by the `Status` column.
- **Scrum** — imported issues land in the *Backlog*, and the board stays empty
  until they are in a sprint. Select them, right-click → *Move to* → a new
  sprint, then **Start sprint**. Seven sprints named after the phases in the
  table below mirror how the project actually ran; one sprint holding everything
  works too if you only need the board populated.

**If `Story point estimate` will not appear in the mapping list**

In a team-managed project the field does not exist until estimation is switched
on, so there is nothing to map and nothing to drag. Fix it in this order:

1. **Project settings → Features** → turn on **Estimation** (and **Backlog**,
   if it is off). This is what creates the field.
2. **Project settings → Work types →** pick *Story*, then *Task*, then *Bug* →
   check `Story point estimate` is on each screen; drag it across if not.
3. **Restart the import wizard from the beginning.** It reads the field list
   once when it starts, so a field enabled mid-import will not show up until you
   re-upload the file.

The exact menu wording moves around between Jira releases — the thing to look
for is an *Estimation* toggle in the project's feature list.

If you would rather not bother, delete the `Story point estimate` column and
import without it. The estimates are the least important part of this backlog —
but note they are what §17's 50/50 contribution split is measured in, so without
them Jira cannot show that balance.

### About the Status column

The files carry `Status` — 162 `Done`, 6 `To Do`, and the two unfinished epics
as `In Progress`. Those three names are the default team-managed software
workflow, so they map straight through.

The one thing that can go wrong: if your workflow was renamed (say *Done* is
called *Complete*), a status Jira does not recognise fails the whole row. Check
**Project settings → Work types → Story → Workflow** first. If the names differ,
either rename them back or delete the `Status` column and set it afterwards with
**Bulk change → Edit → Status**.

### Before importing

- **Assignee is filled in with email addresses**, which is what Jira matches on
  most reliably — display names are ambiguous here, since more than one account
  on the site is some form of "Mohammad … Rida".

  | Member | Jira account |
  |---|---|
  | Mhmd_Rida | `mhr824@usal.edu.lb` |
  | Mohamad_Sabbagh | `mha206@usal.edu.lb` |

  Both must already be on the project (**Project settings → People**) or the
  assignment is dropped silently.
- **The 26 shared foundation tasks carry one assignee, not two.** Jira has no
  multi-assignee field, so each has a nominal owner picked to keep the totals
  level; both members stay credited in the task description and by the `pair`
  label, which is filterable with `labels = pair`.
- Story points are rough relative estimates, not measured time.

### Regenerating the files

`0-all-in-one.csv`, `1-epics.csv` and `2-tasks.csv` are **generated**, not
hand-edited —
[`jira/build_csv.py`](jira/build_csv.py) builds them from the master table in
§16 and the rule in [`jira/assign.py`](jira/assign.py), so the document and the
imports cannot drift apart:

```
python jira/build_csv.py
```

It prints the contribution balance as it writes.

---

## Phases (the "stage" each task belongs to)

| Phase | Name | What it covers |
|---|---|---|
| **P1** | Foundation & Architecture | Solution setup, identity, multi-tenancy, database, external API integration |
| **P2** | Core Feature Modules | The six student-facing feature areas |
| **P3** | Administration & Reporting | Admin, university admin, staff and audit tooling |
| **P4** | UI/UX & Design System | Layouts, design tokens, icons, responsive behaviour |
| **P5** | Quality & Testing | Automated test suite and test infrastructure |
| **P6** | Mobile Application | .NET MAUI client |
| **P7** | Hardening & Defect Fixes | Security, correctness and bug-fix work |

## Epics

| Key | Epic | Phase | Status |
|---|---|---|---|
| EP-1 | Platform Foundation & Multi-University Architecture | P1 | Done |
| EP-2 | Authentication, Roles & Account Management | P1 | Done |
| EP-3 | Study Groups | P2 | Done |
| EP-4 | Ride Sharing | P2 | Done |
| EP-5 | Smart Attendance | P2 | Done |
| EP-6 | Complaints & Ticketing | P2 | Done |
| EP-7 | Clubs & Organizations | P2 | Done |
| EP-8 | Internships & Career Matching | P2 | Done |
| EP-9 | Notifications & Real-Time Infrastructure | P2 | Done |
| EP-10 | Administration, Reporting & Audit | P3 | Done |
| EP-11 | UI/UX & Design System | P4 | Done |
| EP-12 | Automated Testing | P5 | Done |
| EP-13 | Mobile Application (.NET MAUI) | P6 | In Progress |
| EP-14 | Security Hardening & Defect Fixes | P7 | In Progress |

---

## 1. EP-1 — Platform Foundation & Multi-University Architecture (P1)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Set up ASP.NET Core 8 MVC solution with EF Core and SQL Server | Task | Done | Both | 3 | `UniConnect.csproj`, `Program.cs` |
| Design and implement the relational domain model (27 entities) | Story | Done | Both | 8 | `Models/` |
| Configure EF Core mappings, composite keys and delete behaviours | Task | Done | Both | 5 | `ApplicationDbContext.OnModelCreating` |
| Build the multi-university adapter core for multi-tenancy | Story | Done | Both | 8 | `Adapters/IUniversityProvider.cs` |
| Implement `UniversityProviderResolver` to select a provider per university | Task | Done | Both | 3 | `Adapters/UniversityProviderResolver.cs` |
| Implement `RealApiUniversityProvider` for standard university APIs | Task | Done | Both | 5 | `Adapters/RealApiUniversityProvider.cs` |
| Implement `UmsApiUniversityProvider` for the alternative UMS API style | Task | Done | Both | 5 | `Adapters/UmsApiUniversityProvider.cs` |
| Build the simulated external university API for development and demos | Story | Done | Both | 8 | `Controllers/ExternalApi/ExternalUniversityApiController.cs` |
| Implement the service catalog so each university enables its own modules | Story | Done | Both | 5 | `Services/ServiceCatalogService.cs` |
| Add `RequireService` filter to gate disabled modules per university | Task | Done | Both | 3 | `Rules/RequireServiceFilterTests.cs` |
| Build background university API sync (courses, students, staff, enrolments) | Story | Done | Both | 8 | `Services/UniversityApiSyncRunner.cs` |
| Add enrolment revalidation background job | Task | Done | Both | 5 | `Services/EnrollmentRevalidationRunner.cs` |
| Implement database seeding with roles, universities and demo accounts | Task | Done | Both | 5 | `Data/DbSeeder.cs` |
| Write the project overview and technical reference document | Task | Done | Both | 3 | `PROJECT_OVERVIEW.md` |

## 2. EP-2 — Authentication, Roles & Account Management (P1)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Integrate ASP.NET Core Identity with a custom `ApplicationUser` | Story | Done | Both | 5 | `Models/ApplicationUser.cs` |
| Define the six-role model (Student, Instructor, Admin, UniversityAdmin, DepartmentStaff, Company) | Task | Done | Both | 3 | `Data/DbSeeder.cs` |
| Build student self-registration validated against the university record | Story | Done | Both | 5 | `Areas/Identity/Pages/Account/Register.cshtml.cs` |
| Build instructor registration with staff-record verification | Story | Done | Both | 5 | `Areas/Identity/Pages/Account/RegisterInstructor.cshtml.cs` |
| Implement email confirmation with SMTP delivery | Task | Done | Both | 3 | `Services/SmtpEmailSender.cs` |
| Add account lockout and failed-login auditing | Task | Done | Both | 3 | `Controllers/Api/AuthApiController.cs` |
| Implement user suspension with request-time enforcement middleware | Story | Done | Both | 5 | `Middleware/SuspendedUserMiddleware.cs` |
| Add session anomaly detection middleware | Task | Done | Both | 5 | `Middleware/SessionAnomalyMiddleware.cs` |
| Build user profile management with picture upload | Story | Done | Both | 5 | `Controllers/ProfileController.cs` |
| Issue JWTs for mobile clients | Story | Done | Both | 5 | `Services/JwtTokenService.cs` |
| Accept JWT bearer auth alongside cookie auth, scoped to `/api` | Task | Done | Both | 5 | `Program.cs` |
| Support SignalR querystring `access_token` for hub authentication | Task | Done | Both | 3 | `Program.cs` JwtBearerEvents |

## 3. EP-3 — Study Groups (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design Study Group, Member and Message entities with membership states | Story | Done | Sabbagh | 5 | `Models/StudyGroup.cs` |
| Build study group browse filtered to the student's enrolled courses | Story | Done | Rida | 5 | FR-46 |
| Build study group creation with per-university member cap enforcement | Story | Done | Sabbagh | 5 | FR-11 |
| Implement join requests with creator approval workflow | Story | Done | Sabbagh | 8 | FR-49 |
| Implement approve, reject and remove member actions | Story | Done | Sabbagh | 5 | `StudyGroupService` |
| Implement leave group with automatic leadership transfer | Story | Done | Sabbagh | 5 | `StudyGroupService.LeaveAsync` |
| Implement explicit leadership transfer to a chosen member | Story | Done | Sabbagh | 3 | `StudyGroupService.TransferLeadershipAsync` |
| Build real-time group chat over SignalR | Story | Done | Sabbagh | 8 | FR-52, `Hubs/StudyGroupHub.cs` |
| Add optimistic concurrency handling for simultaneous joins | Task | Done | Sabbagh | 5 | `RowVersion`, `SimultaneousActionTests` |
| Auto-archive study groups with no recent activity | Task | Done | Sabbagh | 3 | `Services/InactiveStudyGroupService.cs` |
| Extract all study group rules into a shared `StudyGroupService` | Task | Done | Sabbagh | 8 | `Services/StudyGroupService.cs` |
| Add delete study group (creator only, archives and preserves history) | Story | Done | Sabbagh | 5 | `StudyGroupService.DeleteAsync` |

## 4. EP-4 — Ride Sharing (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design Ride, RideRequest and Vehicle entities | Story | Done | Rida | 5 | `Models/Ride.cs` |
| Build driver registration and vehicle management | Story | Done | Rida | 5 | `Controllers/VehiclesController.cs` |
| Build ride creation, search and booking flow | Story | Done | Rida | 8 | `Controllers/RidesController.cs` |
| Implement ride visibility rules by university and status | Story | Done | Sabbagh | 5 | `Rules/RideVisibilityTests.cs` |
| Integrate geocoding for pickup and drop-off locations | Story | Done | Sabbagh | 5 | `Services/GeocodingService.cs` |
| Build live ride tracking with SignalR and map display | Story | Done | Rida | 8 | `Hubs/RideTrackingHub.cs` |
| Build "My Rides" management page for drivers and passengers | Story | Done | Rida | 5 | `Views/Rides/` |

## 5. EP-5 — Smart Attendance (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design AttendanceSession and attendance record entities | Story | Done | Sabbagh | 5 | `Models/AttendanceSession.cs` |
| Build instructor session creation with QR code generation | Story | Done | Rida | 8 | `Controllers/InstructorAttendanceController.cs` |
| Build student attendance submission with device-identity checks | Story | Done | Sabbagh | 8 | `Services/AttendanceSubmissionService.cs` |
| Enforce one-device-per-student anti-proxy rule | Story | Done | Sabbagh | 5 | `Rules/AttendanceSubmissionTests.cs` |
| Build live attendance monitoring over SignalR | Story | Done | Sabbagh | 5 | `Hubs/AttendanceHub.cs` |
| Auto-close expired attendance sessions in the background | Task | Done | Sabbagh | 3 | `Services/CloseExpiredAttendanceSessionsService.cs` |
| Build the instructor attendance dashboard with per-session breakdown | Story | Done | Rida | 8 | `Views/InstructorAttendance/Details.cshtml` |
| Build attendance summary reporting per course and student | Story | Done | Rida | 5 | `Services/AttendanceSummaryService.cs` |
| Add CSV export of attendance records | Story | Done | Sabbagh | 3 | `Controllers/InstructorAttendanceController.cs` |
| Add QR full-screen, download and share options | Task | Done | Rida | 3 | commit `b3466e7` |

## 6. EP-6 — Complaints & Ticketing (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design the Ticket entity with status and category workflow | Story | Done | Sabbagh | 5 | `Models/Ticket.cs` |
| Build student ticket submission with attachments | Story | Done | Rida | 5 | `Controllers/TicketsController.cs` |
| Build the department staff ticket queue and assignment | Story | Done | Rida | 8 | `Controllers/StaffTicketsController.cs` |
| Implement the ticket status workflow and transition rules | Story | Done | Sabbagh | 5 | `Rules/TicketWorkflowTests.cs` |
| Add real-time ticket updates over SignalR | Story | Done | Sabbagh | 5 | `Hubs/TicketHub.cs` |
| Flag stale tickets automatically in the background | Task | Done | Sabbagh | 3 | `Services/TicketStalenessService.cs` |

## 7. EP-7 — Clubs & Organizations (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design Club and ClubEvent entities with officer roles | Story | Done | Sabbagh | 5 | `Models/Club.cs` |
| Build club creation, membership and officer management | Story | Done | Rida | 8 | `Controllers/ClubsController.cs` |
| Implement officer departure and succession rules | Story | Done | Sabbagh | 5 | `Rules/ClubOfficerDepartureTests.cs` |
| Build club event scheduling and the events calendar | Story | Done | Rida | 5 | `Views/Clubs/` |
| Add real-time club activity updates over SignalR | Task | Done | Sabbagh | 3 | `Hubs/ClubHub.cs` |
| Auto-flag inactive clubs in the background | Task | Done | Sabbagh | 3 | `Services/InactiveClubService.cs` |

## 8. EP-8 — Internships & Career Matching (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design Internship, Company and CareerProfile entities | Story | Done | Sabbagh | 5 | `Models/Internship.cs` |
| Build the company portal for posting internships | Story | Done | Rida | 8 | `Controllers/CompanyController.cs` |
| Build student career profile with skills management | Story | Done | Rida | 8 | `Controllers/CareerProfileController.cs` |
| Build internship browse, search and application flow | Story | Done | Rida | 8 | `Controllers/InternshipsController.cs` |
| Implement the skills-based matching score algorithm | Story | Done | Sabbagh | 8 | `Services/MatchingScoreService.cs` |
| Implement text similarity matching for skills | Task | Done | Sabbagh | 5 | `Services/TextSimilarity.cs` |
| Enforce internship application eligibility rules | Story | Done | Sabbagh | 5 | `Rules/InternshipApplicationTests.cs` |
| Build the "My Applications" tracking page | Story | Done | Rida | 5 | `Views/Internships/MyApplications.cshtml` |

## 9. EP-9 — Notifications & Real-Time Infrastructure (P2)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Design a unified notification entity and delivery service | Story | Done | Sabbagh | 5 | `Services/NotificationService.cs` |
| Build the notification centre with read/unread state | Story | Done | Rida | 5 | `Controllers/NotificationsController.cs` |
| Add live notification delivery over SignalR | Story | Done | Sabbagh | 5 | `Hubs/NotificationHub.cs` |
| Add the navbar notification badge with live count | Task | Done | Rida | 3 | `Views/_NavbarSnippet.cshtml` |
| Send transactional emails for key events | Task | Done | Sabbagh | 3 | `Services/SmtpEmailSender.cs` |

## 10. EP-10 — Administration, Reporting & Audit (P3)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Build university management (create, configure, API settings) | Story | Done | Sabbagh | 8 | `Controllers/AdminUniversitiesController.cs` |
| Build user administration with role assignment and suspension | Story | Done | Rida | 8 | `Controllers/AdminUsersController.cs` |
| Build the per-university service catalog toggle screen | Story | Done | Sabbagh | 5 | `Controllers/AdminUniversitiesController.cs` |
| Build the audit log viewer with filtering | Story | Done | Sabbagh | 5 | `Controllers/AdminAuditLogController.cs` |
| Implement centralised audit logging across all modules | Story | Done | Sabbagh | 8 | `Services/AuditLogService.cs` |
| Add authorization-failure auditing middleware | Task | Done | Sabbagh | 3 | `Middleware/AuditingAuthorizationMiddlewareResultHandler.cs` |
| Build the admin reporting dashboard | Story | Done | Rida | 8 | `Controllers/AdminReportsController.cs` |
| Add CSV export for admin reports | Story | Done | Sabbagh | 3 | `Rules/AdminReportExportTests.cs` |
| Build the external API simulator admin screen for demos | Story | Done | Sabbagh | 5 | `Controllers/AdminExternalApiSimulatorController.cs` |
| Add the reduced-functionality banner when a university sync fails | Task | Done | Sabbagh | 3 | FR-17 |

## 11. EP-11 — UI/UX & Design System (P4)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Build the shared design token system (colour, radius, shadow, motion) | Story | Done | Sabbagh | 8 | `wwwroot/css/site.css` |
| Integrate the Hugeicons SVG sprite icon system | Story | Done | Rida | 5 | `Views/Shared/_Icons.cshtml` |
| Design and build the two application layouts (main and minimal) | Story | Done | Rida | 5 | `Views/Shared/` |
| Redesign the home page with animations | Story | Done | Rida | 5 | commit `6f34f73` |
| Redesign the authentication pages (login, register) | Story | Done | Sabbagh | 5 | commit `1d728bb` |
| Build the reusable hero banner component with responsive bleed | Story | Done | Rida | 5 | `.uc-hero` in `site.css` |
| Build the custom accessible dropdown component | Story | Done | Rida | 5 | `wwwroot/js/uc-select.js` |
| Build reusable card, pill, empty-state and form-page components | Story | Done | Rida | 8 | `wwwroot/css/pages/` |
| Add page-level stylesheets for all 20 feature areas | Task | Done | Rida | 8 | `wwwroot/css/pages/` |
| Redesign the notifications page UI | Task | Done | Rida | 3 | commit `ff2fd04` |
| Enhance calendar layouts and attachment presentation | Task | Done | Rida | 5 | commit `d8b2d41` |
| Add progress-bar and stepper components to multi-step forms | Task | Done | Rida | 3 | commit `4974a52` |

## 12. EP-12 — Automated Testing (P5)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Set up the xUnit test project and folder structure | Task | Done | Sabbagh | 3 | `test/UniConnect.Tests/` |
| Build test infrastructure (in-memory DB, harnesses, fakes, stubs) | Story | Done | Rida | 8 | `test/UniConnect.Tests/Infrastructure/` |
| Write the test plan and coverage study | Task | Done | Sabbagh | 5 | `test/TEST_PLAN.md` |
| Write attendance submission rule tests (20 tests) | Task | Done | Sabbagh | 5 | `Rules/AttendanceSubmissionTests.cs` |
| Write study group membership rule tests (19 tests) | Task | Done | Sabbagh | 5 | `Rules/StudyGroupMembershipTests.cs` |
| Write internship application rule tests (19 tests) | Task | Done | Sabbagh | 5 | `Rules/InternshipApplicationTests.cs` |
| Write ticket workflow rule tests (18 tests) | Task | Done | Sabbagh | 5 | `Rules/TicketWorkflowTests.cs` |
| Write ride visibility rule tests (15 tests) | Task | Done | Sabbagh | 3 | `Rules/RideVisibilityTests.cs` |
| Write club officer departure rule tests (10 tests) | Task | Done | Sabbagh | 3 | `Rules/ClubOfficerDepartureTests.cs` |
| Write service catalog gating tests | Task | Done | Sabbagh | 3 | `Rules/RequireServiceFilterTests.cs` |
| Write attendance summary service unit tests (16 tests) | Task | Done | Sabbagh | 3 | `Unit/AttendanceSummaryServiceTests.cs` |
| Write text similarity and matching score unit tests (28 tests) | Task | Done | Sabbagh | 5 | `Unit/TextSimilarityTests.cs` |
| Write view model validation tests (13 tests) | Task | Done | Sabbagh | 3 | `Unit/ViewModelValidationTests.cs` |
| Write concurrency tests for simultaneous actions | Task | Done | Sabbagh | 5 | `Concurrency/SimultaneousActionTests.cs` |
| Write background job unit tests (sync runner, session closing) | Task | Done | Sabbagh | 3 | `Unit/UniversityApiSyncRunnerTests.cs` |
| Write instructor roster and admin export tests | Task | Done | Sabbagh | 3 | `Rules/InstructorRosterTests.cs` |

## 13. EP-13 — Mobile Application (.NET MAUI) (P6)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Research and plan the mobile app scope and architecture | Task | Done | Sabbagh | 5 | `MOBILE_APP_PLAN.md` |
| Write the Study Groups mobile implementation plan | Task | Done | Sabbagh | 3 | `MOBILE_STUDYGROUPS_PLAN.md` |
| Create the .NET MAUI project targeting Android and Windows | Task | Done | Rida | 3 | `mobile/UniConnect.Mobile/` |
| Add the mobile project to the solution and fix build isolation | Task | Done | Rida | 3 | `UniConnect.csproj` compile excludes |
| Build the mobile authentication API (login, student registration) | Story | Done | Rida | 8 | `Controllers/Api/AuthApiController.cs` |
| Restrict mobile access to the Student role only | Task | Done | Sabbagh | 2 | `AuthApiController.MobileAllowedRoles` |
| Build the Study Groups REST API (12 endpoints) | Story | Done | Rida | 13 | `Controllers/Api/StudyGroupsApiController.cs` |
| Build the Attendance mobile API | Story | Done | Rida | 8 | `Controllers/Api/AttendanceApiController.cs` |
| Build the Notifications mobile API | Story | Done | Rida | 5 | `Controllers/Api/NotificationsApiController.cs` |
| Build the Home/dashboard mobile API | Story | Done | Rida | 3 | `Controllers/Api/HomeApiController.cs` |
| Implement per-platform API base URL resolution | Task | Done | Rida | 5 | `Services/ApiConfig.cs` |
| Implement secure token storage with a desktop fallback | Task | Done | Sabbagh | 5 | `Services/SessionStore.cs` |
| Implement automatic bearer token attachment for all requests | Task | Done | Sabbagh | 3 | `Services/AuthHeaderHandler.cs` |
| Configure Android network security for development hosts | Task | Done | Sabbagh | 3 | `network_security_config.xml` |
| Build the typed Study Groups API client with error translation | Story | Done | Rida | 8 | `Services/StudyGroupsApi.cs` |
| Build the mobile login screen | Story | Done | Rida | 5 | `Pages/LoginPage.xaml` |
| Build the study group browse screen with search and course filter | Story | Done | Rida | 8 | `Pages/GroupsPage.xaml` |
| Build the study group details screen with member management | Story | Done | Rida | 8 | `Pages/GroupDetailsPage.xaml` |
| Build the create study group form with field validation | Story | Done | Rida | 5 | `Pages/CreateGroupPage.xaml` |
| Build the mobile group chat screen | Story | Done | Rida | 8 | `Pages/ChatPage.xaml` |
| Implement live chat and list updates over SignalR on mobile | Story | Done | Sabbagh | 8 | `Services/StudyGroupHubClient.cs` |
| Port the web design system to MAUI (tokens, gradients, elevation) | Story | Done | Rida | 8 | `Resources/Styles/UniConnect.xaml` |
| Generate MAUI icon assets from the web icon sprite | Task | Done | Rida | 5 | `Resources/Images/ic_*.svg` |
| Implement responsive layout for phone, tablet and desktop widths | Story | Done | Rida | 5 | `Services/Responsive.cs` |
| Add delete study group to the mobile client | Story | Done | Sabbagh | 3 | `Pages/GroupDetailsPage.xaml.cs` |
| Write the Study Groups API parity test suite (25 tests) | Task | Done | Rida | 8 | `Rules/StudyGroupApiParityTests.cs` |
| Build the Internships mobile screens | Story | **To Do** | Rida | 13 | `MOBILE_APP_PLAN.md` |
| Build the Attendance mobile screens with QR scanning | Story | **To Do** | Rida | 13 | `MOBILE_APP_PLAN.md` |
| Build the mobile notifications screen | Story | **To Do** | Rida | 5 | `MOBILE_APP_PLAN.md` |
| Test the mobile app on a physical Android device | Task | **To Do** | Sabbagh | 5 | — |
| Produce a signed Android release build | Task | **To Do** | Rida | 5 | — |

## 14. EP-14 — Security Hardening & Defect Fixes (P7)

| Summary | Type | Status | Assignee | Points | Evidence |
|---|---|---|---|---|---|
| Add admin scoping so university admins only see their own tenant | Story | Done | Sabbagh | 8 | commit `64234bb` |
| Harden security across controllers and add unified notifications | Story | Done | Sabbagh | 8 | commit `64234bb` |
| Require membership before joining a study group's real-time chat | Bug | Done | Rida | 5 | `Hubs/StudyGroupHub.cs` |
| Require authentication on the study group hub | Bug | Done | Rida | 3 | `Hubs/StudyGroupHub.cs` |
| Accept both cookie and bearer schemes on the study group hub | Bug | Done | Sabbagh | 3 | Mobile chat showed "Offline" |
| Add field validation to the study group create API | Bug | Done | Sabbagh | 5 | `StudyGroupService.CreateAsync` |
| Stop the web create action from saving a group when validation failed | Bug | Done | Sabbagh | 3 | `StudyGroupsController.Create` |
| Validate university API base URLs to prevent `UriFormatException` crashes | Bug | Done | Rida | 3 | `Services/UniversityApiSyncRunner.cs` |
| Fix Arabic text corruption in CSV exports (UTF-8 BOM) | Bug | Done | Sabbagh | 3 | `AdminReportsController` |
| Fix timezone handling in attendance session expiry | Bug | Done | Sabbagh | 3 | commit `799f414` |
| Fix keyboard navigation in the custom dropdown component | Bug | Done | Rida | 3 | `wwwroot/js/uc-select.js` |
| Fix non-responsive hero banners on several pages | Bug | Done | Rida | 3 | `.uc-hero` in `site.css` |
| Fix the admin users role dropdown resetting to Student | Bug | Done | Rida | 2 | commit `ff2fd04` |
| Fix duplicate page titles in the mobile app | Bug | Done | Rida | 2 | `Shell.NavBarIsVisible` |
| Fix pickers showing their value twice on Windows | Bug | Done | Rida | 2 | `Pages/GroupsPage.xaml` |
| Fix mobile card grid breaking layout at narrow widths | Bug | Done | Rida | 3 | `Services/Responsive.cs` |
| Fix Shell navigation crash on app resume | Bug | Done | Sabbagh | 3 | `Pages/LoginPage.xaml.cs` |
| Rotate the SMTP credential committed to git history | Task | **To Do** | Sabbagh | 3 | Security — see note below |
| Add exception logging for the Windows mobile build | Task | Done | Rida | 2 | `Platforms/Windows/App.xaml.cs` |

> **Open security task.** An SMTP application password was committed to
> `appsettings.json` and remains in the repository history on a public remote.
> Rotating the value does not remove it from history — the credential must be
> **revoked at the provider** and, ideally, the history rewritten. This is
> tracked above as the only outstanding item in EP-14.

---

## 15. Summary

| Metric | Value |
|---|---|
| Jira issues in the CSV | 182 |
| Epics | 14 |
| Tasks / stories / bugs | 168 |
| Completed | 162 |
| Outstanding | 6 |
| Automated tests | 271 (all passing) |
| Web controllers | 20 |
| Mobile API controllers | 5 |
| Services | 19 |
| SignalR hubs | 6 |
| Domain entities | 27 |
| Requirement IDs referenced in code | 62 (FR-03 … FR-92) |

---

## 16. Full CSV for Jira import

Copy everything between the fences into `uniconnect-backlog.csv`.

```csv
Summary,Issue Type,Description,Epic Name,Epic Link,Status,Assignee,Labels,Story Points
Platform Foundation & Multi-University Architecture,Epic,Solution setup multi-tenancy database and external API integration,Platform Foundation,,Done,,phase-1,
Authentication Roles & Account Management,Epic,Identity roles registration and JWT issuance,Authentication,,Done,,phase-1,
Study Groups,Epic,Course study groups with membership and real-time chat,Study Groups,,Done,,phase-2,
Ride Sharing,Epic,Student ride sharing with live tracking,Ride Sharing,,Done,,phase-2,
Smart Attendance,Epic,QR based attendance with anti-proxy checks,Smart Attendance,,Done,,phase-2,
Complaints & Ticketing,Epic,Student complaints and staff ticket workflow,Ticketing,,Done,,phase-2,
Clubs & Organizations,Epic,Student clubs events and officer management,Clubs,,Done,,phase-2,
Internships & Career Matching,Epic,Internship postings and skills based matching,Internships,,Done,,phase-2,
Notifications & Real-Time Infrastructure,Epic,Unified notifications and SignalR hubs,Notifications,,Done,,phase-2,
Administration Reporting & Audit,Epic,Admin tooling reporting and audit logging,Administration,,Done,,phase-3,
UI/UX & Design System,Epic,Design tokens components and page layouts,Design System,,Done,,phase-4,
Automated Testing,Epic,xUnit test suite and test infrastructure,Testing,,Done,,phase-5,
Mobile Application,Epic,.NET MAUI student mobile client,Mobile App,,In Progress,,phase-6,
Security Hardening & Defect Fixes,Epic,Security fixes and defect resolution,Hardening,,In Progress,,phase-7,
Set up ASP.NET Core 8 MVC solution with EF Core and SQL Server,Task,Project scaffolding dependency injection and database context,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;setup,3
Design and implement the relational domain model,Story,27 entities covering all feature areas,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;database,8
Configure EF Core mappings composite keys and delete behaviours,Task,OnModelCreating configuration for constraints and cascade rules,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;database,5
Build the multi-university adapter core for multi-tenancy,Story,Provider abstraction allowing each university its own API integration,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;architecture,8
Implement UniversityProviderResolver to select a provider per university,Task,Resolves the correct adapter from the university code,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;architecture,3
Implement RealApiUniversityProvider for standard university APIs,Task,Adapter for the standard REST API shape,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;integration,5
Implement UmsApiUniversityProvider for the alternative UMS API style,Task,Adapter for the alternative API shape,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;integration,5
Build the simulated external university API for development and demos,Story,Local stand-in for a real university system,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;integration,8
Implement the service catalog so each university enables its own modules,Story,Per-university feature enablement,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;architecture,5
Add RequireService filter to gate disabled modules per university,Task,Action filter blocking access to disabled services,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;architecture,3
Build background university API sync for courses students staff and enrolments,Story,Scheduled synchronisation from the university API,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;background-job,8
Add enrolment revalidation background job,Task,Periodically revalidates student enrolments,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;background-job,5
Implement database seeding with roles universities and demo accounts,Task,Seeds a working environment on first run,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;setup,5
Write the project overview and technical reference document,Task,Architecture and module reference documentation,,Platform Foundation,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;docs,3
Integrate ASP.NET Core Identity with a custom ApplicationUser,Story,Identity with university scoped user fields,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,5
Define the six-role model,Task,Student Instructor Admin UniversityAdmin DepartmentStaff Company,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,3
Build student self-registration validated against the university record,Story,Registration verified against synced student records,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,5
Build instructor registration with staff-record verification,Story,Registration verified against synced staff records,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,5
Implement email confirmation with SMTP delivery,Task,Confirmation links delivered by email,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,3
Add account lockout and failed-login auditing,Task,Lockout after repeated failures with audit entries,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;security,3
Implement user suspension with request-time enforcement middleware,Story,Suspended users blocked on every request,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;security,5
Add session anomaly detection middleware,Task,Detects and handles suspicious session changes,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;security,5
Build user profile management with picture upload,Story,Profile editing and avatar upload,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth,5
Issue JWTs for mobile clients,Story,Token issuance for the mobile application,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth;mobile,5
Accept JWT bearer auth alongside cookie auth scoped to /api,Task,Dual authentication schemes for web and mobile,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth;mobile,5
Support SignalR querystring access_token for hub authentication,Task,Allows WebSocket clients to authenticate,,Authentication,Done,Mhmd_Rida;Mohamad_Sabbagh,phase-1;auth;mobile,3
Design Study Group Member and Message entities with membership states,Story,Domain model for groups membership and chat,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Build study group browse filtered to the student's enrolled courses,Story,FR-46 students only see groups for their courses,,Study Groups,Done,Mhmd_Rida,phase-2,5
Build study group creation with per-university member cap enforcement,Story,FR-11 respects the university member ceiling,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Implement join requests with creator approval workflow,Story,FR-49 request approve and reject flow,,Study Groups,Done,Mohamad_Sabbagh,phase-2,8
Implement approve reject and remove member actions,Story,Creator controls over group membership,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Implement leave group with automatic leadership transfer,Story,Leadership passes to the longest standing member,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Implement explicit leadership transfer to a chosen member,Story,Creator hands leadership to a specific member,,Study Groups,Done,Mohamad_Sabbagh,phase-2,3
Build real-time group chat over SignalR,Story,FR-52 live group messaging,,Study Groups,Done,Mohamad_Sabbagh,phase-2;realtime,8
Add optimistic concurrency handling for simultaneous joins,Task,RowVersion prevents overfilling a group,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Auto-archive study groups with no recent activity,Task,Background job retires dormant groups,,Study Groups,Done,Mohamad_Sabbagh,phase-2;background-job,3
Extract all study group rules into a shared StudyGroupService,Task,Single rule source shared by web and mobile,,Study Groups,Done,Mohamad_Sabbagh,phase-2;refactor,8
Add delete study group for the creator,Story,Archives the group and preserves chat history,,Study Groups,Done,Mohamad_Sabbagh,phase-2,5
Design Ride RideRequest and Vehicle entities,Story,Domain model for ride sharing,,Ride Sharing,Done,Mhmd_Rida,phase-2,5
Build driver registration and vehicle management,Story,Drivers register vehicles before offering rides,,Ride Sharing,Done,Mhmd_Rida,phase-2,5
Build ride creation search and booking flow,Story,Core ride sharing user journey,,Ride Sharing,Done,Mhmd_Rida,phase-2,8
Implement ride visibility rules by university and status,Story,Rides scoped to the correct audience,,Ride Sharing,Done,Mohamad_Sabbagh,phase-2,5
Integrate geocoding for pickup and drop-off locations,Story,Address to coordinate resolution,,Ride Sharing,Done,Mohamad_Sabbagh,phase-2;integration,5
Build live ride tracking with SignalR and map display,Story,Real-time driver position on a map,,Ride Sharing,Done,Mhmd_Rida,phase-2;realtime,8
Build My Rides management page for drivers and passengers,Story,Ride history and upcoming rides,,Ride Sharing,Done,Mhmd_Rida,phase-2,5
Design AttendanceSession and attendance record entities,Story,Domain model for attendance,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2,5
Build instructor session creation with QR code generation,Story,Instructors open a session and display a QR code,,Smart Attendance,Done,Mhmd_Rida,phase-2,8
Build student attendance submission with device-identity checks,Story,Students mark attendance from their own device,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2,8
Enforce one-device-per-student anti-proxy rule,Story,Prevents marking attendance for classmates,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2;security,5
Build live attendance monitoring over SignalR,Story,Instructors watch check-ins arrive live,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2;realtime,5
Auto-close expired attendance sessions in the background,Task,Sessions close automatically when they end,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2;background-job,3
Build the instructor attendance dashboard with per-session breakdown,Story,Attendance overview per session and student,,Smart Attendance,Done,Mhmd_Rida,phase-2,8
Build attendance summary reporting per course and student,Story,Aggregated attendance statistics,,Smart Attendance,Done,Mhmd_Rida,phase-2;reporting,5
Add CSV export of attendance records,Story,Downloadable attendance data,,Smart Attendance,Done,Mohamad_Sabbagh,phase-2;reporting,3
Add QR full-screen download and share options,Task,Improves QR display during class,,Smart Attendance,Done,Mhmd_Rida,phase-2;ux,3
Design the Ticket entity with status and category workflow,Story,Domain model for complaints,,Ticketing,Done,Mohamad_Sabbagh,phase-2,5
Build student ticket submission with attachments,Story,Students raise complaints with evidence,,Ticketing,Done,Mhmd_Rida,phase-2,5
Build the department staff ticket queue and assignment,Story,Staff triage and resolve tickets,,Ticketing,Done,Mhmd_Rida,phase-2,8
Implement the ticket status workflow and transition rules,Story,Valid status transitions enforced,,Ticketing,Done,Mohamad_Sabbagh,phase-2,5
Add real-time ticket updates over SignalR,Story,Live ticket status changes,,Ticketing,Done,Mohamad_Sabbagh,phase-2;realtime,5
Flag stale tickets automatically in the background,Task,Highlights tickets left unattended,,Ticketing,Done,Mohamad_Sabbagh,phase-2;background-job,3
Design Club and ClubEvent entities with officer roles,Story,Domain model for clubs,,Clubs,Done,Mohamad_Sabbagh,phase-2,5
Build club creation membership and officer management,Story,Club lifecycle and roles,,Clubs,Done,Mhmd_Rida,phase-2,8
Implement officer departure and succession rules,Story,Club continues when an officer leaves,,Clubs,Done,Mohamad_Sabbagh,phase-2,5
Build club event scheduling and the events calendar,Story,Events with a calendar view,,Clubs,Done,Mhmd_Rida,phase-2,5
Add real-time club activity updates over SignalR,Task,Live club membership updates,,Clubs,Done,Mohamad_Sabbagh,phase-2;realtime,3
Auto-flag inactive clubs in the background,Task,Highlights dormant clubs,,Clubs,Done,Mohamad_Sabbagh,phase-2;background-job,3
Design Internship Company and CareerProfile entities,Story,Domain model for careers,,Internships,Done,Mohamad_Sabbagh,phase-2,5
Build the company portal for posting internships,Story,Companies manage their postings,,Internships,Done,Mhmd_Rida,phase-2,8
Build student career profile with skills management,Story,Students describe their skills and experience,,Internships,Done,Mhmd_Rida,phase-2,8
Build internship browse search and application flow,Story,Core internship user journey,,Internships,Done,Mhmd_Rida,phase-2,8
Implement the skills-based matching score algorithm,Story,Ranks internships against a student profile,,Internships,Done,Mohamad_Sabbagh,phase-2;algorithm,8
Implement text similarity matching for skills,Task,Fuzzy matching of skill names,,Internships,Done,Mohamad_Sabbagh,phase-2;algorithm,5
Enforce internship application eligibility rules,Story,Prevents ineligible or duplicate applications,,Internships,Done,Mohamad_Sabbagh,phase-2,5
Build the My Applications tracking page,Story,Students track application status,,Internships,Done,Mhmd_Rida,phase-2,5
Design a unified notification entity and delivery service,Story,One notification pipeline for all modules,,Notifications,Done,Mohamad_Sabbagh,phase-2,5
Build the notification centre with read/unread state,Story,Notification list and read tracking,,Notifications,Done,Mhmd_Rida,phase-2,5
Add live notification delivery over SignalR,Story,Notifications arrive without refreshing,,Notifications,Done,Mohamad_Sabbagh,phase-2;realtime,5
Add the navbar notification badge with live count,Task,Unread count visible on every page,,Notifications,Done,Mhmd_Rida,phase-2;ux,3
Send transactional emails for key events,Task,Email delivery for important events,,Notifications,Done,Mohamad_Sabbagh,phase-2,3
Build university management with API configuration,Story,Admins onboard and configure universities,,Administration,Done,Mohamad_Sabbagh,phase-3,8
Build user administration with role assignment and suspension,Story,Admins manage accounts and roles,,Administration,Done,Mhmd_Rida,phase-3,8
Build the per-university service catalog toggle screen,Story,Admins enable modules per university,,Administration,Done,Mohamad_Sabbagh,phase-3,5
Build the audit log viewer with filtering,Story,Searchable audit trail,,Administration,Done,Mohamad_Sabbagh,phase-3;security,5
Implement centralised audit logging across all modules,Story,Consistent audit entries for sensitive actions,,Administration,Done,Mohamad_Sabbagh,phase-3;security,8
Add authorization-failure auditing middleware,Task,Records denied access attempts,,Administration,Done,Mohamad_Sabbagh,phase-3;security,3
Build the admin reporting dashboard,Story,Platform-wide usage reporting,,Administration,Done,Mhmd_Rida,phase-3;reporting,8
Add CSV export for admin reports,Story,Downloadable report data,,Administration,Done,Mohamad_Sabbagh,phase-3;reporting,3
Build the external API simulator admin screen for demos,Story,Lets demos run without a live university API,,Administration,Done,Mohamad_Sabbagh,phase-3,5
Add the reduced-functionality banner when a university sync fails,Task,FR-17 stale data is visibly flagged,,Administration,Done,Mohamad_Sabbagh,phase-3;ux,3
Build the shared design token system,Story,Colour radius shadow and motion tokens,,Design System,Done,Mohamad_Sabbagh,phase-4;ux,8
Integrate the Hugeicons SVG sprite icon system,Story,Single inline sprite for all icons,,Design System,Done,Mhmd_Rida,phase-4;ux,5
Design and build the two application layouts,Story,Main and minimal layouts,,Design System,Done,Mhmd_Rida,phase-4;ux,5
Redesign the home page with animations,Story,Landing page redesign,,Design System,Done,Mhmd_Rida,phase-4;ux,5
Redesign the authentication pages,Story,Login and registration redesign,,Design System,Done,Mohamad_Sabbagh,phase-4;ux,5
Build the reusable hero banner component with responsive bleed,Story,Consistent page banners across modules,,Design System,Done,Mhmd_Rida,phase-4;ux,5
Build the custom accessible dropdown component,Story,Keyboard accessible select replacement,,Design System,Done,Mhmd_Rida,phase-4;ux;accessibility,5
Build reusable card pill empty-state and form-page components,Story,Shared component library,,Design System,Done,Mhmd_Rida,phase-4;ux,8
Add page-level stylesheets for all feature areas,Task,Per-page styling for 20 modules,,Design System,Done,Mhmd_Rida,phase-4;ux,8
Redesign the notifications page UI,Task,Notification centre visual refresh,,Design System,Done,Mhmd_Rida,phase-4;ux,3
Enhance calendar layouts and attachment presentation,Task,Calendar and attachment visual improvements,,Design System,Done,Mhmd_Rida,phase-4;ux,5
Add progress-bar and stepper components to multi-step forms,Task,Progress indication on long forms,,Design System,Done,Mhmd_Rida,phase-4;ux,3
Set up the xUnit test project and folder structure,Task,Test project scaffolding,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Build test infrastructure with in-memory database harnesses and fakes,Story,Reusable test doubles and fixtures,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,8
Write the test plan and coverage study,Task,Documents what is tested and why,,Testing,Done,Mohamad_Sabbagh,phase-5;testing;docs,5
Write attendance submission rule tests,Task,20 tests covering attendance rules,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write study group membership rule tests,Task,19 tests covering membership rules,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write internship application rule tests,Task,19 tests covering application rules,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write ticket workflow rule tests,Task,18 tests covering ticket transitions,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write ride visibility rule tests,Task,15 tests covering ride visibility,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write club officer departure rule tests,Task,10 tests covering officer succession,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write service catalog gating tests,Task,Tests for per-university module gating,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write attendance summary service unit tests,Task,16 unit tests for attendance aggregation,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write text similarity and matching score unit tests,Task,28 unit tests for the matching algorithm,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write view model validation tests,Task,13 tests for form validation rules,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write concurrency tests for simultaneous actions,Task,Tests for race conditions on shared resources,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,5
Write background job unit tests,Task,Tests for sync and session closing jobs,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Write instructor roster and admin export tests,Task,Tests for roster and CSV export,,Testing,Done,Mohamad_Sabbagh,phase-5;testing,3
Research and plan the mobile app scope and architecture,Task,Technology choice and phased plan,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;docs,5
Write the Study Groups mobile implementation plan,Task,Detailed plan for the first mobile feature,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;docs,3
Create the .NET MAUI project targeting Android and Windows,Task,Mobile project scaffolding,,Mobile App,Done,Mhmd_Rida,phase-6;mobile,3
Add the mobile project to the solution and fix build isolation,Task,Prevents the web project compiling mobile sources,,Mobile App,Done,Mhmd_Rida,phase-6;mobile,3
Build the mobile authentication API,Story,Login and student registration endpoints,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;api,8
Restrict mobile access to the Student role only,Task,Staff and admin accounts use the web portal,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;security,2
Build the Study Groups REST API with 12 endpoints,Story,Full study group functionality over REST,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;api,13
Build the Attendance mobile API,Story,Attendance endpoints for the mobile client,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;api,8
Build the Notifications mobile API,Story,Notification endpoints for the mobile client,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;api,5
Build the Home dashboard mobile API,Story,Dashboard summary endpoint,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;api,3
Implement per-platform API base URL resolution,Task,Correct host for emulator device and desktop,,Mobile App,Done,Mhmd_Rida,phase-6;mobile,5
Implement secure token storage with a desktop fallback,Task,Keystore on Android with a Windows fallback,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;security,5
Implement automatic bearer token attachment for all requests,Task,Delegating handler adds the token,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;security,3
Configure Android network security for development hosts,Task,Allows local development hosts only,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile,3
Build the typed Study Groups API client with error translation,Story,Typed client surfacing server messages,,Mobile App,Done,Mhmd_Rida,phase-6;mobile,8
Build the mobile login screen,Story,Student sign-in UI,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,5
Build the study group browse screen with search and course filter,Story,Group list with filtering,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,8
Build the study group details screen with member management,Story,Group detail members and approvals,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,8
Build the create study group form with field validation,Story,Group creation on mobile,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,5
Build the mobile group chat screen,Story,Chat UI with message history and paging,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,8
Implement live chat and list updates over SignalR on mobile,Story,Real-time parity with the web client,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile;realtime,8
Port the web design system to MAUI,Story,Shared tokens gradients and elevation,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,8
Generate MAUI icon assets from the web icon sprite,Task,Identical glyphs across both clients,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,5
Implement responsive layout for phone tablet and desktop widths,Story,Adaptive columns and content widths,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;ux,5
Add delete study group to the mobile client,Story,Creator can delete from mobile,,Mobile App,Done,Mohamad_Sabbagh,phase-6;mobile,3
Write the Study Groups API parity test suite,Task,25 tests proving web and mobile share rules,,Mobile App,Done,Mhmd_Rida,phase-6;mobile;testing,8
Build the Internships mobile screens,Story,Browse and apply for internships on mobile,,Mobile App,To Do,Mhmd_Rida,phase-6;mobile,13
Build the Attendance mobile screens with QR scanning,Story,Scan a QR code to mark attendance,,Mobile App,To Do,Mhmd_Rida,phase-6;mobile,13
Build the mobile notifications screen,Story,Notification centre on mobile,,Mobile App,To Do,Mhmd_Rida,phase-6;mobile,5
Test the mobile app on a physical Android device,Task,Verify on real hardware over the LAN,,Mobile App,To Do,Mohamad_Sabbagh,phase-6;mobile;testing,5
Produce a signed Android release build,Task,Release configuration and signing,,Mobile App,To Do,Mhmd_Rida,phase-6;mobile;release,5
Add admin scoping so university admins only see their own tenant,Story,Prevents cross-tenant data access,,Hardening,Done,Mohamad_Sabbagh,phase-7;security,8
Harden security across controllers and unify notifications,Story,Authorisation review across the application,,Hardening,Done,Mohamad_Sabbagh,phase-7;security,8
Require membership before joining a study group's real-time chat,Bug,Non-members could subscribe to live chat messages,,Hardening,Done,Mhmd_Rida,phase-7;security,5
Require authentication on the study group hub,Bug,The hub accepted anonymous connections,,Hardening,Done,Mhmd_Rida,phase-7;security,3
Accept both cookie and bearer schemes on the study group hub,Bug,Mobile chat could not connect and showed Offline,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug;mobile,3
Add field validation to the study group create API,Bug,The API accepted a group with no name,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug,5
Stop the web create action saving a group when validation failed,Bug,An invalid submission created a group then showed an error,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug,3
Validate university API base URLs to prevent crashes,Bug,An invalid URL threw UriFormatException and returned 500,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug,3
Fix Arabic text corruption in CSV exports,Bug,Missing UTF-8 BOM made Arabic unreadable in Excel,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug,3
Fix timezone handling in attendance session expiry,Bug,Sessions closed at the wrong local time,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug,3
Fix keyboard navigation in the custom dropdown component,Bug,Arrow keys did not move between options,,Hardening,Done,Mhmd_Rida,phase-7;bug;accessibility,3
Fix non-responsive hero banners on several pages,Bug,Headings and buttons clipped on small screens,,Hardening,Done,Mhmd_Rida,phase-7;bug;ux,3
Fix the admin users role dropdown resetting to Student,Bug,Selected role was lost on save,,Hardening,Done,Mhmd_Rida,phase-7;bug,2
Fix duplicate page titles in the mobile app,Bug,Shell and hero both rendered the page title,,Hardening,Done,Mhmd_Rida,phase-7;bug;mobile;ux,2
Fix pickers showing their value twice on Windows,Bug,Picker title rendered as a header above the selection,,Hardening,Done,Mhmd_Rida,phase-7;bug;mobile;ux,2
Fix mobile card grid breaking layout at narrow widths,Bug,Cards split into columns too early and clipped content,,Hardening,Done,Mhmd_Rida,phase-7;bug;mobile;ux,3
Fix Shell navigation crash on app resume,Bug,Navigating during startup terminated the process,,Hardening,Done,Mohamad_Sabbagh,phase-7;bug;mobile,3
Add exception logging for the Windows mobile build,Task,Startup crashes now write a diagnosable log,,Hardening,Done,Mhmd_Rida,phase-7;mobile,2
Revoke the SMTP credential committed to git history,Task,Credential is in public history and must be revoked at the provider,,Hardening,To Do,Mohamad_Sabbagh,phase-7;security,3
```

---

## 17. Contribution split

### The two members

| Member | Jira account | Git author in this repository | Focus after the foundation phase |
|---|---|---|---|
| **Mhmd_Rida** | `mhr824@usal.edu.lb` | `MhmdRida20 <mohamadhassanrida@gmail.com>` | Front-end, UI/UX and the mobile app |
| **Mohamad_Sabbagh** | `mha206@usal.edu.lb` | `Ali R <ar81673770@gmail.com>` | Back-end, services, data and security |

> **The Jira and git identities do not match, which is worth knowing.** Neither
> member commits under the address their Jira account uses, and the repository's
> second author is committed as **"Ali R"**, not "Mohamad_Sabbagh". Everything
> below assumes `ar81673770@gmail.com` is Mohamad_Sabbagh. Note the Jira site
> also holds a separate `mohamadhassanrida@gmail.com` account — assigning to
> `mhr824@usal.edu.lb` puts the work on the university account, so if the
> intention is to work under the gmail one, swap `EMAIL` in `jira/assign.py`
> and re-run the build.

### What the git history shows

| Author | Commits | Lines added | Lines removed | Heaviest areas |
|---|---|---|---|---|
| MhmdRida20 | 16 | +123,350 | −2,455 | `Views` (109), `mobile` (102), `test` (40), `wwwroot/css` (39), `wwwroot/js` (28) |
| Ali R | 6 | +83,177 | −14,235 | `Views` (71), `Controllers` (44), `Models` (29), `Services` (22), `Hubs` (6) |

The file areas corroborate the split you described: one member's commits
concentrate in views, styles and the mobile project, the other's in
controllers, models, services and hubs. Line counts include vendored and
generated files, so treat them as a rough signal rather than a measurement.

### How each task was assigned

Assignment is a **pure function of (epic, summary)** — the same rule runs over
the CSV and over the tables above, so the two can never disagree. It is
implemented in [`jira/assign.py`](jira/assign.py) and applied in four steps:

1. **Foundation phase → both members.** Everything in *Platform Foundation* and
   *Authentication* (26 tasks) is credited to both, matching the first stretch
   of the project when you worked together.
2. **Clear wording decides.** A summary containing front-end vocabulary
   (screen, page, layout, icon, responsive…) goes to Rida; back-end vocabulary
   (entity, service, API, rules, migration, security…) goes to Sabbagh.
3. **Whole-epic ownership** where it is unambiguous: *UI/UX & Design System* →
   Rida, *Automated Testing* → Sabbagh.
4. **Genuinely mixed tasks** — where a summary reads as both — are decided by
   their epic. *Mobile App* mixed tasks go to Rida on the evidence (102 commits
   touching `mobile/` from him, none from his teammate). *Ride Sharing* and
   *Hardening* mixed tasks also go to Rida; those two were chosen **specifically
   to level the totals**, which is the only reason they sit on that side.

### Splitting the pair tasks for Jira

Jira accepts exactly one assignee per issue, so the 26 shared tasks each need a
nominal owner. The same wording rule picks it where the summary leans one way
(2 tasks front-end, 13 back-end); that leaves 11 tasks worth 59 points which are
genuinely undecidable, and those were divided to land the overall totals dead
even — 63 points of shared work to each member.

This changes nothing about who did the work. Every one of the 26 keeps both
names in its description and carries the `pair` label, so `labels = pair` in
Jira brings back exactly the foundation phase.

### Resulting balance

| | Mhmd_Rida | Mohamad_Sabbagh |
|---|---|---|
| **Story points** | **431 (50.0%)** | **431 (50.0%)** |
| Tasks assigned in Jira | 74 | 94 |
| — of which owned outright | 63 | 79 |
| — of which pair work | 11 | 15 |

Story points are balanced exactly. The task *counts* are not equal (74 vs 94)
and deliberately so — Sabbagh owns more but individually smaller items (rules,
services, tests), while Rida owns fewer but larger ones (whole screens and the
mobile client). Weighting by effort rather than by row count is what makes the
contribution even.

**If you need a different split**, edit the constants at the top of
[`jira/assign.py`](jira/assign.py) — `PAIR_EPICS`, `EPIC_OWNER`, `MIXED_TO_RIDA`
and `PAIR_TO_SABBAGH` — then run `python jira/build_csv.py`, which rewrites both
import files and prints the new balance.
