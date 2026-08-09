using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    // Student-only, matching the mobile-scope decision — no instructor
    // branch here at all (unlike HomeController.Index, which serves both).
    [ApiController]
    [Route("api/home")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class HomeApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IUniversityProviderResolver _resolver;
        private readonly IServiceCatalogService _serviceCatalog;
        private readonly ILogger<HomeApiController> _logger;

        public HomeApiController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IUniversityProviderResolver resolver,
            IServiceCatalogService serviceCatalog,
            ILogger<HomeApiController> logger)
        {
            _db = db;
            _userManager = userManager;
            _resolver = resolver;
            _serviceCatalog = serviceCatalog;
            _logger = logger;
        }

        public class DashboardStats
        {
            public int GroupsJoined { get; set; }
            public int RidesTaken { get; set; }
            public int CoursesEnrolled { get; set; }
            public int ClubsJoined { get; set; }
        }

        public class ServiceSummary
        {
            public string Code { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string? IconClass { get; set; }
        }

        public class DashboardResponse
        {
            public string FullName { get; set; } = string.Empty;
            public string UniversityCode { get; set; } = string.Empty;
            public string UniversityName { get; set; } = string.Empty;
            public DashboardStats Stats { get; set; } = new();
            public List<ServiceSummary> EnabledServices { get; set; } = new();
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Dashboard()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var university = await _db.Universities.FindAsync(user.UniversityCode);

            // ---------- Stats — identical logic to HomeController.Index's
            // student branch, kept in sync deliberately. If you change one,
            // change the other, or the web and mobile dashboards will quietly
            // start disagreeing with each other. ----------
            var stats = new DashboardStats
            {
                GroupsJoined = await _db.StudyGroupMembers.CountAsync(
                    m => m.UserId == user.Id && m.Status == MembershipStatus.Approved),
                RidesTaken = await _db.RideRequests.CountAsync(
                    r => r.PassengerId == user.Id && r.Status == RideRequestStatus.Accepted),
                ClubsJoined = await _db.ClubMembers.CountAsync(
                    m => m.UserId == user.Id && m.Status == ClubMembershipStatus.Approved),
            };

            // Live via the adapter — same reasoning as the web dashboard fix:
            // the local Enrollments cache isn't populated for every
            // university's API style, so a live call is the only way this
            // number is trustworthy for all of them.
            try
            {
                var provider = await _resolver.GetProviderAsync(user.UniversityCode);
                stats.CoursesEnrolled = (await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId)).Count;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Live enrollment count failed for {University}/{Id}; falling back to cached count.",
                    user.UniversityCode, user.UniversityId);
                stats.CoursesEnrolled = await _db.Enrollments.CountAsync(
                    e => e.UniversityId == user.UniversityId && e.UniversityCode == user.UniversityCode);
            }

            // ---------- Enabled services — same source as the web Services
            // page (IServiceCatalogService), joined against the Services
            // table for display info (name/description/icon) the mobile
            // app needs to actually render a catalog screen.
            var enabledCodes = await _serviceCatalog.GetEnabledServiceCodesAsync(user.UniversityCode);
            var enabledServices = await _db.Services
                .Where(s => enabledCodes.Contains(s.Code) && s.IsImplemented)
                .Select(s => new ServiceSummary
                {
                    Code = s.Code,
                    Name = s.Name,
                    Description = s.Description,
                    IconClass = s.IconClass
                })
                .ToListAsync();

            return Ok(new DashboardResponse
            {
                FullName = user.FullName,
                UniversityCode = user.UniversityCode,
                UniversityName = university?.Name ?? user.UniversityCode,
                Stats = stats,
                EnabledServices = enabledServices
            });
        }
    }
}
