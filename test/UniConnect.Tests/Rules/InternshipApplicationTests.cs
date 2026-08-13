using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using UniConnect.Controllers;
using UniConnect.Models;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// FR-42 — applying for an internship, and every reason an application is
/// turned away.
///
/// The cross-university cases are the ones that matter most: the browse list is
/// already filtered, so the only way to reach another university's posting is a
/// direct URL or a hand-rolled POST, which is exactly what an automated test is
/// good at and manual clicking never covers.
/// </summary>
public class InternshipApplicationTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly ApplicationUser _student;
    private readonly Company _homeCompany;

    public InternshipApplicationTests()
    {
        _test.Db.AddUniversity(TestData.DefaultUniversity);
        _test.Db.AddUniversity(TestData.OtherUniversity);

        _student = _test.Db.AddUser("U2024001");
        _homeCompany = _test.Db.AddCompany(_test.Db.AddUser("CAREERS-HOME"));

        _provider.WithStudent(_student.UniversityId, major: "Computer Science");
    }

    public void Dispose() => _test.Dispose();

    /// <summary>
    /// The rules moved into InternshipService when the mobile app needed them
    /// too; the controller now only chooses views and messages. These tests
    /// still drive it through the controller, because what they are pinning is
    /// the behaviour a student sees, not the shape of the internals.
    /// </summary>
    private InternshipService Service() =>
        new(_test.Db,
            new MatchingScoreService(_test.Db, _provider, NullLogger<MatchingScoreService>.Instance),
            ServiceHarness.Notifications(_test.Db),
            ServiceHarness.AuditLog(_test.Db),
            _provider,
            NullLogger<InternshipService>.Instance);

    private InternshipsController Controller(ApplicationUser? asUser = null)
    {
        var user = asUser ?? _student;
        return new InternshipsController(Service(), IdentityHarness.CreateUserManager(_test.Db))
            .SignedInAs(user, "Student");
    }

    private Company ForeignCompany()
    {
        var owner = _test.Db.AddUser("CAREERS-OTHER", TestData.OtherUniversity);
        return _test.Db.AddCompany(owner, TestData.OtherUniversity, "Other Careers");
    }

    // ---------- Cross-university isolation ----------

    [Fact]
    public async Task A_student_cannot_open_another_universitys_posting_by_direct_url()
    {
        var foreign = _test.Db.AddInternship(ForeignCompany(), title: "Not Yours");

        var result = await Controller().Details(foreign.Id);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task A_student_cannot_apply_to_another_universitys_posting_by_direct_post()
    {
        // The Details guard alone isn't enough — Apply is reachable without
        // ever loading the page.
        var foreign = _test.Db.AddInternship(ForeignCompany());

        var result = await Controller().Apply(foreign.Id, "hello");

        Assert.IsType<NotFoundResult>(result);
        Assert.Empty(_test.NewContext().InternshipApplications);
    }

    [Fact]
    public async Task The_browse_list_only_shows_this_universitys_postings()
    {
        var mine = _test.Db.AddInternship(_homeCompany, title: "Mine");
        _test.Db.AddInternship(ForeignCompany(), title: "Theirs");

        var controller = Controller();
        await controller.Index(null, null, null, null);

        var scored = (List<(Internship Internship, int Score, bool CourseDataAvailable)>)controller.ViewBag.Scored;

        Assert.Equal(new[] { mine.Id }, scored.Select(s => s.Internship.Id));
    }

    // ---------- Applying ----------

    [Fact]
    public async Task A_valid_application_is_stored_with_its_matching_score()
    {
        var internship = _test.Db.AddInternship(_homeCompany, requiredSkills: "C#");
        _test.Db.AddSkills(_student, "C#");

        var result = await Controller().Apply(internship.Id, "  I'd love to join.  ");

        result.ShouldRedirectToAction(nameof(InternshipsController.MyApplications));

        var application = await _test.NewContext().InternshipApplications.SingleAsync();
        Assert.Equal(InternshipApplicationStatus.Submitted, application.Status);
        Assert.Equal("I'd love to join.", application.CoverMessage);   // trimmed
        Assert.NotNull(application.MatchingScore);
    }

    [Fact]
    public async Task An_empty_cover_message_is_stored_as_null_rather_than_blank()
    {
        var internship = _test.Db.AddInternship(_homeCompany);

        await Controller().Apply(internship.Id, "   ");

        Assert.Null((await _test.NewContext().InternshipApplications.SingleAsync()).CoverMessage);
    }

    [Fact]
    public async Task Applying_notifies_the_career_services_account()
    {
        // FR-44 — the posting side has to learn about it.
        var internship = _test.Db.AddInternship(_homeCompany);

        await Controller().Apply(internship.Id, null);

        var notification = await _test.NewContext().Notifications
            .SingleAsync(n => n.UserId == _homeCompany.UserId);
        Assert.Contains("applied", notification.Message);
    }

    [Fact]
    public async Task Applying_is_audited()
    {
        var internship = _test.Db.AddInternship(_homeCompany);

        await Controller().Apply(internship.Id, null);

        Assert.True(await _test.NewContext().AuditLogs
            .AnyAsync(a => a.Action == "InternshipApplicationSubmitted" && a.UserId == _student.Id));
    }

    // ---------- Rejections ----------

    [Fact]
    public async Task Applying_twice_is_refused()
    {
        // Edge case "duplicate application", backed by a unique index on
        // (InternshipId, UserId).
        var internship = _test.Db.AddInternship(_homeCompany);
        var controller = Controller();

        await controller.Apply(internship.Id, null);
        await controller.Apply(internship.Id, null);

        Assert.Equal("You've already applied to this internship.", controller.TempData["Error"]);
        Assert.Equal(1, await _test.NewContext().InternshipApplications.CountAsync());
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_even_if_the_check_is_bypassed()
    {
        // Proves the unique index is really there, rather than the C# check
        // being the only thing standing between a student and two applications.
        var internship = _test.Db.AddInternship(_homeCompany);

        _test.Db.InternshipApplications.Add(new InternshipApplication
        {
            InternshipId = internship.Id, UserId = _student.Id
        });
        _test.Db.SaveChanges();

        using var second = _test.NewContext();
        second.InternshipApplications.Add(new InternshipApplication
        {
            InternshipId = internship.Id, UserId = _student.Id
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task A_listing_only_posting_refuses_in_app_applications()
    {
        // These send students to the employer's own link; the form is hidden,
        // but a direct POST must be refused too.
        var internship = _test.Db.AddInternship(_homeCompany, mode: InternshipPostingMode.ListingOnly);
        var controller = Controller();

        await controller.Apply(internship.Id, null);

        Assert.Contains("employer's own link", (string)controller.TempData["Error"]!);
        Assert.Empty(_test.NewContext().InternshipApplications);
    }

    [Fact]
    public async Task A_deactivated_posting_refuses_applications()
    {
        var internship = _test.Db.AddInternship(_homeCompany, active: false);
        var controller = Controller();

        await controller.Apply(internship.Id, null);

        Assert.Equal("This internship is no longer accepting applications.", controller.TempData["Error"]);
        Assert.Empty(_test.NewContext().InternshipApplications);
    }

    [Fact]
    public async Task Applying_after_the_deadline_is_refused()
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        internship.ApplicationDeadline = DateTime.Today.AddDays(-1);
        _test.Db.SaveChanges();

        var controller = Controller();
        await controller.Apply(internship.Id, null);

        Assert.Contains("deadline", (string)controller.TempData["Error"]!);
        Assert.Empty(_test.NewContext().InternshipApplications);
    }

    [Fact]
    public async Task Applying_on_the_deadline_day_itself_is_still_allowed()
    {
        // The check is "< Today", so the final day must remain open — an
        // off-by-one here silently costs students a day.
        var internship = _test.Db.AddInternship(_homeCompany);
        internship.ApplicationDeadline = DateTime.Today;
        _test.Db.SaveChanges();

        await Controller().Apply(internship.Id, null);

        Assert.Equal(1, await _test.NewContext().InternshipApplications.CountAsync());
    }

    [Fact]
    public async Task Applying_is_refused_once_every_position_is_filled()
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        internship.NumberOfPositions = 1;
        _test.Db.SaveChanges();

        var hired = _test.Db.AddUser("U2024002");
        _test.Db.InternshipApplications.Add(new InternshipApplication
        {
            InternshipId = internship.Id, UserId = hired.Id,
            Status = InternshipApplicationStatus.Accepted
        });
        _test.Db.SaveChanges();

        var controller = Controller();
        await controller.Apply(internship.Id, null);

        Assert.Contains("already been filled", (string)controller.TempData["Error"]!);
    }

    [Fact]
    public async Task Applying_to_a_posting_that_does_not_exist_is_a_404()
    {
        Assert.IsType<NotFoundResult>(await Controller().Apply(9999, null));
    }

    // ---------- Withdrawing ----------

    [Fact]
    public async Task A_submitted_application_can_be_withdrawn()
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        await Controller().Apply(internship.Id, null);
        var applicationId = (await _test.NewContext().InternshipApplications.SingleAsync()).Id;

        await Controller().Withdraw(applicationId);

        Assert.Equal(InternshipApplicationStatus.Withdrawn,
            (await _test.NewContext().InternshipApplications.SingleAsync()).Status);
    }

    [Theory]
    [InlineData(InternshipApplicationStatus.Accepted)]
    [InlineData(InternshipApplicationStatus.Rejected)]
    [InlineData(InternshipApplicationStatus.Withdrawn)]
    public async Task A_finished_application_cannot_be_withdrawn(InternshipApplicationStatus status)
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        var application = new InternshipApplication
        {
            InternshipId = internship.Id, UserId = _student.Id, Status = status
        };
        _test.Db.InternshipApplications.Add(application);
        _test.Db.SaveChanges();

        var controller = Controller();
        await controller.Withdraw(application.Id);

        Assert.Equal("This application can no longer be withdrawn.", controller.TempData["Error"]);
        Assert.Equal(status, (await _test.NewContext().InternshipApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task A_student_cannot_withdraw_someone_elses_application()
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        var classmate = _test.Db.AddUser("U2024002");
        var theirs = new InternshipApplication { InternshipId = internship.Id, UserId = classmate.Id };
        _test.Db.InternshipApplications.Add(theirs);
        _test.Db.SaveChanges();

        var result = await Controller().Withdraw(theirs.Id);

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(InternshipApplicationStatus.Submitted,
            (await _test.NewContext().InternshipApplications.SingleAsync()).Status);
    }

    [Fact]
    public async Task My_applications_shows_only_my_own()
    {
        var internship = _test.Db.AddInternship(_homeCompany);
        var classmate = _test.Db.AddUser("U2024002");
        _test.Db.InternshipApplications.AddRange(
            new InternshipApplication { InternshipId = internship.Id, UserId = _student.Id },
            new InternshipApplication { InternshipId = internship.Id, UserId = classmate.Id });
        _test.Db.SaveChanges();

        var result = await Controller().MyApplications();
        var mine = result.ShouldBeViewWithModel<List<InternshipApplication>>();

        Assert.Equal(new[] { _student.Id }, mine.Select(a => a.UserId));
    }
}
