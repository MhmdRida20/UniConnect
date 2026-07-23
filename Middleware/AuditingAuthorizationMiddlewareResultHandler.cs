using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Identity;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Middleware
{
    /// <summary>
    /// Edge Case (FR-92 / UC-20): "Unauthorized admin action — a user
    /// without permission attempts an admin action. The system shall deny
    /// access and log the attempt."
    ///
    /// [Authorize] already denies access on its own — this wraps ASP.NET
    /// Core's own default handler just to ALSO record an audit entry
    /// whenever that denial specifically happens because the user is
    /// logged in but lacks the right role ("Forbidden"), not simply because
    /// they aren't logged in at all ("Challenge", i.e. redirect to Login —
    /// not unauthorized *access*, just anonymous, so not logged here).
    /// </summary>
    public class AuditingAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

        public async Task HandleAsync(
            RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
        {
            if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
            {
                var auditLog = context.RequestServices.GetRequiredService<AuditLogService>();
                var userManager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();

                var userId = userManager.GetUserId(context.User);
                string? universityCode = null;
                if (!string.IsNullOrEmpty(userId))
                {
                    var user = await userManager.FindByIdAsync(userId);
                    universityCode = user?.UniversityCode;
                }

                await auditLog.LogAsync(
                    "UnauthorizedAccessAttempt",
                    userId: userId,
                    universityCode: universityCode,
                    entityType: "Route",
                    entityId: context.Request.Path,
                    details: $"{context.Request.Method} {context.Request.Path}");
            }

            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }
    }
}
