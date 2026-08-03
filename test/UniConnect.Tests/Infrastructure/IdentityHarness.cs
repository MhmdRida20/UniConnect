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
            }
        });

        return new UserManager<ApplicationUser>(
            store,
            options,
            new PasswordHasher<ApplicationUser>(),
            new IUserValidator<ApplicationUser>[] { new UserValidator<ApplicationUser>() },
            new IPasswordValidator<ApplicationUser>[] { new PasswordValidator<ApplicationUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<UserManager<ApplicationUser>>.Instance);
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
