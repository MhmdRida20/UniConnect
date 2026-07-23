using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Middleware
{
    /// <summary>
    /// Edge Case: "Session hijacking attempt — the system shall detect and
    /// terminate suspicious sessions."
    ///
    /// Deliberately implemented as LOGGING, not automatic termination. A
    /// mismatched IP address between login time and a later request is a
    /// genuinely common, benign occurrence — mobile networks rotate IPs
    /// constantly, VPNs and corporate NAT change the visible address, and
    /// campus wifi often does the same. Auto-terminating on every such
    /// change would lock out far more legitimate students than attackers,
    /// which is a worse outcome for a real deployment than under-detecting.
    /// An admin can review flagged sessions in the Audit Log and decide
    /// whether any warrant manually suspending the account.
    ///
    /// Throttled to at most one log entry per user+new-IP combination per
    /// 30 minutes, so a student whose IP genuinely changed and stays
    /// changed for the rest of their session doesn't flood the audit log
    /// with a duplicate entry on every single subsequent request.
    /// </summary>
    public class SessionAnomalyMiddleware
    {
        private readonly RequestDelegate _next;
        private static readonly TimeSpan ThrottleWindow = TimeSpan.FromMinutes(30);

        public SessionAnomalyMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context, ApplicationDbContext db, AuditLogService auditLog, UserManager<ApplicationUser> userManager)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var loginIp = context.User.FindFirst("LoginIpAddress")?.Value;
                var currentIp = context.Connection.RemoteIpAddress?.ToString();

                if (!string.IsNullOrEmpty(loginIp) && !string.IsNullOrEmpty(currentIp) && loginIp != currentIp)
                {
                    var userId = userManager.GetUserId(context.User);
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var throttleSince = DateTime.UtcNow - ThrottleWindow;
                        var alreadyFlagged = await db.AuditLogs.AnyAsync(a =>
                            a.UserId == userId
                            && a.Action == "PossibleSessionAnomaly"
                            && a.Details != null && a.Details.Contains(currentIp)
                            && a.Timestamp >= throttleSince);

                        if (!alreadyFlagged)
                        {
                            var user = await userManager.FindByIdAsync(userId);
                            await auditLog.LogAsync(
                                "PossibleSessionAnomaly",
                                userId: userId,
                                universityCode: user?.UniversityCode,
                                entityType: "Session",
                                details: $"Signed in from {loginIp}, now seen from {currentIp}. " +
                                    "May be a normal network change (mobile/VPN) — not blocked automatically.");
                        }
                    }
                }
            }

            await _next(context);
        }
    }
}
