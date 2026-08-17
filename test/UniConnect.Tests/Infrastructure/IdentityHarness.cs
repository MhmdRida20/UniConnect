using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Tests.Infrastructure;

/// <summary>
/// Builds a real UserManager/RoleManager over the test database.
///
/// Deliberately not mocked. UserManager is a concrete class with nine
/// constructor dependencies and a large virtual surface; faking it produces
/// setup code longer than the test and tends to encode assumptions about how
/// Identity behaves rather than testing against how it actually behaves.
/// Wiring the genuine article over SQLite is about twenty lines, done once
/// here, and gives real password hashing, real normalisation, and real
/// uniqueness enforcement.
/// </summary>
public static class IdentityHarness
{
    public static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext db)
    {
        var store = new UserStore<ApplicationUser>(db);

        var options = Options.Create(new IdentityOptions
        {
            // Mirrors Program.cs so tests reject exactly what production rejects.
            Password =
            {
                RequireDigit = true,
                RequiredLength = 6,
                RequireNonAlphanumeric = false,
                RequireUppercase = false,
                RequireLowercase = true
            },
            // Also mirrors Program.cs: email confirmation is a 6-digit code
            // from the Email provider, not a Data Protection token.
            Tokens =
            {
                EmailConfirmationTokenProvider = TokenOptions.DefaultEmailProvider
            }
        });

        var manager = new UserManager<ApplicationUser>(
            store,
            options,
            new PasswordHasher<ApplicationUser>(),
            new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
            new IPasswordValidator<ApplicationUser>[] { new PasswordValidator<ApplicationUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);

        // In the app these arrive via AddDefaultTokenProviders(), which runs at
        // DI registration time. Building UserManager by hand skips that
        // entirely, leaving _tokenProviders empty — so any call naming a
        // provider throws NotSupportedException rather than failing an
        // assertion. Registering the authenticator provider here is what makes
        // TOTP testable at all.
        manager.RegisterTokenProvider(
            TokenOptions.DefaultAuthenticatorProvider,
            new AuthenticatorTokenProvider<ApplicationUser>());

        // Needed for email confirmation now that it uses a typed 6-digit code:
        // GenerateEmailConfirmationTokenAsync resolves this provider by name.
        manager.RegisterTokenProvider(
            TokenOptions.DefaultEmailProvider,
            new EmailTokenProvider<ApplicationUser>());

        // The Data Protection provider, which everything NOT moved to codes
        // still uses — password reset above all. Registering it here is what
        // lets a test assert that reset tokens stayed long and opaque.
        manager.RegisterTokenProvider(
            TokenOptions.DefaultProvider,
            new DataProtectorTokenProvider<ApplicationUser>(
                DataProtectionProvider.Create("UniConnect.Tests"),
                Options.Create(new DataProtectionTokenProviderOptions()),
                NullLogger<DataProtectorTokenProvider<ApplicationUser>>.Instance));

        return manager;
    }

    /// <summary>
    /// A SignInManager whose password and two-factor outcomes are dictated by
    /// the test rather than by Identity.
    ///
    /// The real class cannot be exercised here: PasswordSignInAsync reaches
    /// IsTwoFactorClientRememberedAsync, which calls Context.AuthenticateAsync
    /// and needs a registered IAuthenticationService — an amount of plumbing
    /// far larger than the behaviour under test. Every method used below is
    /// virtual, so overriding them is enough, and it keeps the assertions on
    /// OUR branching rather than on Microsoft's.
    /// </summary>
    public sealed class StubSignInManager : SignInManager<ApplicationUser>
    {
        public SignInResult PasswordResult { get; set; } = SignInResult.Success;
        public SignInResult CheckPasswordResult { get; set; } = SignInResult.Success;
        public ApplicationUser? TwoFactorUser { get; set; }
        public bool SignedOut { get; private set; }

        public StubSignInManager(UserManager<ApplicationUser> users)
            : base(users,
                   new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
                   new UserClaimsPrincipalFactory<ApplicationUser>(
                       users, Microsoft.Extensions.Options.Options.Create(new IdentityOptions())),
                   Microsoft.Extensions.Options.Options.Create(new IdentityOptions()),
                   NullLogger<SignInManager<ApplicationUser>>.Instance,
                   new AuthenticationSchemeProvider(Microsoft.Extensions.Options.Options.Create(new AuthenticationOptions())),
                   new DefaultUserConfirmation<ApplicationUser>())
        {
        }

        public override Task<SignInResult> PasswordSignInAsync(
            string userName, string password, bool isPersistent, bool lockoutOnFailure)
            => Task.FromResult(PasswordResult);

        public override Task<SignInResult> CheckPasswordSignInAsync(
            ApplicationUser user, string password, bool lockoutOnFailure)
            => Task.FromResult(CheckPasswordResult);

        public override Task<ApplicationUser?> GetTwoFactorAuthenticationUserAsync()
            => Task.FromResult(TwoFactorUser);

        public override Task SignOutAsync()
        {
            SignedOut = true;
            return Task.CompletedTask;
        }
    }

    public static RoleManager<IdentityRole> CreateRoleManager(ApplicationDbContext db)
    {
        var store = new RoleStore<IdentityRole>(db);

        return new RoleManager<IdentityRole>(
            store,
            new IRoleValidator<IdentityRole>[] { new RoleValidator<IdentityRole>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole>>.Instance);
    }
}
