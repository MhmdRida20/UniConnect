using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UniConnect.Models;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Puts a controller into a state where its action methods can actually run:
/// a signed-in ClaimsPrincipal that UserManager.GetUserAsync can resolve, plus
/// TempData, which several actions write to before redirecting.
///
/// This is the cost of the decision recorded in TEST_PLAN.md §2.2③ — testing
/// business rules through the public action rather than extracting them out of
/// the controller. Paid once, here.
/// </summary>
public static class ControllerHarness
{
    public static ClaimsPrincipal PrincipalFor(ApplicationUser user, params string[] roles)
    {
        var claims = new List<Claim>
        {
            // UserManager.GetUserAsync reads this claim type by default.
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id)
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
    }

    public static TController SignedInAs<TController>(
        this TController controller, ApplicationUser user, params string[] roles)
        where TController : Controller
    {
        var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            User = PrincipalFor(user, roles),
            RequestServices = services
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
        };

        controller.TempData = new TempDataDictionary(httpContext, new NullTempDataProvider());

        // Assigned rather than resolved. ControllerBase.Url would otherwise ask
        // the request's service provider for IUrlHelperFactory, which pulls in
        // the whole routing stack — endpoint feature, LinkGenerator, the lot —
        // for what the actions under test only ever use to build a redirect
        // string they don't inspect.
        controller.Url = new StubUrlHelper(controller.ControllerContext);

        return controller;
    }

    /// <summary>
    /// The same, for an API controller. ControllerBase has no TempData and no
    /// views, so this only needs the signed-in principal — but it does need a
    /// StubUrlHelper, since CreatedAtAction builds a Location header.
    /// </summary>
    public static TController SignedInApi<TController>(
        this TController controller, ApplicationUser user, params string[] roles)
        where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext
        {
            User = PrincipalFor(user, roles),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
            RouteData = new RouteData(),
            ActionDescriptor = new Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor()
        };
        controller.Url = new StubUrlHelper(controller.ControllerContext);

        return controller;
    }

    private sealed class StubUrlHelper : IUrlHelper
    {
        public StubUrlHelper(ActionContext actionContext) => ActionContext = actionContext;

        public ActionContext ActionContext { get; }

        public string? Action(UrlActionContext context)
            => $"/{context.Controller ?? "Home"}/{context.Action ?? "Index"}";

        public string? RouteUrl(UrlRouteContext routeContext) => "/";

        public string Content(string? contentPath) => contentPath?.TrimStart('~') ?? "/";

        public bool IsLocalUrl(string? url) => url is not null && url.StartsWith('/') && !url.StartsWith("//");

        public string? Link(string? routeName, object? values) => $"https://localhost/{routeName}";
    }

    /// <summary>TempData that lives only in memory — nothing to persist between requests in a unit test.</summary>
    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context)
            => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // ---- result assertions ----

    public static RedirectToActionResult ShouldRedirectToAction(this IActionResult result, string action)
    {
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(action, redirect.ActionName);
        return redirect;
    }

    public static ViewResult ShouldBeView(this IActionResult result)
        => Assert.IsType<ViewResult>(result);

    public static TModel ShouldBeViewWithModel<TModel>(this IActionResult result)
    {
        var view = Assert.IsType<ViewResult>(result);
        return Assert.IsAssignableFrom<TModel>(view.Model);
    }
}
