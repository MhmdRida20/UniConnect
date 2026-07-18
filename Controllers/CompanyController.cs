using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// The university's own internship-posting tool (UC-08). Every account
    /// reaching this controller is a university's career services login
    /// (see AdminUniversitiesController.Create) — there is no path where a
    /// real external company logs in itself; every posting is always made
    /// ON BEHALF OF a real named employer (Internship.ExternalEmployerName).
    ///
    /// The two posting modes:
    ///   ListingOnly     — no in-app applications; students are given the
    ///                     employer's own apply link and/or apply email.
    ///   FullApplication — students apply in-app; the university reviews
    ///                     everyone and forwards the best candidates to the
    ///                     real employer by email (SendShortlist below) —
    ///                     since a real employer normally has no UniConnect
    ///                     login of their own to grant dashboard access to.
    /// </summary>
    [Authorize(Roles = "Company")]
    [RequireService(ServiceCodes.Internships)]
    public class CompanyController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly NotificationService _notifications;
        private readonly IWebHostEnvironment _env;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;

        public CompanyController(
            ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            NotificationService notifications, IWebHostEnvironment env,
            IEmailSender emailSender, IConfiguration config)
        {
            _db = db;
            _userManager = userManager;
            _notifications = notifications;
            _env = env;
            _emailSender = emailSender;
            _config = config;
        }

        private async Task<Company?> GetMyCompanyAsync()
        {
            var userId = _userManager.GetUserId(User);
            return await _db.Companies.FirstOrDefaultAsync(c => c.UserId == userId);
        }

        // Edge Case (UC-08 E1) extended for the mode-specific requirements
        // [Required] alone can't express, since it depends on which mode
        // was chosen. The employer name is required regardless of mode —
        // every posting is on behalf of someone real.
        private void ValidatePostingMode(InternshipPostVM vm)
        {
            if (string.IsNullOrWhiteSpace(vm.ExternalEmployerName))
                ModelState.AddModelError(nameof(vm.ExternalEmployerName), "Please enter the employer's name.");

            if (vm.PostingMode == InternshipPostingMode.ListingOnly
                && string.IsNullOrWhiteSpace(vm.ExternalApplyUrl)
                && string.IsNullOrWhiteSpace(vm.ExternalApplyEmail))
            {
                ModelState.AddModelError(nameof(vm.ExternalApplyUrl), "Provide an application link, an application email, or both.");
            }
        }

        private async Task<string?> SaveEmployerLogoAsync(IFormFile? logo)
        {
            if (logo is not { Length: > 0 }) return null;
            var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
            if (!new[] { ".png", ".jpg", ".jpeg" }.Contains(ext) || logo.Length > 2 * 1024 * 1024) return null;

            var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "companies");
            Directory.CreateDirectory(uploadsDir);
            var storedName = $"{Guid.NewGuid()}{ext}";
            using var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create);
            await logo.CopyToAsync(stream);
            return $"/uploads/companies/{storedName}";
        }

        // ---------- DASHBOARD: profile + my postings ---------------------------
        public async Task<IActionResult> Index()
        {
            var company = await GetMyCompanyAsync();
            if (company is null) return NotFound();

            var internships = await _db.Internships
                .Include(i => i.Applications)
                .Where(i => i.CompanyId == company.Id)
                .OrderByDescending(i => i.CreatedAt)
                .ToListAsync();

            ViewBag.Company = company;
            return View(internships);
        }

        // ---------- POST INTERNSHIP (GET) — FR-39 -------------------------------
        public IActionResult PostInternship() => View(new InternshipPostVM());

        // ---------- POST INTERNSHIP (POST) --------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostInternship(InternshipPostVM vm)
        {
            var company = await GetMyCompanyAsync();
            if (company is null) return NotFound();

            ValidatePostingMode(vm);
            if (!ModelState.IsValid) return View(vm);

            var internship = new Internship
            {
                CompanyId = company.Id,
                Title = vm.Title.Trim(),
                Description = vm.Description.Trim(),
                RequiredSkills = vm.RequiredSkills?.Trim(),
                RecommendedCourses = vm.RecommendedCourses?.Trim(),
                RelevantMajors = vm.RelevantMajors?.Trim(),
                Location = vm.Location.Trim(),
                DurationWeeks = vm.DurationWeeks,
                ApplicationDeadline = vm.ApplicationDeadline,
                NumberOfPositions = vm.NumberOfPositions,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                PostingMode = vm.PostingMode,
                ExternalEmployerName = vm.ExternalEmployerName.Trim(),
                ExternalEmployerContactEmail = vm.ExternalEmployerContactEmail?.Trim(),
                ExternalEmployerLogoPath = await SaveEmployerLogoAsync(vm.ExternalEmployerLogo),
                ExternalApplyUrl = vm.PostingMode == InternshipPostingMode.ListingOnly ? vm.ExternalApplyUrl?.Trim() : null,
                ExternalApplyEmail = vm.PostingMode == InternshipPostingMode.ListingOnly ? vm.ExternalApplyEmail?.Trim() : null
            };
            _db.Internships.Add(internship);
            await _db.SaveChangesAsync();

            TempData["Success"] = "Internship posted — visible to students now.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- EDIT INTERNSHIP ---------------------------------------------
        public async Task<IActionResult> EditInternship(int id)
        {
            var company = await GetMyCompanyAsync();
            var internship = await _db.Internships.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == company!.Id);
            if (internship is null) return NotFound();

            ViewBag.InternshipId = id;
            return View(new InternshipPostVM
            {
                Title = internship.Title,
                Description = internship.Description,
                RequiredSkills = internship.RequiredSkills,
                RecommendedCourses = internship.RecommendedCourses,
                RelevantMajors = internship.RelevantMajors,
                Location = internship.Location,
                DurationWeeks = internship.DurationWeeks,
                ApplicationDeadline = internship.ApplicationDeadline,
                NumberOfPositions = internship.NumberOfPositions,
                PostingMode = internship.PostingMode,
                ExternalEmployerName = internship.ExternalEmployerName,
                ExternalEmployerContactEmail = internship.ExternalEmployerContactEmail,
                ExternalApplyUrl = internship.ExternalApplyUrl,
                ExternalApplyEmail = internship.ExternalApplyEmail
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditInternship(int id, InternshipPostVM vm)
        {
            var company = await GetMyCompanyAsync();
            var internship = await _db.Internships.FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == company!.Id);
            if (internship is null) return NotFound();

            ValidatePostingMode(vm);
            if (!ModelState.IsValid)
            {
                ViewBag.InternshipId = id;
                return View(vm);
            }

            internship.Title = vm.Title.Trim();
            internship.Description = vm.Description.Trim();
            internship.RequiredSkills = vm.RequiredSkills?.Trim();
            internship.RecommendedCourses = vm.RecommendedCourses?.Trim();
            internship.RelevantMajors = vm.RelevantMajors?.Trim();
            internship.Location = vm.Location.Trim();
            internship.DurationWeeks = vm.DurationWeeks;
            internship.ApplicationDeadline = vm.ApplicationDeadline;
            internship.NumberOfPositions = vm.NumberOfPositions;
            internship.PostingMode = vm.PostingMode;
            internship.ExternalEmployerName = vm.ExternalEmployerName.Trim();
            internship.ExternalEmployerContactEmail = vm.ExternalEmployerContactEmail?.Trim();
            if (vm.ExternalEmployerLogo is { Length: > 0 })
                internship.ExternalEmployerLogoPath = await SaveEmployerLogoAsync(vm.ExternalEmployerLogo);

            if (vm.PostingMode == InternshipPostingMode.ListingOnly)
            {
                internship.ExternalApplyUrl = vm.ExternalApplyUrl?.Trim();
                internship.ExternalApplyEmail = vm.ExternalApplyEmail?.Trim();
            }
            else
            {
                internship.ExternalApplyUrl = null;
                internship.ExternalApplyEmail = null;
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "Internship updated.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- TOGGLE ACTIVE (deactivate/reactivate) -----------------------
        // Edge Case: "Company deactivates internship with active applications —
        // the system shall notify affected applicants and update application
        // statuses."
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleActive(int id)
        {
            var company = await GetMyCompanyAsync();
            var internship = await _db.Internships.Include(i => i.Applications)
                .FirstOrDefaultAsync(i => i.Id == id && i.CompanyId == company!.Id);
            if (internship is null) return NotFound();

            var wasActive = internship.IsActive;
            internship.IsActive = !internship.IsActive;

            if (wasActive && !internship.IsActive)
            {
                var nonTerminal = new[]
                {
                    InternshipApplicationStatus.Submitted,
                    InternshipApplicationStatus.UnderReview,
                    InternshipApplicationStatus.Shortlisted
                };
                var affected = internship.Applications.Where(a => nonTerminal.Contains(a.Status)).ToList();

                foreach (var app in affected)
                {
                    app.Status = InternshipApplicationStatus.Rejected;
                    app.UpdatedAt = DateTime.UtcNow;
                    await _notifications.NotifyAsync(
                        app.UserId,
                        "Internship listing closed",
                        $"\"{internship.Title}\" was closed before a decision was made on your application.",
                        "/Internships/MyApplications");
                }
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = internship.IsActive ? "Internship reactivated." : "Internship deactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- REVIEW APPLICATIONS (UC-08 A1) ------------------------------
        public async Task<IActionResult> Applications(int internshipId)
        {
            var company = await GetMyCompanyAsync();
            var internship = await _db.Internships.FirstOrDefaultAsync(i => i.Id == internshipId && i.CompanyId == company!.Id);
            if (internship is null) return NotFound();

            var applications = await _db.InternshipApplications
                .Include(a => a.User)
                .Where(a => a.InternshipId == internshipId)
                .OrderByDescending(a => a.MatchingScore)
                .ThenByDescending(a => a.AppliedAt)
                .ToListAsync();

            var userIds = applications.Select(a => a.UserId).ToList();
            var profiles = await _db.CareerProfiles.Where(p => userIds.Contains(p.UserId)).ToListAsync();
            var skills = await _db.StudentSkills.Where(s => userIds.Contains(s.UserId)).ToListAsync();

            ViewBag.Internship = internship;
            ViewBag.Profiles = profiles;
            ViewBag.SkillsByUser = skills.GroupBy(s => s.UserId).ToDictionary(g => g.Key, g => g.ToList());

            return View(applications);
        }

        // ---------- SEND SHORTLIST TO EXTERNAL EMPLOYER -------------------------
        // For FullApplication postings only — the real employer normally has
        // no UniConnect account, so this emails them the current Shortlisted
        // applicants directly. Re-runnable any time more candidates get
        // shortlisted — always sends the FULL current shortlist.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendShortlist(int internshipId)
        {
            var company = await GetMyCompanyAsync();
            var internship = await _db.Internships
                .Include(i => i.Applications).ThenInclude(a => a.User)
                .FirstOrDefaultAsync(i => i.Id == internshipId && i.CompanyId == company!.Id);
            if (internship is null) return NotFound();

            if (internship.PostingMode != InternshipPostingMode.FullApplication)
            {
                TempData["Error"] = "Sending a shortlist only applies to postings that accept in-app applications.";
                return RedirectToAction(nameof(Applications), new { internshipId });
            }

            if (string.IsNullOrWhiteSpace(internship.ExternalEmployerContactEmail))
            {
                TempData["Error"] = "This posting has no employer contact email on file — edit the internship to add one.";
                return RedirectToAction(nameof(Applications), new { internshipId });
            }

            // Only candidates who are BOTH currently Shortlisted AND haven't
            // already been sent — this is what makes repeat sends behave as
            // "send additional candidates" rather than re-forwarding people
            // the employer has already seen.
            var toSend = internship.Applications
                .Where(a => a.Status == InternshipApplicationStatus.Shortlisted && !a.SentToEmployerAt.HasValue)
                .OrderByDescending(a => a.MatchingScore)
                .ToList();

            if (toSend.Count == 0)
            {
                TempData["Error"] = "No new shortlisted candidates to send — everyone currently shortlisted has already been forwarded.";
                return RedirectToAction(nameof(Applications), new { internshipId });
            }

            var alreadySentCount = internship.Applications.Count(a => a.SentToEmployerAt.HasValue);
            var isFollowUp = alreadySentCount > 0;

            var appBaseUrl = (_config["App:BaseUrl"] ?? $"{Request.Scheme}://{Request.Host}").TrimEnd('/');
            var userIds = toSend.Select(a => a.UserId).ToList();
            var profiles = await _db.CareerProfiles.Where(p => userIds.Contains(p.UserId)).ToListAsync();
            var skillsByUser = (await _db.StudentSkills.Where(s => userIds.Contains(s.UserId)).ToListAsync())
                .GroupBy(s => s.UserId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var body = new StringBuilder();
            body.Append($"<p>Dear {System.Net.WebUtility.HtmlEncode(internship.ExternalEmployerName)} team,</p>");
            body.Append(isFollowUp
                ? $"<p>{System.Net.WebUtility.HtmlEncode(company!.CompanyName)} has identified {toSend.Count} additional " +
                  $"strong candidate(s) for <strong>\"{System.Net.WebUtility.HtmlEncode(internship.Title)}\"</strong>, " +
                  "beyond those already sent:</p><ol>"
                : $"<p>{System.Net.WebUtility.HtmlEncode(company!.CompanyName)} has reviewed all applications for " +
                  $"<strong>\"{System.Net.WebUtility.HtmlEncode(internship.Title)}\"</strong> and identified the " +
                  $"following {toSend.Count} candidate(s) as the strongest fit, based on their skills, " +
                  "coursework, and stated interests:</p><ol>");

            foreach (var app in toSend)
            {
                var profile = profiles.FirstOrDefault(p => p.UserId == app.UserId);
                var name = System.Net.WebUtility.HtmlEncode(app.User?.FullName ?? "Unknown");
                var email = System.Net.WebUtility.HtmlEncode(app.User?.Email ?? "—");
                var cvLink = !string.IsNullOrWhiteSpace(profile?.CvFilePath)
                    ? $"<a href='{appBaseUrl}{profile.CvFilePath}'>Download CV</a>"
                    : "No CV on file";

                var skills = skillsByUser.TryGetValue(app.UserId, out var s) ? s : new List<StudentSkill>();
                var skillsText = skills.Count > 0
                    ? string.Join(", ", skills.Select(sk => System.Net.WebUtility.HtmlEncode(
                        sk.ProficiencyLevel.HasValue ? $"{sk.SkillName} ({sk.ProficiencyLevel})" : sk.SkillName)))
                    : "No skills listed";

                body.Append("<li style='margin-bottom:14px;'>");
                body.Append($"<strong>{name}</strong> — Matching score: {app.MatchingScore ?? 0}%<br/>");
                body.Append($"Email: {email}<br/>");
                body.Append($"Skills: {skillsText}<br/>");
                body.Append($"{cvLink}<br/>");
                if (!string.IsNullOrWhiteSpace(app.CoverMessage))
                    body.Append($"Cover message: <em>{System.Net.WebUtility.HtmlEncode(app.CoverMessage)}</em>");
                body.Append("</li>");
            }

            body.Append("</ol>");
            body.Append($"<p>Please reach out to these candidates directly, or contact {System.Net.WebUtility.HtmlEncode(company.ContactEmail)} " +
                        "for more information.</p><p>— Sent via UniConnect</p>");

            await _emailSender.SendEmailAsync(
                internship.ExternalEmployerContactEmail,
                isFollowUp ? $"Additional Candidates — {internship.Title}" : $"Candidate Shortlist — {internship.Title}",
                body.ToString());

            var now = DateTime.UtcNow;
            foreach (var app in toSend)
                app.SentToEmployerAt = now;
            internship.ShortlistSentAt = now;
            await _db.SaveChangesAsync();

            TempData["Success"] = $"{toSend.Count} candidate(s) sent to {internship.ExternalEmployerContactEmail}.";
            return RedirectToAction(nameof(Applications), new { internshipId });
        }

        // ---------- UPDATE APPLICATION STATUS -----------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int applicationId, InternshipApplicationStatus newStatus)
        {
            var company = await GetMyCompanyAsync();
            var application = await _db.InternshipApplications
                .Include(a => a.Internship)
                .FirstOrDefaultAsync(a => a.Id == applicationId && a.Internship!.CompanyId == company!.Id);
            if (application is null) return NotFound();

            application.Status = newStatus;
            application.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _notifications.NotifyAsync(
                application.UserId,
                "Application status updated",
                $"Your application to \"{application.Internship!.Title}\" is now: {newStatus}.",
                "/Internships/MyApplications");

            if (newStatus == InternshipApplicationStatus.Accepted)
            {
                var internship = application.Internship!;
                var acceptedCount = await _db.InternshipApplications.CountAsync(
                    a => a.InternshipId == internship.Id && a.Status == InternshipApplicationStatus.Accepted);

                if (acceptedCount >= internship.NumberOfPositions)
                {
                    var pending = await _db.InternshipApplications
                        .Where(a => a.InternshipId == internship.Id
                                 && a.Id != application.Id
                                 && (a.Status == InternshipApplicationStatus.Submitted
                                     || a.Status == InternshipApplicationStatus.UnderReview
                                     || a.Status == InternshipApplicationStatus.Shortlisted))
                        .ToListAsync();

                    foreach (var pendingApp in pending)
                    {
                        await _notifications.NotifyAsync(
                            pendingApp.UserId,
                            "Positions filled",
                            $"All positions for \"{internship.Title}\" have been filled by other candidates.",
                            "/Internships/MyApplications");
                    }
                }
            }

            TempData["Success"] = "Application status updated.";
            return RedirectToAction(nameof(Applications), new { internshipId = application.InternshipId });
        }

        // ---------- EDIT PROFILE (the university's own posting-account profile) --
        public async Task<IActionResult> EditProfile()
        {
            var company = await GetMyCompanyAsync();
            if (company is null) return NotFound();
            return View(company);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(string companyName, string? description, string contactEmail, IFormFile? logo)
        {
            var company = await GetMyCompanyAsync();
            if (company is null) return NotFound();

            company.CompanyName = companyName.Trim();
            company.Description = description?.Trim();
            company.ContactEmail = contactEmail.Trim();

            if (logo is { Length: > 0 })
            {
                var ext = Path.GetExtension(logo.FileName).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg" }.Contains(ext) && logo.Length <= 2 * 1024 * 1024)
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "companies");
                    Directory.CreateDirectory(uploadsDir);
                    var storedName = $"{Guid.NewGuid()}{ext}";
                    using var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create);
                    await logo.CopyToAsync(stream);
                    company.LogoPath = $"/uploads/companies/{storedName}";
                }
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = "Profile updated.";
            return RedirectToAction(nameof(Index));
        }
    }
}
