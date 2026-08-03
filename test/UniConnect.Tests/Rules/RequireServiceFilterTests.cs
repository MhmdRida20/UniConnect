using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using UniConnect.Filters;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// The server-side half of per-university service enablement.
///
/// The nav bar hides links for services a university hasn't switched on; this
/// filter is what stops someone simply typing the URL. Testing it directly is
/// worthwhile because the failure mode — a service quietly reachable when it
/// shouldn't be — looks identical to everything working.
/// </summary>
public class RequireServiceFilterTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly FakeServiceCatalog _catalog = new();

    public RequireServiceFilterTests() => _test.Db.AddUniversity();

    public void Dispose() => _test.Dispose();

    private async Task<(IActionResult? Result, bool ActionRan)> Run(
        string serviceCode, ApplicationUser? signedInAs)
    {
        var services = new ServiceCollection()
            .AddSingleton(IdentityHarness.CreateUserManager(_test.Db))
            .AddSingleton<IServiceCatalogService>(_catalog)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = services };
        if (signedInAs is not null)
            httpContext.User = ControllerHarness.PrincipalFor(signedInAs, "Student");

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(
            actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: null!);

        var ran = false;

        await new RequireServiceAttribute(serviceCode).OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(
                actionContext, new List<IFilterMetadata>(), controller: null!));
        });

        return (executing.Result, ran);
    }

    [Fact]
    public async Task An_enabled_service_lets_the_action_run()
    {
        var user = _test.Db.AddUser("U2024001");

        var (result, ran) = await Run(ServiceCodes.Clubs, user);

        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public async Task A_disabled_service_redirects_instead_of_running_the_action()
    {
        var user = _test.Db.AddUser("U2024001");
        _catalog.Disable(user.UniversityCode, ServiceCodes.Clubs);

        var (result, ran) = await Run(ServiceCodes.Clubs, user);

        Assert.False(ran);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("NotAvailable", redirect.ActionName);
        Assert.Equal("Home", redirect.ControllerName);
        Assert.Equal(ServiceCodes.Clubs, redirect.RouteValues!["service"]);
    }

    [Fact]
    public async Task Disabling_one_service_does_not_close_the_others()
    {
        var user = _test.Db.AddUser("U2024001");
        _catalog.Disable(user.UniversityCode, ServiceCodes.Clubs);

        var (_, ran) = await Run(ServiceCodes.RideSharing, user);

        Assert.True(ran);
    }

    [Fact]
    public async Task An_anonymous_visitor_is_challenged_rather_than_redirected()
    {
        var (result, ran) = await Run(ServiceCodes.Clubs, signedInAs: null);

        Assert.False(ran);
        Assert.IsType<ChallengeResult>(result);
    }

    [Fact]
    public async Task Enablement_follows_the_users_own_university()
    {
        // Two tenants, one disabled service: the student from the university
        // that still has it switched on must be unaffected.
        _test.Db.AddUniversity(TestData.OtherUniversity);
        var blocked = _test.Db.AddUser("U2024001", TestData.DefaultUniversity);
        var allowed = _test.Db.AddUser("U2024002", TestData.OtherUniversity);
        _catalog.Disable(TestData.DefaultUniversity, ServiceCodes.Attendance);

        var (_, blockedRan) = await Run(ServiceCodes.Attendance, blocked);
        var (_, allowedRan) = await Run(ServiceCodes.Attendance, allowed);

        Assert.False(blockedRan);
        Assert.True(allowedRan);
    }
}
