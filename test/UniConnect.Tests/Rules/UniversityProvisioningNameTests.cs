using System.Reflection;
using UniConnect.Controllers;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Names generated when a university is provisioned must fit the columns they
/// are stored in.
///
/// This is a regression test for a live 500. University.Name allows 150
/// characters but ApplicationUser.FullName allows 50, and provisioning built
/// the career-services account as "{name} — Career Services". Any university
/// named longer than 31 characters therefore threw SqlException from inside
/// UserManager.CreateAsync — and because the University row is saved before the
/// accounts are, the failed attempt left an institution that existed, had no
/// logins, and blocked its own code from being reused. "University of Science
/// and Art in Lebanon" is 40 characters, so it failed twice before anyone
/// noticed the pattern.
///
/// The two helpers are private, and deliberately tested through reflection
/// rather than being widened to internal: the point is to pin the arithmetic,
/// not to add a seam to the controller's public surface.
/// </summary>
public class UniversityProvisioningNameTests
{
    private const int FullNameMax = 50;       // ApplicationUser.FullName
    private const int UniversityIdMax = 20;   // ApplicationUser.UniversityId

    private static string ScopedAccountName(string name, string code, string suffix, int max = FullNameMax) =>
        (string)typeof(AdminUniversitiesController)
            .GetMethod("ScopedAccountName", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { name, code, suffix, max })!;

    private static string ScopedUniversityId(string prefix, string code, int max = UniversityIdMax) =>
        (string)typeof(AdminUniversitiesController)
            .GetMethod("ScopedUniversityId", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { prefix, code, max })!;

    // ---- the name that actually broke it ------------------------------------

    [Fact]
    public void The_university_name_that_caused_the_outage_now_fits()
    {
        const string name = "University of Science and Art in Lebanon";
        Assert.True(name.Length > 31, "precondition: this name must be long enough to have triggered the bug");

        var career = ScopedAccountName(name, "USAL", "Career Services");
        var admin = ScopedAccountName(name, "USAL", "Admin");

        Assert.True(career.Length <= FullNameMax, $"career name was {career.Length} chars: {career}");
        Assert.True(admin.Length <= FullNameMax, $"admin name was {admin.Length} chars: {admin}");
    }

    [Fact]
    public void A_long_name_falls_back_to_the_code_rather_than_being_chopped_mid_word()
    {
        var result = ScopedAccountName("University of Science and Art in Lebanon", "USAL", "Career Services");

        // "USAL — Career Services" is a label; a truncated name is a glitch.
        Assert.Equal("USAL — Career Services", result);
    }

    [Fact]
    public void A_short_name_keeps_the_full_university_name()
    {
        // The fallback must not fire when it is not needed — most universities
        // should still read as their own name.
        var result = ScopedAccountName("UniConnect Demo", "DEFAULT", "Admin");

        Assert.Equal("UniConnect Demo — Admin", result);
    }

    [Theory]
    [InlineData("Career Services")]
    [InlineData("Admin")]
    public void No_university_name_of_any_allowed_length_can_overflow(string suffix)
    {
        // University.Name permits 150 characters, so the boundary worth testing
        // is the whole allowed range, not one example.
        foreach (var length in new[] { 1, 10, 30, 31, 32, 40, 60, 100, 149, 150 })
        {
            var name = new string('X', length);
            var result = ScopedAccountName(name, "ABCDEFGHIJKLMNOPQRST", suffix);

            Assert.True(result.Length <= FullNameMax,
                $"name length {length} produced {result.Length} chars for '{suffix}'");
        }
    }

    // ---- the identifiers, which had the same class of bug -------------------

    [Fact]
    public void A_maximum_length_code_cannot_overflow_the_university_id()
    {
        // University.Code allows 20 and UniversityId allows 20, so any prefix
        // at all can overflow. This never fired in practice only because the
        // codes used so far were short.
        var maxCode = new string('C', 20);

        Assert.True(ScopedUniversityId("CAREER-", maxCode).Length <= UniversityIdMax);
        Assert.True(ScopedUniversityId("UNIADMIN-", maxCode).Length <= UniversityIdMax);
    }

    [Fact]
    public void A_normal_code_keeps_its_readable_identifier()
    {
        Assert.Equal("CAREER-USAL", ScopedUniversityId("CAREER-", "USAL"));
        Assert.Equal("UNIADMIN-USAL", ScopedUniversityId("UNIADMIN-", "USAL"));
    }

    [Fact]
    public void The_two_provisioned_accounts_never_collide_on_university_id()
    {
        // Truncation must not make the career and admin identifiers equal, or
        // the second CreateAsync would fail on a unique index instead.
        var maxCode = new string('C', 20);

        Assert.NotEqual(
            ScopedUniversityId("CAREER-", maxCode),
            ScopedUniversityId("UNIADMIN-", maxCode));
    }
}
