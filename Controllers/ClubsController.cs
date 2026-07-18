using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Filters;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Implements the Clubs & Organizations service (FR-67 through FR-76):
    /// register clubs, join with officer approval, announcements, events with
    /// RSVP, real-time chat, leadership transfer, and inactivity detection.
    ///
    /// Structurally this mirrors StudyGroupsController closely (same approval
    /// flow, same "everyone joins the SignalR group" pattern) — the real
    /// differences are the officer role hierarchy (President/VP/Officer vs.
    /// a single Creator) and the two new concepts, announcements and events.
    /// </summary>
    [Authorize]
    [RequireService(ServiceCodes.Clubs)]
    public class ClubsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<ClubHub> _hub;
        private readonly IWebHostEnvironment _env;
        private readonly UniConnect.Services.AuditLogService _auditLog;

        private static readonly string[] OfficerRoleNames = { "President", "VicePresident", "Officer" };

        public ClubsController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<ClubHub> hub,
            IWebHostEnvironment env,
            UniConnect.Services.AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _env = env;
            _auditLog = auditLog;
        }

        private Task BroadcastClubUpdated(int clubId)
            => _hub.Clients.Group($"club-{clubId}").SendAsync("ClubUpdated");

        private Task BroadcastListChanged()
            => _hub.Clients.Group("clubs-lobby").SendAsync("ClubListChanged");

        // ---------- INDEX -----------------------------------------------------
        public async Task<IActionResult> Index(string? category)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var query = _db.Clubs
                .Include(c => c.Creator)
                .Include(c => c.Members)
                .Where(c => c.UniversityCode == user.UniversityCode && c.Status != ClubStatus.Archived);

            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<ClubCategory>(category, out var cat))
                query = query.Where(c => c.Category == cat);

            var clubs = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();

            ViewBag.SelectedCategory = category;
            ViewBag.CurrentUserId = user.Id;
            return View(clubs);
        }

        // ---------- CREATE (GET) -----------------------------------------------
        public IActionResult Create() => View(new ClubCreateVM());

        // ---------- CREATE (POST) ----------------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ClubCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            if (!ModelState.IsValid) return View(vm);

            // UC-16 E1 — duplicate club name warns rather than blocks.
            if (!vm.ConfirmDuplicate)
            {
                var duplicate = await _db.Clubs.AnyAsync(c =>
                    c.UniversityCode == user.UniversityCode
                    && c.Status != ClubStatus.Archived
                    && c.ClubName == vm.ClubName.Trim());

                if (duplicate)
                {
                    ViewBag.DuplicateWarning = true;
                    return View(vm);
                }
            }

            string? logoPath = null;
            if (vm.Logo is { Length: > 0 })
            {
                var ext = Path.GetExtension(vm.Logo.FileName).ToLowerInvariant();
                if (new[] { ".png", ".jpg", ".jpeg" }.Contains(ext) && vm.Logo.Length <= 2 * 1024 * 1024)
                {
                    var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "clubs");
                    Directory.CreateDirectory(uploadsDir);
                    var storedName = $"{Guid.NewGuid()}{ext}";
                    using var stream = new FileStream(Path.Combine(uploadsDir, storedName), FileMode.Create);
                    await vm.Logo.CopyToAsync(stream);
                    logoPath = $"/uploads/clubs/{storedName}";
                }
            }

            var club = new Club
            {
                UniversityCode = user.UniversityCode,
                CreatorId = user.Id,
                ClubName = vm.ClubName.Trim(),
                Description = vm.Description?.Trim(),
                Category = vm.Category,
                LogoPath = logoPath,
                MaxMembers = vm.MaxMembers,
                Status = ClubStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.Clubs.Add(club);
            await _db.SaveChangesAsync();

            _db.ClubMembers.Add(new ClubMember
            {
                ClubId = club.Id,
                UserId = user.Id,
                Role = ClubRole.President,
                Status = ClubMembershipStatus.Approved,
                JoinedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "ClubCreated",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "Club",
                entityId: club.Id.ToString(),
                details: club.ClubName);

            await BroadcastListChanged();

            TempData["Success"] = "Club registered — you're the President.";
            return RedirectToAction(nameof(Details), new { id = club.Id });
        }

        // ---------- DETAILS ------------------------------------------------
        public async Task<IActionResult> Details(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var club = await _db.Clubs
                .Include(c => c.Creator)
                .Include(c => c.Members).ThenInclude(m => m.User)
                .Include(c => c.Announcements.OrderByDescending(a => a.CreatedAt)).ThenInclude(a => a.Author)
                .Include(c => c.Events.OrderBy(e => e.EventDateTime)).ThenInclude(e => e.Rsvps)
                .Include(c => c.Messages.OrderBy(m => m.SentAt)).ThenInclude(m => m.Sender)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (club is null) return NotFound();
            if (club.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This club doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

            var myMembership = club.Members.FirstOrDefault(m => m.UserId == user.Id);

            ViewBag.CurrentUserId = user.Id;
            ViewBag.IsMember = myMembership?.Status == ClubMembershipStatus.Approved;
            ViewBag.IsOfficer = myMembership?.Status == ClubMembershipStatus.Approved
                              && OfficerRoleNames.Contains(myMembership.Role.ToString());
            ViewBag.IsPresident = myMembership?.Status == ClubMembershipStatus.Approved
                                && myMembership.Role == ClubRole.President;
            ViewBag.MyMembershipStatus = myMembership?.Status;
            ViewBag.ApprovedCount = club.Members.Count(m => m.Status == ClubMembershipStatus.Approved);

            return View(club);
        }

        // ---------- JOIN (creates a Pending request — FR-69) ---------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var club = await _db.Clubs.Include(c => c.Members).FirstOrDefaultAsync(c => c.Id == id);
            if (club is null) return NotFound();
            if (club.UniversityCode != user.UniversityCode)
            {
                TempData["Error"] = "This club doesn't belong to your university.";
                return RedirectToAction(nameof(Index));
            }

            var existing = club.Members.FirstOrDefault(m => m.UserId == user.Id);
            if (existing != null)
            {
                TempData["Error"] = existing.Status switch
                {
                    ClubMembershipStatus.Approved => "You're already a member of this club.",
                    ClubMembershipStatus.Pending => "You already have a pending request for this club.",
                    _ => "You can't request to join right now."
                };
                return RedirectToAction(nameof(Details), new { id });
            }

            // UC-17 E1 — membership limit reached
            var approvedCount = club.Members.Count(m => m.Status == ClubMembershipStatus.Approved);
            if (club.MaxMembers.HasValue && approvedCount >= club.MaxMembers.Value)
            {
                TempData["Error"] = "This club has reached its maximum member count.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _db.ClubMembers.Add(new ClubMember
            {
                ClubId = id,
                UserId = user.Id,
                Role = ClubRole.Member,
                Status = ClubMembershipStatus.Pending,
                JoinedAt = DateTime.UtcNow
            });

            if (club.Status == ClubStatus.Inactive)
                club.Status = ClubStatus.Active;

            await _db.SaveChangesAsync();
            await BroadcastClubUpdated(id);
            await BroadcastListChanged();

            TempData["Success"] = "Your request to join has been sent to the club officers.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------- LEAVE (also cancels a pending request) ------------------
        // Edge case: if the President leaves, auto-assign to the Vice
        // President if one exists, else the longest-standing remaining
        // approved member; if nobody is left, archive the club.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.ClubMembers
                .FirstOrDefaultAsync(m => m.ClubId == id && m.UserId == user.Id);
            if (membership is null) return RedirectToAction(nameof(Index));

            var club = await _db.Clubs.FindAsync(id);
            var wasPending = membership.Status == ClubMembershipStatus.Pending;
            var wasPresident = membership.Status == ClubMembershipStatus.Approved
                             && membership.Role == ClubRole.President;

            _db.ClubMembers.Remove(membership);
            await _db.SaveChangesAsync();

            if (club is not null && !wasPending)
            {
                var remaining = await _db.ClubMembers
                    .Where(m => m.ClubId == id && m.Status == ClubMembershipStatus.Approved)
                    .OrderBy(m => m.JoinedAt)
                    .ToListAsync();

                if (!remaining.Any())
                {
                    club.Status = ClubStatus.Archived;
                }
                else if (wasPresident)
                {
                    var successor = remaining.FirstOrDefault(m => m.Role == ClubRole.VicePresident)
                                     ?? remaining.First();
                    successor.Role = ClubRole.President;
                    club.CreatorId = successor.UserId;
                }

                await _db.SaveChangesAsync();
            }

            await BroadcastClubUpdated(id);
            await BroadcastListChanged();

            TempData["Success"] = wasPending ? "Your request was withdrawn."
                                 : wasPresident ? "You left the club. Leadership was passed on."
                                 : "You left the club.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- APPROVE MEMBER (officer only) ---------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.ClubMembers.Include(m => m.Club).Include(m => m.User).FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.Club is null) return NotFound();

            if (!await IsOfficerAsync(membership.ClubId, user.Id)) return Forbid();

            if (membership.Status != ClubMembershipStatus.Pending)
            {
                TempData["Error"] = "That request is no longer pending.";
                return RedirectToAction(nameof(Details), new { id = membership.ClubId });
            }

            var club = membership.Club;
            var approvedCount = await _db.ClubMembers.CountAsync(
                m => m.ClubId == club.Id && m.Status == ClubMembershipStatus.Approved);

            if (club.MaxMembers.HasValue && approvedCount >= club.MaxMembers.Value)
            {
                TempData["Error"] = "The club is already at its maximum member count.";
                return RedirectToAction(nameof(Details), new { id = club.Id });
            }

            membership.Status = ClubMembershipStatus.Approved;
            if (club.Status == ClubStatus.Inactive) club.Status = ClubStatus.Active;
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "ClubMembershipApproved",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "Club",
                entityId: club.Id.ToString(),
                details: $"Member: {membership.UserId}");

            await _hub.Clients.Group($"club-{club.Id}").SendAsync("MemberJoined", new
            {
                name = membership.User?.FullName ?? "A new member"
            });
            await BroadcastClubUpdated(club.Id);
            await BroadcastListChanged();
            TempData["Success"] = "Member approved.";
            return RedirectToAction(nameof(Details), new { id = club.Id });
        }

        // ---------- REJECT MEMBER (officer only) -----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.ClubMembers.Include(m => m.Club).FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.Club is null) return NotFound();
            if (!await IsOfficerAsync(membership.ClubId, user.Id)) return Forbid();

            if (membership.Status != ClubMembershipStatus.Pending)
            {
                TempData["Error"] = "That request is no longer pending.";
                return RedirectToAction(nameof(Details), new { id = membership.ClubId });
            }

            var clubId = membership.ClubId;
            _db.ClubMembers.Remove(membership);
            await _db.SaveChangesAsync();

            await BroadcastClubUpdated(clubId);
            await BroadcastListChanged();
            TempData["Success"] = "Request rejected.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- REMOVE MEMBER (officer only) -----------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.ClubMembers.Include(m => m.Club).FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.Club is null) return NotFound();
            if (!await IsOfficerAsync(membership.ClubId, user.Id)) return Forbid();

            if (membership.Role == ClubRole.President)
            {
                TempData["Error"] = "The President can't be removed this way — transfer leadership first.";
                return RedirectToAction(nameof(Details), new { id = membership.ClubId });
            }

            var clubId = membership.ClubId;
            _db.ClubMembers.Remove(membership);
            await _db.SaveChangesAsync();

            await BroadcastClubUpdated(clubId);
            await BroadcastListChanged();
            TempData["Success"] = "Member removed from the club.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- PROMOTE / DEMOTE MEMBER (President only) -----------------
        // Only one Vice President at a time — if one already exists, the
        // President must demote them to Officer first (an explicit action)
        // rather than the system silently swapping them out.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(int memberId, ClubRole newRole)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var membership = await _db.ClubMembers.Include(m => m.Club).Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.Club is null) return NotFound();

            var clubId = membership.ClubId;
            if (!await IsPresidentAsync(clubId, user.Id)) return Forbid();

            if (membership.Role == ClubRole.President || newRole == ClubRole.President)
            {
                TempData["Error"] = "Use \"Transfer Leadership\" to change the President.";
                return RedirectToAction(nameof(Details), new { id = clubId });
            }

            if (newRole == membership.Role)
            {
                return RedirectToAction(nameof(Details), new { id = clubId });
            }

            if (newRole == ClubRole.VicePresident)
            {
                var currentVp = await _db.ClubMembers.Include(m => m.User).FirstOrDefaultAsync(
                    m => m.ClubId == clubId && m.Role == ClubRole.VicePresident && m.Id != memberId);
                if (currentVp is not null)
                {
                    TempData["Error"] = $"{currentVp.User?.FullName ?? "Someone"} is already Vice President — demote them first.";
                    return RedirectToAction(nameof(Details), new { id = clubId });
                }
            }

            membership.Role = newRole;
            await _db.SaveChangesAsync();

            await BroadcastClubUpdated(clubId);
            await NotifyRoleChangeAsync(membership.UserId, membership.Club.ClubName, newRole);

            TempData["Success"] = $"{membership.User?.FullName ?? "Member"}'s role updated to {newRole}.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- TRANSFER LEADERSHIP (President only) — FR-74 -------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TransferLeadership(int memberId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var newPresident = await _db.ClubMembers.Include(m => m.Club).Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (newPresident?.Club is null) return NotFound();

            var clubId = newPresident.ClubId;
            if (!await IsPresidentAsync(clubId, user.Id)) return Forbid();
            if (newPresident.Status != ClubMembershipStatus.Approved)
            {
                TempData["Error"] = "Only an approved member can become President.";
                return RedirectToAction(nameof(Details), new { id = clubId });
            }

            var currentPresident = await _db.ClubMembers.FirstOrDefaultAsync(
                m => m.ClubId == clubId && m.Role == ClubRole.President);
            if (currentPresident is not null) currentPresident.Role = ClubRole.Officer;

            newPresident.Role = ClubRole.President;
            newPresident.Club!.CreatorId = newPresident.UserId;
            await _db.SaveChangesAsync();

            await BroadcastClubUpdated(clubId);
            await NotifyRoleChangeAsync(newPresident.UserId, newPresident.Club.ClubName, ClubRole.President);
            if (currentPresident is not null)
                await NotifyRoleChangeAsync(currentPresident.UserId, newPresident.Club.ClubName, ClubRole.Officer);

            TempData["Success"] = $"Leadership transferred to {newPresident.User?.FullName ?? "the selected member"}.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- POST ANNOUNCEMENT (officer only) — FR-70 -----------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostAnnouncement(int clubId, string title, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (!await IsOfficerAsync(clubId, user.Id)) return Forbid();

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            {
                TempData["Error"] = "Please enter a title and message.";
                return RedirectToAction(nameof(Details), new { id = clubId });
            }

            _db.ClubAnnouncements.Add(new ClubAnnouncement
            {
                ClubId = clubId,
                AuthorId = user.Id,
                Title = title.Trim(),
                Content = content.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            var club = await _db.Clubs.FindAsync(clubId);
            if (club is not null && club.Status == ClubStatus.Inactive) club.Status = ClubStatus.Active;

            await _db.SaveChangesAsync();
            await BroadcastClubUpdated(clubId);

            TempData["Success"] = "Announcement posted.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- CREATE EVENT (GET, officer only) — FR-71 -----------------
        public async Task<IActionResult> CreateEvent(int clubId)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (!await IsOfficerAsync(clubId, user.Id)) return Forbid();

            var club = await _db.Clubs.FindAsync(clubId);
            if (club is null) return NotFound();

            ViewBag.Club = club;
            return View(new ClubEventCreateVM());
        }

        // ---------- CREATE EVENT (POST) --------------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateEvent(int clubId, ClubEventCreateVM vm)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (!await IsOfficerAsync(clubId, user.Id)) return Forbid();

            var club = await _db.Clubs.FindAsync(clubId);
            if (club is null) return NotFound();

            // UC-18 E1 — event date in the past is rejected.
            if (vm.EventDateTime <= DateTime.Now)
                ModelState.AddModelError(nameof(vm.EventDateTime), "The event date and time must be in the future.");

            if (!ModelState.IsValid)
            {
                ViewBag.Club = club;
                return View(vm);
            }

            var clubEvent = new ClubEvent
            {
                ClubId = clubId,
                CreatorId = user.Id,
                Title = vm.Title.Trim(),
                Description = vm.Description?.Trim(),
                EventDateTime = vm.EventDateTime,
                Location = vm.Location.Trim(),
                MaxAttendees = vm.MaxAttendees,
                CreatedAt = DateTime.UtcNow
            };
            _db.ClubEvents.Add(clubEvent);

            if (club.Status == ClubStatus.Inactive) club.Status = ClubStatus.Active;

            await _db.SaveChangesAsync();
            await BroadcastClubUpdated(clubId);

            TempData["Success"] = "Event created — members can now RSVP.";
            return RedirectToAction(nameof(Details), new { id = clubId });
        }

        // ---------- RSVP (member only) — FR-72 -------------------------------
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Rsvp(int eventId, RsvpStatus status)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();

            var clubEvent = await _db.ClubEvents.Include(e => e.Rsvps).FirstOrDefaultAsync(e => e.Id == eventId);
            if (clubEvent is null) return NotFound();

            var isMember = await _db.ClubMembers.AnyAsync(
                m => m.ClubId == clubEvent.ClubId && m.UserId == user.Id && m.Status == ClubMembershipStatus.Approved);
            if (!isMember) return Forbid();

            var existingRsvp = clubEvent.Rsvps.FirstOrDefault(r => r.UserId == user.Id);

            if (status == RsvpStatus.Attending && clubEvent.MaxAttendees.HasValue)
            {
                var attendingCount = clubEvent.Rsvps.Count(r => r.RsvpStatus == RsvpStatus.Attending && r.UserId != user.Id);
                if (attendingCount >= clubEvent.MaxAttendees.Value)
                {
                    TempData["Error"] = "This event is full.";
                    return RedirectToAction(nameof(Details), new { id = clubEvent.ClubId });
                }
            }

            if (existingRsvp is null)
            {
                _db.EventRsvps.Add(new EventRsvp
                {
                    ClubEventId = eventId,
                    UserId = user.Id,
                    RsvpStatus = status,
                    RespondedAt = DateTime.UtcNow
                });
            }
            else
            {
                existingRsvp.RsvpStatus = status;
                existingRsvp.RespondedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            await BroadcastClubUpdated(clubEvent.ClubId);

            TempData["Success"] = "RSVP saved.";
            return RedirectToAction(nameof(Details), new { id = clubEvent.ClubId });
        }

        // ---------- POST MESSAGE (member only) — FR-73 -----------------------
        // Edge case: a non-member attempting to post is denied (Forbid),
        // matching "Non-member accesses club chat" in the edge cases doc.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PostMessage(int clubId, string content)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is null) return Challenge();
            if (string.IsNullOrWhiteSpace(content)) return Json(new { ok = false });

            var isMember = await _db.ClubMembers.AnyAsync(
                m => m.ClubId == clubId && m.UserId == user.Id && m.Status == ClubMembershipStatus.Approved);
            if (!isMember) return Forbid();

            var message = new ClubMessage
            {
                ClubId = clubId,
                SenderId = user.Id,
                Content = content.Trim(),
                SentAt = DateTime.UtcNow
            };
            _db.ClubMessages.Add(message);

            var club = await _db.Clubs.FindAsync(clubId);
            var reactivated = false;
            if (club is not null && club.Status == ClubStatus.Inactive)
            {
                club.Status = ClubStatus.Active;
                reactivated = true;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"club-{clubId}").SendAsync("ReceiveMessage", new
            {
                senderName = user.FullName,
                content = message.Content,
                sentAt = message.SentAt.ToString("MMM dd, HH:mm")
            });

            if (reactivated) await BroadcastListChanged();

            return Json(new { ok = true });
        }

        // ---------- Helpers --------------------------------------------------
        private async Task<bool> IsOfficerAsync(int clubId, string userId)
        {
            var membership = await _db.ClubMembers.FirstOrDefaultAsync(
                m => m.ClubId == clubId && m.UserId == userId && m.Status == ClubMembershipStatus.Approved);
            return membership is not null && OfficerRoleNames.Contains(membership.Role.ToString());
        }

        private async Task<bool> IsPresidentAsync(int clubId, string userId)
        {
            return await _db.ClubMembers.AnyAsync(
                m => m.ClubId == clubId && m.UserId == userId
                  && m.Status == ClubMembershipStatus.Approved && m.Role == ClubRole.President);
        }

        // Personal notification so a member finds out about their own role
        // change directly, rather than only inferring it from a page reload.
        private Task NotifyRoleChangeAsync(string userId, string clubName, ClubRole newRole)
        {
            var roleLabel = newRole switch
            {
                ClubRole.President => "President",
                ClubRole.VicePresident => "Vice President",
                ClubRole.Officer => "Officer",
                _ => "Member"
            };
            return _hub.Clients.Group($"club-user-{userId}").SendAsync("YourRoleChanged", new
            {
                clubName,
                newRole = roleLabel
            });
        }
    }
}
