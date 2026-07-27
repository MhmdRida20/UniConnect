using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUniversityProviderResolver _resolver;
        private readonly IServiceCatalogService _serviceCatalog;

        public HomeController(
            ILogger<HomeController> logger, ApplicationDbContext db,
            UserManager<ApplicationUser> userManager, IUniversityProviderResolver resolver,
            IServiceCatalogService serviceCatalog)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
            _resolver = resolver;
            _serviceCatalog = serviceCatalog;
        }

        public async Task<IActionResult> Index()
        {
            // Staff/admin/company accounts don't use the student-facing app —
            // send them straight to their portal instead of the student home page.
            if (User.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin"))
                    return RedirectToAction("Index", "AdminUniversities");
                if (User.IsInRole("UniversityAdmin"))
                    return RedirectToAction("Index", "AdminUniversities");
                if (User.IsInRole("DepartmentStaff"))
                    return RedirectToAction("Index", "StaffTickets");
                if (User.IsInRole("Company"))
                    return RedirectToAction("Index", "Company");

                // Instructors stay on this page, but see their own taught
                // courses (and how many students are enrolled in each)
                // instead of the generic student services showcase — those
                // six services mostly don't apply to an instructor account.
                if (User.IsInRole("Instructor"))
                {
                    var user = await _userManager.GetUserAsync(User);
                    if (user is not null)
                    {
                        var courseCards = new List<(string Code, string Name, int StudentCount)>();
                        try
                        {
                            var provider = await _resolver.GetProviderAsync(user.UniversityCode);
                            var taughtCourses = await provider.GetTaughtCoursesAsync(user.UniversityCode, user.UniversityId);
                            foreach (var course in taughtCourses)
                            {
                                var roster = await provider.GetEnrolledStudentsAsync(user.UniversityCode, course.CourseCode);
                                courseCards.Add((course.CourseCode, course.CourseName, roster.Count));
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Could not load taught courses for instructor {User} on the home page.", user.Id);
                        }
                        ViewBag.InstructorCourseCards = courseCards;

                        // Personal stats for the hero cards + counter row —
                        // deliberately cheap: total students reuses the
                        // course-card data already fetched above (no extra
                        // adapter calls), and the other two are plain local
                        // DB counts, not further adapter round-trips.
                        ViewBag.InstructorTotalStudents = courseCards.Sum(c => c.StudentCount);
                        ViewBag.InstructorSessionsHeld = await _db.AttendanceSessions.CountAsync(s => s.InstructorId == user.Id);
                        ViewBag.InstructorTicketsSubmitted = await _db.Tickets.CountAsync(t => t.SubmitterId == user.Id);
                    }
                }
                else
                {
                    // Students see the services showcase further down the
                    // page — it used to always show all six regardless of
                    // what their own university actually has enabled,
                    // inconsistent with every other part of the app (which
                    // all correctly gate on this). Fetched once here and
                    // passed down instead.
                    var user = await _userManager.GetUserAsync(User);
                    if (user is not null)
                    {
                        ViewBag.EnabledServices = await _serviceCatalog.GetEnabledServiceCodesAsync(user.UniversityCode);

                        // Same "cheap" approach as the instructor stats above
                        // — plain local DB counts, no adapter calls at all,
                        // used for the hero cards + counter row.
                        ViewBag.StudentGroupsJoined = await _db.StudyGroupMembers.CountAsync(
                            m => m.UserId == user.Id && m.Status == MembershipStatus.Approved);
                        ViewBag.StudentRidesTaken = await _db.RideRequests.CountAsync(
                            r => r.PassengerId == user.Id && r.Status == RideRequestStatus.Accepted);
                        ViewBag.StudentCoursesEnrolled = await _db.Enrollments.CountAsync(
                            e => e.UniversityId == user.UniversityId && e.UniversityCode == user.UniversityCode);
                        ViewBag.StudentClubsJoined = await _db.ClubMembers.CountAsync(
                            m => m.UserId == user.Id && m.Status == ClubMembershipStatus.Approved);
                    }
                }
            }

            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        // Shown when RequireServiceAttribute blocks access because the current
        // user's university hasn't enabled that service (or it isn't built yet).
        public async Task<IActionResult> NotAvailable(string? service)
        {
            Service? serviceInfo = null;
            if (!string.IsNullOrWhiteSpace(service))
                serviceInfo = await _db.Services.FindAsync(service);

            ViewBag.ServiceInfo = serviceInfo;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
