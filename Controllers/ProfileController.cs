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
        private readonly ProfileService _service;
        private readonly IServiceCatalogService _serviceCatalog;

        public ProfileController(
            UserManager<ApplicationUser> userManager,
            ProfileService service,
            IServiceCatalogService serviceCatalog)
        {
            _userManager = userManager;
            _service = service;
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

            var upload = vm.ProfilePicture is { Length: > 0 }
                ? new ProfileService.PictureUpload(
                    vm.ProfilePicture.OpenReadStream(), vm.ProfilePicture.FileName, vm.ProfilePicture.Length)
                : null;

            var result = await _service.UpdateAsync(user, vm.PhoneNumber, upload);

            if (!result.Ok)
            {
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["Success"] = "Profile updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemovePicture()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var result = await _service.RemovePictureAsync(user);

            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Index));
        }
    }
}
