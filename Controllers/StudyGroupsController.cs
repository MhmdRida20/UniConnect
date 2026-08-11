using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.ViewModels;
using Microsoft.AspNetCore.SignalR;
using UniConnect.Hubs;
using UniConnect.Adapters;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Implements the Study Group use cases (FR-46 through FR-54):
    ///   Create, search, join (with approval), manage members (approve/reject/
    ///   remove/transfer leadership), real-time chat, and inactivity detection.
    ///
    /// Enrollment/course data is read through IUniversityProviderResolver
    /// instead of querying _db.Enrollments/_db.Courses directly — this is the
    /// adapter architecture in practice: this controller has no idea whether a
    /// given university's data comes from — every university calls a real
    /// external registrar API. See /Adapters/IUniversityProvider.cs.
    /// </summary>
    [Authorize]   // every action requires a logged-in user
    [UniConnect.Filters.RequireService(UniConnect.Models.ServiceCodes.StudyGroups)]
    public class StudyGroupsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<StudyGroupHub> _hub;
        private readonly IUniversityProviderResolver _providerResolver;
        private readonly UniConnect.Services.AuditLogService _auditLog;
        private readonly UniConnect.Services.NotificationService _notifications;
        private readonly UniConnect.Services.StudyGroupService _service;

        public StudyGroupsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<StudyGroupHub> hub,
            IUniversityProviderResolver providerResolver,
            UniConnect.Services.AuditLogService auditLog,
            UniConnect.Services.NotificationService notifications,
            UniConnect.Services.StudyGroupService service)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _providerResolver = providerResolver;
            _auditLog = auditLog;
            _notifications = notifications;
            _service = service;
        }

        // Turns a service outcome into the web's redirect + TempData convention.
        // The mobile API maps the same outcomes onto status codes instead — same
        // rules and the same message text, two presentations.
        private IActionResult FromResult(
            UniConnect.Services.StudyGroupService.Result result, int groupId, string? fallbackAction = null)
        {
            switch (result.Outcome)
            {
                case UniConnect.Services.StudyGroupService.Outcome.NotFound:
                    return NotFound();
                case UniConnect.Services.StudyGroupService.Outcome.Forbidden:
                    return Forbid();
                case UniConnect.Services.StudyGroupService.Outcome.Success:
                    if (result.Message is not null) TempData["Success"] = result.Message;
                    break;
                default:
                    TempData["Error"] = result.Message;
                    break;
            }

            return fallbackAction is not null
                ? RedirectToAction(fallbackAction)
                : RedirectToAction(nameof(Details), new { id = groupId });
        }

        // Broadcasts to everyone currently viewing this group's Details page —
        // covers new join requests, approvals, rejections, removals, leadership
        // transfers, and leave events, so nobody needs to refresh to see them.
        private Task BroadcastGroupUpdated(int groupId)
            => _hub.Clients.Group($"group-{groupId}").SendAsync("GroupUpdated");

        // Broadcasts to anyone browsing the Study Groups list, so a new group,
        // a group filling up, or a status change shows up live.
        private Task BroadcastListChanged()
            => _hub.Clients.Group("study-groups-lobby").SendAsync("StudyGroupListChanged");

        // ---------- INDEX: list all study groups in courses the user is enrolled in -------
        public async Task<IActionResult> Index(string? courseCode)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var groups = await _service.GetVisibleGroupsAsync(user, courseCode);
            var myCourses = await _service.GetMyCoursesAsync(user);

            // Build filter dropdown
            ViewBag.MyCourses = new SelectList(myCourses, "CourseCode", "CourseName", courseCode);
            ViewBag.SelectedCourse = courseCode;
            ViewBag.CurrentUserId = user.Id;

            return View(groups);
        }

        // ---------- CREATE (GET) ---------------------------------------------------
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Only allow group creation for courses the user is enrolled in (FR-46 precondition)
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var myCourses = await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);

            var vm = new StudyGroupCreateVM
            {
                AvailableCourses = new SelectList(myCourses, "CourseCode", "CourseName")
            };
            return View(vm);
        }

        // ---------- CREATE (POST) --------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(StudyGroupCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Model-level validation (Required/Range) has already run; the
            // service owns the rules that need the database or the adapter, and
            // returns them as field errors so they land on the right input.
            var (errors, group) = await _service.CreateAsync(user,
                new UniConnect.Services.StudyGroupService.CreateRequest(
                    vm.GroupName, vm.Description, vm.CourseCode,
                    vm.MaxMembers, vm.MinMembers, vm.MeetingLocation));

            foreach (var error in errors)
                ModelState.AddModelError(error.Field, error.Message);

            if (!ModelState.IsValid || group is null)
            {
                var myCourses = await _service.GetMyCoursesAsync(user);
                vm.AvailableCourses = new SelectList(myCourses, "CourseCode", "CourseName", vm.CourseCode);
                return View(vm);
            }

            TempData["Success"] = "Study group created successfully.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        // ---------- DETAILS --------------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, detail) = await _service.GetDetailAsync(user, id);
            if (!result.Ok || detail is null)
                return FromResult(result, id, fallbackAction: nameof(Index));

            var group = detail.Group;

            // The service leaves chat history out on purpose — the mobile client
            // pages it separately. This page still renders the whole thread, so
            // load it explicitly onto the already-tracked entity.
            await _db.Entry(group)
                .Collection(g => g.Messages)
                .Query()
                .Include(m => m.Sender)
                .OrderBy(m => m.SentAt)
                .LoadAsync();

            ViewBag.CurrentUserId = user.Id;
            ViewBag.IsMember = detail.CanPost;
            ViewBag.IsCreator = detail.AmCreator;
            ViewBag.ApprovedCount = detail.ApprovedCount;
            ViewBag.MyMembershipStatus = detail.MyMembership?.Status; // null | Pending | Approved | Rejected

            return View(group);
        }

        // ---------- JOIN (creates a PENDING request — FR-51 approval flow) ---------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var result = await _service.JoinAsync(user, id);

            // A cross-university refusal sends the user back to the list, since
            // the group they asked for isn't theirs to look at.
            return result.Code == "CROSS_UNIVERSITY"
                ? FromResult(result, id, fallbackAction: nameof(Index))
                : FromResult(result, id);
        }

        // ---------- APPROVE MEMBER (creator only) — FR-51 ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, groupId) = await _service.ApproveMemberAsync(user, memberId);
            return FromResult(result, groupId);
        }

        // ---------- REJECT MEMBER (creator only) — FR-51 ----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, groupId) = await _service.RejectMemberAsync(user, memberId);
            return FromResult(result, groupId);
        }

        // ---------- REMOVE MEMBER (creator only) — FR-51 ----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, groupId) = await _service.RemoveMemberAsync(user, memberId);
            return FromResult(result, groupId);
        }

        // ---------- TRANSFER LEADERSHIP (creator only) — FR-51 ----------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferLeadership(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, groupId) = await _service.TransferLeadershipAsync(user, memberId);
            return FromResult(result, groupId);
        }

        // ---------- LEAVE ----------------------------------------------------------
        // Also used to cancel your own pending join request (same "leave your
        // relationship with this group" semantics either way).
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var result = await _service.LeaveAsync(user, id);

            // Leaving always returns to the list — including the "you weren't a
            // member" case, which the service reports as NotFound.
            if (result.Outcome == UniConnect.Services.StudyGroupService.Outcome.NotFound)
                return RedirectToAction(nameof(Index));

            return FromResult(result, id, fallbackAction: nameof(Index));
        }

        // ---------- POST A MESSAGE (FR-52) ----------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostMessage(int id, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var (result, _) = await _service.PostMessageAsync(user, id, content);

            if (result.Outcome == UniConnect.Services.StudyGroupService.Outcome.Forbidden)
                return Forbid();

            // JSON rather than a redirect so the page does NOT reload. An empty
            // message was previously a silent redirect; it is now a refusal the
            // page can show, matching what the API returns.
            return result.Ok
                ? Json(new { ok = true })
                : Json(new { ok = false, error = result.Message, code = result.Code });
        }

        // ---------- MY COURSES (FR-49 related) -------------------------------------
        public async Task<IActionResult> MyCourses()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var courses = await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);

            return View(courses);
        }
    }
}
