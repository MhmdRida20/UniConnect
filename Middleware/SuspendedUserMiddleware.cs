using Microsoft.AspNetCore.Identity;
using UniConnect.Models;

namespace UniConnect.Middleware
{
    /// <summary>
    /// Auth Edge Cases: "Account suspended during active session — the
    /// system shall terminate the active session and deny further access."
    ///
    /// A suspended user's existing auth cookie would otherwise stay valid
    /// until it naturally expires (or until the next SecurityStamp
    /// validation cycle, which only runs periodically) — neither is
    /// "terminate the active session" in any immediate sense. This
    /// middleware checks IsSuspended on every authenticated request and
    /// signs them out right away if it's set, so suspension takes effect on
    /// their very next click, not eventually.
    /// </summary>
    public class SuspendedUserMiddleware
    {
        private readonly RequestDelegate _next;

        public SuspendedUserMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var user = await userManager.GetUserAsync(context.User);
                if (user is not null && user.IsSuspended)
                {
                    await signInManager.SignOutAsync();
                    context.Response.Redirect("/Identity/Account/Login?suspended=true");
                    return;
                }
            }

            await _next(context);
        }
    }
}
