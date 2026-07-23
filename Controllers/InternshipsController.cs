using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Student-facing side of the Internship and Career Matching service
    /// (UC-07): browse/search, view a live matching score, apply, and
    /// track/withdraw applications.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Internships)]
    public class InternshipsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly MatchingScoreService _matching;
        private readonly NotificationService _notifications;
        private readonly AuditLogService _auditLog;
        private readonly IUniversityProviderResolver _resolver;
        private readonly ILogger<InternshipsController> _logger;

        public InternshipsController(
            ApplicationDbContext db, UserManager<ApplicationUser> userManager,
            MatchingScoreService matching, NotificationService notifications, AuditLogService auditLog,
            IUniversityProviderResolver resolver, ILogger<InternshipsController> logger)
        {
            _db = db;
            _userManager = userManager;
            _matching = matching;
            _notifications = notifications;
            _auditLog = auditLog;
            _resolver = resolver;
            _logger = logger;
        }

        // Fetched ONCE per request (not per-listing) and passed into
        // MatchingScoreService.CalculateAsync — a student's major doesn't
        // change between listings on the same page, so re-fetching it via
        // the adapter for every single internship in the browse loop would
        // be a wasteful, redundant call. Returns null (treated as neutral —
        // never penalized) if the student has no major on file or the
        // adapter call fails for any reason.
        private async Task<string?> GetStudentMajorAsync(ApplicationUser user)
        {
            try
            {
                var provider = await _resolver.GetProviderAsync(user.UniversityCode);
                var info = await provider.GetStudentInfoAsync(user.UniversityCode, user.UniversityId);
                return info?.Major;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not retrieve major for {User} — major-based matching will be neutral for this request.",
                    user.Id);
                return null;
            }
        }

        // ---------- INDEX: browse / search / filter (FR-40) --------------------
        public async Task<IActionResult> Index(string? skill, string? location, int? maxDuration, string? sort, bool myMajorOnly = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var query = _db.Internships
                .Include(i => i.Company)
                .Where(i => i.IsActive && i.ApplicationDeadline >= DateTime.Today
                         && i.Company!.UniversityCode == user.UniversityCode);

            if (!string.IsNullOrWhiteSpace(skill))
                query = query.Where(i => i.RequiredSkills != null && i.RequiredSkills.Contains(skill));
            if (!string.IsNullOrWhiteSpace(location))
                query = query.Where(i => i.Location.Contains(location));
            if (maxDuration.HasValue)
                query = query.Where(i => i.DurationWeeks == null || i.DurationWeeks <= maxDuration);

            var internships = await query.ToListAsync();

            // Fetched/built once for the whole request — see
            // GetStudentMajorAsync's and BuildCorpusAsync's own comments for
            // why these must not happen inside the loop below.
            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();

            // "Show only internships for my major" — only actually filters
            // if we know the student's major; if we don't (undeclared, or
            // the adapter couldn't tell us), the checkbox is harmless and
            // simply shows everything, consistent with treating an unknown
            // major as neutral everywhere else in this feature.
            if (myMajorOnly && !string.IsNullOrWhiteSpace(studentMajor))
            {
                internships = internships.Where(i =>
                {
                    var relevantMajors = string.IsNullOrWhiteSpace(i.RelevantMajors)
                        ? new List<string>()
                        : i.RelevantMajors.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
                    // Open-to-all postings (no majors specified) still count
                    // as relevant — "my major only" shouldn't hide genuinely
                    // open opportunities, only ones explicitly for OTHER majors.
                    return relevantMajors.Count == 0
                        || relevantMajors.Any(m => string.Equals(m, studentMajor, StringComparison.OrdinalIgnoreCase));
                }).ToList();
            }

            // Live matching score for every listing — Edge Case: "Matching
            // score ties — order by recency... display the score transparently."
            var scored = new List<(Internship Internship, int Score, bool CourseDataAvailable)>();
            foreach (var i in internships)
            {
                var result = await _matching.CalculateAsync(user, i, studentMajor, corpus);
                scored.Add((i, result.Score, result.CourseDataAvailable));
            }

            scored = scored
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Internship.CreatedAt)
                .ToList();

            ViewBag.Scored = scored;
            ViewBag.SearchSkill = skill;
            ViewBag.SearchLocation = location;
            ViewBag.MaxDuration = maxDuration;
            ViewBag.MyMajorOnly = myMajorOnly;
            ViewBag.StudentMajor = studentMajor;

            // Edge Case: "No matching internships — student's skills and
            // courses do not match any available internship. The system
            // shall display a message and suggest updating their career
            // profile." — trigger this specifically when the BEST score is
            // low, not just when the list happens to be empty from filters.
            var hasProfile = await _db.CareerProfiles.AnyAsync(p => p.UserId == user.Id);
            var hasSkills = await _db.StudentSkills.AnyAsync(s => s.UserId == user.Id);
            ViewBag.SuggestProfileUpdate = scored.Count > 0 && scored.Max(x => x.Score) < 20 && (!hasProfile || !hasSkills);

            return View();
        }

        // ---------- DETAILS: listing + live matching score + apply form -------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var internship = await _db.Internships.Include(i => i.Company).FirstOrDefaultAsync(i => i.Id == id);
            if (internship is null) return NotFound();

            // Cross-university guard — a student should only ever reach
            // postings from their own university's career services account,
            // even via a direct URL, not just via the (already-filtered)
            // browse list.
            if (internship.Company?.UniversityCode != user.UniversityCode)
                return NotFound();

            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();
            var result = await _matching.CalculateAsync(user, internship, studentMajor, corpus);
            ViewBag.Score = result.Score;
            ViewBag.CourseDataAvailable = result.CourseDataAvailable;

            var existingApplication = await _db.InternshipApplications
                .FirstOrDefaultAsync(a => a.InternshipId == id && a.UserId == user.Id);
            ViewBag.ExistingApplication = existingApplication;

            var acceptedCount = await _db.InternshipApplications.CountAsync(
                a => a.InternshipId == id && a.Status == InternshipApplicationStatus.Accepted);
            ViewBag.PositionsFilled = acceptedCount >= internship.NumberOfPositions;
            ViewBag.DeadlinePassed = internship.ApplicationDeadline < DateTime.Today;

            // The student's CV/skills matter directly here — CV gets
            // forwarded to the real employer in the shortlist email, and
            // skills feed the matching score — so surface their current
            // status right on the apply page instead of leaving them to
            // separately discover Career Profile exists.
            var careerProfile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var skillCount = await _db.StudentSkills.CountAsync(s => s.UserId == user.Id);
            ViewBag.HasCv = !string.IsNullOrWhiteSpace(careerProfile?.CvFilePath);
            ViewBag.SkillCount = skillCount;

            return View(internship);
        }

        // ---------- APPLY (FR-42) ----------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(int internshipId, string? coverMessage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var internship = await _db.Internships.Include(i => i.Company).FirstOrDefaultAsync(i => i.Id == internshipId);
            if (internship is null) return NotFound();

            // Cross-university guard — same as Details, defends a direct POST.
            if (internship.Company?.UniversityCode != user.UniversityCode)
                return NotFound();

            // ListingOnly postings never accept in-app applications — the
            // UI already hides the form for these, but defend against a
            // direct POST too.
            if (internship.PostingMode == InternshipPostingMode.ListingOnly)
            {
                TempData["Error"] = "This listing accepts applications through the employer's own link/email, not in UniConnect.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            if (!internship.IsActive)
            {
                TempData["Error"] = "This internship is no longer accepting applications.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            // Edge Case: "Application after deadline."
            if (internship.ApplicationDeadline < DateTime.Today)
            {
                TempData["Error"] = "The application deadline for this internship has passed.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            // Edge Case: "Duplicate application" (also backstopped by a unique
            // DB index in case of a race between two near-simultaneous clicks).
            var alreadyApplied = await _db.InternshipApplications.AnyAsync(
                a => a.InternshipId == internshipId && a.UserId == user.Id);
            if (alreadyApplied)
            {
                TempData["Error"] = "You've already applied to this internship.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            // Edge Case: "All positions filled while new applications are pending."
            var acceptedCount = await _db.InternshipApplications.CountAsync(
                a => a.InternshipId == internshipId && a.Status == InternshipApplicationStatus.Accepted);
            if (acceptedCount >= internship.NumberOfPositions)
            {
                TempData["Error"] = "All positions for this internship have already been filled.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();
            var scoreResult = await _matching.CalculateAsync(user, internship, studentMajor, corpus);

            var application = new InternshipApplication
            {
                InternshipId = internshipId,
                UserId = user.Id,
                CoverMessage = string.IsNullOrWhiteSpace(coverMessage) ? null : coverMessage.Trim(),
                MatchingScore = scoreResult.Score,
                Status = InternshipApplicationStatus.Submitted,
                AppliedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _db.InternshipApplications.Add(application);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The unique index caught a race (two near-simultaneous
                // submits) that the earlier check missed.
                TempData["Error"] = "You've already applied to this internship.";
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            // FR-44: notify the company of the new application.
            if (internship.Company is not null)
            {
                await _notifications.NotifyAsync(
                    internship.Company.UserId,
                    "New internship application",
                    $"{user.FullName} applied to \"{internship.Title}\" (matching score: {scoreResult.Score}).",
                    "/Company/Applications?internshipId=" + internshipId);
            }

            TempData["Success"] = "Application submitted!";
            await _auditLog.LogAsync(
                "InternshipApplicationSubmitted",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "InternshipApplication",
                entityId: application.Id.ToString(),
                details: $"Internship: {internship.Title}, score: {scoreResult.Score}");
            return RedirectToAction(nameof(MyApplications));
        }

        // ---------- MY APPLICATIONS + WITHDRAW (A1) ----------------------------
        public async Task<IActionResult> MyApplications()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var applications = await _db.InternshipApplications
                .Include(a => a.Internship).ThenInclude(i => i!.Company)
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.AppliedAt)
                .ToListAsync();

            return View(applications);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var application = await _db.InternshipApplications.FirstOrDefaultAsync(a => a.Id == id && a.UserId == user.Id);
            if (application is null) return NotFound();

            var terminal = new[] { InternshipApplicationStatus.Accepted, InternshipApplicationStatus.Rejected, InternshipApplicationStatus.Withdrawn };
            if (terminal.Contains(application.Status))
            {
                TempData["Error"] = "This application can no longer be withdrawn.";
                return RedirectToAction(nameof(MyApplications));
            }

            application.Status = InternshipApplicationStatus.Withdrawn;
            application.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Success"] = "Application withdrawn.";
            return RedirectToAction(nameof(MyApplications));
        }
    }
}
