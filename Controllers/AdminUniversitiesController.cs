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
    /// Scope note: this implements a single "Admin" role acting as what your
    /// ER diagram calls a Super Admin (manages ALL universities). The separate
    /// "University Admin" role (scoped to just their own university) isn't
    /// built yet — a reasonable next step, not an oversight, since the access
    /// pattern here (manage everything vs. manage-your-own) is a small filter
    /// away from what already exists.
    /// </summary>
    [Authorize(Roles = "Admin")]
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

        // ---------- GENERATE API KEY (AJAX) ------------------------------------
        // Called from the Create form's "Generate" button. Creates a unique key
        // AND immediately provisions a fresh, independent, PERSISTED dataset
        // for it — so a brand new university has its own distinct students/
        // courses the moment it's created, and that data survives app restarts
        // (it's no longer just an in-memory dictionary).
        [HttpPost]
        [ValidateAntiForgeryToken]
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

        // ---------- INDEX: all universities on the platform -------------------
        public async Task<IActionResult> Index()
        {
            var universities = await _db.Universities
                .OrderBy(u => u.Name)
                .ToListAsync();

            var enabledCounts = await _db.UniversityServices
                .Where(us => us.IsEnabled)
                .GroupBy(us => us.UniversityCode)
                .Select(g => new { UniversityCode = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.UniversityCode, x => x.Count);

            ViewBag.EnabledCounts = enabledCounts;
            return View(universities);
        }

        // ---------- CREATE (GET) ---------------------------------------------
        public IActionResult Create() => View(new UniversityCreateVM());

        // ---------- CREATE (POST) --------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
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

            var university = new University
            {
                Code = code,
                Name = vm.Name.Trim(),
                ApiBaseUrl = vm.ApiBaseUrl.Trim(),
                ApiKey = vm.ApiKey.Trim(),
                IsActive = true
            };
            _db.Universities.Add(university);
            await _db.SaveChangesAsync();

            // Don't make the admin wait for the next scheduled sync cycle to
            // see whether the connection actually works.
            await _syncRunner.SyncOneUniversityAsync(university);

            // Every university gets exactly one internship-posting account,
            // created automatically here rather than through self-registration
            // — a real university partner already has a real career services
            // department and email; there's no separate "company" to sign up.
            var generatedPassword = GenerateSecurePassword();
            var careerServicesUser = new ApplicationUser
            {
                UserName = vm.CareerServicesEmail.Trim(),
                Email = vm.CareerServicesEmail.Trim(),
                EmailConfirmed = true, // admin-provisioned, same as Staff/Instructor accounts
                FullName = $"{university.Name} — Career Services",
                UniversityCode = university.Code,
                UniversityId = $"CAREER-{code}",
            };
            var createResult = await _userManager.CreateAsync(careerServicesUser, generatedPassword);
            if (createResult.Succeeded)
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

                TempData["Success"] = $"{university.Name} added and synced (status: {university.LastSyncStatus}). " +
                    $"Career services login created — email: {vm.CareerServicesEmail}, password: {generatedPassword} " +
                    "(save this now, it won't be shown again). Now choose which services to enable.";
            }
            else
            {
                TempData["Success"] = $"{university.Name} added and synced (status: {university.LastSyncStatus}), " +
                    "but creating its career services login failed — you can add one manually later.";
            }

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

        // ---------- SYNC NOW (manual trigger) ----------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SyncNow(string code)
        {
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            await _syncRunner.SyncOneUniversityAsync(university);

            TempData["Success"] = university.LastSyncStatus == "Success"
                ? $"{university.Name} synced successfully."
                : $"Sync failed for {university.Name}: {university.LastSyncError}";
            return RedirectToAction(nameof(Index));
        }

        // ---------- TOGGLE ACTIVE ---------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(string code)
        {
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            university.IsActive = !university.IsActive;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{university.Name} is now {(university.IsActive ? "active" : "inactive")}.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- DELETE (full teardown) -------------------------------------
        // Removes the university and everything scoped to it: courses,
        // enrollments (cascade from students), tickets/ticket categories, the
        // service-enablement rows, and any accounts registered under it.
        //
        // This is meant for cleaning up test universities you just created —
        // if real activity exists (rides, study groups, ticket responses, etc.
        // created by an account tied to this university), those tables still
        // protect themselves with a Restrict delete rule, so this will fail
        // safely with a clear message rather than silently orphaning data.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string code)
        {
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            try
            {
                // Students first (their Enrollments cascade automatically).
                var students = await _db.Students.Where(s => s.UniversityCode == code).ToListAsync();
                _db.Students.RemoveRange(students);
                await _db.SaveChangesAsync();

                // Any login accounts registered under this university —
                // UserManager handles Identity's own related tables correctly.
                var accounts = await _userManager.Users.Where(u => u.UniversityCode == code).ToListAsync();
                foreach (var account in accounts)
                    await _userManager.DeleteAsync(account);

                // The university itself — Courses, UniversityServices, and
                // TicketCategories all cascade-delete automatically.
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
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            var allServices = await _db.Services.OrderBy(s => s.Name).ToListAsync();
            var enabledCodes = await _db.UniversityServices
                .Where(us => us.UniversityCode == code && us.IsEnabled)
                .Select(us => us.ServiceCode)
                .ToListAsync();

            ViewBag.University = university;
            ViewBag.EnabledCodes = enabledCodes;

            // Populated by the sync job (or the immediate sync right after
            // creation) — reads whatever was most recently pulled from this
            // university's external API and cached locally.
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
            var university = await _db.Universities.FindAsync(code);
            if (university is null) return NotFound();

            enabledServiceCodes ??= new List<string>();

            // A university can only actually ENABLE a service that's really
            // built — defense in depth in case the posted list is tampered
            // with, matching the same rule ServiceCatalogService enforces.
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
            return RedirectToAction(nameof(Index));
        }
    }
}
