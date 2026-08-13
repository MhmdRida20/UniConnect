using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    /// <summary>
    /// Mobile-facing Internships API — the student's side of UC-07: browse,
    /// view, apply, track and withdraw.
    ///
    /// Every rule lives in InternshipService, which the web controller calls
    /// too. This class only translates outcomes into HTTP and entities into
    /// DTOs, which is what keeps the two clients from drifting apart.
    /// </summary>
    [ApiController]
    [Route("api/internships")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    [RequireService(ServiceCodes.Internships)]
    public class InternshipsApiController : ControllerBase
    {
        private readonly InternshipService _service;
        private readonly UserManager<ApplicationUser> _userManager;

        public InternshipsApiController(InternshipService service, UserManager<ApplicationUser> userManager)
        {
            _service = service;
            _userManager = userManager;
        }

        // ---------- DTOs ----------

        public class ErrorResponse
        {
            public string Error { get; set; } = string.Empty;
            public string? Code { get; set; }
        }

        public class ActionResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
        }

        public class InternshipSummary
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string CompanyName { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public int? DurationWeeks { get; set; }
            public DateTime ApplicationDeadline { get; set; }
            public int NumberOfPositions { get; set; }
            public string? RequiredSkills { get; set; }
            public string PostingMode { get; set; } = string.Empty;

            /// <summary>0–100. How well the student's skills and courses fit.</summary>
            public int MatchingScore { get; set; }

            /// <summary>
            /// False when the student's course history could not be read, which
            /// makes the score less meaningful — the app says so rather than
            /// presenting a number that looks authoritative.
            /// </summary>
            public bool CourseDataAvailable { get; set; }

            /// <summary>The caller's application status, or null if they have not applied.</summary>
            public string? MyApplicationStatus { get; set; }
        }

        public class InternshipDetailDto : InternshipSummary
        {
            public string Description { get; set; } = string.Empty;
            public string? RecommendedCourses { get; set; }
            public string? RelevantMajors { get; set; }
            public string? ExternalApplyUrl { get; set; }
            public string? ExternalApplyEmail { get; set; }

            public bool CanApply { get; set; }
            public bool IsExternal { get; set; }
            public bool PositionsFilled { get; set; }
            public bool DeadlinePassed { get; set; }

            /// <summary>Whether a CV is on file — it is forwarded if the student is shortlisted.</summary>
            public bool HasCv { get; set; }
            public int SkillCount { get; set; }

            public int? MyApplicationId { get; set; }
        }

        public class ApplicationSummary
        {
            public int Id { get; set; }
            public int InternshipId { get; set; }
            public string InternshipTitle { get; set; } = string.Empty;
            public string CompanyName { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int? MatchingScore { get; set; }
            public DateTime AppliedAt { get; set; }
            public string? CoverMessage { get; set; }

            /// <summary>Whether Withdraw would be accepted — see InternshipService.</summary>
            public bool CanWithdraw { get; set; }
        }

        public class ApplyRequest
        {
            public string? CoverMessage { get; set; }
        }

        // ---------- endpoints ----------

        /// <summary>Live listings for the student's university, best match first.</summary>
        [HttpGet]
        public async Task<IActionResult> Index(
            string? skill = null, string? location = null, int? maxDuration = null, bool myMajorOnly = false)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var scored = await _service.BrowseAsync(user, skill, location, maxDuration, myMajorOnly);

            // One query for the caller's applications rather than one per
            // listing, so the badge on each card costs nothing extra.
            var mine = (await _service.MyApplicationsAsync(user))
                .GroupBy(a => a.InternshipId)
                .ToDictionary(g => g.Key, g => g.First().Status.ToString());

            return Ok(scored.Select(s => ToSummary(s, mine)).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var detail = await _service.GetDetailAsync(user, id);
            if (detail is null) return NotFound();

            return Ok(ToDetail(detail));
        }

        [HttpPost("{id:int}/apply")]
        public async Task<IActionResult> Apply(int id, [FromBody] ApplyRequest? request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var (result, _) = await _service.ApplyAsync(user, id, request?.CoverMessage);
            if (!result.Ok) return Problem(result);

            return Ok(new ActionResponse { Success = true, Message = result.Message });
        }

        [HttpGet("applications")]
        public async Task<IActionResult> MyApplications()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var applications = await _service.MyApplicationsAsync(user);
            return Ok(applications.Select(ToApplication).ToList());
        }

        [HttpPost("applications/{id:int}/withdraw")]
        public async Task<IActionResult> Withdraw(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Unauthorized();

            var result = await _service.WithdrawAsync(user, id);
            if (!result.Ok) return Problem(result);

            return Ok(new ActionResponse { Success = true, Message = result.Message });
        }

        // ---------- mapping ----------

        private static InternshipSummary ToSummary(
            InternshipService.ScoredInternship scored, IReadOnlyDictionary<int, string> myApplications)
        {
            var i = scored.Internship;
            return new InternshipSummary
            {
                Id = i.Id,
                Title = i.Title,
                CompanyName = CompanyNameOf(i),
                Location = i.Location,
                DurationWeeks = i.DurationWeeks,
                ApplicationDeadline = i.ApplicationDeadline,
                NumberOfPositions = i.NumberOfPositions,
                RequiredSkills = i.RequiredSkills,
                PostingMode = i.PostingMode.ToString(),
                MatchingScore = scored.Score,
                CourseDataAvailable = scored.CourseDataAvailable,
                MyApplicationStatus = myApplications.TryGetValue(i.Id, out var status) ? status : null
            };
        }

        private static InternshipDetailDto ToDetail(InternshipService.InternshipDetail detail)
        {
            var i = detail.Internship;
            return new InternshipDetailDto
            {
                Id = i.Id,
                Title = i.Title,
                CompanyName = CompanyNameOf(i),
                Location = i.Location,
                DurationWeeks = i.DurationWeeks,
                ApplicationDeadline = i.ApplicationDeadline,
                NumberOfPositions = i.NumberOfPositions,
                RequiredSkills = i.RequiredSkills,
                PostingMode = i.PostingMode.ToString(),
                MatchingScore = detail.Score,
                CourseDataAvailable = detail.CourseDataAvailable,
                MyApplicationStatus = detail.MyApplication?.Status.ToString(),

                Description = i.Description,
                RecommendedCourses = i.RecommendedCourses,
                RelevantMajors = i.RelevantMajors,
                ExternalApplyUrl = i.ExternalApplyUrl,
                ExternalApplyEmail = i.ExternalApplyEmail,

                CanApply = detail.CanApply,
                IsExternal = detail.IsExternal,
                PositionsFilled = detail.PositionsFilled,
                DeadlinePassed = detail.DeadlinePassed,
                HasCv = detail.HasCv,
                SkillCount = detail.SkillCount,
                MyApplicationId = detail.MyApplication?.Id
            };
        }

        private static ApplicationSummary ToApplication(InternshipApplication a) => new()
        {
            Id = a.Id,
            InternshipId = a.InternshipId,
            InternshipTitle = a.Internship?.Title ?? "Internship",
            CompanyName = a.Internship is null ? "—" : CompanyNameOf(a.Internship),
            Location = a.Internship?.Location ?? string.Empty,
            Status = a.Status.ToString(),
            MatchingScore = a.MatchingScore,
            AppliedAt = a.AppliedAt,
            CoverMessage = a.CoverMessage,
            CanWithdraw = a.Status is not (InternshipApplicationStatus.Accepted
                or InternshipApplicationStatus.Rejected
                or InternshipApplicationStatus.Withdrawn)
        };

        /// <summary>
        /// Externally-sourced postings carry the employer's name in their own
        /// field rather than in a Company row, so both are checked.
        /// </summary>
        private static string CompanyNameOf(Internship i) =>
            !string.IsNullOrWhiteSpace(i.ExternalEmployerName)
                ? i.ExternalEmployerName
                : i.Company?.CompanyName ?? "—";

        private IActionResult Problem(InternshipService.Result result) => result.Outcome switch
        {
            InternshipService.Outcome.NotFound => NotFound(),
            _ => BadRequest(new ErrorResponse { Error = result.Message ?? "Request refused.", Code = result.Code })
        };
    }
}
