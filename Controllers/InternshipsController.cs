using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Student-facing side of the Internship and Career Matching service
    /// (UC-07): browse/search, view a live matching score, apply, and
    /// track/withdraw applications.
    ///
    /// The rules live in InternshipService, which the mobile API calls too —
    /// this controller only chooses views and messages.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Internships)]
    public class InternshipsController : Controller
    {
        private readonly InternshipService _service;
        private readonly UserManager<ApplicationUser> _userManager;

        public InternshipsController(InternshipService service, UserManager<ApplicationUser> userManager)
        {
            _service = service;
            _userManager = userManager;
        }

        // ---------- INDEX: browse / search / filter (FR-40) --------------------
        public async Task<IActionResult> Index(
            string? skill, string? location, int? maxDuration, string? sort, bool myMajorOnly = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var scored = await _service.BrowseAsync(user, skill, location, maxDuration, myMajorOnly);

            // The view reads this as a list of tuples, which is the shape it was
            // built against before the rules moved into the service.
            ViewBag.Scored = scored
                .Select(s => (s.Internship, s.Score, s.CourseDataAvailable))
                .ToList();

            ViewBag.SearchSkill = skill;
            ViewBag.SearchLocation = location;
            ViewBag.MaxDuration = maxDuration;
            ViewBag.MyMajorOnly = myMajorOnly;
            ViewBag.StudentMajor = await _service.GetStudentMajorAsync(user);

            // Edge Case: "No matching internships — the system shall display a
            // message and suggest updating their career profile." Triggered by a
            // poor best score, not merely by an empty list from the filters.
            ViewBag.SuggestProfileUpdate = await _service.ShouldSuggestProfileUpdateAsync(user, scored);

            return View();
        }

        // ---------- DETAILS: listing + live matching score + apply form -------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Null covers both "no such listing" and "another university's
            // listing" — deliberately indistinguishable.
            var detail = await _service.GetDetailAsync(user, id);
            if (detail is null) return NotFound();

            ViewBag.Score = detail.Score;
            ViewBag.CourseDataAvailable = detail.CourseDataAvailable;
            ViewBag.ExistingApplication = detail.MyApplication;
            ViewBag.PositionsFilled = detail.PositionsFilled;
            ViewBag.DeadlinePassed = detail.DeadlinePassed;

            // The student's CV and skills matter here — the CV is forwarded to
            // the employer on shortlisting and the skills feed the score — so
            // their status is shown on the apply page rather than left to be
            // discovered separately under Career Profile.
            ViewBag.HasCv = detail.HasCv;
            ViewBag.SkillCount = detail.SkillCount;

            return View(detail.Internship);
        }

        // ---------- APPLY (FR-42) ----------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(int internshipId, string? coverMessage)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, _) = await _service.ApplyAsync(user, internshipId, coverMessage);

            if (result.Outcome == InternshipService.Outcome.NotFound) return NotFound();

            if (!result.Ok)
            {
                TempData["Error"] = result.Message;
                return RedirectToAction(nameof(Details), new { id = internshipId });
            }

            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(MyApplications));
        }

        // ---------- MY APPLICATIONS + WITHDRAW (A1) ----------------------------
        public async Task<IActionResult> MyApplications()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            return View(await _service.MyApplicationsAsync(user));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var result = await _service.WithdrawAsync(user, id);

            if (result.Outcome == InternshipService.Outcome.NotFound) return NotFound();

            if (result.Ok) TempData["Success"] = result.Message;
            else TempData["Error"] = result.Message;

            return RedirectToAction(nameof(MyApplications));
        }
    }
}
