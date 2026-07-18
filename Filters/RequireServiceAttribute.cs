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

            var user = await userManager.GetUserAsync(context.HttpContext.User);
            if (user is null)
            {
                context.Result = new ChallengeResult();
                return;
            }

            var enabled = await catalog.IsServiceEnabledAsync(user.UniversityCode, _serviceCode);
            if (!enabled)
            {
                context.Result = new RedirectToActionResult("NotAvailable", "Home", new { service = _serviceCode });
                return;
            }

            await next();
        }
    }
}
