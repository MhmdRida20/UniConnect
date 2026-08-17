using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Controllers
{
    /// <summary>
    /// Admin screen for suspending/reactivating accounts (Auth Edge Cases:
    /// "Account suspended during active session"). Enforcement itself lives
    /// in SuspendedUserMiddleware — this controller is just the on/off switch.
    ///
    /// A UniversityAdmin only ever sees/manages accounts belonging to their
    /// own university; a Super Admin ("Admin") sees everyone.
    /// </summary>
    [Authorize(Roles = "Admin,UniversityAdmin")]
    public class AdminUsersController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AuditLogService _auditLog;

        public AdminUsersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _auditLog = auditLog;
        }

        private bool IsSuperAdmin => User.IsInRole("Admin");

        // Controls both the ORDER role groups appear in within a university,
        // and which role a user with multiple roles (rare, but possible) is
        // grouped under — whichever of these appears earliest wins.
        private static readonly string[] RoleDisplayOrder =
            { "UniversityAdmin", "Instructor", "DepartmentStaff", "Company", "Student", "Admin" };

        public async Task<IActionResult> Index(string? search)
        {
            var query = _db.Users.AsQueryable();

            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null) return Challenge();
                query = query.Where(u => u.UniversityCode == currentUser.UniversityCode);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(u =>
                    u.FullName.Contains(term) ||
                    u.Email!.Contains(term) ||
                    u.UniversityId.Contains(term));
            }

            var users = await query.OrderBy(u => u.FullName).Take(500).ToListAsync();

            var roleNames = new Dictionary<string, List<string>>();
            foreach (var u in users)
                roleNames[u.Id] = (await _userManager.GetRolesAsync(u)).ToList();

            var universityNames = await _db.Universities.ToDictionaryAsync(u => u.Code, u => u.Name);

            // Group by university first, then by role within each — a flat,
            // unlabeled list made it hard to tell at a glance which
            // institution a user belonged to or what they actually were.
            var universityGroups = users
                .GroupBy(u => u.UniversityCode)
                .OrderBy(g => universityNames.TryGetValue(g.Key, out var name) ? name : g.Key)
                .Select(uniGroup => new AdminUserUniversityGroup
                {
                    UniversityCode = uniGroup.Key,
                    UniversityName = universityNames.TryGetValue(uniGroup.Key, out var n) ? n : uniGroup.Key,
                    RoleGroups = uniGroup
                        .GroupBy(u =>
                        {
                            var roles = roleNames[u.Id];
                            return RoleDisplayOrder.FirstOrDefault(r => roles.Contains(r))
                                ?? (roles.FirstOrDefault() ?? "Student");
                        })
                        .OrderBy(rg => Array.IndexOf(RoleDisplayOrder, rg.Key) is var idx && idx >= 0 ? idx : int.MaxValue)
                        .Select(rg => new AdminUserRoleGroup { RoleName = rg.Key, Users = rg.ToList() })
                        .ToList()
                })
                .ToList();

            ViewBag.RoleNames = roleNames;
            ViewBag.Search = search;
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            ViewBag.IsSuperAdmin = IsSuperAdmin;

            return View(universityGroups);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleSuspend(string userId)
        {
            if (userId == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You can't suspend your own account.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            // A UniversityAdmin may only suspend/reactivate accounts that
            // belong to their OWN university — never another institution's.
            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null || user.UniversityCode != currentUser.UniversityCode)
                    return Forbid();
            }

            user.IsSuspended = !user.IsSuspended;

            // Belt-and-suspenders: invalidating the security stamp means even
            // the periodic revalidation cycle (see Program.cs) would catch
            // this independently of the immediate middleware check.
            await _userManager.UpdateSecurityStampAsync(user);
            await _userManager.UpdateAsync(user);

            await _auditLog.LogAsync(
                user.IsSuspended ? "AccountDeactivation" : "AccountActivation",
                userId: _userManager.GetUserId(User),
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id,
                details: $"Target user: {user.FullName} ({user.Email})");

            TempData["Success"] = user.IsSuspended
                ? $"{user.FullName} has been suspended and signed out."
                : $"{user.FullName} has been reactivated.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- RESET TWO-FACTOR ---------------------------------------------
        // The escape hatch for a student who has lost both their authenticator
        // device and their recovery codes. Without it, the only remedy is
        // editing AspNetUsers by hand, which is not something to be doing on a
        // live system.
        //
        // What stops this becoming a back door is the audit entry: every reset
        // names the administrator who performed it. The same tenant restriction
        // as ToggleSuspend applies, so a UniversityAdmin cannot clear 2FA on
        // another institution's account.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetTwoFactor(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null || user.UniversityCode != currentUser.UniversityCode)
                    return Forbid();
            }

            if (!user.TwoFactorEnabled && await _userManager.GetAuthenticatorKeyAsync(user) is null)
            {
                TempData["Error"] = $"{user.FullName} does not have two-factor authentication set up.";
                return RedirectToAction(nameof(Index));
            }

            // Order matters. Clearing the flag first means that if the second
            // call fails, the user can still log in with their password alone
            // rather than being stranded at a challenge they cannot answer.
            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);

            // Invalidates any remembered-browser cookies still carrying the old
            // stamp, so a machine that was skipping the challenge stops doing so.
            await _userManager.UpdateSecurityStampAsync(user);

            await _auditLog.LogAsync(
                "TwoFactorAdminReset",
                userId: _userManager.GetUserId(User),
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id,
                details: $"Two-factor reset by administrator. Target user: {user.FullName} ({user.Email})");

            TempData["Success"] =
                $"Two-factor authentication has been reset for {user.FullName}. " +
                "They can sign in with their password and set it up again.";
            return RedirectToAction(nameof(Index));
        }

        // ---------- CHANGE ROLE — FR-83 ------------------------------------------
        // Deliberately does NOT allow assigning "Admin" or "Company" through
        // this screen: Super Admin accounts are provisioned deliberately
        // (there's exactly one seeded), and Company accounts are tied to
        // the university-creation flow specifically (one per university) —
        // not something to casually reassign here.
        private static readonly string[] SuperAdminAssignableRoles = { "Student", "Instructor", "DepartmentStaff", "UniversityAdmin" };
        private static readonly string[] UniversityAdminAssignableRoles = { "Student", "Instructor", "DepartmentStaff" };

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(string userId, string newRole)
        {
            if (userId == _userManager.GetUserId(User))
            {
                TempData["Error"] = "You can't change your own role.";
                return RedirectToAction(nameof(Index));
            }

            var allowedRoles = IsSuperAdmin ? SuperAdminAssignableRoles : UniversityAdminAssignableRoles;
            if (!allowedRoles.Contains(newRole))
            {
                TempData["Error"] = "That role isn't available to assign from this screen.";
                return RedirectToAction(nameof(Index));
            }

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null) return NotFound();

            if (!IsSuperAdmin)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser is null || user.UniversityCode != currentUser.UniversityCode)
                    return Forbid();
            }

            var currentRoles = await _userManager.GetRolesAsync(user);

            // Never touch Admin/Company/UniversityAdmin accounts through
            // this screen either, even to REMOVE those roles — those stay
            // deliberate, out-of-band actions.
            if (currentRoles.Any(r => r is "Admin" or "Company") || (!IsSuperAdmin && currentRoles.Contains("UniversityAdmin")))
            {
                TempData["Error"] = "This account's role can't be changed from this screen.";
                return RedirectToAction(nameof(Index));
            }

            if (currentRoles.Contains(newRole))
            {
                TempData["Error"] = $"{user.FullName} already has the {newRole} role.";
                return RedirectToAction(nameof(Index));
            }

            await _userManager.RemoveFromRolesAsync(user, currentRoles);
            await _userManager.AddToRoleAsync(user, newRole);
            await _userManager.UpdateSecurityStampAsync(user); // role change takes effect within the existing 1-minute revalidation window

            await _auditLog.LogAsync(
                "UserRoleChanged",
                userId: _userManager.GetUserId(User),
                universityCode: user.UniversityCode,
                entityType: "User",
                entityId: user.Id,
                details: $"{user.FullName}: {string.Join(",", currentRoles)} -> {newRole}");

            TempData["Success"] = $"{user.FullName}'s role changed to {newRole}.";
            return RedirectToAction(nameof(Index));
        }
    }

    public class AdminUserUniversityGroup
    {
        public string UniversityCode { get; set; } = string.Empty;
        public string UniversityName { get; set; } = string.Empty;
        public List<AdminUserRoleGroup> RoleGroups { get; set; } = new();
    }

    public class AdminUserRoleGroup
    {
        public string RoleName { get; set; } = string.Empty;
        public List<ApplicationUser> Users { get; set; } = new();
    }
}
