using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Controllers.Api
{
    [ApiController]
    [Route("api/notifications")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Student")]
    public class NotificationsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public NotificationsApiController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public class NotificationDto
        {
            public int Id { get; set; }
            public string Title { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string? Link { get; set; }
            public DateTime CreatedAt { get; set; }
            public bool WasUnread { get; set; } // true = new since your last visit — same distinction NotificationsController.Index's ViewBag.NewIds gives the web view
        }

        // Same behavior as NotificationsController.Index — fetching this
        // list marks everything in it read. Deliberately identical logic,
        // not just identical output, so web and mobile can never disagree
        // about what counts as "read."
        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var userId = _userManager.GetUserId(User);
            var notifications = await _db.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(100)
                .ToListAsync();

            var unread = notifications.Where(n => !n.IsRead).ToList();
            var newIds = unread.Select(n => n.Id).ToHashSet();

            if (unread.Any())
            {
                foreach (var n in unread) n.IsRead = true;
                await _db.SaveChangesAsync();
            }

            var result = notifications.Select(n => new NotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Message = n.Message,
                Link = n.Link,
                CreatedAt = n.CreatedAt,
                WasUnread = newIds.Contains(n.Id)
            }).ToList();

            return Ok(result);
        }

        // Read-only — does NOT mark anything read. Exists so the dashboard's
        // notification badge can show an accurate count without silently
        // clearing it just because the app happened to check.
        [HttpGet("unread-count")]
        public async Task<IActionResult> UnreadCount()
        {
            var userId = _userManager.GetUserId(User);
            var count = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);
            return Ok(new { count });
        }
    }
}
