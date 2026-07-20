# UniConnect — Project Overview & Technical Reference

> A **multi-tenant university student-services platform** built as an ASP.NET Core 8 MVC web application. UniConnect gives students, instructors, department staff, employers, and platform admins a single portal for **study groups, ride sharing, smart attendance, complaints/ticketing, clubs & organizations, and internships/career matching** — all layered over a genuine (simulated) per-university API integration and a per-university service catalog.

**Project type:** Final Year Project (FYP), ~70% complete. Core services are functional end-to-end; some admin scoping (per-university admin role) and UI polish remain.

**Table of contents**
1. [Technology Stack](#1-technology-stack)
2. [Solution Structure](#2-solution-structure)
3. [Application Startup & Pipeline](#3-application-startup--pipeline-programcs)
4. [Core Architecture Concepts](#4-core-architecture-concepts)
5. [Roles & Seeded Accounts](#5-roles--seeded-accounts)
6. [Feature Modules (deep dive)](#6-feature-modules-deep-dive)
7. [Domain Model / Entities](#7-domain-model--entities)
8. [Controllers Reference](#8-controllers-reference)
9. [Services Layer](#9-services-layer)
10. [Background Hosted Services](#10-background-hosted-services)
11. [SignalR Hubs (real-time)](#11-signalr-hubs-real-time)
12. [The Simulated External University API](#12-the-simulated-external-university-api)
13. [Security & Auth Edge Cases](#13-security--auth-edge-cases)
14. [Front-End & Design System](#14-front-end--design-system)
15. [Database & Migrations](#15-database--migrations)
16. [Configuration Reference](#16-configuration-reference)
17. [Running the Project](#17-running-the-project)
18. [Known Gaps / Next Steps](#18-known-gaps--next-steps)

---

## 1. Technology Stack

| Layer | Technology | Notes |
|-------|-----------|-------|
| Framework | **ASP.NET Core 8.0 MVC** | `net8.0`, `Nullable` + `ImplicitUsings` enabled |
| Language | C# | |
| ORM / Data | **Entity Framework Core 8** | SQL Server / LocalDB (SQLite package also referenced but unused by default) |
| Auth | **ASP.NET Core Identity** | `ApplicationUser : IdentityUser` + `IdentityRole`, role-based `[Authorize]` |
| Real-time | **SignalR** | 6 hubs (chat, live tracking, live rosters, notifications) |
| UI | **Razor Views + Bootstrap 5** | Bootstrap Icons, Inter font, custom `.uc-*` green design system |
| Email | Custom `SmtpEmailSender` | Real email confirmation via SMTP (overrides Identity's no-op sender) |
| Maps / Geo | **Nominatim (OpenStreetMap)** | Text address → coordinates; Leaflet-based map JS on ride pages |
| Background work | 6 × `IHostedService` | Sync, revalidation, inactivity, session closing, staleness |

**NuGet packages** (`UniConnect.csproj`): `Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.UI`, `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Tools`, `Microsoft.VisualStudio.Web.CodeGeneration.Design`.

---

## 2. Solution Structure

```
UniConnect/
├── Program.cs                 # Composition root: DI, Identity, pipeline, hub mapping, seeding
├── appsettings.json           # Connection string, Email, University API, base URLs, keys
├── UniConnect.sln / .csproj
│
├── Adapters/                  # Multi-university provider abstraction (Adapter pattern)
│   ├── IUniversityProvider.cs         # The one read-only contract for academic data + DTOs
│   ├── RealApiUniversityProvider.cs   # Only impl — calls each university's HTTP API
│   └── UniversityProviderResolver.cs  # Picks the provider for a UniversityCode
│
├── Areas/Identity/Pages/Account/      # Scaffolded Identity (Login/Register customized)
├── Controllers/               # 13 MVC controllers + ExternalApi/ sub-controller
├── Data/
│   ├── ApplicationDbContext.cs        # DbSets + all relationship/index configuration
│   ├── DbSeeder.cs                    # Seeds university, roles, accounts, courses, enrolments
│   └── Migrations/                    # 24 EF migrations (feature history)
├── ExternalApi/
│   └── ExternalUniversityDataStore.cs # Persisted per-API-key simulated registrar datasets
├── Filters/RequireServiceAttribute.cs # Per-university service gate (enforcement half)
├── Hubs/                      # 6 SignalR hubs
├── Middleware/SuspendedUserMiddleware.cs
├── Models/                    # EF entities / domain model (~24 entity types)
├── Services/                  # Business services + background hosted services
├── ViewModels/                # Form/edit view models (11)
├── Views/                     # Razor views per controller + Shared layouts
└── wwwroot/                   # css/ (site + per-page), js/ (per-page), lib/ (bootstrap, jquery, signalr)
```

---

## 3. Application Startup & Pipeline (`Program.cs`)

**Service registration (DI):**
- `ApplicationDbContext` → SQL Server via `DefaultConnection`.
- Identity: `AddDefaultIdentity<ApplicationUser>` with `RequireConfirmedAccount = true`, password policy (digit + lowercase required, min length 6, no uppercase/non-alphanumeric required) + `AddRoles<IdentityRole>`.
- **Login audit hook** — `ConfigureApplicationCookie.OnSignedIn` writes a `"Login"` `AuditLog` on every successful sign-in (failed logins hooked separately in `Login.cshtml.cs`).
- **`SmtpEmailSender`** replaces the default no-op `IEmailSender` (this is why email confirmation actually works).
- **Security-stamp revalidation** interval shortened to **1 minute** (default 30) so role/permission/suspension changes take effect within ~1 min.
- Scoped services: `ExternalUniversityDataStore`, `UniversityApiSyncRunner`, `EnrollmentRevalidationRunner`, `NotificationService`, `MatchingScoreService`, `AuditLogService`, `RealApiUniversityProvider`, `IUniversityProviderResolver`, `IServiceCatalogService`.
- Named `HttpClient "UniversityApi"`; typed `HttpClient` for `IGeocodingService → NominatimGeocodingService`.
- `AddControllersWithViews`, `AddRazorPages`, `AddSignalR`, `AddHttpContextAccessor`.
- **6 hosted services** registered (see §10).

**Startup sequence:** `db.Database.MigrateAsync()` → `DbSeeder.SeedAsync(...)`.

**Middleware pipeline order:** DeveloperExceptionPage/MigrationsEndpoint (dev) or `UseExceptionHandler("/Home/Error")` + HSTS (prod) → `UseHttpsRedirection` → `UseStaticFiles` → `UseRouting` → `UseAuthentication` → **`SuspendedUserMiddleware`** → `UseAuthorization` → endpoints.

**Endpoints:** default MVC route `{controller=Home}/{action=Index}/{id?}`, Razor Pages, and 6 hub mappings: `/studygroupHub`, `/rideTrackingHub`, `/ticketHub`, `/clubHub`, `/attendanceHub`, `/notificationHub`.

---

## 4. Core Architecture Concepts

### 4.1 Multi-University Adapter Core (multi-tenancy)
Every account, course, ride, ticket, and club is scoped to a **`UniversityCode`** (the tenant key). UniConnect is built to serve many universities at once.

- **`IUniversityProvider`** is the *single* contract UniConnect owns for reading academic data. Methods:
  - `IsEnrolledAsync(universityCode, studentNumber, courseCode)`
  - `GetEnrolledCoursesAsync(universityCode, studentNumber)`
  - `GetAllCoursesAsync(universityCode)`
  - `GetStudentInfoAsync(universityCode, studentNumber)`
  - `GetTaughtCoursesAsync(universityCode, instructorId)`
  - `GetEnrolledStudentsAsync(universityCode, courseCode)`
  - DTOs: `UniversityCourseDto(CourseCode, CourseName, InstructorName?, Credits)`, `UniversityStudentDto(StudentNumber, FullName, UniversityEmail, Major?, YearOfStudy)`.
- **All academic data is READ-ONLY** from UniConnect's side (no Create/Update/Delete) — the university stays the system of record.
- The **only** implementation is `RealApiUniversityProvider`, which calls each university's HTTP API. There is deliberately **no mock/local provider** — even the built-in `DEFAULT` university integrates through a genuine API round-trip.
- `IUniversityProviderResolver` (→ `UniversityProviderResolver`) selects the provider for a given university.
- The API those calls hit is *simulated in-app* by `ExternalUniversityApiController` (see §12), so it's a real HTTP integration rather than a hardcoded table.

### 4.2 Service Catalog & Per-University Enablement
- **`Service`** catalog defines the 6 offered services, each with `Code`, `Name`, `Description`, `IconClass`, `IsImplemented`.
- **`UniversityService`** join rows enable/disable each service **per university** (unique on `(UniversityCode, ServiceCode)`).
- **`IServiceCatalogService`**: `IsServiceEnabledAsync(universityCode, serviceCode)` and `GetEnabledServiceCodesAsync(universityCode)` (implemented services only).
- **Enforcement**: `[RequireService(ServiceCodes.X)]` attribute (`Filters/`) blocks controllers whose service the user's university hasn't enabled → redirects to `Home/NotAvailable`.
- **Navigation**: `_Layout.cshtml` hides nav items for disabled services (anonymous visitors see everything, since there's no tenant context yet).

### 4.3 Two Layouts
- **`_Layout.cshtml`** — public/student-facing app: sticky top navbar, green theme, service nav filtered by enablement **and** role (e.g. instructors see "My Sessions", students see "My Attendance"; companies/instructors don't see "Internships").
- **`_PortalLayout.cshtml`** — staff/admin/company "portal": left sidebar with role-specific menu (Staff → Tickets; Company → My Internships; Admin → Universities/Users/Reports/External API Simulator), user card, unread-notification badge.
- `HomeController.Index` redirects non-student accounts straight to their portal landing.

---

## 5. Roles & Seeded Accounts

Five Identity roles, all created by `DbSeeder`:

| Role | Purpose | Seeded credentials |
|------|---------|--------------------|
| **Student** | Self-registers with a valid University ID (checked against Students table); uses student services | *(self-registered)* — valid IDs include `U2024001`–`U2024008` |
| **Instructor** | Creates attendance sessions, manages rosters, overrides records | `instructor.chami@uni.edu`, `instructor.habib@uni.edu` · **`Instructor@12345`** |
| **DepartmentStaff** | Handles tickets for one department (`Department` matches a `TicketCategory.Name`) | 7 accounts: `it.staff@uni.edu`, `registration.staff@uni.edu`, `finance.staff@uni.edu`, `studentaffairs.staff@uni.edu`, `facilities.staff@uni.edu`, `academicaffairs.staff@uni.edu`, `other.staff@uni.edu` · **`Staff@12345`** |
| **Company** | University career-services login; posts internships **on behalf of** named external employers | `careers@uniconnectdemo.edu` · **`Career@12345`** |
| **Admin** | Platform **super-admin**: manages all universities, users, reports, audit, external-API simulator | `admin@uniconnect.local` · **`Admin@12345`** |

**`ApplicationUser`** (extends `IdentityUser`) adds: `UniversityId` (the student's ID *number*, unique across accounts), `UniversityCode` (tenant), `FullName`, `Department` (staff only), `IsSuspended`, and navigation to study-group memberships/created groups.

**What `DbSeeder` provisions** (idempotent — checks existence first):
1. The `DEFAULT` university (`UniConnect Demo University`, `ApiBaseUrl = {App:BaseUrl}/external-api/v1`, `ApiKey` from config).
2. The 6-service catalog + enablement rows for `DEFAULT`.
3. The 5 roles.
4. 7 ticket categories (IT, Registration, Finance, Student Affairs, Facilities, Academic Affairs, Other).
5. 7 staff accounts (one per category), 1 admin, 2 instructors, 1 career-services (Company) account.
6. A demo `Company` + 2 seed internships (one `FullApplication`, one `ListingOnly`).
7. 8 courses (`CSC301`, `CSC340`, `CSC410`, `CSC420`, `MAT202`, `MAT310`, `PHY201`, `ENG101`), with instructors linked to some.
8. 8 students (`U2024001`–`U2024008`) each enrolled in **≥4 courses** (a project-wide rule).
9. **Mirrors** courses/students/enrolments into the external API's dataset for the default key (so the local tables act as a pre-warmed cache of what the API returns).

---

## 6. Feature Modules (deep dive)

Each module = controller(s) + entities + views + (usually) a SignalR hub + a background service. Requirement IDs (FR-xx, UC-xx) referenced throughout are preserved from the original spec in code comments.

### 6.1 Study Groups — `StudyGroupsController`, `StudyGroupHub`, `InactiveStudyGroupService`
Course-based groups with a **join-approval flow** and **real-time chat**.
- **Actions:** `Index` (browse/filter by course), `Create`, `Details`, `Join` (creates `Pending`), `ApproveMember`, `RejectMember`, `RemoveMember`, `TransferLeadership`, `Leave`, `PostMessage` (real-time via hub), `MyCourses`.
- **Rules:** `MinMembers` (FR-20, default 2) / `MaxMembers` (2–50, default 10); creator is auto-approved; membership is unique per (group, user); `RowVersion` optimistic concurrency.
- **Enrollment revalidation:** if a member drops the course (detected via the adapter), `EnrollmentRevalidationRunner` removes/flags them and notifies (Study Group edge case). Inactivity → `Inactive` status (FR-53).

### 6.2 Ride Sharing — `RidesController`, `RideTrackingHub`, `GeocodingService`
Students offer/request rides to campus with **live GPS tracking** ("Uber-style").
- Rides show only **active, seats-remaining, not-yet-departed** rides by **other** students **at the same university**.
- **Ride** carries departure/destination text + coordinates, `TotalSeats`/`AvailableSeats`, `VehicleType`, `Status` (`Active`/`Full`/`Completed`/`Cancelled`), live `LastLat/LastLng/LastLocationAt`, `TripStartedAt`, and `RowVersion` concurrency.
- **RideRequest**: pickup location + coords, `Status` (`Pending`/`Accepted`/`Rejected`/`Cancelled`). Driver manages requests (accept reserves a seat).
- Geocoding via Nominatim; live location pushed through `RideTrackingHub` (validated server-side in the controller, not trusted from the browser).
- UC-03 Create, UC-04 Request, UC-05 Manage Requests, plus status tracking (FR-13) and cancellation (FR-14). Views: Index, Create, Details, MyRides.

### 6.3 Smart Attendance — `InstructorAttendanceController`, `AttendanceController`, `AttendanceHub`, `CloseExpiredAttendanceSessionsService`
QR + GPS attendance with a live roster.
- **Instructor** (`InstructorAttendanceController`, role-gated): `Index` (my sessions), `Create` (course + date + start/end + classroom lat/lng + GPS radius + grace period), `EditSession`/`CancelSession` (before submissions arrive), `Details` (QR code + live roster), `CloseSession` (early). QR encodes a URL built from `Attendance:PublicBaseUrl`.
- **Student** (`AttendanceController`): `ScanSubmit` (landing from QR), `ManualEntry` (type token), `Submit`, `Result`, `MyAttendance` (history).
- **`AttendanceSession`**: `QrToken` (unique) + `QrExpiresAt`, `GracePeriodMinutes` (10), `GpsRadiusMeters` (100), `Status` (`Active`/`Closed`/`Cancelled`).
- **`AttendanceRecord`**: `Status` (`Present`/`Late`/`Absent`/`Excused`), submitted coords, `DistanceFromClassroom`, `DeviceFingerprint`, `IsSuspicious` + `SuspiciousReason`. Unique per (session, user).
- **Validation (FR-21/FR-23):** enrollment (via adapter), time window, token validity/expiry, GPS radius (great-circle distance), duplicate submission, same-device-different-student (flagged, not rejected). `Present` within grace period, else `Late`.
- Background job closes expired sessions and writes an `Absent` record for every enrolled non-submitter (so reports are complete).
- **Documented browser limits:** no native mock-location detection; device fingerprint is a per-browser localStorage ID, not hardware. Set `Attendance:PublicBaseUrl` to a LAN IP so QR codes resolve from phones.

### 6.4 Complaints & Ticketing — `TicketsController`, `StaffTicketsController`, `TicketHub`, `TicketStalenessService`
Students submit tickets to departments; staff triage/respond in real time.
- **Student** (`TicketsController`): `Index`, `Create` (category, title, description, priority, optional attachment), `Details`, `Reply`.
- **Staff** (`StaffTicketsController`, role-gated to their department): `Index` (filter by status), `Details`, `PickUp` (assign to self), `Respond` (with optional status change), `Reassign` (change category/staff), `Reject` (with reason), `ToggleOffensiveFlag`.
- **`Ticket`**: `Priority` (`Low`/`Medium`/`High`/`Urgent`), `Status` (`Open`/`InProgress`/`WaitingForStudent`/`Resolved`/`Closed`/`Rejected`), attachment, `IsEscalated`/`EscalatedAt`, `IsFlaggedOffensive`. `TicketResponse` records `PreviousStatus`/`NewStatus` transitions.
- 7 seeded categories, each with a staff account. Stale `Open` tickets are escalated/flagged by the background staleness job.

### 6.5 Clubs & Organizations — `ClubsController`, `ClubHub`, `InactiveClubService`
Register clubs, join with officer approval, announcements, events with RSVP, real-time chat, officer hierarchy, inactivity archiving.
- **Actions:** `Index` (filter by category), `Create`, `Details`, `Join` (Pending), `Leave`, `ApproveMember`, `RejectMember`, `RemoveMember`, plus announcement/event/RSVP/chat actions and leadership transfer.
- **`Club`**: `Category` (`Academic`/`Sports`/`Cultural`/`Social`/`Technology`/`Other`), `MaxMembers?`, `Status` (`Active`/`Inactive`/`Archived`), logo.
- **`ClubMember`**: `Role` (`President`/`VicePresident`/`Officer`/`Member`), `Status` (`Pending`/`Approved`/`Rejected`/`Left`).
- **Officer departure edge case:** if the President leaves, leadership auto-transfers to the VP, else the longest-standing approved member; if nobody remains, the club is archived.
- Entities: `Club`, `ClubMember`, `ClubAnnouncement`, `ClubEvent` (+ `EventRsvp` with `Attending`/`Maybe`/`NotAttending`), `ClubMessage`. Inactivity (FR-75) mirrors study-group logic.

### 6.6 Internships & Career Matching — `InternshipsController`, `CompanyController`, `CareerProfileController`, `MatchingScoreService`
- **Student career profile** (`CareerProfileController`, gated on Internships service): `Index`, `Edit` (interests/goals/preferred location/availability), `UploadCv` (≤5 MB, format-validated — CV edge case), `DeleteCv`, `AddSkill`/`RemoveSkill` (`StudentSkill` with `Beginner`/`Intermediate`/`Advanced`).
- **Browse/apply** (`InternshipsController`): `Index` (search by skill/location/max-duration/sort, "my major only" filter, **live matching score** per listing), `Details` (cross-university guard + apply form), apply/withdraw, `MyApplications`.
- **Matching score (FR-41):** student skills vs. required skills + completed courses vs. recommended courses + declared major vs. relevant majors + interests + location. Ties broken by recency; score shown transparently; low best-score triggers a "improve your profile" suggestion.
- **Company/career-services** (`CompanyController`, role `Company`): `Index` (dashboard), `PostInternship`, `EditInternship`, `ToggleActive` (deactivation notifies applicants), `Applications` (review + shortlist).
- **Two posting modes** (`InternshipPostingMode`): `ListingOnly` (students get the employer's external apply URL/email) and `FullApplication` (students apply in-app; university reviews and **emails a shortlist** to the real employer — `ShortlistSentAt`, per-candidate `SentToEmployerAt`). Every posting names a real `ExternalEmployerName`.
- **`InternshipApplication`**: `Status` (`Submitted`/`UnderReview`/`Shortlisted`/`Accepted`/`Rejected`/`Withdrawn`), `MatchingScore`, `CoverMessage`, unique per (internship, user).

### 6.7 Notifications — `NotificationsController`, `NotificationHub`, `NotificationService`
Cross-cutting in-app notifications with **real-time toasts** and unread badges (both layouts).
- `NotificationService` persists a `Notification` row **and** broadcasts live over `NotificationHub` (a personal channel joined on every page via `wwwroot/js/global-notifications.js`).
- `Notification`: `Title`, `Message`, optional `Link`, `IsRead`. Viewing `Index` marks all read.

### 6.8 Administration, Reporting & Audit (Admin role)
- **`AdminUniversitiesController`** — `Index`, `Create` (POST auto-provisions an independent persisted external dataset + a career-services Company account with a generated password + an immediate connectivity test/sync), `GenerateApiKey` (AJAX), and per-university service toggles (`Services` view).
- **`AdminUsersController`** — `Index` (search), `ToggleSuspend` (sets `IsSuspended` + invalidates security stamp; enforced by middleware).
- **`AdminReportsController`** — `Index`, `Report`, `Export`. **8 report types (FR-84–FR-91):** Service Usage, Attendance, Complaints/Tickets, Internships (global), Study Groups, Ride Sharing, Clubs, Notifications (global). Date + university filters; every report caps detail rows (`MaxRows`) and flags `Truncated` (report-timeout / export-size edge cases).
- **`AdminAuditLogController`** — `Index` filterable by action type / user / date (FR-92). `AuditLog` fields: `UserId?`, `UniversityCode?`, `Action`, `EntityType?`, `EntityId?`, `Details?`, `IpAddress?`, `Timestamp`.
- **`AdminExternalApiSimulatorController`** — test harness: pick a university's external dataset and `AddStudent`/`AddEnrollment`/`RemoveEnrollment`. Each mutation immediately (1) re-syncs, (2) notifies the affected real account, (3) for a drop, runs the study-group revalidation — so the full chain (external change → sync → notification → study-group consequence) is visible in one pass.

---

## 7. Domain Model / Entities

All entities live in `Models/`; all `DbSet`s and relationships are configured in `ApplicationDbContext.OnModelCreating`.

### Core / tenancy
- **`University`** — `Code` (PK), `Name`, `ApiBaseUrl`, `ApiKey`, `IsActive`, nav to `Students`/`Users`.
- **`Service`** — `Code`, `Name`, `Description`, `IconClass`, `IsImplemented`.
- **`UniversityService`** — `(UniversityCode, ServiceCode)` unique; `IsEnabled`.
- **`ApplicationUser`** — see §5.

### Academic (synced from the university API; local tables = cache)
- **`Student`** — `UniversityId` (PK, string), `UniversityCode`, `FullName`, `UniversityEmail`, `Major?`, `YearOfStudy`.
- **`Course`** — composite PK `(UniversityCode, CourseCode)`; `CourseName`, `InstructorName?`, `InstructorId?` (→ `ApplicationUser`, `SetNull`), `Credits`.
- **`Enrollment`** — `UniversityId` + `(UniversityCode, CourseCode)`; unique on `(UniversityId, CourseCode)`; `Semester` (default "Fall 2026").

### Study Groups
- **`StudyGroup`** — `GroupName`, `Description?`, `(UniversityCode, CourseCode)`, `CreatorId`, `Min/MaxMembers`, `Status` (`Active`/`Full`/`Archived`/`Inactive`), `MeetingLocation?`, `RowVersion`.
- **`StudyGroupMember`** — `(StudyGroupId, UserId)` unique; `Status` (`Pending`/`Approved`/`Rejected`/`Left`).
- **`StudyGroupMessage`** — `SenderId`, `Content`, `SentAt`.

### Ride Sharing
- **`Ride`**, **`RideRequest`** — see §6.2.

### Ticketing
- **`TicketCategory`**, **`Ticket`**, **`TicketResponse`** — see §6.4.

### Clubs
- **`Club`**, **`ClubMember`**, **`ClubAnnouncement`**, **`ClubEvent`**, **`EventRsvp`**, **`ClubMessage`** — see §6.5.

### Attendance
- **`AttendanceSession`**, **`AttendanceRecord`** — see §6.3.

### Internships
- **`Company`** — `UserId`, `UniversityCode` (unique — one posting account per university), `CompanyName`, `ContactEmail`, `IsActive`, logo.
- **`CareerProfile`** — `UserId` (unique — one per student), interests/goals/preferred location/availability, `CvFilePath`/`CvFileName`.
- **`StudentSkill`** — `UserId`, `SkillName`, `ProficiencyLevel?`.
- **`Internship`**, **`InternshipApplication`** — see §6.6.

### Cross-cutting
- **`Notification`**, **`AuditLog`** — see §6.7 / §6.8.

### External-simulator persistence (not FK-linked to UniConnect tables)
- **`ExternalSimCourse`** — PK `(ApiKey, CourseCode)`.
- **`ExternalSimStudent`** — PK `(ApiKey, StudentNumber)`.
- **`ExternalSimEnrollment`** — PK `(ApiKey, StudentNumber, CourseCode)`.

### Key constraints (from `OnModelCreating`)
- Unique: `ApplicationUser.UniversityId`, `UniversityService(UniversityCode, ServiceCode)`, `Enrollment(UniversityId, CourseCode)`, `StudyGroupMember(StudyGroupId, UserId)`, `ClubMember(ClubId, UserId)`, `EventRsvp(ClubEventId, UserId)`, `AttendanceSession.QrToken`, `AttendanceRecord(AttendanceSessionId, UserId)`, `Company.UniversityCode`, `CareerProfile.UserId`, `InternshipApplication(InternshipId, UserId)`.
- Delete behavior: mostly `Restrict` on principals; `Cascade` on owned children (members, messages, records, responses, RSVPs, applications); `SetNull` for `Course.Instructor` and `AuditLog.User`.

---

## 8. Controllers Reference

| Controller | Auth | Responsibility |
|-----------|------|----------------|
| `HomeController` | mixed | Landing (`Index` redirects staff/admin/company to portal), `Privacy`, `NotAvailable`, `Error` |
| `StudyGroupsController` | `[Authorize]` + `RequireService` | Study group lifecycle + chat |
| `RidesController` | `[Authorize]` + `RequireService` | Ride sharing + live tracking |
| `InstructorAttendanceController` | `Roles = "Instructor"` | Create/manage attendance sessions |
| `AttendanceController` | `[Authorize]` | Student attendance submission + history |
| `TicketsController` | `[Authorize]` + `RequireService` | Student-side ticketing |
| `StaffTicketsController` | `Roles = "DepartmentStaff"` | Staff-side ticket handling |
| `ClubsController` | `[Authorize]` + `RequireService` | Clubs lifecycle |
| `InternshipsController` | `[Authorize]` + `RequireService` | Browse/apply + matching score |
| `CareerProfileController` | `[Authorize]` + `RequireService` | Student career profile / CV / skills |
| `CompanyController` | `Roles = "Company"` | University career-services posting tool |
| `NotificationsController` | `[Authorize]` | Notification list (marks read) |
| `AdminUniversitiesController` | `Roles = "Admin"` | Manage universities + service enablement |
| `AdminUsersController` | `Roles = "Admin"` | Suspend/reactivate accounts |
| `AdminReportsController` | `Roles = "Admin"` | 8 reports + export |
| `AdminAuditLogController` | `Roles = "Admin"` | Audit trail viewer |
| `AdminExternalApiSimulatorController` | `Roles = "Admin"` | External-API test harness |
| `ExternalApi/ExternalUniversityApiController` | API-key header | Simulated registrar API (§12) |

---

## 9. Services Layer

| Service | Role |
|---------|------|
| `IUniversityProviderResolver` / `RealApiUniversityProvider` | Adapter core — HTTP calls to a university's API |
| `IServiceCatalogService` / `ServiceCatalogService` | Which services are enabled per university |
| `MatchingScoreService` | Computes student↔internship matching score (FR-41) |
| `NotificationService` | Persist + live-broadcast a notification |
| `AuditLogService` | Single audit-entry writer (retry once, then Critical log) |
| `SmtpEmailSender` | Real SMTP email (confirmation, shortlists) |
| `IGeocodingService` / `NominatimGeocodingService` | Address → coordinates via OpenStreetMap |
| `UniversityApiSyncRunner` | Test + pull + cache one university's data (shared by job & manual "Sync Now") |
| `EnrollmentRevalidationRunner` | Re-check + remove/flag + notify stale study-group members |

---

## 10. Background Hosted Services

| Service | Trigger | Responsibility |
|---------|---------|----------------|
| `UniversityApiSyncService` | every `UniversityApi:SyncIntervalMinutes` (default 10) | Full test+sync cycle for every "api"-mode university |
| `EnrollmentRevalidationService` | periodic | Runs `EnrollmentRevalidationRunner` |
| `InactiveStudyGroupService` | periodic | Flag study groups with no recent activity `Inactive` (FR-53) |
| `InactiveClubService` | periodic | Flag clubs inactive (FR-75) |
| `CloseExpiredAttendanceSessionsService` | periodic | Close past-`EndTime` sessions + write `Absent` for non-submitters |
| `TicketStalenessService` | periodic | Escalate/flag long-`Open` tickets |

---

## 11. SignalR Hubs (real-time)

All hubs follow the same pattern: **the hub only manages group membership**; all authorization and business logic lives in the controllers, which push updates via `IHubContext<T>` after validating each request.

| Hub | Endpoint | Purpose |
|-----|----------|---------|
| `StudyGroupHub` | `/studygroupHub` | Study-group chat |
| `ClubHub` | `/clubHub` | Club chat/announcements/events |
| `RideTrackingHub` | `/rideTrackingHub` | Live ride location ("ride-{id}" groups) |
| `AttendanceHub` | `/attendanceHub` | Live roster check-ins for instructors |
| `TicketHub` | `/ticketHub` | Live ticket updates |
| `NotificationHub` | `/notificationHub` | Personal channel joined on every page (global toasts) |

---

## 12. The Simulated External University API

`Controllers/ExternalApi/ExternalUniversityApiController.cs` (`[Route("external-api/v1")]`) stands in for a real university registrar. **Every request is routed to a different dataset by its `X-Api-Key` header** — each university gets its own independent, persisted students/courses/enrolments, exactly as two real, separate partners would never share a database. Backed by `ExternalUniversityDataStore` (persisted via `ExternalSim*` tables, so data survives restarts).

**Endpoints:**
- `GET /students/{studentNumber}`
- `GET /students/{studentNumber}/enrollments`
- `GET /courses`
- `GET /students`
- `GET /courses/{courseCode}/roster`
- `GET /instructors/{instructorStaffId}/courses`
- `GET /health`

`RealApiUniversityProvider` calls these over the named `HttpClient "UniversityApi"`. The `AdminExternalApiSimulator` screen mutates these datasets to demo the sync chain; a `SimulatedFailureRatePercent` config lets you inject failures.

---

## 13. Security & Auth Edge Cases

- **Email confirmation** is genuinely required — `SmtpEmailSender` overrides Identity's no-op sender.
- **Suspension mid-session** — `SuspendedUserMiddleware` signs a suspended user out on their next request (doesn't wait for cookie expiry); `ToggleSuspend` also invalidates the security stamp as a backstop.
- **Role change mid-session** — security-stamp revalidation interval shortened to 1 minute, so nav/permission changes apply within ~1 min.
- **Login auditing** — successful logins via the Identity cookie `OnSignedIn` event; failed logins in `Login.cshtml.cs`.
- **Audit-log failure** — `AuditLogService` retries once, then logs `Critical` via `ILogger`.
- **Cross-university guards** — e.g. `InternshipsController.Details` blocks reaching another university's postings even by direct URL.
- **Optimistic concurrency** — `RowVersion` on `Ride` and `StudyGroup`.
- **Password policy** — min 6, requires digit + lowercase (no uppercase/non-alphanumeric required).

---

## 14. Front-End & Design System

- **`wwwroot/css/site.css`** — the global green design system built on CSS custom properties:
  - Brand greens `--uc-primary: #16a34a` (→ dark/darker/deep/light/soft/tint), teal accent `--uc-accent: #0d9488`; neutrals; status colors; radii (`--uc-radius*`); shadows (`--uc-shadow*`, incl. `--uc-shadow-green`); motion token; maps Bootstrap `--bs-*` vars to the brand.
  - Restyles Bootstrap primitives + provides reusable `.uc-*` components: `.uc-navbar`, `.uc-brand`, `.uc-hero`, `.uc-eyebrow`, `.uc-pill` (green/amber/red/blue/grey), `.uc-section-head`, `.uc-empty`, `.uc-reveal` (scroll-in animation), etc.
- **`wwwroot/css/portal-layout.css`** — the admin/staff sidebar portal shell.
- **`wwwroot/css/pages/*.css`** + **`wwwroot/js/pages/*.js`** — one file per page/feature (attendance, clubs, rides, internships, tickets, study groups, reports, admin-universities, home, career-profile-cv, etc.).
- Font: **Inter** (Google Fonts). Icons: **Bootstrap Icons** (CDN). Real-time: `signalr.js`; global toasts: `global-notifications.js`.
- **Recent UI polish (this branch):** home hero got soft glowing aura blobs + a staggered fade-up entrance + a gently floating preview card; stat tiles and feature cards got gradient accent bars + hover lifts; the navbar active link got a tinted pill + animated underline and the brand mark a shine-sweep on hover. All motion is guarded by `prefers-reduced-motion`.

---

## 15. Database & Migrations

- **Database:** `UniConnectDb` on `(localdb)\mssqllocaldb`. Migrations auto-apply at startup (`MigrateAsync`), then `DbSeeder` runs.
- **24 migrations** under `Data/Migrations/` chart the feature history, e.g. `InitialCreate` → `AddRideSharing` → `AddRideCoordinates` → `AddRideLiveTracking` → `AddUniversityAdapterCore` → `AddServiceCatalog` → `AddTicketingAndRoles` → `AddPerUniversityCourseCatalogs` → `AddClubsService` → `AddSmartAttendance` → `AddUniversityApiIntegration` → `PersistExternalSimData` → `AddInternships` → `AddReportingAndAudit` → `UniversityPostsInternships` → `AddRelevantMajors` (latest).
- Reading the migration names top-to-bottom is the quickest way to see how the project grew.

---

## 16. Configuration Reference (`appsettings.json`)

| Key | Meaning |
|-----|---------|
| `ConnectionStrings:DefaultConnection` | SQL Server LocalDB connection |
| `Attendance:PublicBaseUrl` | Base URL embedded in attendance QR codes (set to a LAN IP for phone scanning) |
| `Email:*` | SMTP host/port/username/password/from/EnableSsl for confirmation emails (blank by default) |
| `App:BaseUrl` | App base URL (used to build the default university's `ApiBaseUrl`) |
| `DefaultUniversity:ApiKey` | API key for the built-in `DEFAULT` university |
| `ExternalApiDemo:ApiKey` | Demo external-university key |
| `ExternalApiDemo:SimulatedFailureRatePercent` | Inject simulated API failures (0 = off) |
| `UniversityApi:SyncIntervalMinutes` | Background sync cadence (default 10) |

> **Note:** email credentials and any real API keys should move to user-secrets / environment variables before any non-demo deployment.

---

## 17. Running the Project

1. Ensure SQL Server **LocalDB** is available (`(localdb)\mssqllocaldb`).
2. `dotnet run` (or F5 in Visual Studio). Migrations apply and `DbSeeder` provisions roles, accounts, courses, students, enrolments, and the external-API dataset automatically.
3. Log in with any seeded account (§5), or register a **new student** using a seeded University ID (e.g. `U2024001`). Email confirmation is required unless SMTP is left unconfigured for local testing.
4. Explore the student app (top navbar) or, for staff/admin/company accounts, the portal (sidebar).

**Useful EF commands:** `dotnet ef migrations add <Name>` · `dotnet ef database update` · `dotnet ef migrations list`.

---

## 18. Known Gaps / Next Steps

- **Per-university "University Admin" role** (scoped to one university) — not yet built; the current single `Admin` acts as a super-admin. Noted in code as a small filter away from what exists.
- **`README.md`** is a one-line stub — this file is the real overview.
- **Device/location integrity** in attendance is browser-limited by design (§6.3); a native app would strengthen it.
- **Secrets** (SMTP, API keys) are in `appsettings.json` for demo convenience — move to secure config before real deployment.
- **UI refinement** — the current in-progress work (starting from home + navbar).
