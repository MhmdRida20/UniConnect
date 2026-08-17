using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Admin-facing side of the Core Platform: manage which universities exist
    /// on UniConnect and which services each one has enabled
    /// (Services.docx: "Service catalog management", "Per-university service
    /// enablement and configuration").
    ///
    /// Two roles reach this controller now:
    ///   Admin           — Super Admin, manages EVERY university.
    ///   UniversityAdmin — scoped to exactly one university (their own,
    ///                     via ApplicationUser.UniversityCode). Can manage
    ///                     their own services/sync, but cannot create,
    ///                     delete, activate/deactivate, or view any OTHER
    ///                     university — those stay Super-Admin-only, gated
    ///                     with an explicit [Authorize(Roles = "Admin")] on
    ///                     the specific actions below.
    /// </summary>
    [Authorize(Roles = "Admin,UniversityAdmin")]
    public class AdminUniversitiesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly UniConnect.Services.UniversityApiSyncRunner _syncRunner;
        private readonly UniConnect.ExternalApi.ExternalUniversityDataStore _externalStore;
        private readonly UniConnect.Services.AuditLogService _auditLog;
        private readonly ILogger<AdminUniversitiesController> _logger;

        public AdminUniversitiesController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            UniConnect.Services.UniversityApiSyncRunner syncRunner,
            UniConnect.ExternalApi.ExternalUniversityDataStore externalStore,
            UniConnect.Services.AuditLogService auditLog,
            ILogger<AdminUniversitiesController> logger)
        {
            _db = db;
            _userManager = userManager;
            _syncRunner = syncRunner;
            _externalStore = externalStore;
            _auditLog = auditLog;
            _logger = logger;
        }

        private bool IsSuperAdmin => User.IsInRole("Admin");

        // A UniversityAdmin may only manage their OWN university; a Super
        // Admin may manage any of them.
        private async Task<bool> CanManageAsync(string universityCode)
        {
            if (IsSuperAdmin) return true;
            var currentUser = await _userManager.GetUserAsync(User);
            return currentUser is not null && currentUser.UniversityCode == universityCode;
        }

        // ---------- GENERATE API KEY (AJAX) — Super Admin only -----------------
        // Only relevant when CREATING a brand new university, which is itself
        // Super-Admin-only (see Create below).
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GenerateApiKey(string? universityName)
        {
            string key;
            do
            {
                key = UniConnect.ExternalApi.ExternalUniversityDataStore.GenerateApiKey();
            } while (await _db.Universities.AnyAsync(u => u.ApiKey == key));

            var (studentCount, courseCount) = await _externalStore.ProvisionRandomDatasetAsync(key);

            return Json(new
            {
                apiKey = key,
                studentCount,
                courseCount
            });
        }

        // ---------- INDEX: all universities on the platform (Super Admin) -----
        // A UniversityAdmin doesn't manage a LIST of universities — they only
        // ever have the one — so send them straight to its Services page
        // instead of a one-row list.
        public async Task<IActionResult> Index()
        {
            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null) return Challenge();
                return RedirectToAction(nameof(Services), new { code = currentUser.UniversityCode });
            }

            var universities = await _db.Universities
                .OrderBy(u => u.Name)
                .ToListAsync();

            var enabledCounts = await _db.UniversityServices
                .Where(us => us.IsEnabled)
                .GroupBy(us => us.UniversityCode)
                .Select(g => new { UniversityCode = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UniversityCode, x => x.Count);

            ViewBag.EnabledCounts = enabledCounts;
            // Platform-wide overview strip on the Index page.
            ViewBag.TotalServices = await _db.Services.CountAsync(s => s.IsImplemented);
            ViewBag.TotalStudents = await _db.Students.CountAsync();
            return View(universities);
        }

        // ---------- CREATE (GET/POST) — Super Admin only ------------------------
        // Creating a brand new university is a platform-level action; a
        // UniversityAdmin managing their own institution has no reason to
        // create a SECOND one.
        [Authorize(Roles = "Admin")]
        public IActionResult Create() => View(new UniversityCreateVM());

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create(UniversityCreateVM vm)
        {
            if (string.IsNullOrWhiteSpace(vm.ApiBaseUrl))
                ModelState.AddModelError(nameof(vm.ApiBaseUrl), "An API base URL is required.");
            // Caught here so a mistyped address is a field error on the form
            // rather than a half-created university: this method saves the
            // University row before it provisions the accounts and service
            // catalog, so anything that throws in between leaves an institution
            // that exists, can't be used, and blocks the code from being reused.
            else if (!Uri.TryCreate(vm.ApiBaseUrl.Trim(), UriKind.Absolute, out var parsedApiBaseUrl)
                     || (parsedApiBaseUrl.Scheme != Uri.UriSchemeHttp && parsedApiBaseUrl.Scheme != Uri.UriSchemeHttps))
                ModelState.AddModelError(nameof(vm.ApiBaseUrl),
                    "Enter the full address including https://, e.g. https://registrar.uni.edu/api/v1.");
            if (string.IsNullOrWhiteSpace(vm.ApiKey))
                ModelState.AddModelError(nameof(vm.ApiKey), "Click \"Generate\" to create an API key for this university.");

            if (!ModelState.IsValid) return View(vm);

            var code = vm.Code.Trim().ToUpperInvariant();
            if (await _db.Universities.AnyAsync(u => u.Code == code))
            {
                ModelState.AddModelError(nameof(vm.Code), "A university with this code already exists.");
                return View(vm);
            }

            if (await _userManager.FindByEmailAsync(vm.CareerServicesEmail) is not null)
            {
                ModelState.AddModelError(nameof(vm.CareerServicesEmail), "An account already exists for this email.");
                return View(vm);
            }
            if (await _userManager.FindByEmailAsync(vm.UniversityAdminEmail) is not null)
            {
                ModelState.AddModelError(nameof(vm.UniversityAdminEmail), "An account already exists for this email.");
                return View(vm);
            }
            if (string.Equals(vm.CareerServicesEmail.Trim(), vm.UniversityAdminEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(vm.UniversityAdminEmail), "The university admin and career services emails must be different.");
                return View(vm);
            }

            var university = new University
            {
                Code = code,
                Name = vm.Name.Trim(),
                ApiBaseUrl = vm.ApiBaseUrl.Trim(),
                ApiKey = vm.ApiKey.Trim(),
                ApiStyle = vm.ApiStyle,
                IsActive = true
            };
            _db.Universities.Add(university);
            _db.UniversitySettings.Add(new UniversitySettings { UniversityCode = code });
            await _db.SaveChangesAsync();

            // From here on the University row exists. Anything that throws while
            // provisioning would otherwise leave an institution that exists, has
            // no logins, and blocks its own code from being reused — which is
            // exactly what happened when a long university name overflowed
            // ApplicationUser.FullName. The provisioning is wrapped so that a
            // failure removes the half-built university and reports a form error,
            // leaving the admin able to simply correct the input and retry.
            try
            {

            // SyncOneUniversityAsync itself now knows to report "NotApplicable"
            // rather than attempt anything for a non-Simulated university —
            // see the guard at the top of that method for why.
            await _syncRunner.SyncOneUniversityAsync(university);

            var credentialMessages = new List<string>();

            // Every university gets exactly one internship-posting account,
            // created automatically here rather than through self-registration
            // — a real university partner already has a real career services
            // department and email; there's no separate "company" to sign up.
            var careerPassword = GenerateSecurePassword();
            var careerServicesUser = new ApplicationUser
            {
                UserName = vm.CareerServicesEmail.Trim(),
                Email = vm.CareerServicesEmail.Trim(),
                EmailConfirmed = true, // admin-provisioned, same as Staff/Instructor accounts
                FullName = ScopedAccountName(university.Name, code, "Career Services"),
                UniversityCode = university.Code,
                UniversityId = ScopedUniversityId("CAREER-", code),
            };
            var careerCreateResult = await _userManager.CreateAsync(careerServicesUser, careerPassword);
            if (careerCreateResult.Succeeded)
            {
                await _userManager.AddToRoleAsync(careerServicesUser, "Company");
                _db.Companies.Add(new Company
                {
                    UserId = careerServicesUser.Id,
                    UniversityCode = university.Code,
                    // 150 here, not 50 — but University.Name is also 150, so the
                    // suffix can still push it over on its own.
                    CompanyName = ScopedAccountName(university.Name, code, "Career Services", maxLength: 150),
                    ContactEmail = vm.CareerServicesEmail.Trim(),
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                await _auditLog.LogAsync(
                    "CareerServicesAccountCreated",
                    userId: _userManager.GetUserId(User),
                    universityCode: university.Code,
                    entityType: "Company",
                    entityId: careerServicesUser.Id,
                    details: $"Email: {vm.CareerServicesEmail}");

                credentialMessages.Add($"Career services — email: {vm.CareerServicesEmail}, password: {careerPassword}");
            }
            else
            {
                credentialMessages.Add("Career services login creation FAILED — you can add one manually later.");
            }

            // The university's own scoped admin — distinct from Super Admin,
            // can only ever manage this one institution (see CanManageAsync).
            var uniAdminPassword = GenerateSecurePassword();
            var universityAdminUser = new ApplicationUser
            {
                UserName = vm.UniversityAdminEmail.Trim(),
                Email = vm.UniversityAdminEmail.Trim(),
                EmailConfirmed = true,
                FullName = ScopedAccountName(university.Name, code, "Admin"),
                UniversityCode = university.Code,
                UniversityId = ScopedUniversityId("UNIADMIN-", code),
            };
            var uniAdminCreateResult = await _userManager.CreateAsync(universityAdminUser, uniAdminPassword);
            if (uniAdminCreateResult.Succeeded)
            {
                await _userManager.AddToRoleAsync(universityAdminUser, "UniversityAdmin");

                await _auditLog.LogAsync(
                    "UniversityAdminAccountCreated",
                    userId: _userManager.GetUserId(User),
                    universityCode: university.Code,
                    entityType: "User",
                    entityId: universityAdminUser.Id,
                    details: $"Email: {vm.UniversityAdminEmail}");

                credentialMessages.Add($"University admin — email: {vm.UniversityAdminEmail}, password: {uniAdminPassword}");
            }
            else
            {
                credentialMessages.Add("University admin login creation FAILED — you can add one manually later.");
            }

            // Instructor and staff RECORDS were already generated alongside
            // this university's courses (ProvisionRandomDatasetAsync) — no
            // login accounts are created for them here anymore. They now
            // self-register exactly like students already do, using the
            // Staff ID + email shown on the Services page. This keeps the
            // "who has an account" story consistent across every role:
            // nobody is a real person here without having gone through
            // registration themselves.
            var instructorCount = (await _externalStore.GetDistinctInstructorsAsync(vm.ApiKey.Trim())).Count;
            var staffCount = (await _externalStore.GetAllStaffAsync(vm.ApiKey.Trim())).Count;
            if (instructorCount > 0 || staffCount > 0)
            {
                credentialMessages.Add(
                    $"{instructorCount} instructor(s) and {staffCount} staff record(s) generated — " +
                    "they can self-register using the Staff IDs shown on this page below.");
            }

            var syncStatusNote = university.LastSyncStatus == "NotApplicable"
                ? "added. This university uses a real external API — student registration, enrollment checks, and " +
                  "attendance will call it live; the automatic course/roster sync isn't supported for this API style yet. "
                : $"added and synced (status: {university.LastSyncStatus}). ";

            TempData["Success"] = $"{university.Name} {syncStatusNote}" +
                string.Join(" | ", credentialMessages) +
                " (save these now, they won't be shown again). Now choose which services to enable.";

            return RedirectToAction(nameof(Services), new { code = university.Code });
            }
            catch (Exception ex)
            {
                // Undo the university so the code stays available. Done on a
                // clean context because the failing one still holds the entities
                // that could not be saved, and would replay them.
                _db.ChangeTracker.Clear();

                var orphanSettings = await _db.UniversitySettings
                    .Where(s => s.UniversityCode == code).ToListAsync();
                _db.UniversitySettings.RemoveRange(orphanSettings);

                var orphan = await _db.Universities.FirstOrDefaultAsync(u => u.Code == code);
                if (orphan is not null) _db.Universities.Remove(orphan);

                await _db.SaveChangesAsync();

                _logger.LogError(ex, "Provisioning failed for university {Code}; the partial record was removed.", code);

                ModelState.AddModelError(string.Empty,
                    "The university could not be set up, so nothing was saved and the code is still free. " +
                    "Please check the details and try again. (" + ex.GetBaseException().Message + ")");
                return View(vm);
            }
        }

        /// <summary>
        /// Builds "{university} — {suffix}" so that it fits ApplicationUser.FullName,
        /// which is 50 characters while University.Name is 150.
        ///
        /// Without this, provisioning threw SqlException ("String or binary data
        /// would be truncated") for any university whose name was longer than 31
        /// characters — and because the University row is saved before the accounts
        /// are, the failure left an institution that existed, had no logins, and
        /// blocked its own code from being reused.
        ///
        /// Falls back to the university CODE rather than chopping the name
        /// mid-word: "USAL — Career Services" reads like a deliberate label,
        /// "University of Science and Ar — Career Services" reads like a bug.
        /// </summary>
        private static string ScopedAccountName(string universityName, string code, string suffix, int maxLength = 50)
        {
            var preferred = $"{universityName} — {suffix}";
            if (preferred.Length <= maxLength) return preferred;

            var byCode = $"{code} — {suffix}";
            // Only if even the code form overflows — a 20-char code with a long
            // suffix — does this trim, and then it trims the suffix, not the name.
            return byCode.Length <= maxLength ? byCode : byCode[..maxLength];
        }

        /// <summary>
        /// ApplicationUser.UniversityId is 20 characters and University.Code is 20,
        /// so a prefixed identifier can overflow on its own. Truncating is safe
        /// here: these are synthetic identifiers for provisioned accounts, unique
        /// because the code is, not values matched against university records.
        /// </summary>
        private static string ScopedUniversityId(string prefix, string code, int maxLength = 20)
        {
            var value = $"{prefix}{code}";
            return value.Length <= maxLength ? value : value[..maxLength];
        }

        private static string GenerateSecurePassword()
        {
            // Meets ASP.NET Core Identity's default password rules (digit,
            // lowercase, min length 6) with enough extra length/variety to
            // be a reasonable one-time credential, not just the bare minimum.
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%";
            var bytes = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            var password = new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
            return password + "Aa1!"; // guarantee every required character class is present
        }

        // ---------- SYNC NOW (manual trigger) — either role, own university only ---
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncNow(string code)
        {
            if (!await CanManageAsync(code)) return Forbid();

            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            await _syncRunner.SyncOneUniversityAsync(university);

            TempData["Success"] = university.LastSyncStatus switch
            {
                "Success" => $"{university.Name} synced successfully.",
                "NotApplicable" => $"{university.Name} uses a real external API — there's nothing to sync automatically yet; registration and enrollment checks already call it live.",
                _ => $"Sync failed for {university.Name}: {university.LastSyncError}"
            };
            return RedirectToAction(nameof(Index));
        }

        // ---------- TOGGLE ACTIVE — Super Admin only ----------------------------
        // Deactivating a WHOLE university is a platform-level action — a
        // University Admin self-service-disabling their own institution
        // would be an odd, risky thing to allow.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> ToggleActive(string code)
        {
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            university.IsActive = !university.IsActive;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{university.Name} is now {(university.IsActive ? "active" : "inactive")}.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- DELETE (full teardown) — Super Admin only -------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(string code)
        {
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            // All or nothing. Without this the teardown committed in stages, so a
            // delete that failed at the last step still destroyed everything the
            // earlier steps had removed — the university survived with its synced
            // students already gone, and the admin had no way to tell.
            await using var tx = await _db.Database.BeginTransactionAsync();

            try
            {
                // Everything the SYNC brought in, cleared together. These three
                // are a mirror of the university's own records, not activity
                // anyone created here, so a teardown owns all of them equally.
                //
                // Students alone used to be removed, which made the delete fail
                // on any university that had ever synced: Instructors and
                // StaffRecords are ON DELETE NO_ACTION, so SQL Server refused to
                // drop the parent row while they existed, and the catch below
                // then blamed rides and tickets that were not there.
                _db.Students.RemoveRange(
                    await _db.Students.Where(s => s.UniversityCode == code).ToListAsync());
                _db.Instructors.RemoveRange(
                    await _db.Instructors.Where(i => i.UniversityCode == code).ToListAsync());
                _db.StaffRecords.RemoveRange(
                    await _db.StaffRecords.Where(s => s.UniversityCode == code).ToListAsync());
                // Groups and clubs the UI has already retired. Deleting either
                // one archives it rather than dropping the row — the members and
                // the whole message history hang off it — so an archived row is
                // the only trace left of something the users consider gone.
                //
                // Without this the teardown could never succeed: nothing in the
                // app physically removes these rows, so a single group anyone
                // had ever created and then deleted pinned the university in
                // place permanently, with no path out from any screen.
                //
                // Anything still ACTIVE deliberately keeps blocking. That is the
                // safety check doing its job, and DescribeRemainingActivityAsync
                // below now names it. Children cascade in the database.
                _db.StudyGroups.RemoveRange(
                    await _db.StudyGroups
                        .Where(g => g.UniversityCode == code && g.Status == StudyGroupStatus.Archived)
                        .ToListAsync());
                _db.Clubs.RemoveRange(
                    await _db.Clubs
                        .Where(c => c.UniversityCode == code && c.Status == ClubStatus.Archived)
                        .ToListAsync());

                await _db.SaveChangesAsync();

                var accounts = await _userManager.Users.Where(u => u.UniversityCode == code).ToListAsync();
                foreach (var account in accounts)
                    await _userManager.DeleteAsync(account);

                // Courses, settings, service enablement, ticket categories and
                // the company account all cascade from here.
                _db.Universities.Remove(university);
                await _db.SaveChangesAsync();

                await tx.CommitAsync();

                _logger.LogInformation("Deleted university {Code} ({Name}).", code, university.Name);
                TempData["Success"] = $"{university.Name} and its data have been deleted.";
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                _logger.LogWarning(ex, "Delete blocked for university {Code}.", code);

                // Say what is actually holding the row, rather than guessing.
                // The old message named a fixed list, so an admin blocked by
                // something else was sent looking in the wrong place.
                var blockers = await DescribeRemainingActivityAsync(code);

                TempData["Error"] = blockers.Count > 0
                    ? $"Couldn't delete {university.Name} — it still has {NaturalList(blockers)} attached. " +
                      "Remove those first; this safety check only lets you delete universities with no real usage left."
                    : $"Couldn't delete {university.Name} — something still references it. " +
                      "The server log has the details.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>
        /// Lists, in plain words, what is still pointing at a university after a
        /// failed teardown.
        ///
        /// Only the ON DELETE NO_ACTION relationships are worth naming — the
        /// cascading ones (courses, settings, service enablement, ticket
        /// categories, the company account) can never be the obstacle. Counting
        /// rather than loading, so the change tracker left dirty by the failed
        /// SaveChanges cannot colour the answer.
        /// </summary>
        private async Task<List<string>> DescribeRemainingActivityAsync(string code)
        {
            var found = new List<string>();

            void Note(int count, string singular, string plural)
            {
                if (count > 0) found.Add($"{count} {(count == 1 ? singular : plural)}");
            }

            Note(await _db.Rides.CountAsync(r => r.UniversityCode == code), "ride", "rides");
            Note(await _db.Tickets.CountAsync(t => t.UniversityCode == code), "support ticket", "support tickets");
            Note(await _db.AttendanceSessions.CountAsync(a => a.UniversityCode == code), "attendance session", "attendance sessions");

            // Archived ones are cleared by the teardown, so only live ones can
            // still be the obstacle — naming an archived group here would send
            // the admin looking for something already deleted.
            Note(await _db.StudyGroups.CountAsync(
                g => g.UniversityCode == code && g.Status != StudyGroupStatus.Archived), "active study group", "active study groups");
            Note(await _db.Clubs.CountAsync(
                c => c.UniversityCode == code && c.Status != ClubStatus.Archived), "active club", "active clubs");

            // Accounts are deleted by the teardown itself, so listing them
            // alongside a real blocker just adds noise — an account almost
            // always survives BECAUSE it created one of the things above. Only
            // worth naming when it is the one thing left unexplained.
            if (found.Count == 0)
                Note(await _userManager.Users.CountAsync(u => u.UniversityCode == code), "user account", "user accounts");

            return found;
        }

        private static string NaturalList(IReadOnlyList<string> items) => items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} and {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
        };

        // ---------- SERVICES (GET): the enablement checklist for one university ---
        public async Task<IActionResult> Services(string code)
        {
            if (!await CanManageAsync(code)) return Forbid();

            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            var allServices = await _db.Services.OrderBy(s => s.Name).ToListAsync();
            var enabledCodes = await _db.UniversityServices
                .Where(us => us.UniversityCode == code && us.IsEnabled)
                .Select(us => us.ServiceCode)
                .ToListAsync();

            ViewBag.University = university;
            ViewBag.EnabledCodes = enabledCodes;
            ViewBag.IsSuperAdmin = IsSuperAdmin;

            ViewBag.SyncedStudents = await _db.Students
                .Where(s => s.UniversityCode == code)
                .OrderBy(s => s.UniversityId)
                .ToListAsync();

            ViewBag.SyncedInstructors = await _db.Instructors
                .Where(i => i.UniversityCode == code)
                .OrderBy(i => i.StaffId)
                .ToListAsync();

            ViewBag.SyncedStaff = await _db.StaffRecords
                .Where(s => s.UniversityCode == code)
                .OrderBy(s => s.Department).ThenBy(s => s.StaffId)
                .ToListAsync();

            return View(allServices);
        }

        // ---------- SERVICES (POST): save the enablement checklist ---------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Services(string code, List<string>? enabledServiceCodes)
        {
            if (!await CanManageAsync(code)) return Forbid();

            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            enabledServiceCodes ??= new List<string>();

            var implementedCodes = await _db.Services
                .Where(s => s.IsImplemented)
                .Select(s => s.Code)
                .ToListAsync();
            var toEnable = enabledServiceCodes.Intersect(implementedCodes).ToHashSet();

            var existingRows = await _db.UniversityServices
                .Where(us => us.UniversityCode == code)
                .ToListAsync();

            foreach (var serviceCode in implementedCodes)
            {
                var row = existingRows.FirstOrDefault(r => r.ServiceCode == serviceCode);
                var shouldBeEnabled = toEnable.Contains(serviceCode);

                if (row is null)
                {
                    if (shouldBeEnabled)
                    {
                        _db.UniversityServices.Add(new UniversityService
                        {
                            UniversityCode = code,
                            ServiceCode = serviceCode,
                            IsEnabled = true
                        });
                    }
                }
                else
                {
                    row.IsEnabled = shouldBeEnabled;
                }
            }

            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "ServiceEnablementChanged",
                userId: _userManager.GetUserId(User),
                universityCode: code,
                entityType: "University",
                entityId: code,
                details: $"Enabled services: {string.Join(", ", toEnable)}");

            TempData["Success"] = $"Services updated for {university.Name}.";
            return RedirectToAction(IsSuperAdmin ? nameof(Index) : nameof(Services), IsSuperAdmin ? null : new { code });
        }

        // ---------- UNIVERSITY SETTINGS (GET/POST) — FR-11 ----------------------
        public async Task<IActionResult> Settings(string code)
        {
            if (!await CanManageAsync(code)) return Forbid();

            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            var settings = await GetOrCreateSettingsAsync(code);
            ViewBag.University = university;
            ViewBag.IsSuperAdmin = IsSuperAdmin;

            return View(new UniversitySettingsEditVM
            {
                MaxStudyGroupMembers = settings.MaxStudyGroupMembers,
                DefaultAttendanceGpsRadiusMeters = settings.DefaultAttendanceGpsRadiusMeters,
                DefaultAttendanceGraceMinutes = settings.DefaultAttendanceGraceMinutes,
                MaxClubMembers = settings.MaxClubMembers,
                MaxRideRequestsPerWindow = settings.MaxRideRequestsPerWindow,
                RideRequestWindowMinutes = settings.RideRequestWindowMinutes
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(string code, UniversitySettingsEditVM vm)
        {
            if (!await CanManageAsync(code)) return Forbid();

            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            if (!ModelState.IsValid)
            {
                ViewBag.University = university;
                ViewBag.IsSuperAdmin = IsSuperAdmin;
                return View(vm);
            }

            var settings = await GetOrCreateSettingsAsync(code);
            settings.MaxStudyGroupMembers = vm.MaxStudyGroupMembers;
            settings.DefaultAttendanceGpsRadiusMeters = vm.DefaultAttendanceGpsRadiusMeters;
            settings.DefaultAttendanceGraceMinutes = vm.DefaultAttendanceGraceMinutes;
            settings.MaxClubMembers = vm.MaxClubMembers;
            settings.MaxRideRequestsPerWindow = vm.MaxRideRequestsPerWindow;
            settings.RideRequestWindowMinutes = vm.RideRequestWindowMinutes;
            settings.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "UniversitySettingsChanged",
                userId: _userManager.GetUserId(User),
                universityCode: code,
                entityType: "UniversitySettings",
                entityId: code);

            TempData["Success"] = $"Settings updated for {university.Name}.";
            return RedirectToAction(nameof(Settings), new { code });
        }

        private async Task<UniversitySettings> GetOrCreateSettingsAsync(string code)
        {
            var settings = await _db.UniversitySettings.FindAsync(code);
            if (settings is null)
            {
                settings = new UniversitySettings { UniversityCode = code };
                _db.UniversitySettings.Add(settings);
                await _db.SaveChangesAsync();
            }
            return settings;
        }
    }
}
