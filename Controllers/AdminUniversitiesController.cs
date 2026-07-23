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

        public AdminUniversitiesController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            UniConnect.Services.UniversityApiSyncRunner syncRunner,
            UniConnect.ExternalApi.ExternalUniversityDataStore externalStore,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _syncRunner = syncRunner;
            _externalStore = externalStore;
            _auditLog = auditLog;
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
                IsActive = true
            };
            _db.Universities.Add(university);
            _db.UniversitySettings.Add(new UniversitySettings { UniversityCode = code });
            await _db.SaveChangesAsync();

            // Don't make the admin wait for the next scheduled sync cycle to
            // see whether the connection actually works.
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
                FullName = $"{university.Name} — Career Services",
                UniversityCode = university.Code,
                UniversityId = $"CAREER-{code}",
            };
            var careerCreateResult = await _userManager.CreateAsync(careerServicesUser, careerPassword);
            if (careerCreateResult.Succeeded)
            {
                await _userManager.AddToRoleAsync(careerServicesUser, "Company");
                _db.Companies.Add(new Company
                {
                    UserId = careerServicesUser.Id,
                    UniversityCode = university.Code,
                    CompanyName = $"{university.Name} — Career Services",
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
                FullName = $"{university.Name} — Admin",
                UniversityCode = university.Code,
                UniversityId = $"UNIADMIN-{code}",
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

            TempData["Success"] = $"{university.Name} added and synced (status: {university.LastSyncStatus}). " +
                string.Join(" | ", credentialMessages) +
                " (save these now, they won't be shown again). Now choose which services to enable.";

            return RedirectToAction(nameof(Services), new { code = university.Code });
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

            TempData["Success"] = university.LastSyncStatus == "Success"
                ? $"{university.Name} synced successfully."
                : $"Sync failed for {university.Name}: {university.LastSyncError}";
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

            try
            {
                var students = await _db.Students.Where(s => s.UniversityCode == code).ToListAsync();
                _db.Students.RemoveRange(students);
                await _db.SaveChangesAsync();

                var accounts = await _userManager.Users.Where(u => u.UniversityCode == code).ToListAsync();
                foreach (var account in accounts)
                    await _userManager.DeleteAsync(account);

                _db.Universities.Remove(university);
                await _db.SaveChangesAsync();

                TempData["Success"] = $"{university.Name} and its data have been deleted.";
            }
            catch (DbUpdateException)
            {
                TempData["Error"] = $"Couldn't delete {university.Name} — it still has activity attached " +
                    "(rides, study groups, tickets, or similar created by one of its accounts). " +
                    "This safety check only lets you delete universities with no real usage yet.";
            }

            return RedirectToAction(nameof(Index));
        }

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
