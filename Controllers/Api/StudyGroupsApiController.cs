using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers.Api
{
    /// <summary>
    /// Mobile-facing Study Groups API. Every rule lives in StudyGroupService,
    /// which the web controller calls too — this class only translates outcomes
    /// into HTTP and entities into DTOs. No business logic here, deliberately:
    /// that is what keeps the two clients from drifting apart.
    ///
    /// Refusal messages are passed through verbatim so the app can display the
    /// server's wording, with a stable `code` alongside so it can react without
    /// parsing English.
    /// </summary>
    [ApiController]
    [Route("api/study-groups")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class StudyGroupsApiController : ControllerBase
    {
        private readonly StudyGroupService _service;
        private readonly UserManager<ApplicationUser> _userManager;

        public StudyGroupsApiController(StudyGroupService service, UserManager<ApplicationUser> userManager)
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

        public class FieldErrorResponse
        {
            public string Error { get; set; } = "Please correct the highlighted fields.";
            public List<FieldErrorItem> Fields { get; set; } = new();
        }

        public class FieldErrorItem
        {
            public string Field { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
        }

        public class GroupSummary
        {
            public int Id { get; set; }
            public string GroupName { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string CourseCode { get; set; } = string.Empty;
            public string? CourseName { get; set; }
            public string Status { get; set; } = string.Empty;
            public int MaxMembers { get; set; }
            public int MinMembers { get; set; }
            public int ApprovedCount { get; set; }
            public string? MeetingLocation { get; set; }
            public DateTime CreatedAt { get; set; }
            public string? CreatorName { get; set; }

            /// <summary>
            /// The caller's membership status on this group, or null if they
            /// have none. The browse list on the web shows a "Member" badge and
            /// an "N you're in" count, which it derives from the loaded member
            /// collection — a mobile client has no equivalent, so the server
            /// states it outright.
            /// </summary>
            public string? MyStatus { get; set; }

            public bool AmMember => MyStatus == nameof(MembershipStatus.Approved);
        }

        public class MemberDto
        {
            public int MemberId { get; set; }
            public string UserId { get; set; } = string.Empty;
            public string FullName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public DateTime JoinedAt { get; set; }
        }

        public class MyMembershipDto
        {
            public int MemberId { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        public class GroupDetailResponse : GroupSummary
        {
            public string CreatorId { get; set; } = string.Empty;
            public MyMembershipDto? MyMembership { get; set; }
            public bool AmCreator { get; set; }
            public bool CanJoin { get; set; }
            public bool CanPost { get; set; }
            public List<MemberDto> Members { get; set; } = new();
            /// <summary>Only populated when the caller is the creator.</summary>
            public List<MemberDto> Pending { get; set; } = new();
        }

        public class MessageDto
        {
            public int Id { get; set; }
            public string SenderId { get; set; } = string.Empty;
            public string SenderName { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public DateTime SentAtUtc { get; set; }
        }

        public class CourseDto
        {
            public string CourseCode { get; set; } = string.Empty;
            public string CourseName { get; set; } = string.Empty;
            public int Credits { get; set; }
        }

        public class CreateGroupRequest
        {
            public string GroupName { get; set; } = string.Empty;
            public string? Description { get; set; }
            public string CourseCode { get; set; } = string.Empty;
            public int MaxMembers { get; set; } = 10;
            public int MinMembers { get; set; } = 2;
            public string? MeetingLocation { get; set; }
        }

        public class PostMessageRequest
        {
            public string Content { get; set; } = string.Empty;
        }

        public class ActionResponse
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
        }

        // ---------- mapping ----------

        private static GroupSummary ToSummary(StudyGroup g, string? currentUserId = null) => new()
        {
            MyStatus = currentUserId is null
                ? null
                : g.Members.FirstOrDefault(m => m.UserId == currentUserId)?.Status.ToString(),
            Id = g.Id,
            GroupName = g.GroupName,
            Description = g.Description,
            CourseCode = g.CourseCode,
            CourseName = g.Course?.CourseName,
            Status = g.Status.ToString(),
            MaxMembers = g.MaxMembers,
            MinMembers = g.MinMembers,
            ApprovedCount = g.Members.Count(m => m.Status == MembershipStatus.Approved),
            MeetingLocation = g.MeetingLocation,
            CreatedAt = g.CreatedAt,
            CreatorName = g.Creator?.FullName
        };

        private static MemberDto ToMember(StudyGroupMember m) => new()
        {
            MemberId = m.Id,
            UserId = m.UserId,
            FullName = m.User?.FullName ?? "Unknown",
            Status = m.Status.ToString(),
            JoinedAt = m.JoinedAt
        };

        /// <summary>Maps a service outcome onto the matching HTTP status.</summary>
        private IActionResult Problem(StudyGroupService.Result result) => result.Outcome switch
        {
            StudyGroupService.Outcome.NotFound => NotFound(),
            StudyGroupService.Outcome.Forbidden => Forbid(),
            // A lost optimistic-concurrency race is a genuine conflict, and the
            // app is expected to re-fetch and let the user retry.
            StudyGroupService.Outcome.Concurrency =>
                Conflict(new ErrorResponse { Error = result.Message!, Code = result.Code }),
            _ => BadRequest(new ErrorResponse { Error = result.Message ?? "Request refused.", Code = result.Code })
        };

        private async Task<ApplicationUser?> CurrentUserAsync() => await _userManager.GetUserAsync(User);

        // ---------- reads ----------

        [HttpGet]
        public async Task<IActionResult> Index([FromQuery] string? courseCode)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var groups = await _service.GetVisibleGroupsAsync(user, courseCode);
            return Ok(groups.Select(g => ToSummary(g, user.Id)).ToList());
        }

        [HttpGet("my-courses")]
        public async Task<IActionResult> MyCourses()
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var courses = await _service.GetMyCoursesAsync(user);
            return Ok(courses.Select(c => new CourseDto
            {
                CourseCode = c.CourseCode,
                CourseName = c.CourseName,
                Credits = c.Credits
            }).ToList());
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Details(int id)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, detail) = await _service.GetDetailAsync(user, id);
            if (!result.Ok || detail is null) return Problem(result);

            var summary = ToSummary(detail.Group, user.Id);
            var response = new GroupDetailResponse
            {
                Id = summary.Id,
                GroupName = summary.GroupName,
                Description = summary.Description,
                CourseCode = summary.CourseCode,
                CourseName = summary.CourseName,
                Status = summary.Status,
                MaxMembers = summary.MaxMembers,
                MinMembers = summary.MinMembers,
                ApprovedCount = detail.ApprovedCount,
                MeetingLocation = summary.MeetingLocation,
                CreatedAt = summary.CreatedAt,
                CreatorName = summary.CreatorName,
                MyStatus = summary.MyStatus,
                CreatorId = detail.Group.CreatorId,
                AmCreator = detail.AmCreator,
                CanJoin = detail.CanJoin,
                CanPost = detail.CanPost,
                MyMembership = detail.MyMembership is null ? null : new MyMembershipDto
                {
                    MemberId = detail.MyMembership.Id,
                    Status = detail.MyMembership.Status.ToString()
                },
                Members = detail.Group.Members
                    .Where(m => m.Status == MembershipStatus.Approved)
                    .OrderBy(m => m.JoinedAt)
                    .Select(ToMember).ToList()
            };

            // Pending requests are the creator's business only.
            if (detail.AmCreator)
            {
                response.Pending = detail.Group.Members
                    .Where(m => m.Status == MembershipStatus.Pending)
                    .OrderBy(m => m.JoinedAt)
                    .Select(ToMember).ToList();
            }

            return Ok(response);
        }

        [HttpGet("{id:int}/messages")]
        public async Task<IActionResult> Messages(int id, [FromQuery] int? before, [FromQuery] int take = 30)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, messages) = await _service.GetMessagesAsync(user, id, before, take);
            if (!result.Ok) return Problem(result);

            // Newest-first from the service; reversed here so the client can
            // append straight to the bottom of a chat list.
            return Ok(messages
                .OrderBy(m => m.Id)
                .Select(m => new MessageDto
                {
                    Id = m.Id,
                    SenderId = m.SenderId,
                    SenderName = m.Sender?.FullName ?? "Unknown",
                    Content = m.Content,
                    SentAtUtc = m.SentAt
                }).ToList());
        }

        // ---------- writes ----------

        [HttpPost]
        public async Task<IActionResult> Create(CreateGroupRequest request)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (errors, group) = await _service.CreateAsync(user, new StudyGroupService.CreateRequest(
                request.GroupName, request.Description, request.CourseCode,
                request.MaxMembers, request.MinMembers, request.MeetingLocation));

            if (errors.Count > 0 || group is null)
            {
                return BadRequest(new FieldErrorResponse
                {
                    Fields = errors.Select(e => new FieldErrorItem { Field = e.Field, Message = e.Message }).ToList()
                });
            }

            return CreatedAtAction(nameof(Details), new { id = group.Id }, ToSummary(group, user.Id));
        }

        [HttpPost("{id:int}/join")]
        public async Task<IActionResult> Join(int id)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var result = await _service.JoinAsync(user, id);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        /// <summary>Creator-only. Archives the group; see StudyGroupService.DeleteAsync.</summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var result = await _service.DeleteAsync(user, id);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("{id:int}/leave")]
        public async Task<IActionResult> Leave(int id)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var result = await _service.LeaveAsync(user, id);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("members/{memberId:int}/approve")]
        public async Task<IActionResult> ApproveMember(int memberId)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, _) = await _service.ApproveMemberAsync(user, memberId);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("members/{memberId:int}/reject")]
        public async Task<IActionResult> RejectMember(int memberId)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, _) = await _service.RejectMemberAsync(user, memberId);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("members/{memberId:int}/remove")]
        public async Task<IActionResult> RemoveMember(int memberId)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, _) = await _service.RemoveMemberAsync(user, memberId);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("members/{memberId:int}/transfer-leadership")]
        public async Task<IActionResult> TransferLeadership(int memberId)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, _) = await _service.TransferLeadershipAsync(user, memberId);
            return result.Ok
                ? Ok(new ActionResponse { Success = true, Message = result.Message })
                : Problem(result);
        }

        [HttpPost("{id:int}/messages")]
        public async Task<IActionResult> PostMessage(int id, PostMessageRequest request)
        {
            var user = await CurrentUserAsync();
            if (user is null) return Unauthorized();

            var (result, message) = await _service.PostMessageAsync(user, id, request.Content);
            if (!result.Ok || message is null) return Problem(result);

            return Ok(new MessageDto
            {
                Id = message.Id,
                SenderId = message.SenderId,
                SenderName = user.FullName,
                Content = message.Content,
                SentAtUtc = message.SentAt
            });
        }
    }
}
