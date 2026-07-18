using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-35 (career profile), FR-36 (CV), FR-37 (skills). Company users
    /// don't have career profiles — this is student-only, enforced by
    /// gating on the Internships service (companies use a separate
    /// dashboard, see CompanyController).
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Internships)]
    public class CareerProfileController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _env;

        // CVs are a common target for oversized/wrong-format uploads —
        // Edge Case: "CV upload fails — the CV file is too large or in an
        // unsupported format. The system shall reject the upload and
        // display format requirements."
        private static readonly string[] AllowedCvExtensions = { ".pdf", ".doc", ".docx" };
        private const long MaxCvSizeBytes = 5 * 1024 * 1024; // 5 MB

        public CareerProfileController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IWebHostEnvironment env)
        {
            _db = db;
            _userManager = userManager;
            _env = env;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var skills = await _db.StudentSkills.Where(s => s.UserId == user.Id).ToListAsync();

            ViewBag.Skills = skills;
            ViewBag.CvFileName = profile?.CvFileName;
            return View(profile is null ? new CareerProfileEditVM() : new CareerProfileEditVM
            {
                CareerInterests = profile.CareerInterests,
                CareerGoals = profile.CareerGoals,
                PreferredLocation = profile.PreferredLocation,
                Availability = profile.Availability
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CareerProfileEditVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!ModelState.IsValid)
            {
                ViewBag.Skills = await _db.StudentSkills.Where(s => s.UserId == user.Id).ToListAsync();
                ViewBag.CvFileName = (await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id))?.CvFileName;
                return View(nameof(Index), vm);
            }

            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile is null)
            {
                profile = new CareerProfile { UserId = user.Id, CreatedAt = DateTime.UtcNow };
                _db.CareerProfiles.Add(profile);
            }

            profile.CareerInterests = vm.CareerInterests?.Trim();
            profile.CareerGoals = vm.CareerGoals?.Trim();
            profile.PreferredLocation = vm.PreferredLocation?.Trim();
            profile.Availability = vm.Availability?.Trim();
            profile.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            TempData["Success"] = "Career profile updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadCv(IFormFile cv)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (cv is null || cv.Length == 0)
            {
                TempData["Error"] = "Please choose a file.";
                return RedirectToAction(nameof(Index));
            }

            var ext = Path.GetExtension(cv.FileName).ToLowerInvariant();
            if (!AllowedCvExtensions.Contains(ext))
            {
                TempData["Error"] = $"Unsupported file format. Please upload a PDF or Word document ({string.Join(", ", AllowedCvExtensions)}).";
                return RedirectToAction(nameof(Index));
            }
            if (cv.Length > MaxCvSizeBytes)
            {
                TempData["Error"] = $"That file is too large — the maximum CV size is {MaxCvSizeBytes / (1024 * 1024)} MB.";
                return RedirectToAction(nameof(Index));
            }

            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile is null)
            {
                profile = new CareerProfile { UserId = user.Id, CreatedAt = DateTime.UtcNow };
                _db.CareerProfiles.Add(profile);
            }

            // Remove the previous file, if any, before saving the new one.
            if (!string.IsNullOrWhiteSpace(profile.CvFilePath))
            {
                var oldPath = Path.Combine(_env.WebRootPath, profile.CvFilePath.TrimStart('/'));
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "cvs");
            Directory.CreateDirectory(uploadsDir);
            var storedName = $"{Guid.NewGuid()}{ext}";
            using (var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create))
                await cv.CopyToAsync(stream);

            profile.CvFilePath = $"/uploads/cvs/{storedName}";
            profile.CvFileName = cv.FileName;
            profile.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = "CV uploaded.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCv()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            if (profile?.CvFilePath is not null)
            {
                var path = Path.Combine(_env.WebRootPath, profile.CvFilePath.TrimStart('/'));
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

                profile.CvFilePath = null;
                profile.CvFileName = null;
                profile.UpdatedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = "CV removed.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddSkill(string skillName, SkillProficiency? proficiencyLevel)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!string.IsNullOrWhiteSpace(skillName))
            {
                var exists = await _db.StudentSkills.AnyAsync(
                    s => s.UserId == user.Id && s.SkillName.ToLower() == skillName.Trim().ToLower());
                if (!exists)
                {
                    _db.StudentSkills.Add(new StudentSkill
                    {
                        UserId = user.Id,
                        SkillName = skillName.Trim(),
                        ProficiencyLevel = proficiencyLevel
                    });
                    await _db.SaveChangesAsync();
                }
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveSkill(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var skill = await _db.StudentSkills.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id);
            if (skill is not null)
            {
                _db.StudentSkills.Remove(skill);
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
