using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UniConnect.Models;
using UniConnect.Services;

namespace UniConnect.Filters
{
    /// <summary>
    /// Apply to a controller (or action) that belongs to a toggleable service —
    /// e.g. [RequireService(ServiceCodes.RideSharing)] on RidesController.
    ///
    /// Blocks access if the current user's university hasn't enabled that
    /// service (Services.docx: "Per-university service enablement"). This is
    /// the enforcement half of the service catalog — the nav bar hides the
    /// link, and this is the server-side backstop in case someone navigates
    /// there directly by URL.
    /// </summary>
    public class RequireServiceAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _serviceCode;

        public RequireServiceAttribute(string serviceCode)
        {
            _serviceCode = serviceCode;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var userManager = context.HttpContext.RequestServices
                .GetRequiredService<UserManager<ApplicationUser>>();
            var catalog = context.HttpContext.RequestServices
                .GetRequiredService<IServiceCatalogService>();

            // An API caller gets status codes, not redirects. Sending the mobile
            // app to an HTML "not available" page would arrive as a 200 full of
            // markup, which its JSON parser reads as an empty result — a
            // disabled service would look like "no internships" instead of
            // saying so.
            var isApi = context.HttpContext.Request.Path.StartsWithSegments("/api");

            var user = await userManager.GetUserAsync(context.HttpContext.User);
            if (user is null)
            {
                context.Result = isApi ? new UnauthorizedResult() : new ChallengeResult();
                return;
            }

            var enabled = await catalog.IsServiceEnabledAsync(user.UniversityCode, _serviceCode);
            if (!enabled)
            {
                context.Result = isApi
                    ? new ObjectResult(new
                    {
                        error = "This feature is not enabled for your university.",
                        code = "SERVICE_DISABLED"
                    })
                    { StatusCode = StatusCodes.Status403Forbidden }
                    : new RedirectToActionResult("NotAvailable", "Home", new { service = _serviceCode });
                return;
            }

            await next();
        }
    }
}
