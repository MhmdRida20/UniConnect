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

namespace UniConnect.Controllers
{
    /// <summary>
    /// Implements the Study Group use cases:
    ///   UC-06 Create Study Group
    ///   UC-07 Join Study Group
    ///   UC-08 View Registered Courses (here used to filter courses you can use)
    /// </summary>
    [Authorize]   // every action requires a logged-in user
    public class StudyGroupsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<StudyGroupHub> _hub;

        public StudyGroupsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<StudyGroupHub> hub)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
        }

        // ---------- INDEX: list all study groups in courses the user is enrolled in -------
        public async Task<IActionResult> Index(string? courseCode)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Get the user's enrolled courses (FR-19, FR-24)
            var myCourseCodes = await _db.Enrollments
                .Where(e => e.UniversityId == user.UniversityId)
                .Select(e => e.CourseCode)
                .ToListAsync();

            // Only show groups for courses I'm enrolled in
            var query = _db.StudyGroups
                .Include(g => g.Course)
                .Include(g => g.Creator)
                .Include(g => g.Members)
                .Where(g => myCourseCodes.Contains(g.CourseCode)
                            && g.Status != StudyGroupStatus.Archived);

            if (!string.IsNullOrWhiteSpace(courseCode))
                query = query.Where(g => g.CourseCode == courseCode);

            var groups = await query.OrderByDescending(g => g.CreatedAt).ToListAsync();

            // Build filter dropdown
            ViewBag.MyCourses = new SelectList(
                await _db.Courses.Where(c => myCourseCodes.Contains(c.CourseCode)).ToListAsync(),
                "CourseCode", "CourseName", courseCode);
            ViewBag.SelectedCourse = courseCode;
            ViewBag.CurrentUserId = user.Id;

            return View(groups);
        }

        // ---------- CREATE (GET) ---------------------------------------------------
        public async Task<IActionResult> Create()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            // Only allow group creation for courses the user is enrolled in (UC-06 precondition)
            var myCourses = await _db.Enrollments
                .Where(e => e.UniversityId == user.UniversityId)
                .Include(e => e.Course)
                .Select(e => e.Course!)
                .ToListAsync();

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

            // E1 of UC-06: must be enrolled in the course
            var enrolled = await _db.Enrollments.AnyAsync(
                e => e.UniversityId == user.UniversityId && e.CourseCode == vm.CourseCode);
            if (!enrolled)
                ModelState.AddModelError(nameof(vm.CourseCode),
                    "You are not enrolled in this course.");

            if (vm.MinMembers > vm.MaxMembers)
                ModelState.AddModelError(nameof(vm.MinMembers),
                    "Minimum members cannot exceed maximum members.");

            if (!ModelState.IsValid)
            {
                var myCourses = await _db.Enrollments
                    .Where(e => e.UniversityId == user.UniversityId)
                    .Include(e => e.Course)
                    .Select(e => e.Course!).ToListAsync();
                vm.AvailableCourses = new SelectList(myCourses, "CourseCode", "CourseName", vm.CourseCode);
                return View(vm);
            }

            var group = new StudyGroup
            {
                GroupName = vm.GroupName.Trim(),
                Description = vm.Description?.Trim(),
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
                Status = MembershipStatus.Approved
            });
            await _db.SaveChangesAsync();

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

            // Must be enrolled in the course to view (FR-19)
            var enrolled = await _db.Enrollments.AnyAsync(
                e => e.UniversityId == user.UniversityId && e.CourseCode == group.CourseCode);
            if (!enrolled)
            {
                TempData["Error"] = "You must be enrolled in the course to view this group.";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.CurrentUserId = user.Id;
            ViewBag.IsMember = group.Members.Any(
                m => m.UserId == user.Id && m.Status == MembershipStatus.Approved);
            ViewBag.IsCreator = group.CreatorId == user.Id;
            ViewBag.ApprovedCount = group.Members.Count(m => m.Status == MembershipStatus.Approved);

            return View(group);
        }

        // ---------- JOIN -----------------------------------------------------------
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

            // FR-19 — must be enrolled in the course
            var enrolled = await _db.Enrollments.AnyAsync(
                e => e.UniversityId == user.UniversityId && e.CourseCode == group.CourseCode);
            if (!enrolled)
            {
                TempData["Error"] = "You are not enrolled in this course.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // Already a member?
            if (group.Members.Any(m => m.UserId == user.Id && m.Status == MembershipStatus.Approved))
            {
                TempData["Error"] = "You are already in this group.";
                return RedirectToAction(nameof(Details), new { id });
            }

            // E1 of UC-07 — capacity check
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
                Status = MembershipStatus.Approved   // auto-approve for now
            });
            await _db.SaveChangesAsync();

            // Mark Full if we just hit capacity
            if (approvedCount + 1 >= group.MaxMembers)
            {
                group.Status = StudyGroupStatus.Full;
                await _db.SaveChangesAsync();
            }

            TempData["Success"] = "You joined the group.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- LEAVE ----------------------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.StudyGroupMembers
                .FirstOrDefaultAsync(m => m.StudyGroupId == id && m.UserId == user.Id);
            if (membership is null) return RedirectToAction(nameof(Index));

            _db.StudyGroupMembers.Remove(membership);
            await _db.SaveChangesAsync();

            // If group is now empty, archive it (Empty study group edge case)
            var remaining = await _db.StudyGroupMembers
                .CountAsync(m => m.StudyGroupId == id && m.Status == MembershipStatus.Approved);
            if (remaining == 0)
            {
                var grp = await _db.StudyGroups.FindAsync(id);
                if (grp is not null)
                {
                    grp.Status = StudyGroupStatus.Archived;
                    await _db.SaveChangesAsync();
                }
            }

            TempData["Success"] = "You left the group.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- POST A MESSAGE (FR-22) ----------------------------------------
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
            await _db.SaveChangesAsync();

            // 2. Broadcast it live to everyone currently viewing this group
            await _hub.Clients.Group($"group-{id}").SendAsync("ReceiveMessage", new
            {
                senderName = user.FullName,
                content = message.Content,
                sentAt = message.SentAt.ToString("MMM dd, HH:mm")
            });

            // 3. Return JSON instead of a redirect so the page does NOT reload
            return Json(new { ok = true });
        }

        // ---------- MY COURSES (UC-08 / FR-26) -------------------------------------
        public async Task<IActionResult> MyCourses()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var courses = await _db.Enrollments
                .Where(e => e.UniversityId == user.UniversityId)
                .Include(e => e.Course)
                .Select(e => e.Course!)
                .ToListAsync();

            return View(courses);
        }
    }
}
