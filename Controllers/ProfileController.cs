using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-06: "The system shall allow a user to manage their profile" —
    /// academic fields (Full Name, University ID, email) are read-only here
    /// since they're sourced from the adapter/registration, not editable by
    /// the user directly. Only Phone Number and Profile Picture are
    /// genuinely editable platform fields.
    ///
    /// Deliberately available to EVERY authenticated role (student, staff,
    /// instructor, company, admin) — this is basic account management, not
    /// a specific service, so it's not gated by RequireServiceAttribute.
    /// </summary>
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;
        private readonly AuditLogService _auditLog;
        private readonly IServiceCatalogService _serviceCatalog;

        private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg" };
        private const long MaxImageBytes = 2 * 1024 * 1024; // 2 MB

        public ProfileController(
            UserManager<ApplicationUser> userManager, IWebHostEnvironment env,
            AuditLogService auditLog, IServiceCatalogService serviceCatalog)
        {
            _userManager = userManager;
            _env = env;
            _auditLog = auditLog;
            _serviceCatalog = serviceCatalog;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            ViewBag.User = user;
            // Only show links to services this university actually has
            // enabled — a university without Ride Sharing/Internships
            // turned on shouldn't dead-end a student into those pages.
            ViewBag.ShowVehicles = await _serviceCatalog.IsServiceEnabledAsync(user.UniversityCode, ServiceCodes.RideSharing);
            ViewBag.ShowCareerProfile = await _serviceCatalog.IsServiceEnabledAsync(user.UniversityCode, ServiceCodes.Internships);

            return View(new ProfileEditVM { PhoneNumber = user.PhoneNumber });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ProfileEditVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!ModelState.IsValid)
            {
                ViewBag.User = user;
                ViewBag.ShowVehicles = await _serviceCatalog.IsServiceEnabledAsync(user.UniversityCode, ServiceCodes.RideSharing);
                ViewBag.ShowCareerProfile = await _serviceCatalog.IsServiceEnabledAsync(user.UniversityCode, ServiceCodes.Internships);
                return View(nameof(Index), vm);
            }

            user.PhoneNumber = string.IsNullOrWhiteSpace(vm.PhoneNumber) ? null : vm.PhoneNumber.Trim();

            if (vm.ProfilePicture is { Length: > 0 })
            {
                var ext = Path.GetExtension(vm.ProfilePicture.FileName).ToLowerInvariant();
                if (!AllowedImageExtensions.Contains(ext))
                {
                    TempData["Error"] = "Unsupported image format — please upload a PNG or JPEG.";
                    return RedirectToAction(nameof(Index));
                }
                if (vm.ProfilePicture.Length > MaxImageBytes)
                {
                    TempData["Error"] = $"That image is too large — the maximum is {MaxImageBytes / (1024 * 1024)} MB.";
                    return RedirectToAction(nameof(Index));
                }

                // Remove the previous picture, if any, before saving the new one.
                if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, user.ProfilePicturePath.TrimStart('/'));
                    if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                }

                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profiles");
                Directory.CreateDirectory(uploadsDir);
                var storedName = $"{Guid.NewGuid()}{ext}";
                using (var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create))
                    await vm.ProfilePicture.CopyToAsync(stream);

                user.ProfilePicturePath = $"/uploads/profiles/{storedName}";
            }

            await _userManager.UpdateAsync(user);

            await _auditLog.LogAsync(
                "ProfileUpdated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id);

            TempData["Success"] = "Profile updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePicture()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!string.IsNullOrWhiteSpace(user.ProfilePicturePath))
            {
                var path = Path.Combine(_env.WebRootPath, user.ProfilePicturePath.TrimStart('/'));
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

                user.ProfilePicturePath = null;
                await _userManager.UpdateAsync(user);
            }

            TempData["Success"] = "Profile picture removed.";
            return RedirectToAction(nameof(Index));
        }
    }
}
