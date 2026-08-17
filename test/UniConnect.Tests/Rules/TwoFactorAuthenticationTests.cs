using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UniConnect.Areas.Identity.Pages.Account;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;
// SignInResult exists in both Identity and MVC; the MVC one arrives via
// the Microsoft.AspNetCore.Mvc using above. Identity's is the one meant here.
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Two-factor authentication, web portal only.
///
/// The first test here is the one that matters most. PasswordSignInAsync has a
/// fourth outcome — RequiresTwoFactor — which the login page did not handle;
/// it fell through to "Invalid login attempt", so the first user ever to enable
/// 2FA could never log in again, and the audit log recorded a false failed
/// login for someone who had done nothing wrong. That branch is unreachable
/// until a user actually has 2FA on, which is exactly why it survived unnoticed
/// and why it needs a test rather than an eyeball.
///
/// The remaining tests cover the pieces a student's account recovery depends
/// on: that codes verify, that wrong codes do not, that a recovery code cannot
/// be spent twice, and that neither path lets a suspended account back in.
/// </summary>
public class TwoFactorAuthenticationTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly UserManager<ApplicationUser> _users;

    public TwoFactorAuthenticationTests()
    {
        _test.Db.AddUniversity();
        _users = IdentityHarness.CreateUserManager(_test.Db);
    }

    public void Dispose()
    {
        _users.Dispose();
        _test.Dispose();
    }

    // ---- helpers -----------------------------------------------------------

    private ApplicationUser SeedUser(bool suspended = false) =>
        _test.Db.AddUser("S1001", suspended: suspended);

    /// <summary>
    /// Enrols the user the way EnableAuthenticator does: generate a key, store
    /// it, then switch the flag on. Both halves are required — TwoFactorEnabled
    /// alone does not make Identity ask for a second factor, because
    /// IsTwoFactorEnabledAsync also needs a provider that can generate a token.
    /// </summary>
    private async Task<string> EnrolAsync(ApplicationUser user)
    {
        await _users.ResetAuthenticatorKeyAsync(user);
        await _users.SetTwoFactorEnabledAsync(user, true);
        return (await _users.GetAuthenticatorKeyAsync(user))!;
    }

    private LoginModel BuildLoginPage(IdentityHarness.StubSignInManager signIn)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        return new LoginModel(
            signIn,
            _users,
            NullLogger<LoginModel>.Instance,
            ServiceHarness.AuditLog(_test.Db),
            new ConfigurationBuilder().Build())
        {
            Input = new LoginModel.InputModel
            {
                Email = "s1001@uni.edu",
                Password = "Passw0rd",
                RememberMe = false
            },
            PageContext = new PageContext { HttpContext = http },
            Url = UrlFor(http)
        };
    }


    /// <summary>
    /// PageModel.Url is populated by the framework, not by the constructor, and
    /// every OnPost here opens with Url.Content("~/"). Without this the pages
    /// throw NullReferenceException before reaching the behaviour under test.
    /// </summary>
    private static IUrlHelper UrlFor(Microsoft.AspNetCore.Http.HttpContext http) =>
        new UrlHelper(new Microsoft.AspNetCore.Mvc.ActionContext(
            http, new RouteData(), new ActionDescriptor()));

    // ---- the regression test ----------------------------------------------

    [Fact]
    public async Task Login_page_sends_a_two_factor_user_to_the_challenge_not_to_an_error()
    {
        SeedUser();
        var signIn = new IdentityHarness.StubSignInManager(_users)
        {
            PasswordResult = SignInResult.TwoFactorRequired
        };

        var page = BuildLoginPage(signIn);

        var result = await page.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./LoginWith2fa", redirect.PageName);
        Assert.True(page.ModelState.IsValid);
    }

    [Fact]
    public async Task A_two_factor_challenge_is_not_recorded_as_a_failed_login()
    {
        SeedUser();
        var signIn = new IdentityHarness.StubSignInManager(_users)
        {
            PasswordResult = SignInResult.TwoFactorRequired
        };

        await BuildLoginPage(signIn).OnPostAsync();

        // The branch returns above the audit call precisely so this stays empty.
        Assert.Empty(_test.NewContext().AuditLogs.Where(a => a.Action == "FailedLogin"));
    }

    [Fact]
    public async Task A_genuinely_wrong_password_is_still_recorded_as_a_failed_login()
    {
        SeedUser();
        var signIn = new IdentityHarness.StubSignInManager(_users)
        {
            PasswordResult = SignInResult.Failed
        };

        var page = BuildLoginPage(signIn);
        var result = await page.OnPostAsync();

        // Guards the fix against over-reach: the new branch must not swallow
        // ordinary failures on its way past.
        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.Single(_test.NewContext().AuditLogs.Where(a => a.Action == "FailedLogin"));
    }

    // ---- the token itself --------------------------------------------------

    [Fact]
    public async Task A_code_from_the_enrolled_key_is_accepted()
    {
        var user = SeedUser();
        var key = await EnrolAsync(user);

        // Computed independently of Identity, from the shared key and the clock
        // alone — the same inputs a phone has.
        var code = TotpCalculator.Generate(key);

        Assert.True(await _users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task A_code_from_an_adjacent_time_window_is_still_accepted(int stepOffset)
    {
        // Phone clocks drift and students type slowly. Identity allows two
        // 30-second steps either side; this pins that tolerance so a code read
        // at the very end of its window is not rejected during the demo.
        var user = SeedUser();
        var key = await EnrolAsync(user);

        var code = TotpCalculator.Generate(key, stepOffset);

        Assert.True(await _users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code));
    }

    [Fact]
    public async Task A_code_from_far_outside_the_window_is_rejected()
    {
        // The other edge of the same rule: tolerance is not unlimited, or a
        // code screenshotted an hour ago would still work.
        var user = SeedUser();
        var key = await EnrolAsync(user);

        var code = TotpCalculator.Generate(key, stepOffset: 20);

        Assert.False(await _users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code));
    }

    [Fact]
    public async Task A_wrong_code_is_rejected()
    {
        var user = SeedUser();
        await EnrolAsync(user);

        Assert.False(await _users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, "000000"));
    }

    [Fact]
    public async Task A_code_from_a_different_key_is_rejected()
    {
        var user = SeedUser();
        var code = TotpCalculator.Generate(await EnrolAsync(user));

        // Resetting the key is what "I changed phone" does. Codes minted from
        // the old secret must stop working immediately.
        await _users.ResetAuthenticatorKeyAsync(user);

        Assert.False(await _users.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, code));
    }

    // ---- recovery codes ----------------------------------------------------

    [Fact]
    public async Task A_recovery_code_cannot_be_used_twice()
    {
        var user = SeedUser();
        await EnrolAsync(user);

        var codes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        Assert.Equal(10, codes.Length);

        var first = await _users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0]);
        Assert.True(first.Succeeded);
        Assert.Equal(9, await _users.CountRecoveryCodesAsync(user));

        // Spending it again is the attack this guards against — a code read off
        // a shoulder or a screenshot must be worthless once used.
        var second = await _users.RedeemTwoFactorRecoveryCodeAsync(user, codes[0]);
        Assert.False(second.Succeeded);
        Assert.Equal(9, await _users.CountRecoveryCodesAsync(user));
    }

    [Fact]
    public async Task Regenerating_recovery_codes_invalidates_the_previous_set()
    {
        var user = SeedUser();
        await EnrolAsync(user);

        var oldCodes = (await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10))!.ToArray();
        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

        var result = await _users.RedeemTwoFactorRecoveryCodeAsync(user, oldCodes[0]);

        Assert.False(result.Succeeded);
    }

    // ---- suspension --------------------------------------------------------

    [Fact]
    public async Task A_suspended_user_cannot_complete_the_two_factor_challenge()
    {
        var user = SeedUser(suspended: true);
        await EnrolAsync(user);

        var signIn = new IdentityHarness.StubSignInManager(_users) { TwoFactorUser = user };
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        var page = new LoginWith2faModel(
            signIn, _users, NullLogger<LoginWith2faModel>.Instance,
            ServiceHarness.AuditLog(_test.Db))
        {
            Input = new LoginWith2faModel.InputModel { TwoFactorCode = "123456" },
            PageContext = new PageContext { HttpContext = http },
            Url = UrlFor(http)
        };

        var result = await page.OnPostAsync(rememberMe: false);

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.True(signIn.SignedOut);

        var audit = _test.NewContext().AuditLogs.Single(a => a.Action == "FailedLogin");
        Assert.Contains("suspended", audit.Details!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_suspended_user_cannot_complete_the_recovery_code_challenge()
    {
        var user = SeedUser(suspended: true);
        await EnrolAsync(user);

        var signIn = new IdentityHarness.StubSignInManager(_users) { TwoFactorUser = user };
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        var page = new LoginWithRecoveryCodeModel(
            signIn, _users, NullLogger<LoginWithRecoveryCodeModel>.Instance,
            ServiceHarness.AuditLog(_test.Db))
        {
            Input = new LoginWithRecoveryCodeModel.InputModel { RecoveryCode = "abcd-efgh" },
            PageContext = new PageContext { HttpContext = http },
            Url = UrlFor(http)
        };

        var result = await page.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.False(page.ModelState.IsValid);
        Assert.True(signIn.SignedOut);
    }

    // ---- an expired or bookmarked challenge --------------------------------

    [Fact]
    public async Task A_challenge_with_no_pending_user_returns_to_login_instead_of_throwing()
    {
        // The stock scaffolded page throws InvalidOperationException here,
        // rendering a 500 for the entirely ordinary case of a bookmarked URL.
        var signIn = new IdentityHarness.StubSignInManager(_users) { TwoFactorUser = null };

        var page = new LoginWith2faModel(
            signIn, _users, NullLogger<LoginWith2faModel>.Instance,
            ServiceHarness.AuditLog(_test.Db))
        {
            PageContext = new PageContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() }
        };

        var result = await page.OnGetAsync(rememberMe: false);

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./Login", redirect.PageName);
    }
}
