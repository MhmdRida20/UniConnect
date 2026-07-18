using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.ExternalApi;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Test/demo harness for the simulated external universities. Each
    /// university has its OWN independent, PERSISTED dataset (see
    /// ExternalUniversityDataStore) — this screen lets you pick WHICH one
    /// you're looking at, then add/enroll/drop students on that specific
    /// university's simulated external side.
    ///
    /// Every mutation here immediately: (1) re-syncs that university so the
    /// change is visible in UniConnect right away instead of waiting for the
    /// next scheduled cycle, (2) notifies the affected student directly if
    /// they have a real account, and (3) for a drop specifically, also runs
    /// the study-group enrollment re-check immediately, so you can see the
    /// whole chain (external change → sync → notification → study group
    /// consequence) in one demo pass instead of waiting for background jobs.
    ///
    /// A real external partner would obviously never let UniConnect edit
    /// their data this way — this is a test harness only.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminExternalApiSimulatorController : Controller
    {
        private readonly ExternalUniversityDataStore _store;
        private readonly ApplicationDbContext _db;
        private readonly UniversityApiSyncRunner _syncRunner;
        private readonly EnrollmentRevalidationRunner _enrollmentRunner;
        private readonly NotificationService _notifications;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<NotificationHub> _notificationHub;

        public AdminExternalApiSimulatorController(
            ExternalUniversityDataStore store,
            ApplicationDbContext db,
            UniversityApiSyncRunner syncRunner,
            EnrollmentRevalidationRunner enrollmentRunner,
            NotificationService notifications,
            UserManager<ApplicationUser> userManager,
            IHubContext<NotificationHub> notificationHub)
        {
            _store = store;
            _db = db;
            _syncRunner = syncRunner;
            _enrollmentRunner = enrollmentRunner;
            _notifications = notifications;
            _userManager = userManager;
            _notificationHub = notificationHub;
        }

        public async Task<IActionResult> Index(string? apiKey)
        {
            var universities = await _db.Universities.OrderBy(u => u.Name).ToListAsync();
            ViewBag.ApiUniversities = universities;

            apiKey ??= universities.FirstOrDefault()?.ApiKey;
            ViewBag.SelectedApiKey = apiKey;

            var dataset = string.IsNullOrWhiteSpace(apiKey) ? null : await _store.GetDatasetAsync(apiKey);
            ViewBag.Dataset = dataset;

            return View();
        }

        private async Task<University?> ResyncIfMatchedAsync(string apiKey)
        {
            var university = await _db.Universities.FirstOrDefaultAsync(u => u.ApiKey == apiKey);
            if (university is not null)
                await _syncRunner.SyncOneUniversityAsync(university);
            return university;
        }

        // If this external student number matches a real, registered
        // UniConnect account at this university, return it — otherwise null
        // (e.g. the sync hasn't run yet, or nobody registered under that ID).
        private async Task<ApplicationUser?> FindMatchingAccountAsync(University university, string studentNumber)
        {
            return await _userManager.Users.FirstOrDefaultAsync(
                u => u.UniversityCode == university.Code && u.UniversityId == studentNumber);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddStudent(string apiKey, string fullName, string email, string? major, int yearOfStudy)
        {
            if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "Please enter a name and email.";
                return RedirectToAction(nameof(Index), new { apiKey });
            }

            var number = await _store.AddStudentAsync(apiKey, fullName.Trim(), email.Trim(), major?.Trim(), yearOfStudy);
            if (number is not null) await ResyncIfMatchedAsync(apiKey);

            TempData[number is not null ? "Success" : "Error"] = number is not null
                ? $"Added {fullName} as {number} on the external side, and synced into UniConnect right away."
                : "Couldn't add that student — dataset not found.";
            return RedirectToAction(nameof(Index), new { apiKey });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEnrollment(string apiKey, string studentNumber, string courseCode)
        {
            var result = await _store.AddEnrollmentAsync(apiKey, studentNumber, courseCode);

            switch (result)
            {
                case AddEnrollmentResult.AlreadyEnrolled:
                    // Nothing actually changed — don't sync, don't notify,
                    // just tell the admin plainly why nothing happened.
                    TempData["Error"] = $"{studentNumber} is already enrolled in {courseCode} — nothing to do.";
                    break;

                case AddEnrollmentResult.NotFound:
                    TempData["Error"] = "Couldn't add that enrollment — check the student/course exist.";
                    break;

                case AddEnrollmentResult.Added:
                    var university = await ResyncIfMatchedAsync(apiKey);
                    if (university is not null)
                    {
                        var account = await FindMatchingAccountAsync(university, studentNumber);
                        if (account is not null)
                        {
                            var dataset = await _store.GetDatasetAsync(apiKey);
                            var courseName = dataset?.Courses.FirstOrDefault(c => c.CourseCode == courseCode)?.CourseName ?? courseCode;
                            await _notifications.NotifyAsync(
                                account.Id,
                                "Enrolled in a new course",
                                $"You've been enrolled in {courseCode} — {courseName}.",
                                "/StudyGroups/MyCourses");

                            await _notificationHub.Clients.Group($"notify-{account.Id}").SendAsync("CoursesChanged");
                        }
                    }
                    TempData["Success"] = $"Enrolled {studentNumber} in {courseCode} on the external side, synced into UniConnect, and notified the student (if registered).";
                    break;
            }

            return RedirectToAction(nameof(Index), new { apiKey });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveEnrollment(string apiKey, string studentNumber, string courseCode)
        {
            var datasetBefore = await _store.GetDatasetAsync(apiKey);
            var courseName = datasetBefore?.Courses.FirstOrDefault(c => c.CourseCode == courseCode)?.CourseName ?? courseCode;
            var ok = await _store.RemoveEnrollmentAsync(apiKey, studentNumber, courseCode);

            if (ok)
            {
                var university = await ResyncIfMatchedAsync(apiKey);
                if (university is not null)
                {
                    var account = await FindMatchingAccountAsync(university, studentNumber);
                    if (account is not null)
                    {
                        await _notifications.NotifyAsync(
                            account.Id,
                            "Dropped from a course",
                            $"You're no longer enrolled in {courseCode} — {courseName}.",
                            "/StudyGroups/MyCourses");

                        // Live push — if this student currently has "My
                        // Courses" open, it updates immediately instead of
                        // only being correct on their next reload.
                        await _notificationHub.Clients.Group($"notify-{account.Id}").SendAsync("CoursesChanged");
                    }
                }

                // Run the study-group consequence check immediately (rather
                // than waiting for its own background cycle) so the whole
                // chain is visible in one demo pass.
                await _enrollmentRunner.RevalidateAsync();
            }

            TempData[ok ? "Success" : "Error"] = ok
                ? $"{studentNumber} dropped from {courseCode} on the external side, synced, and notified. Any study group " +
                  "membership for that course was just re-checked too."
                : "That enrollment didn't exist.";
            return RedirectToAction(nameof(Index), new { apiKey });
        }
    }
}
