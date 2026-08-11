# UniConnect — Jira Backlog

A complete task breakdown of the UniConnect project, derived from the actual
codebase (21 commits, 20 web controllers, 5 mobile API controllers, 19 services,
6 SignalR hubs, 27 entities, 271 automated tests).

Every task sits on **one line** in the CSV block at the end so it can be pasted
straight into Jira. The tables above it carry the same tasks with more context
for reading and for filling in assignees.

---

## How to load this into Jira

**Option A — CSV import (recommended, creates everything at once)**

1. Copy the whole CSV block in [§16 Full CSV](#16-full-csv-for-jira-import) into a file named `uniconnect-backlog.csv`.
2. Jira → **Settings (⚙) → System → External System Import → CSV**.
3. Upload the file, map `Summary`, `Issue Type`, `Description`, `Epic Name`, `Epic Link`, `Status`, `Labels`, `Story Points`.
4. Import into your UniConnect project.

**Option B — bulk create from a list**

Copy any single column of summaries from the tables below into Jira's
**Create → Bulk create** text box; each line becomes one issue.

**Before importing**

- Fill the `Assignee` column — it is intentionally left blank so you and your
  teammate can split the work as it actually happened.
- The **Epics must be created first** if you use Option B. With CSV import
  (Option A) the `Epic Name` rows create them automatically.
- Story points are rough relative estimates, not measured time.

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

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Set up ASP.NET Core 8 MVC solution with EF Core and SQL Server | Task | Done | 3 | `UniConnect.csproj`, `Program.cs` |
| Design and implement the relational domain model (27 entities) | Story | Done | 8 | `Models/` |
| Configure EF Core mappings, composite keys and delete behaviours | Task | Done | 5 | `ApplicationDbContext.OnModelCreating` |
| Build the multi-university adapter core for multi-tenancy | Story | Done | 8 | `Adapters/IUniversityProvider.cs` |
| Implement `UniversityProviderResolver` to select a provider per university | Task | Done | 3 | `Adapters/UniversityProviderResolver.cs` |
| Implement `RealApiUniversityProvider` for standard university APIs | Task | Done | 5 | `Adapters/RealApiUniversityProvider.cs` |
| Implement `UmsApiUniversityProvider` for the alternative UMS API style | Task | Done | 5 | `Adapters/UmsApiUniversityProvider.cs` |
| Build the simulated external university API for development and demos | Story | Done | 8 | `Controllers/ExternalApi/ExternalUniversityApiController.cs` |
| Implement the service catalog so each university enables its own modules | Story | Done | 5 | `Services/ServiceCatalogService.cs` |
| Add `RequireService` filter to gate disabled modules per university | Task | Done | 3 | `Rules/RequireServiceFilterTests.cs` |
| Build background university API sync (courses, students, staff, enrolments) | Story | Done | 8 | `Services/UniversityApiSyncRunner.cs` |
| Add enrolment revalidation background job | Task | Done | 5 | `Services/EnrollmentRevalidationRunner.cs` |
| Implement database seeding with roles, universities and demo accounts | Task | Done | 5 | `Data/DbSeeder.cs` |
| Write the project overview and technical reference document | Task | Done | 3 | `PROJECT_OVERVIEW.md` |

## 2. EP-2 — Authentication, Roles & Account Management (P1)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Integrate ASP.NET Core Identity with a custom `ApplicationUser` | Story | Done | 5 | `Models/ApplicationUser.cs` |
| Define the six-role model (Student, Instructor, Admin, UniversityAdmin, DepartmentStaff, Company) | Task | Done | 3 | `Data/DbSeeder.cs` |
| Build student self-registration validated against the university record | Story | Done | 5 | `Areas/Identity/Pages/Account/Register.cshtml.cs` |
| Build instructor registration with staff-record verification | Story | Done | 5 | `Areas/Identity/Pages/Account/RegisterInstructor.cshtml.cs` |
| Implement email confirmation with SMTP delivery | Task | Done | 3 | `Services/SmtpEmailSender.cs` |
| Add account lockout and failed-login auditing | Task | Done | 3 | `Controllers/Api/AuthApiController.cs` |
| Implement user suspension with request-time enforcement middleware | Story | Done | 5 | `Middleware/SuspendedUserMiddleware.cs` |
| Add session anomaly detection middleware | Task | Done | 5 | `Middleware/SessionAnomalyMiddleware.cs` |
| Build user profile management with picture upload | Story | Done | 5 | `Controllers/ProfileController.cs` |
| Issue JWTs for mobile clients | Story | Done | 5 | `Services/JwtTokenService.cs` |
| Accept JWT bearer auth alongside cookie auth, scoped to `/api` | Task | Done | 5 | `Program.cs` |
| Support SignalR querystring `access_token` for hub authentication | Task | Done | 3 | `Program.cs` JwtBearerEvents |

## 3. EP-3 — Study Groups (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design Study Group, Member and Message entities with membership states | Story | Done | 5 | `Models/StudyGroup.cs` |
| Build study group browse filtered to the student's enrolled courses | Story | Done | 5 | FR-46 |
| Build study group creation with per-university member cap enforcement | Story | Done | 5 | FR-11 |
| Implement join requests with creator approval workflow | Story | Done | 8 | FR-49 |
| Implement approve, reject and remove member actions | Story | Done | 5 | `StudyGroupService` |
| Implement leave group with automatic leadership transfer | Story | Done | 5 | `StudyGroupService.LeaveAsync` |
| Implement explicit leadership transfer to a chosen member | Story | Done | 3 | `StudyGroupService.TransferLeadershipAsync` |
| Build real-time group chat over SignalR | Story | Done | 8 | FR-52, `Hubs/StudyGroupHub.cs` |
| Add optimistic concurrency handling for simultaneous joins | Task | Done | 5 | `RowVersion`, `SimultaneousActionTests` |
| Auto-archive study groups with no recent activity | Task | Done | 3 | `Services/InactiveStudyGroupService.cs` |
| Extract all study group rules into a shared `StudyGroupService` | Task | Done | 8 | `Services/StudyGroupService.cs` |
| Add delete study group (creator only, archives and preserves history) | Story | Done | 5 | `StudyGroupService.DeleteAsync` |

## 4. EP-4 — Ride Sharing (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design Ride, RideRequest and Vehicle entities | Story | Done | 5 | `Models/Ride.cs` |
| Build driver registration and vehicle management | Story | Done | 5 | `Controllers/VehiclesController.cs` |
| Build ride creation, search and booking flow | Story | Done | 8 | `Controllers/RidesController.cs` |
| Implement ride visibility rules by university and status | Story | Done | 5 | `Rules/RideVisibilityTests.cs` |
| Integrate geocoding for pickup and drop-off locations | Story | Done | 5 | `Services/GeocodingService.cs` |
| Build live ride tracking with SignalR and map display | Story | Done | 8 | `Hubs/RideTrackingHub.cs` |
| Build "My Rides" management page for drivers and passengers | Story | Done | 5 | `Views/Rides/` |

## 5. EP-5 — Smart Attendance (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design AttendanceSession and attendance record entities | Story | Done | 5 | `Models/AttendanceSession.cs` |
| Build instructor session creation with QR code generation | Story | Done | 8 | `Controllers/InstructorAttendanceController.cs` |
| Build student attendance submission with device-identity checks | Story | Done | 8 | `Services/AttendanceSubmissionService.cs` |
| Enforce one-device-per-student anti-proxy rule | Story | Done | 5 | `Rules/AttendanceSubmissionTests.cs` |
| Build live attendance monitoring over SignalR | Story | Done | 5 | `Hubs/AttendanceHub.cs` |
| Auto-close expired attendance sessions in the background | Task | Done | 3 | `Services/CloseExpiredAttendanceSessionsService.cs` |
| Build the instructor attendance dashboard with per-session breakdown | Story | Done | 8 | `Views/InstructorAttendance/Details.cshtml` |
| Build attendance summary reporting per course and student | Story | Done | 5 | `Services/AttendanceSummaryService.cs` |
| Add CSV export of attendance records | Story | Done | 3 | `Controllers/InstructorAttendanceController.cs` |
| Add QR full-screen, download and share options | Task | Done | 3 | commit `b3466e7` |

## 6. EP-6 — Complaints & Ticketing (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design the Ticket entity with status and category workflow | Story | Done | 5 | `Models/Ticket.cs` |
| Build student ticket submission with attachments | Story | Done | 5 | `Controllers/TicketsController.cs` |
| Build the department staff ticket queue and assignment | Story | Done | 8 | `Controllers/StaffTicketsController.cs` |
| Implement the ticket status workflow and transition rules | Story | Done | 5 | `Rules/TicketWorkflowTests.cs` |
| Add real-time ticket updates over SignalR | Story | Done | 5 | `Hubs/TicketHub.cs` |
| Flag stale tickets automatically in the background | Task | Done | 3 | `Services/TicketStalenessService.cs` |

## 7. EP-7 — Clubs & Organizations (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design Club and ClubEvent entities with officer roles | Story | Done | 5 | `Models/Club.cs` |
| Build club creation, membership and officer management | Story | Done | 8 | `Controllers/ClubsController.cs` |
| Implement officer departure and succession rules | Story | Done | 5 | `Rules/ClubOfficerDepartureTests.cs` |
| Build club event scheduling and the events calendar | Story | Done | 5 | `Views/Clubs/` |
| Add real-time club activity updates over SignalR | Task | Done | 3 | `Hubs/ClubHub.cs` |
| Auto-flag inactive clubs in the background | Task | Done | 3 | `Services/InactiveClubService.cs` |

## 8. EP-8 — Internships & Career Matching (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design Internship, Company and CareerProfile entities | Story | Done | 5 | `Models/Internship.cs` |
| Build the company portal for posting internships | Story | Done | 8 | `Controllers/CompanyController.cs` |
| Build student career profile with skills management | Story | Done | 8 | `Controllers/CareerProfileController.cs` |
| Build internship browse, search and application flow | Story | Done | 8 | `Controllers/InternshipsController.cs` |
| Implement the skills-based matching score algorithm | Story | Done | 8 | `Services/MatchingScoreService.cs` |
| Implement text similarity matching for skills | Task | Done | 5 | `Services/TextSimilarity.cs` |
| Enforce internship application eligibility rules | Story | Done | 5 | `Rules/InternshipApplicationTests.cs` |
| Build the "My Applications" tracking page | Story | Done | 5 | `Views/Internships/MyApplications.cshtml` |

## 9. EP-9 — Notifications & Real-Time Infrastructure (P2)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Design a unified notification entity and delivery service | Story | Done | 5 | `Services/NotificationService.cs` |
| Build the notification centre with read/unread state | Story | Done | 5 | `Controllers/NotificationsController.cs` |
| Add live notification delivery over SignalR | Story | Done | 5 | `Hubs/NotificationHub.cs` |
| Add the navbar notification badge with live count | Task | Done | 3 | `Views/_NavbarSnippet.cshtml` |
| Send transactional emails for key events | Task | Done | 3 | `Services/SmtpEmailSender.cs` |

## 10. EP-10 — Administration, Reporting & Audit (P3)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Build university management (create, configure, API settings) | Story | Done | 8 | `Controllers/AdminUniversitiesController.cs` |
| Build user administration with role assignment and suspension | Story | Done | 8 | `Controllers/AdminUsersController.cs` |
| Build the per-university service catalog toggle screen | Story | Done | 5 | `Controllers/AdminUniversitiesController.cs` |
| Build the audit log viewer with filtering | Story | Done | 5 | `Controllers/AdminAuditLogController.cs` |
| Implement centralised audit logging across all modules | Story | Done | 8 | `Services/AuditLogService.cs` |
| Add authorization-failure auditing middleware | Task | Done | 3 | `Middleware/AuditingAuthorizationMiddlewareResultHandler.cs` |
| Build the admin reporting dashboard | Story | Done | 8 | `Controllers/AdminReportsController.cs` |
| Add CSV export for admin reports | Story | Done | 3 | `Rules/AdminReportExportTests.cs` |
| Build the external API simulator admin screen for demos | Story | Done | 5 | `Controllers/AdminExternalApiSimulatorController.cs` |
| Add the reduced-functionality banner when a university sync fails | Task | Done | 3 | FR-17 |

## 11. EP-11 — UI/UX & Design System (P4)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Build the shared design token system (colour, radius, shadow, motion) | Story | Done | 8 | `wwwroot/css/site.css` |
| Integrate the Hugeicons SVG sprite icon system | Story | Done | 5 | `Views/Shared/_Icons.cshtml` |
| Design and build the two application layouts (main and minimal) | Story | Done | 5 | `Views/Shared/` |
| Redesign the home page with animations | Story | Done | 5 | commit `6f34f73` |
| Redesign the authentication pages (login, register) | Story | Done | 5 | commit `1d728bb` |
| Build the reusable hero banner component with responsive bleed | Story | Done | 5 | `.uc-hero` in `site.css` |
| Build the custom accessible dropdown component | Story | Done | 5 | `wwwroot/js/uc-select.js` |
| Build reusable card, pill, empty-state and form-page components | Story | Done | 8 | `wwwroot/css/pages/` |
| Add page-level stylesheets for all 20 feature areas | Task | Done | 8 | `wwwroot/css/pages/` |
| Redesign the notifications page UI | Task | Done | 3 | commit `ff2fd04` |
| Enhance calendar layouts and attachment presentation | Task | Done | 5 | commit `d8b2d41` |
| Add progress-bar and stepper components to multi-step forms | Task | Done | 3 | commit `4974a52` |

## 12. EP-12 — Automated Testing (P5)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Set up the xUnit test project and folder structure | Task | Done | 3 | `test/UniConnect.Tests/` |
| Build test infrastructure (in-memory DB, harnesses, fakes, stubs) | Story | Done | 8 | `test/UniConnect.Tests/Infrastructure/` |
| Write the test plan and coverage study | Task | Done | 5 | `test/TEST_PLAN.md` |
| Write attendance submission rule tests (20 tests) | Task | Done | 5 | `Rules/AttendanceSubmissionTests.cs` |
| Write study group membership rule tests (19 tests) | Task | Done | 5 | `Rules/StudyGroupMembershipTests.cs` |
| Write internship application rule tests (19 tests) | Task | Done | 5 | `Rules/InternshipApplicationTests.cs` |
| Write ticket workflow rule tests (18 tests) | Task | Done | 5 | `Rules/TicketWorkflowTests.cs` |
| Write ride visibility rule tests (15 tests) | Task | Done | 3 | `Rules/RideVisibilityTests.cs` |
| Write club officer departure rule tests (10 tests) | Task | Done | 3 | `Rules/ClubOfficerDepartureTests.cs` |
| Write service catalog gating tests | Task | Done | 3 | `Rules/RequireServiceFilterTests.cs` |
| Write attendance summary service unit tests (16 tests) | Task | Done | 3 | `Unit/AttendanceSummaryServiceTests.cs` |
| Write text similarity and matching score unit tests (28 tests) | Task | Done | 5 | `Unit/TextSimilarityTests.cs` |
| Write view model validation tests (13 tests) | Task | Done | 3 | `Unit/ViewModelValidationTests.cs` |
| Write concurrency tests for simultaneous actions | Task | Done | 5 | `Concurrency/SimultaneousActionTests.cs` |
| Write background job unit tests (sync runner, session closing) | Task | Done | 3 | `Unit/UniversityApiSyncRunnerTests.cs` |
| Write instructor roster and admin export tests | Task | Done | 3 | `Rules/InstructorRosterTests.cs` |

## 13. EP-13 — Mobile Application (.NET MAUI) (P6)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Research and plan the mobile app scope and architecture | Task | Done | 5 | `MOBILE_APP_PLAN.md` |
| Write the Study Groups mobile implementation plan | Task | Done | 3 | `MOBILE_STUDYGROUPS_PLAN.md` |
| Create the .NET MAUI project targeting Android and Windows | Task | Done | 3 | `mobile/UniConnect.Mobile/` |
| Add the mobile project to the solution and fix build isolation | Task | Done | 3 | `UniConnect.csproj` compile excludes |
| Build the mobile authentication API (login, student registration) | Story | Done | 8 | `Controllers/Api/AuthApiController.cs` |
| Restrict mobile access to the Student role only | Task | Done | 2 | `AuthApiController.MobileAllowedRoles` |
| Build the Study Groups REST API (12 endpoints) | Story | Done | 13 | `Controllers/Api/StudyGroupsApiController.cs` |
| Build the Attendance mobile API | Story | Done | 8 | `Controllers/Api/AttendanceApiController.cs` |
| Build the Notifications mobile API | Story | Done | 5 | `Controllers/Api/NotificationsApiController.cs` |
| Build the Home/dashboard mobile API | Story | Done | 3 | `Controllers/Api/HomeApiController.cs` |
| Implement per-platform API base URL resolution | Task | Done | 5 | `Services/ApiConfig.cs` |
| Implement secure token storage with a desktop fallback | Task | Done | 5 | `Services/SessionStore.cs` |
| Implement automatic bearer token attachment for all requests | Task | Done | 3 | `Services/AuthHeaderHandler.cs` |
| Configure Android network security for development hosts | Task | Done | 3 | `network_security_config.xml` |
| Build the typed Study Groups API client with error translation | Story | Done | 8 | `Services/StudyGroupsApi.cs` |
| Build the mobile login screen | Story | Done | 5 | `Pages/LoginPage.xaml` |
| Build the study group browse screen with search and course filter | Story | Done | 8 | `Pages/GroupsPage.xaml` |
| Build the study group details screen with member management | Story | Done | 8 | `Pages/GroupDetailsPage.xaml` |
| Build the create study group form with field validation | Story | Done | 5 | `Pages/CreateGroupPage.xaml` |
| Build the mobile group chat screen | Story | Done | 8 | `Pages/ChatPage.xaml` |
| Implement live chat and list updates over SignalR on mobile | Story | Done | 8 | `Services/StudyGroupHubClient.cs` |
| Port the web design system to MAUI (tokens, gradients, elevation) | Story | Done | 8 | `Resources/Styles/UniConnect.xaml` |
| Generate MAUI icon assets from the web icon sprite | Task | Done | 5 | `Resources/Images/ic_*.svg` |
| Implement responsive layout for phone, tablet and desktop widths | Story | Done | 5 | `Services/Responsive.cs` |
| Add delete study group to the mobile client | Story | Done | 3 | `Pages/GroupDetailsPage.xaml.cs` |
| Write the Study Groups API parity test suite (25 tests) | Task | Done | 8 | `Rules/StudyGroupApiParityTests.cs` |
| Build the Internships mobile screens | Story | **To Do** | 13 | `MOBILE_APP_PLAN.md` |
| Build the Attendance mobile screens with QR scanning | Story | **To Do** | 13 | `MOBILE_APP_PLAN.md` |
| Build the mobile notifications screen | Story | **To Do** | 5 | `MOBILE_APP_PLAN.md` |
| Test the mobile app on a physical Android device | Task | **To Do** | 5 | — |
| Produce a signed Android release build | Task | **To Do** | 5 | — |

## 14. EP-14 — Security Hardening & Defect Fixes (P7)

| Summary | Type | Status | Points | Evidence |
|---|---|---|---|---|
| Add admin scoping so university admins only see their own tenant | Story | Done | 8 | commit `64234bb` |
| Harden security across controllers and add unified notifications | Story | Done | 8 | commit `64234bb` |
| Require membership before joining a study group's real-time chat | Bug | Done | 5 | `Hubs/StudyGroupHub.cs` |
| Require authentication on the study group hub | Bug | Done | 3 | `Hubs/StudyGroupHub.cs` |
| Accept both cookie and bearer schemes on the study group hub | Bug | Done | 3 | Mobile chat showed "Offline" |
| Add field validation to the study group create API | Bug | Done | 5 | `StudyGroupService.CreateAsync` |
| Stop the web create action from saving a group when validation failed | Bug | Done | 3 | `StudyGroupsController.Create` |
| Validate university API base URLs to prevent `UriFormatException` crashes | Bug | Done | 3 | `Services/UniversityApiSyncRunner.cs` |
| Fix Arabic text corruption in CSV exports (UTF-8 BOM) | Bug | Done | 3 | `AdminReportsController` |
| Fix timezone handling in attendance session expiry | Bug | Done | 3 | commit `799f414` |
| Fix keyboard navigation in the custom dropdown component | Bug | Done | 3 | `wwwroot/js/uc-select.js` |
| Fix non-responsive hero banners on several pages | Bug | Done | 3 | `.uc-hero` in `site.css` |
| Fix the admin users role dropdown resetting to Student | Bug | Done | 2 | commit `ff2fd04` |
| Fix duplicate page titles in the mobile app | Bug | Done | 2 | `Shell.NavBarIsVisible` |
| Fix pickers showing their value twice on Windows | Bug | Done | 2 | `Pages/GroupsPage.xaml` |
| Fix mobile card grid breaking layout at narrow widths | Bug | Done | 3 | `Services/Responsive.cs` |
| Fix Shell navigation crash on app resume | Bug | Done | 3 | `Pages/LoginPage.xaml.cs` |
| Rotate the SMTP credential committed to git history | Task | **To Do** | 3 | Security — see note below |
| Add exception logging for the Windows mobile build | Task | Done | 2 | `Platforms/Windows/App.xaml.cs` |

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
Summary,Issue Type,Description,Epic Name,Epic Link,Status,Labels,Story Points
Platform Foundation & Multi-University Architecture,Epic,Solution setup multi-tenancy database and external API integration,Platform Foundation,,Done,phase-1,
Authentication Roles & Account Management,Epic,Identity roles registration and JWT issuance,Authentication,,Done,phase-1,
Study Groups,Epic,Course study groups with membership and real-time chat,Study Groups,,Done,phase-2,
Ride Sharing,Epic,Student ride sharing with live tracking,Ride Sharing,,Done,phase-2,
Smart Attendance,Epic,QR based attendance with anti-proxy checks,Smart Attendance,,Done,phase-2,
Complaints & Ticketing,Epic,Student complaints and staff ticket workflow,Ticketing,,Done,phase-2,
Clubs & Organizations,Epic,Student clubs events and officer management,Clubs,,Done,phase-2,
Internships & Career Matching,Epic,Internship postings and skills based matching,Internships,,Done,phase-2,
Notifications & Real-Time Infrastructure,Epic,Unified notifications and SignalR hubs,Notifications,,Done,phase-2,
Administration Reporting & Audit,Epic,Admin tooling reporting and audit logging,Administration,,Done,phase-3,
UI/UX & Design System,Epic,Design tokens components and page layouts,Design System,,Done,phase-4,
Automated Testing,Epic,xUnit test suite and test infrastructure,Testing,,Done,phase-5,
Mobile Application,Epic,.NET MAUI student mobile client,Mobile App,,In Progress,phase-6,
Security Hardening & Defect Fixes,Epic,Security fixes and defect resolution,Hardening,,In Progress,phase-7,
Set up ASP.NET Core 8 MVC solution with EF Core and SQL Server,Task,Project scaffolding dependency injection and database context,,Platform Foundation,Done,phase-1;setup,3
Design and implement the relational domain model,Story,27 entities covering all feature areas,,Platform Foundation,Done,phase-1;database,8
Configure EF Core mappings composite keys and delete behaviours,Task,OnModelCreating configuration for constraints and cascade rules,,Platform Foundation,Done,phase-1;database,5
Build the multi-university adapter core for multi-tenancy,Story,Provider abstraction allowing each university its own API integration,,Platform Foundation,Done,phase-1;architecture,8
Implement UniversityProviderResolver to select a provider per university,Task,Resolves the correct adapter from the university code,,Platform Foundation,Done,phase-1;architecture,3
Implement RealApiUniversityProvider for standard university APIs,Task,Adapter for the standard REST API shape,,Platform Foundation,Done,phase-1;integration,5
Implement UmsApiUniversityProvider for the alternative UMS API style,Task,Adapter for the alternative API shape,,Platform Foundation,Done,phase-1;integration,5
Build the simulated external university API for development and demos,Story,Local stand-in for a real university system,,Platform Foundation,Done,phase-1;integration,8
Implement the service catalog so each university enables its own modules,Story,Per-university feature enablement,,Platform Foundation,Done,phase-1;architecture,5
Add RequireService filter to gate disabled modules per university,Task,Action filter blocking access to disabled services,,Platform Foundation,Done,phase-1;architecture,3
Build background university API sync for courses students staff and enrolments,Story,Scheduled synchronisation from the university API,,Platform Foundation,Done,phase-1;background-job,8
Add enrolment revalidation background job,Task,Periodically revalidates student enrolments,,Platform Foundation,Done,phase-1;background-job,5
Implement database seeding with roles universities and demo accounts,Task,Seeds a working environment on first run,,Platform Foundation,Done,phase-1;setup,5
Write the project overview and technical reference document,Task,Architecture and module reference documentation,,Platform Foundation,Done,phase-1;docs,3
Integrate ASP.NET Core Identity with a custom ApplicationUser,Story,Identity with university scoped user fields,,Authentication,Done,phase-1;auth,5
Define the six-role model,Task,Student Instructor Admin UniversityAdmin DepartmentStaff Company,,Authentication,Done,phase-1;auth,3
Build student self-registration validated against the university record,Story,Registration verified against synced student records,,Authentication,Done,phase-1;auth,5
Build instructor registration with staff-record verification,Story,Registration verified against synced staff records,,Authentication,Done,phase-1;auth,5
Implement email confirmation with SMTP delivery,Task,Confirmation links delivered by email,,Authentication,Done,phase-1;auth,3
Add account lockout and failed-login auditing,Task,Lockout after repeated failures with audit entries,,Authentication,Done,phase-1;security,3
Implement user suspension with request-time enforcement middleware,Story,Suspended users blocked on every request,,Authentication,Done,phase-1;security,5
Add session anomaly detection middleware,Task,Detects and handles suspicious session changes,,Authentication,Done,phase-1;security,5
Build user profile management with picture upload,Story,Profile editing and avatar upload,,Authentication,Done,phase-1;auth,5
Issue JWTs for mobile clients,Story,Token issuance for the mobile application,,Authentication,Done,phase-1;auth;mobile,5
Accept JWT bearer auth alongside cookie auth scoped to /api,Task,Dual authentication schemes for web and mobile,,Authentication,Done,phase-1;auth;mobile,5
Support SignalR querystring access_token for hub authentication,Task,Allows WebSocket clients to authenticate,,Authentication,Done,phase-1;auth;mobile,3
Design Study Group Member and Message entities with membership states,Story,Domain model for groups membership and chat,,Study Groups,Done,phase-2,5
Build study group browse filtered to the student's enrolled courses,Story,FR-46 students only see groups for their courses,,Study Groups,Done,phase-2,5
Build study group creation with per-university member cap enforcement,Story,FR-11 respects the university member ceiling,,Study Groups,Done,phase-2,5
Implement join requests with creator approval workflow,Story,FR-49 request approve and reject flow,,Study Groups,Done,phase-2,8
Implement approve reject and remove member actions,Story,Creator controls over group membership,,Study Groups,Done,phase-2,5
Implement leave group with automatic leadership transfer,Story,Leadership passes to the longest standing member,,Study Groups,Done,phase-2,5
Implement explicit leadership transfer to a chosen member,Story,Creator hands leadership to a specific member,,Study Groups,Done,phase-2,3
Build real-time group chat over SignalR,Story,FR-52 live group messaging,,Study Groups,Done,phase-2;realtime,8
Add optimistic concurrency handling for simultaneous joins,Task,RowVersion prevents overfilling a group,,Study Groups,Done,phase-2,5
Auto-archive study groups with no recent activity,Task,Background job retires dormant groups,,Study Groups,Done,phase-2;background-job,3
Extract all study group rules into a shared StudyGroupService,Task,Single rule source shared by web and mobile,,Study Groups,Done,phase-2;refactor,8
Add delete study group for the creator,Story,Archives the group and preserves chat history,,Study Groups,Done,phase-2,5
Design Ride RideRequest and Vehicle entities,Story,Domain model for ride sharing,,Ride Sharing,Done,phase-2,5
Build driver registration and vehicle management,Story,Drivers register vehicles before offering rides,,Ride Sharing,Done,phase-2,5
Build ride creation search and booking flow,Story,Core ride sharing user journey,,Ride Sharing,Done,phase-2,8
Implement ride visibility rules by university and status,Story,Rides scoped to the correct audience,,Ride Sharing,Done,phase-2,5
Integrate geocoding for pickup and drop-off locations,Story,Address to coordinate resolution,,Ride Sharing,Done,phase-2;integration,5
Build live ride tracking with SignalR and map display,Story,Real-time driver position on a map,,Ride Sharing,Done,phase-2;realtime,8
Build My Rides management page for drivers and passengers,Story,Ride history and upcoming rides,,Ride Sharing,Done,phase-2,5
Design AttendanceSession and attendance record entities,Story,Domain model for attendance,,Smart Attendance,Done,phase-2,5
Build instructor session creation with QR code generation,Story,Instructors open a session and display a QR code,,Smart Attendance,Done,phase-2,8
Build student attendance submission with device-identity checks,Story,Students mark attendance from their own device,,Smart Attendance,Done,phase-2,8
Enforce one-device-per-student anti-proxy rule,Story,Prevents marking attendance for classmates,,Smart Attendance,Done,phase-2;security,5
Build live attendance monitoring over SignalR,Story,Instructors watch check-ins arrive live,,Smart Attendance,Done,phase-2;realtime,5
Auto-close expired attendance sessions in the background,Task,Sessions close automatically when they end,,Smart Attendance,Done,phase-2;background-job,3
Build the instructor attendance dashboard with per-session breakdown,Story,Attendance overview per session and student,,Smart Attendance,Done,phase-2,8
Build attendance summary reporting per course and student,Story,Aggregated attendance statistics,,Smart Attendance,Done,phase-2;reporting,5
Add CSV export of attendance records,Story,Downloadable attendance data,,Smart Attendance,Done,phase-2;reporting,3
Add QR full-screen download and share options,Task,Improves QR display during class,,Smart Attendance,Done,phase-2;ux,3
Design the Ticket entity with status and category workflow,Story,Domain model for complaints,,Ticketing,Done,phase-2,5
Build student ticket submission with attachments,Story,Students raise complaints with evidence,,Ticketing,Done,phase-2,5
Build the department staff ticket queue and assignment,Story,Staff triage and resolve tickets,,Ticketing,Done,phase-2,8
Implement the ticket status workflow and transition rules,Story,Valid status transitions enforced,,Ticketing,Done,phase-2,5
Add real-time ticket updates over SignalR,Story,Live ticket status changes,,Ticketing,Done,phase-2;realtime,5
Flag stale tickets automatically in the background,Task,Highlights tickets left unattended,,Ticketing,Done,phase-2;background-job,3
Design Club and ClubEvent entities with officer roles,Story,Domain model for clubs,,Clubs,Done,phase-2,5
Build club creation membership and officer management,Story,Club lifecycle and roles,,Clubs,Done,phase-2,8
Implement officer departure and succession rules,Story,Club continues when an officer leaves,,Clubs,Done,phase-2,5
Build club event scheduling and the events calendar,Story,Events with a calendar view,,Clubs,Done,phase-2,5
Add real-time club activity updates over SignalR,Task,Live club membership updates,,Clubs,Done,phase-2;realtime,3
Auto-flag inactive clubs in the background,Task,Highlights dormant clubs,,Clubs,Done,phase-2;background-job,3
Design Internship Company and CareerProfile entities,Story,Domain model for careers,,Internships,Done,phase-2,5
Build the company portal for posting internships,Story,Companies manage their postings,,Internships,Done,phase-2,8
Build student career profile with skills management,Story,Students describe their skills and experience,,Internships,Done,phase-2,8
Build internship browse search and application flow,Story,Core internship user journey,,Internships,Done,phase-2,8
Implement the skills-based matching score algorithm,Story,Ranks internships against a student profile,,Internships,Done,phase-2;algorithm,8
Implement text similarity matching for skills,Task,Fuzzy matching of skill names,,Internships,Done,phase-2;algorithm,5
Enforce internship application eligibility rules,Story,Prevents ineligible or duplicate applications,,Internships,Done,phase-2,5
Build the My Applications tracking page,Story,Students track application status,,Internships,Done,phase-2,5
Design a unified notification entity and delivery service,Story,One notification pipeline for all modules,,Notifications,Done,phase-2,5
Build the notification centre with read/unread state,Story,Notification list and read tracking,,Notifications,Done,phase-2,5
Add live notification delivery over SignalR,Story,Notifications arrive without refreshing,,Notifications,Done,phase-2;realtime,5
Add the navbar notification badge with live count,Task,Unread count visible on every page,,Notifications,Done,phase-2;ux,3
Send transactional emails for key events,Task,Email delivery for important events,,Notifications,Done,phase-2,3
Build university management with API configuration,Story,Admins onboard and configure universities,,Administration,Done,phase-3,8
Build user administration with role assignment and suspension,Story,Admins manage accounts and roles,,Administration,Done,phase-3,8
Build the per-university service catalog toggle screen,Story,Admins enable modules per university,,Administration,Done,phase-3,5
Build the audit log viewer with filtering,Story,Searchable audit trail,,Administration,Done,phase-3;security,5
Implement centralised audit logging across all modules,Story,Consistent audit entries for sensitive actions,,Administration,Done,phase-3;security,8
Add authorization-failure auditing middleware,Task,Records denied access attempts,,Administration,Done,phase-3;security,3
Build the admin reporting dashboard,Story,Platform-wide usage reporting,,Administration,Done,phase-3;reporting,8
Add CSV export for admin reports,Story,Downloadable report data,,Administration,Done,phase-3;reporting,3
Build the external API simulator admin screen for demos,Story,Lets demos run without a live university API,,Administration,Done,phase-3,5
Add the reduced-functionality banner when a university sync fails,Task,FR-17 stale data is visibly flagged,,Administration,Done,phase-3;ux,3
Build the shared design token system,Story,Colour radius shadow and motion tokens,,Design System,Done,phase-4;ux,8
Integrate the Hugeicons SVG sprite icon system,Story,Single inline sprite for all icons,,Design System,Done,phase-4;ux,5
Design and build the two application layouts,Story,Main and minimal layouts,,Design System,Done,phase-4;ux,5
Redesign the home page with animations,Story,Landing page redesign,,Design System,Done,phase-4;ux,5
Redesign the authentication pages,Story,Login and registration redesign,,Design System,Done,phase-4;ux,5
Build the reusable hero banner component with responsive bleed,Story,Consistent page banners across modules,,Design System,Done,phase-4;ux,5
Build the custom accessible dropdown component,Story,Keyboard accessible select replacement,,Design System,Done,phase-4;ux;accessibility,5
Build reusable card pill empty-state and form-page components,Story,Shared component library,,Design System,Done,phase-4;ux,8
Add page-level stylesheets for all feature areas,Task,Per-page styling for 20 modules,,Design System,Done,phase-4;ux,8
Redesign the notifications page UI,Task,Notification centre visual refresh,,Design System,Done,phase-4;ux,3
Enhance calendar layouts and attachment presentation,Task,Calendar and attachment visual improvements,,Design System,Done,phase-4;ux,5
Add progress-bar and stepper components to multi-step forms,Task,Progress indication on long forms,,Design System,Done,phase-4;ux,3
Set up the xUnit test project and folder structure,Task,Test project scaffolding,,Testing,Done,phase-5;testing,3
Build test infrastructure with in-memory database harnesses and fakes,Story,Reusable test doubles and fixtures,,Testing,Done,phase-5;testing,8
Write the test plan and coverage study,Task,Documents what is tested and why,,Testing,Done,phase-5;testing;docs,5
Write attendance submission rule tests,Task,20 tests covering attendance rules,,Testing,Done,phase-5;testing,5
Write study group membership rule tests,Task,19 tests covering membership rules,,Testing,Done,phase-5;testing,5
Write internship application rule tests,Task,19 tests covering application rules,,Testing,Done,phase-5;testing,5
Write ticket workflow rule tests,Task,18 tests covering ticket transitions,,Testing,Done,phase-5;testing,5
Write ride visibility rule tests,Task,15 tests covering ride visibility,,Testing,Done,phase-5;testing,3
Write club officer departure rule tests,Task,10 tests covering officer succession,,Testing,Done,phase-5;testing,3
Write service catalog gating tests,Task,Tests for per-university module gating,,Testing,Done,phase-5;testing,3
Write attendance summary service unit tests,Task,16 unit tests for attendance aggregation,,Testing,Done,phase-5;testing,3
Write text similarity and matching score unit tests,Task,28 unit tests for the matching algorithm,,Testing,Done,phase-5;testing,5
Write view model validation tests,Task,13 tests for form validation rules,,Testing,Done,phase-5;testing,3
Write concurrency tests for simultaneous actions,Task,Tests for race conditions on shared resources,,Testing,Done,phase-5;testing,5
Write background job unit tests,Task,Tests for sync and session closing jobs,,Testing,Done,phase-5;testing,3
Write instructor roster and admin export tests,Task,Tests for roster and CSV export,,Testing,Done,phase-5;testing,3
Research and plan the mobile app scope and architecture,Task,Technology choice and phased plan,,Mobile App,Done,phase-6;mobile;docs,5
Write the Study Groups mobile implementation plan,Task,Detailed plan for the first mobile feature,,Mobile App,Done,phase-6;mobile;docs,3
Create the .NET MAUI project targeting Android and Windows,Task,Mobile project scaffolding,,Mobile App,Done,phase-6;mobile,3
Add the mobile project to the solution and fix build isolation,Task,Prevents the web project compiling mobile sources,,Mobile App,Done,phase-6;mobile,3
Build the mobile authentication API,Story,Login and student registration endpoints,,Mobile App,Done,phase-6;mobile;api,8
Restrict mobile access to the Student role only,Task,Staff and admin accounts use the web portal,,Mobile App,Done,phase-6;mobile;security,2
Build the Study Groups REST API with 12 endpoints,Story,Full study group functionality over REST,,Mobile App,Done,phase-6;mobile;api,13
Build the Attendance mobile API,Story,Attendance endpoints for the mobile client,,Mobile App,Done,phase-6;mobile;api,8
Build the Notifications mobile API,Story,Notification endpoints for the mobile client,,Mobile App,Done,phase-6;mobile;api,5
Build the Home dashboard mobile API,Story,Dashboard summary endpoint,,Mobile App,Done,phase-6;mobile;api,3
Implement per-platform API base URL resolution,Task,Correct host for emulator device and desktop,,Mobile App,Done,phase-6;mobile,5
Implement secure token storage with a desktop fallback,Task,Keystore on Android with a Windows fallback,,Mobile App,Done,phase-6;mobile;security,5
Implement automatic bearer token attachment for all requests,Task,Delegating handler adds the token,,Mobile App,Done,phase-6;mobile;security,3
Configure Android network security for development hosts,Task,Allows local development hosts only,,Mobile App,Done,phase-6;mobile,3
Build the typed Study Groups API client with error translation,Story,Typed client surfacing server messages,,Mobile App,Done,phase-6;mobile,8
Build the mobile login screen,Story,Student sign-in UI,,Mobile App,Done,phase-6;mobile;ux,5
Build the study group browse screen with search and course filter,Story,Group list with filtering,,Mobile App,Done,phase-6;mobile;ux,8
Build the study group details screen with member management,Story,Group detail members and approvals,,Mobile App,Done,phase-6;mobile;ux,8
Build the create study group form with field validation,Story,Group creation on mobile,,Mobile App,Done,phase-6;mobile;ux,5
Build the mobile group chat screen,Story,Chat UI with message history and paging,,Mobile App,Done,phase-6;mobile;ux,8
Implement live chat and list updates over SignalR on mobile,Story,Real-time parity with the web client,,Mobile App,Done,phase-6;mobile;realtime,8
Port the web design system to MAUI,Story,Shared tokens gradients and elevation,,Mobile App,Done,phase-6;mobile;ux,8
Generate MAUI icon assets from the web icon sprite,Task,Identical glyphs across both clients,,Mobile App,Done,phase-6;mobile;ux,5
Implement responsive layout for phone tablet and desktop widths,Story,Adaptive columns and content widths,,Mobile App,Done,phase-6;mobile;ux,5
Add delete study group to the mobile client,Story,Creator can delete from mobile,,Mobile App,Done,phase-6;mobile,3
Write the Study Groups API parity test suite,Task,25 tests proving web and mobile share rules,,Mobile App,Done,phase-6;mobile;testing,8
Build the Internships mobile screens,Story,Browse and apply for internships on mobile,,Mobile App,To Do,phase-6;mobile,13
Build the Attendance mobile screens with QR scanning,Story,Scan a QR code to mark attendance,,Mobile App,To Do,phase-6;mobile,13
Build the mobile notifications screen,Story,Notification centre on mobile,,Mobile App,To Do,phase-6;mobile,5
Test the mobile app on a physical Android device,Task,Verify on real hardware over the LAN,,Mobile App,To Do,phase-6;mobile;testing,5
Produce a signed Android release build,Task,Release configuration and signing,,Mobile App,To Do,phase-6;mobile;release,5
Add admin scoping so university admins only see their own tenant,Story,Prevents cross-tenant data access,,Hardening,Done,phase-7;security,8
Harden security across controllers and unify notifications,Story,Authorisation review across the application,,Hardening,Done,phase-7;security,8
Require membership before joining a study group's real-time chat,Bug,Non-members could subscribe to live chat messages,,Hardening,Done,phase-7;security,5
Require authentication on the study group hub,Bug,The hub accepted anonymous connections,,Hardening,Done,phase-7;security,3
Accept both cookie and bearer schemes on the study group hub,Bug,Mobile chat could not connect and showed Offline,,Hardening,Done,phase-7;bug;mobile,3
Add field validation to the study group create API,Bug,The API accepted a group with no name,,Hardening,Done,phase-7;bug,5
Stop the web create action saving a group when validation failed,Bug,An invalid submission created a group then showed an error,,Hardening,Done,phase-7;bug,3
Validate university API base URLs to prevent crashes,Bug,An invalid URL threw UriFormatException and returned 500,,Hardening,Done,phase-7;bug,3
Fix Arabic text corruption in CSV exports,Bug,Missing UTF-8 BOM made Arabic unreadable in Excel,,Hardening,Done,phase-7;bug,3
Fix timezone handling in attendance session expiry,Bug,Sessions closed at the wrong local time,,Hardening,Done,phase-7;bug,3
Fix keyboard navigation in the custom dropdown component,Bug,Arrow keys did not move between options,,Hardening,Done,phase-7;bug;accessibility,3
Fix non-responsive hero banners on several pages,Bug,Headings and buttons clipped on small screens,,Hardening,Done,phase-7;bug;ux,3
Fix the admin users role dropdown resetting to Student,Bug,Selected role was lost on save,,Hardening,Done,phase-7;bug,2
Fix duplicate page titles in the mobile app,Bug,Shell and hero both rendered the page title,,Hardening,Done,phase-7;bug;mobile;ux,2
Fix pickers showing their value twice on Windows,Bug,Picker title rendered as a header above the selection,,Hardening,Done,phase-7;bug;mobile;ux,2
Fix mobile card grid breaking layout at narrow widths,Bug,Cards split into columns too early and clipped content,,Hardening,Done,phase-7;bug;mobile;ux,3
Fix Shell navigation crash on app resume,Bug,Navigating during startup terminated the process,,Hardening,Done,phase-7;bug;mobile,3
Add exception logging for the Windows mobile build,Task,Startup crashes now write a diagnosable log,,Hardening,Done,phase-7;mobile,2
Revoke the SMTP credential committed to git history,Task,Credential is in public history and must be revoked at the provider,,Hardening,To Do,phase-7;security,3
```
