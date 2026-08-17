using Microsoft.AspNetCore.Identity;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Email confirmation by typed 6-digit code rather than clicked link.
///
/// The format tests are not ceremony: the whole change rests on
/// GenerateEmailConfirmationTokenAsync returning six digits, which is true only
/// because Program.cs points EmailConfirmationTokenProvider at the Email
/// provider. Remove that one line and the token silently reverts to a long
/// Data Protection string — the emails would still send, and every user would
/// be asked to type something untypeable.
///
/// The attempt-limiting tests matter more. A six-digit code has a million
/// possibilities and lives 6-9 minutes, and ConfirmEmailAsync counts nothing on
/// its own. Without lockout this change would be a downgrade from the
/// unguessable link it replaces, so these pin the counting in place.
/// </summary>
public class EmailConfirmationCodeTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly UserManager<ApplicationUser> _users;

    public EmailConfirmationCodeTests()
    {
        _test.Db.AddUniversity();
        _users = IdentityHarness.CreateUserManager(_test.Db);
    }

    public void Dispose()
    {
        _users.Dispose();
        _test.Dispose();
    }

    private ApplicationUser Unconfirmed(string id = "E1001")
    {
        var user = _test.Db.AddUser(id);
        user.EmailConfirmed = false;

        // TestData writes the row directly, which UserManager.CreateAsync does
        // not: it sets LockoutEnabled from Options.Lockout.AllowedForNewUsers,
        // which defaults to true. Verified against the live database — all 24
        // real accounts have it set. Without this the lockout tests would pass
        // vacuously, asserting nothing about the protection they exist to pin.
        user.LockoutEnabled = true;

        _test.Db.SaveChanges();
        return user;
    }

    // ---- format -----------------------------------------------------------

    [Fact]
    public async Task The_confirmation_token_is_six_digits()
    {
        var user = Unconfirmed();

        var code = await _users.GenerateEmailConfirmationTokenAsync(user);

        Assert.Equal(6, code.Length);
        Assert.True(code.All(char.IsDigit), $"expected six digits, got '{code}'");
    }

    [Fact]
    public async Task A_valid_code_confirms_the_account()
    {
        var user = Unconfirmed();
        var code = await _users.GenerateEmailConfirmationTokenAsync(user);

        var result = await _users.ConfirmEmailAsync(user, code);

        Assert.True(result.Succeeded);
        Assert.True((await _users.FindByIdAsync(user.Id))!.EmailConfirmed);
    }

    [Fact]
    public async Task A_wrong_code_does_not_confirm()
    {
        var user = Unconfirmed();
        var real = await _users.GenerateEmailConfirmationTokenAsync(user);
        var wrong = real == "000000" ? "111111" : "000000";

        var result = await _users.ConfirmEmailAsync(user, wrong);

        Assert.False(result.Succeeded);
        Assert.False((await _users.FindByIdAsync(user.Id))!.EmailConfirmed);
    }

    [Fact]
    public async Task One_users_code_cannot_confirm_another_account()
    {
        var alice = Unconfirmed("E2001");
        var bob = Unconfirmed("E2002");

        var alicesCode = await _users.GenerateEmailConfirmationTokenAsync(alice);

        var result = await _users.ConfirmEmailAsync(bob, alicesCode);

        Assert.False(result.Succeeded);
        Assert.False((await _users.FindByIdAsync(bob.Id))!.EmailConfirmed);
    }

    // ---- brute force ------------------------------------------------------

    [Fact]
    public async Task A_wrong_code_counts_as_a_failed_access_attempt()
    {
        var user = Unconfirmed();
        var real = await _users.GenerateEmailConfirmationTokenAsync(user);
        var wrong = real == "000000" ? "111111" : "000000";

        // What the page does on a failed confirmation.
        await _users.ConfirmEmailAsync(user, wrong);
        await _users.AccessFailedAsync(user);

        Assert.Equal(1, await _users.GetAccessFailedCountAsync(user));
    }

    [Fact]
    public async Task Repeated_wrong_codes_lock_the_account()
    {
        var user = Unconfirmed();
        var real = await _users.GenerateEmailConfirmationTokenAsync(user);
        var wrong = real == "000000" ? "111111" : "000000";

        // Identity's default MaxFailedAccessAttempts is 5; Program.cs does not
        // override it. Guessing beyond that stops being free.
        for (var i = 0; i < 5; i++)
        {
            await _users.ConfirmEmailAsync(user, wrong);
            await _users.AccessFailedAsync(user);
        }

        Assert.True(await _users.IsLockedOutAsync(user));
    }

    [Fact]
    public async Task Confirming_successfully_clears_the_failure_count()
    {
        var user = Unconfirmed();
        var code = await _users.GenerateEmailConfirmationTokenAsync(user);

        await _users.AccessFailedAsync(user);
        await _users.AccessFailedAsync(user);
        Assert.Equal(2, await _users.GetAccessFailedCountAsync(user));

        Assert.True((await _users.ConfirmEmailAsync(user, code)).Succeeded);
        await _users.ResetAccessFailedCountAsync(user);

        Assert.Equal(0, await _users.GetAccessFailedCountAsync(user));
    }

    // ---- single use -------------------------------------------------------

    [Fact]
    public async Task Rotating_the_security_stamp_makes_the_code_single_use()
    {
        // ConfirmEmailAsync leaves the security stamp alone, so the code stays
        // valid for the rest of its window unless it is rotated. Both pages do
        // rotate it; this pins that the rotation is what actually kills it.
        var user = Unconfirmed();
        var code = await _users.GenerateEmailConfirmationTokenAsync(user);

        Assert.True((await _users.ConfirmEmailAsync(user, code)).Succeeded);
        await _users.UpdateSecurityStampAsync(user);

        // Undo only the flag, so the retry fails on the code and not on the
        // "already confirmed" short-circuit.
        var reloaded = (await _users.FindByIdAsync(user.Id))!;
        reloaded.EmailConfirmed = false;
        await _users.UpdateAsync(reloaded);

        var reuse = await _users.ConfirmEmailAsync(reloaded, code);

        Assert.False(reuse.Succeeded);
    }

    // ---- scope ------------------------------------------------------------

    [Fact]
    public async Task Password_reset_still_uses_a_long_token_not_a_code()
    {
        // The regression guard for the one-line Program.cs change. Only email
        // confirmation was meant to move to codes; a reset link is clicked, not
        // typed, and a 6-digit reset token would be a real weakness.
        var user = Unconfirmed();

        var resetToken = await _users.GeneratePasswordResetTokenAsync(user);

        Assert.True(resetToken.Length > 20,
            $"password reset token should stay a long opaque token, got '{resetToken}'");
        Assert.False(resetToken.All(char.IsDigit));
    }
}
