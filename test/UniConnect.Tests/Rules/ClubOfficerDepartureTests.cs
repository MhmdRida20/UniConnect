using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// The club "officer departure" edge case: what happens to a club when its
/// President walks away.
///
/// Three branches, chosen in order — hand over to the Vice President, else to
/// the longest-standing approved member, else archive the club. Nothing in the
/// UI shows which branch ran, and the situation is rare enough that a mistake
/// could sit unnoticed for a long time, which is exactly why it's worth
/// pinning down here.
/// </summary>
public class ClubOfficerDepartureTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly StubHubContext<ClubHub> _hub = new();
    private readonly Club _club;
    private readonly ApplicationUser _president;

    public ClubOfficerDepartureTests()
    {
        _test.Db.AddUniversity();
        _president = _test.Db.AddUser("U2024001", fullName: "Pat President");

        _club = new Club
        {
            UniversityCode = TestData.DefaultUniversity,
            CreatorId = _president.Id,
            ClubName = "Robotics Society",
            Category = ClubCategory.Technology
        };
        _test.Db.Clubs.Add(_club);
        _test.Db.SaveChanges();

        Join(_president, ClubRole.President, ClubMembershipStatus.Approved, joinedDaysAgo: 100);
    }

    public void Dispose() => _test.Dispose();

    private ClubMember Join(
        ApplicationUser user, ClubRole role, ClubMembershipStatus status, int joinedDaysAgo)
    {
        var member = new ClubMember
        {
            ClubId = _club.Id,
            UserId = user.Id,
            Role = role,
            Status = status,
            JoinedAt = DateTime.UtcNow.AddDays(-joinedDaysAgo)
        };
        _test.Db.ClubMembers.Add(member);
        _test.Db.SaveChanges();
        return member;
    }

    private ClubsController Controller(ApplicationUser user) =>
        new ClubsController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _hub,
                new StubWebHostEnvironment(),
                ServiceHarness.AuditLog(_test.Db),
                ServiceHarness.Notifications(_test.Db))
            .SignedInAs(user, "Student");

    private ClubMember MembershipOf(ApplicationUser user) =>
        _test.NewContext().ClubMembers.Single(m => m.ClubId == _club.Id && m.UserId == user.Id);

    // ---------- Branch 1: the Vice President inherits ----------

    [Fact]
    public async Task When_the_president_leaves_the_vice_president_takes_over()
    {
        var vp = _test.Db.AddUser("U2024002", fullName: "Vic VP");
        var member = _test.Db.AddUser("U2024003", fullName: "Older Member");

        // Deliberately joined AFTER the plain member, so seniority alone would
        // pick the wrong person — the VP must win on rank, not on tenure.
        Join(member, ClubRole.Member, ClubMembershipStatus.Approved, joinedDaysAgo: 90);
        Join(vp, ClubRole.VicePresident, ClubMembershipStatus.Approved, joinedDaysAgo: 10);

        await Controller(_president).Leave(_club.Id);

        Assert.Equal(ClubRole.President, MembershipOf(vp).Role);
        Assert.Equal(ClubRole.Member, MembershipOf(member).Role);
        Assert.Equal(vp.Id, _test.NewContext().Clubs.Single().CreatorId);
    }

    // ---------- Branch 2: longest-standing member inherits ----------

    [Fact]
    public async Task With_no_vice_president_the_longest_standing_member_takes_over()
    {
        var senior = _test.Db.AddUser("U2024002", fullName: "Senior");
        var recent = _test.Db.AddUser("U2024003", fullName: "Recent");

        Join(senior, ClubRole.Officer, ClubMembershipStatus.Approved, joinedDaysAgo: 80);
        Join(recent, ClubRole.Member, ClubMembershipStatus.Approved, joinedDaysAgo: 5);

        await Controller(_president).Leave(_club.Id);

        Assert.Equal(ClubRole.President, MembershipOf(senior).Role);
        Assert.Equal(ClubRole.Member, MembershipOf(recent).Role);
    }

    [Fact]
    public async Task Members_still_awaiting_approval_are_not_eligible_to_inherit()
    {
        // Someone who hasn't been let in yet must not end up running the club.
        var pending = _test.Db.AddUser("U2024002", fullName: "Pending");
        var approved = _test.Db.AddUser("U2024003", fullName: "Approved");

        Join(pending, ClubRole.Member, ClubMembershipStatus.Pending, joinedDaysAgo: 95);
        Join(approved, ClubRole.Member, ClubMembershipStatus.Approved, joinedDaysAgo: 5);

        await Controller(_president).Leave(_club.Id);

        Assert.Equal(ClubRole.President, MembershipOf(approved).Role);
        Assert.Equal(ClubStatus.Active, _test.NewContext().Clubs.Single().Status);
    }

    // ---------- Branch 3: nobody left ----------

    [Fact]
    public async Task A_club_with_nobody_left_is_archived()
    {
        await Controller(_president).Leave(_club.Id);

        var club = _test.NewContext().Clubs.Single();
        Assert.Equal(ClubStatus.Archived, club.Status);
        Assert.Empty(_test.NewContext().ClubMembers);
    }

    [Fact]
    public async Task A_club_whose_only_survivors_are_pending_is_archived()
    {
        // Pending members don't count as "remaining", so there is genuinely
        // nobody able to run the club.
        var pending = _test.Db.AddUser("U2024002");
        Join(pending, ClubRole.Member, ClubMembershipStatus.Pending, joinedDaysAgo: 1);

        await Controller(_president).Leave(_club.Id);

        Assert.Equal(ClubStatus.Archived, _test.NewContext().Clubs.Single().Status);
    }

    // ---------- Ordinary departures ----------

    [Fact]
    public async Task An_ordinary_member_leaving_changes_no_roles()
    {
        var member = _test.Db.AddUser("U2024002");
        Join(member, ClubRole.Member, ClubMembershipStatus.Approved, joinedDaysAgo: 5);

        await Controller(member).Leave(_club.Id);

        Assert.Equal(ClubRole.President, MembershipOf(_president).Role);
        Assert.Equal(ClubStatus.Active, _test.NewContext().Clubs.Single().Status);
        Assert.Equal(1, _test.NewContext().ClubMembers.Count());
    }

    [Fact]
    public async Task Withdrawing_a_pending_request_never_triggers_succession()
    {
        // A withdrawal isn't a departure — the club's leadership must be left
        // completely alone, including when the club would otherwise archive.
        var applicant = _test.Db.AddUser("U2024002");
        Join(applicant, ClubRole.Member, ClubMembershipStatus.Pending, joinedDaysAgo: 1);

        var controller = Controller(applicant);
        await controller.Leave(_club.Id);

        Assert.Equal("Your request was withdrawn.", controller.TempData["Success"]);
        Assert.Equal(ClubStatus.Active, _test.NewContext().Clubs.Single().Status);
        Assert.Equal(ClubRole.President, MembershipOf(_president).Role);
    }

    [Fact]
    public async Task Leaving_a_club_you_are_not_in_is_a_no_op()
    {
        var stranger = _test.Db.AddUser("U2024002");

        var result = await Controller(stranger).Leave(_club.Id);

        result.ShouldRedirectToAction(nameof(ClubsController.Index));
        Assert.Equal(1, _test.NewContext().ClubMembers.Count());
    }

    [Fact]
    public async Task Leaving_tells_the_live_club_page_to_refresh()
    {
        var member = _test.Db.AddUser("U2024002");
        Join(member, ClubRole.Member, ClubMembershipStatus.Approved, joinedDaysAgo: 5);

        await Controller(member).Leave(_club.Id);

        Assert.True(_hub.SentTo($"club-{_club.Id}", "ClubUpdated"));
        Assert.True(_hub.SentTo("clubs-lobby", "ClubListChanged"));
    }

    // ---------- Membership uniqueness ----------

    [Fact]
    public async Task The_database_refuses_a_second_membership_row_for_the_same_person()
    {
        using var context = _test.NewContext();
        context.ClubMembers.Add(new ClubMember { ClubId = _club.Id, UserId = _president.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    /// <summary>ClubsController takes IWebHostEnvironment for logo uploads; nothing under test writes a file.</summary>
    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "UniConnect.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Test";
    }
}
