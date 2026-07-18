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

        public StudyGroupsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<StudyGroupHub> hub,
            IUniversityProviderResolver providerResolver,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _providerResolver = providerResolver;
            _auditLog = auditLog;
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

            // Get the user's enrolled courses through the adapter (FR-49) —
            // this call is identical regardless of which university's API is
            // actually running behind the scenes.
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var myCourses = await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);
            var myCourseCodes = myCourses.Select(c => c.CourseCode).ToList();

            // Only show groups for courses I'm enrolled in, at my own
            // university (course codes can coincide across universities
            // now that each has its own catalog — UniversityCode disambiguates).
            var query = _db.StudyGroups
                .Include(g => g.Course)
                .Include(g => g.Creator)
                .Include(g => g.Members)
                .Where(g => g.UniversityCode == user.UniversityCode
                            && myCourseCodes.Contains(g.CourseCode)
                            && g.Status != StudyGroupStatus.Archived);

            if (!string.IsNullOrWhiteSpace(courseCode))
                query = query.Where(g => g.CourseCode == courseCode);

            var groups = await query.OrderByDescending(g => g.CreatedAt).ToListAsync();

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

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);

            // E1 of FR-46: must be enrolled in the course
            var enrolled = await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, vm.CourseCode);
            if (!enrolled)
                ModelState.AddModelError(nameof(vm.CourseCode),
                    "You are not enrolled in this course.");

            if (vm.MinMembers > vm.MaxMembers)
                ModelState.AddModelError(nameof(vm.MinMembers),
                    "Minimum members cannot exceed maximum members.");

            if (!ModelState.IsValid)
            {
                var myCourses = await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);
                vm.AvailableCourses = new SelectList(myCourses, "CourseCode", "CourseName", vm.CourseCode);
                return View(vm);
            }

            var group = new StudyGroup
            {
                GroupName = vm.GroupName.Trim(),
                Description = vm.Description?.Trim(),
                UniversityCode = user.UniversityCode,
                CourseCode = vm.CourseCode,
                CreatorId = user.Id,
                MaxMembers = vm.MaxMembers,
                MinMembers = vm.MinMembers,
                MeetingLocation = vm.MeetingLocation?.Trim(),
                Status = StudyGroupStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.StudyGroups.Add(group);
            await _db.SaveChangesAsync();

            // Auto-add the creator as an approved member
            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = group.Id,
                UserId = user.Id,
                Status = MembershipStatus.Approved,
                JoinedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "StudyGroupCreated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "StudyGroup",
                entityId: group.Id.ToString(),
                details: $"Course: {group.CourseCode}");

            await BroadcastListChanged();

            TempData["Success"] = "Study group created successfully.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        // ---------- DETAILS --------------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var group = await _db.StudyGroups
                .Include(g => g.Course)
                .Include(g => g.Creator)
                .Include(g => g.Members).ThenInclude(m => m.User)
                .Include(g => g.Messages.OrderBy(msg => msg.SentAt)).ThenInclude(m => m.Sender)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group is null) return NotFound();

            // A group belongs to exactly one university — never let a match
            // on course code alone leak across universities, even if two
            // universities coincidentally use the same course code.
            if (group.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This group doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

            // Must be enrolled in the course to view (FR-49)
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var enrolled = await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, group.CourseCode);
            if (!enrolled)
            {
                TempData["Error"] = "You must be enrolled in the course to view this group.";
                return RedirectToAction(nameof(Index));
            }

            var myMembership = group.Members.FirstOrDefault(m => m.UserId == user.Id);

            ViewBag.CurrentUserId = user.Id;
            ViewBag.IsMember = myMembership?.Status == MembershipStatus.Approved;
            ViewBag.IsCreator = group.CreatorId == user.Id;
            ViewBag.ApprovedCount = group.Members.Count(m => m.Status == MembershipStatus.Approved);
            ViewBag.MyMembershipStatus = myMembership?.Status; // null | Pending | Approved | Rejected

            return View(group);
        }

        // ---------- JOIN (creates a PENDING request — FR-51 approval flow) ---------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var group = await _db.StudyGroups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == id);
            if (group is null) return NotFound();

            // Same cross-university guard as Details — belt and suspenders.
            if (group.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This group doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

            // FR-49 — must be enrolled in the course
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var enrolled = await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, group.CourseCode);
            if (!enrolled)
            {
                TempData["Error"] = "You are not enrolled in this course.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Already a member, or already have a pending request?
            var existing = group.Members.FirstOrDefault(m => m.UserId == user.Id);
            if (existing != null)
            {
                if (existing.Status == MembershipStatus.Approved)
                    TempData["Error"] = "You are already in this group.";
                else if (existing.Status == MembershipStatus.Pending)
                    TempData["Error"] = "You already have a pending request for this group.";
                else
                    TempData["Error"] = "You can't request to join right now.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // E1 — capacity check (no point creating a request for a full group)
            var approvedCount = group.Members.Count(m => m.Status == MembershipStatus.Approved);
            if (approvedCount >= group.MaxMembers)
            {
                group.Status = StudyGroupStatus.Full;
                await _db.SaveChangesAsync();
                TempData["Error"] = "This study group is already full.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = id,
                UserId = user.Id,
                Status = MembershipStatus.Pending,
                JoinedAt = DateTime.UtcNow
            });

            // A new request is activity — wake the group back up if it had gone quiet.
            if (group.Status == StudyGroupStatus.Inactive)
                group.Status = StudyGroupStatus.Active;

            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(id);
            await BroadcastListChanged();

            TempData["Success"] = "Your request to join has been sent to the group creator.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- APPROVE MEMBER (creator only) — FR-51 ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return NotFound();

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return Forbid();

            if (membership.Status != MembershipStatus.Pending)
            {
                TempData["Error"] = "That request is no longer pending.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            var approvedCount = await _db.StudyGroupMembers.CountAsync(
                m => m.StudyGroupId == group.Id && m.Status == MembershipStatus.Approved);

            if (approvedCount >= group.MaxMembers)
            {
                TempData["Error"] = "The group is already full — reject or remove someone first.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            membership.Status = MembershipStatus.Approved;

            if (approvedCount + 1 >= group.MaxMembers)
                group.Status = StudyGroupStatus.Full;
            else if (group.Status == StudyGroupStatus.Inactive)
                group.Status = StudyGroupStatus.Active;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // Someone else changed this group (another approval, a
                // removal, etc.) between our capacity check and this save —
                // don't guess at the outcome, just ask the officer to retry
                // against current data.
                TempData["Error"] = "This group changed while you were approving that request — please check the group and try again.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            await _auditLog.LogAsync(
                "StudyGroupMembershipApproved",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "StudyGroup",
                entityId: group.Id.ToString(),
                details: $"Member: {membership.UserId}");

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            TempData["Success"] = "Member approved.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        // ---------- REJECT MEMBER (creator only) — FR-51 ----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return NotFound();

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return Forbid();

            if (membership.Status != MembershipStatus.Pending)
            {
                TempData["Error"] = "That request is no longer pending.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            // Remove the request outright rather than leaving a "Rejected" row
            // lying around — the requester can request again later if they want.
            _db.StudyGroupMembers.Remove(membership);
            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            TempData["Success"] = "Request rejected.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        // ---------- REMOVE MEMBER (creator only) — FR-51 ----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return NotFound();

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return Forbid();

            if (membership.UserId == group.CreatorId)
            {
                TempData["Error"] = "The creator can't remove themselves this way — transfer leadership first, or leave the group.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            _db.StudyGroupMembers.Remove(membership);

            // A seat just opened up — the group can't still be "Full".
            if (group.Status == StudyGroupStatus.Full)
                group.Status = StudyGroupStatus.Active;

            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            TempData["Success"] = "Member removed from the group.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
        }

        // ---------- TRANSFER LEADERSHIP (creator only) — FR-51 ----------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferLeadership(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup)
                .Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return NotFound();

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return Forbid();

            if (membership.Status != MembershipStatus.Approved)
            {
                TempData["Error"] = "Only an approved member can become the group leader.";
                return RedirectToAction(nameof(Details), new { id = group.Id });
            }

            group.CreatorId = membership.UserId;
            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            TempData["Success"] = $"Leadership transferred to {membership.User?.FullName ?? "the selected member"}.";
            return RedirectToAction(nameof(Details), new { id = group.Id });
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

            var membership = await _db.StudyGroupMembers
                .FirstOrDefaultAsync(m => m.StudyGroupId == id && m.UserId == user.Id);
            if (membership is null) return RedirectToAction(nameof(Index));

            var group = await _db.StudyGroups.FindAsync(id);
            var wasCreator = group is not null && group.CreatorId == user.Id
                              && membership.Status == MembershipStatus.Approved;
            var wasPendingRequest = membership.Status == MembershipStatus.Pending;

            _db.StudyGroupMembers.Remove(membership);
            await _db.SaveChangesAsync();

            if (group is not null && !wasPendingRequest)
            {
                // Edge case (Study Group Edge Cases): if the leader leaves,
                // reassign to the longest-standing remaining member; if nobody
                // is left, archive the group.
                var remainingApproved = await _db.StudyGroupMembers
                    .Where(m => m.StudyGroupId == id && m.Status == MembershipStatus.Approved)
                    .OrderBy(m => m.JoinedAt)
                    .ToListAsync();

                if (!remainingApproved.Any())
                {
                    group.Status = StudyGroupStatus.Archived;
                }
                else if (wasCreator)
                {
                    group.CreatorId = remainingApproved.First().UserId;
                    if (group.Status == StudyGroupStatus.Full && remainingApproved.Count < group.MaxMembers)
                        group.Status = StudyGroupStatus.Active;
                }
                else if (group.Status == StudyGroupStatus.Full)
                {
                    group.Status = StudyGroupStatus.Active; // a seat opened up
                }

                await _db.SaveChangesAsync();
            }

            await BroadcastGroupUpdated(id);
            await BroadcastListChanged();

            TempData["Success"] = wasPendingRequest ? "Your request was withdrawn."
                                 : wasCreator ? "You left the group. Leadership was passed to the longest-standing member."
                                 : "You left the group.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- POST A MESSAGE (FR-52) ----------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostMessage(int id, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (string.IsNullOrWhiteSpace(content))
                return RedirectToAction(nameof(Details), new { id });

            // Must be an approved member to post
            var isMember = await _db.StudyGroupMembers.AnyAsync(
                m => m.StudyGroupId == id && m.UserId == user.Id
                  && m.Status == MembershipStatus.Approved);
            if (!isMember) return Forbid();

            // 1. Save to the database
            var message = new StudyGroupMessage
            {
                StudyGroupId = id,
                SenderId = user.Id,
                Content = content.Trim(),
                SentAt = DateTime.UtcNow
            };
            _db.StudyGroupMessages.Add(message);

            // A message is activity — wake the group back up if it had gone quiet.
            var group = await _db.StudyGroups.FindAsync(id);
            var reactivated = false;
            if (group is not null && group.Status == StudyGroupStatus.Inactive)
            {
                group.Status = StudyGroupStatus.Active;
                reactivated = true;
            }

            await _db.SaveChangesAsync();

            // 2. Broadcast it live to everyone currently viewing this group
            await _hub.Clients.Group($"group-{id}").SendAsync("ReceiveMessage", new
            {
                senderName = user.FullName,
                content = message.Content,
                sentAt = message.SentAt.ToString("MMM dd, HH:mm")
            });

            if (reactivated) await BroadcastListChanged();

            // 3. Return JSON instead of a redirect so the page does NOT reload
            return Json(new { ok = true });
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
