using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using UniConnect.Controllers.Api;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;
// SignInResult exists in both Identity and MVC; the MVC one arrives via
// the Microsoft.AspNetCore.Mvc using above. Identity's is the one meant here.
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

namespace UniConnect.Tests.Rules;

/// <summary>
/// The mobile half of the two-factor decision.
///
/// The API signs users in with CheckPasswordSignInAsync, which validates the
/// password and applies lockout but ignores TwoFactorEnabled completely. Left
/// alone, that means a student could turn two-factor on in the web portal and
/// still be admitted here by password alone — the second factor would be
/// decorative, and the claim that the system enforces it would be false.
///
/// The app has no code-entry screen yet, so the honest answer is to refuse the
/// login and say why. These tests pin that refusal in place: it is the kind of
/// gap that reappears silently the next time this method is edited, because
/// nothing else about the request looks wrong.
/// </summary>
public class MobileTwoFactorRefusalTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly UserManager<ApplicationUser> _users;

    public MobileTwoFactorRefusalTests()
    {
        _test.Db.AddUniversity();
        _users = IdentityHarness.CreateUserManager(_test.Db);
    }

    public void Dispose()
    {
        _users.Dispose();
        _test.Dispose();
    }

    private AuthApiController BuildController(IdentityHarness.StubSignInManager signIn)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-long-enough-for-hmac-sha256-aaaaaaaa"
            })
            .Build();

        return new AuthApiController(
            _test.Db,
            _users,
            signIn,
            new NoOpEmailSender(),
            new JwtTokenService(config),
            ServiceHarness.AuditLog(_test.Db));
    }

    private sealed class NoOpEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) => Task.CompletedTask;
    }

    [Fact]
    public async Task An_account_with_two_factor_enabled_is_refused_with_403()
    {
        var user = _test.Db.AddUser("S2001");
        user.TwoFactorEnabled = true;
        _test.Db.SaveChanges();

        var controller = BuildController(new IdentityHarness.StubSignInManager(_users)
        {
            // The password is correct — that is the whole point. The refusal
            // must come from the 2FA flag, not from a failed credential check.
            CheckPasswordResult = SignInResult.Success
        });

        var result = await controller.Login(new AuthApiController.LoginRequest
        {
            Email = user.Email!,
            Password = "Passw0rd"
        });

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        Assert.Contains("two_factor_required", System.Text.Json.JsonSerializer.Serialize(status.Value));
    }

    [Fact]
    public async Task The_refusal_is_written_to_the_audit_log()
    {
        var user = _test.Db.AddUser("S2002");
        user.TwoFactorEnabled = true;
        _test.Db.SaveChanges();

        var controller = BuildController(new IdentityHarness.StubSignInManager(_users)
        {
            CheckPasswordResult = SignInResult.Success
        });

        await controller.Login(new AuthApiController.LoginRequest
        {
            Email = user.Email!,
            Password = "Passw0rd"
        });

        var audit = _test.NewContext().AuditLogs.Single();
        Assert.Equal("FailedLogin", audit.Action);
        Assert.Contains("two-factor", audit.Details!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task No_token_is_issued_to_a_two_factor_account()
    {
        var user = _test.Db.AddUser("S2003");
        user.TwoFactorEnabled = true;
        _test.Db.SaveChanges();

        var controller = BuildController(new IdentityHarness.StubSignInManager(_users)
        {
            CheckPasswordResult = SignInResult.Success
        });

        var result = await controller.Login(new AuthApiController.LoginRequest
        {
            Email = user.Email!,
            Password = "Passw0rd"
        });

        // Belt and braces: a 403 that still handed back a bearer token would
        // defeat the entire point of the refusal.
        var payload = System.Text.Json.JsonSerializer.Serialize(
            Assert.IsType<ObjectResult>(result).Value);

        Assert.DoesNotContain("token", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_ordinary_account_without_two_factor_is_unaffected()
    {
        // The regression guard. The refusal must be narrow: everyone else has
        // to keep logging in exactly as before.
        var user = _test.Db.AddUser("S2004");
        _test.Db.SaveChanges();

        // AddToRoleAsync requires the role row to exist; nothing seeds roles
        // into a bare test database.
        using var roles = IdentityHarness.CreateRoleManager(_test.Db);
        await roles.CreateAsync(new IdentityRole("Student"));
        await _users.AddToRoleAsync(user, "Student");

        var controller = BuildController(new IdentityHarness.StubSignInManager(_users)
        {
            CheckPasswordResult = SignInResult.Success
        });

        var result = await controller.Login(new AuthApiController.LoginRequest
        {
            Email = user.Email!,
            Password = "Passw0rd"
        });

        Assert.IsType<OkObjectResult>(result);
    }
}
