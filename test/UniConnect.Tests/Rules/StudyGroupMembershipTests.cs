using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;
using UniConnect.ViewModels;

namespace UniConnect.Tests.Rules;

/// <summary>
/// FR-46 through FR-54 — creating a study group and getting into one.
///
/// The join flow is approval-gated and capacity-limited, and the capacity check
/// appears in two places (requesting, and approving). Both are covered, because
/// only enforcing the first would let an officer approve past the ceiling.
/// </summary>
public class StudyGroupMembershipTests : IDisposable
{
    private const string Course = "MAT202";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly StubHubContext<StudyGroupHub> _hub = new();
    private readonly ApplicationUser _creator;

    public StudyGroupMembershipTests()
    {
        _test.Db.AddUniversity();
        _creator = _test.Db.AddUser("U2024001", fullName: "Creator");
        _provider.WithCourse(Course, "Discrete Maths").Enroll(_creator.UniversityId, Course);

        // StudyGroup carries a real composite FK to the local Courses table, so
        // the course has to have been mirrored by the sync job before a group
        // can exist for it — the adapter's view of the course isn't enough.
        // (SQLite enforces this; the InMemory provider would not have.)
        _test.Db.AddCourse(Course, name: "Discrete Maths");
    }

    public void Dispose() => _test.Dispose();

    private StudyGroupsController Controller(ApplicationUser user) =>
        new StudyGroupsController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _hub,
                _provider,
                ServiceHarness.AuditLog(_test.Db),
                ServiceHarness.Notifications(_test.Db),
                ServiceHarness.StudyGroups(_test.Db, _provider, _hub))
            .SignedInAs(user, "Student");

    private static StudyGroupCreateVM NewGroup(int max = 10, int min = 2) => new()
    {
        GroupName = "Calculus Crew",
        CourseCode = Course,
        MaxMembers = max,
        MinMembers = min
    };

    private ApplicationUser Classmate(string number)
    {
        var user = _test.Db.AddUser(number);
        _provider.Enroll(number, Course);
        return user;
    }

    private StudyGroup TheGroup() => _test.NewContext().StudyGroups.Single();

    // ---------- Creating ----------

    [Fact]
    public async Task Creating_a_group_makes_the_creator_an_approved_member_immediately()
    {
        // Otherwise the creator would be sitting in their own approval queue.
        await Controller(_creator).Create(NewGroup());

        var membership = await _test.NewContext().StudyGroupMembers.SingleAsync();
        Assert.Equal(_creator.Id, membership.UserId);
        Assert.Equal(MembershipStatus.Approved, membership.Status);
    }

    [Fact]
    public async Task A_student_not_enrolled_in_the_course_cannot_create_a_group_for_it()
    {
        // FR-46 E1 — enrollment is checked against the registrar, not locally.
        var outsider = _test.Db.AddUser("U2024099");

        var controller = Controller(outsider);
        var result = await controller.Create(NewGroup());

        result.ShouldBeView();
        Assert.True(controller.ModelState.ContainsKey(nameof(StudyGroupCreateVM.CourseCode)));
        Assert.Empty(_test.NewContext().StudyGroups);
    }

    [Fact]
    public async Task A_minimum_larger_than_the_maximum_is_rejected()
    {
        var controller = Controller(_creator);

        await controller.Create(NewGroup(max: 4, min: 6));

        Assert.True(controller.ModelState.ContainsKey(nameof(StudyGroupCreateVM.MinMembers)));
        Assert.Empty(_test.NewContext().StudyGroups);
    }

    [Fact]
    public async Task A_group_larger_than_the_universitys_ceiling_is_rejected()
    {
        // FR-11 — a student may pick a smaller cap than their institution
        // allows, never a larger one.
        _test.Db.UniversitySettings.Add(new UniversitySettings
        {
            UniversityCode = TestData.DefaultUniversity,
            MaxStudyGroupMembers = 5
        });
        _test.Db.SaveChanges();

        var controller = Controller(_creator);
        await controller.Create(NewGroup(max: 8));

        Assert.True(controller.ModelState.ContainsKey(nameof(StudyGroupCreateVM.MaxMembers)));
        Assert.Empty(_test.NewContext().StudyGroups);
    }

    [Fact]
    public async Task Group_names_and_locations_are_trimmed_before_saving()
    {
        var vm = NewGroup();
        vm.GroupName = "  Calculus Crew  ";
        vm.MeetingLocation = "  Library, floor 2  ";

        await Controller(_creator).Create(vm);

        var group = TheGroup();
        Assert.Equal("Calculus Crew", group.GroupName);
        Assert.Equal("Library, floor 2", group.MeetingLocation);
    }

    // ---------- Joining ----------

    [Fact]
    public async Task Joining_creates_a_pending_request_rather_than_a_membership()
    {
        await Controller(_creator).Create(NewGroup());
        var joiner = Classmate("U2024002");

        await Controller(joiner).Join(TheGroup().Id);

        var request = await _test.NewContext().StudyGroupMembers.SingleAsync(m => m.UserId == joiner.Id);
        Assert.Equal(MembershipStatus.Pending, request.Status);
    }

    [Fact]
    public async Task A_student_not_enrolled_in_the_course_cannot_request_to_join()
    {
        // FR-49.
        await Controller(_creator).Create(NewGroup());
        var outsider = _test.Db.AddUser("U2024099");

        var controller = Controller(outsider);
        await controller.Join(TheGroup().Id);

        Assert.Equal("You are not enrolled in this course.", controller.TempData["Error"]);
        Assert.Equal(1, await _test.NewContext().StudyGroupMembers.CountAsync());
    }

    [Fact]
    public async Task Requesting_twice_is_refused()
    {
        await Controller(_creator).Create(NewGroup());
        var joiner = Classmate("U2024002");
        var groupId = TheGroup().Id;

        await Controller(joiner).Join(groupId);
        var controller = Controller(joiner);
        await controller.Join(groupId);

        Assert.Equal("You already have a pending request for this group.", controller.TempData["Error"]);
        Assert.Equal(2, await _test.NewContext().StudyGroupMembers.CountAsync());
    }

    [Fact]
    public async Task An_existing_member_is_told_they_are_already_in()
    {
        await Controller(_creator).Create(NewGroup());

        var controller = Controller(_creator);
        await controller.Join(TheGroup().Id);

        Assert.Equal("You are already in this group.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task A_full_group_refuses_new_requests_and_marks_itself_Full()
    {
        await Controller(_creator).Create(NewGroup(max: 2));
        var groupId = TheGroup().Id;

        var second = Classmate("U2024002");
        await Controller(second).Join(groupId);
        var pendingId = (await _test.NewContext().StudyGroupMembers
            .SingleAsync(m => m.UserId == second.Id)).Id;
        await Controller(_creator).ApproveMember(pendingId);

        var third = Classmate("U2024003");
        var controller = Controller(third);
        await controller.Join(groupId);

        Assert.Equal("This study group is already full.", controller.TempData["Error"]);
        Assert.Equal(StudyGroupStatus.Full, TheGroup().Status);
    }

    [Fact]
    public async Task A_group_from_another_university_cannot_be_joined()
    {
        await Controller(_creator).Create(NewGroup());
        _test.Db.AddUniversity(TestData.OtherUniversity);
        var outsider = _test.Db.AddUser("U2024002", TestData.OtherUniversity);
        _provider.Enroll(outsider.UniversityId, Course);

        var controller = Controller(outsider);
        var result = await controller.Join(TheGroup().Id);

        result.ShouldRedirectToAction(nameof(StudyGroupsController.Index));
        Assert.Equal("This group doesn't belong to your university.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task A_request_wakes_a_group_that_had_gone_quiet()
    {
        await Controller(_creator).Create(NewGroup());
        var group = _test.Db.StudyGroups.Single();
        group.Status = StudyGroupStatus.Inactive;
        _test.Db.SaveChanges();

        await Controller(Classmate("U2024002")).Join(group.Id);

        Assert.Equal(StudyGroupStatus.Active, TheGroup().Status);
    }

    // ---------- Approving ----------

    [Fact]
    public async Task The_creator_can_approve_a_pending_request()
    {
        await Controller(_creator).Create(NewGroup());
        var joiner = Classmate("U2024002");
        await Controller(joiner).Join(TheGroup().Id);

        var pendingId = (await _test.NewContext().StudyGroupMembers
            .SingleAsync(m => m.UserId == joiner.Id)).Id;

        await Controller(_creator).ApproveMember(pendingId);

        Assert.Equal(MembershipStatus.Approved,
            (await _test.NewContext().StudyGroupMembers.SingleAsync(m => m.UserId == joiner.Id)).Status);
    }

    [Fact]
    public async Task Only_the_creator_can_approve_requests()
    {
        // Anyone who can guess a membership id must not be able to let people in.
        await Controller(_creator).Create(NewGroup());
        var joiner = Classmate("U2024002");
        var meddler = Classmate("U2024003");
        await Controller(joiner).Join(TheGroup().Id);

        var pendingId = (await _test.NewContext().StudyGroupMembers
            .SingleAsync(m => m.UserId == joiner.Id)).Id;

        var result = await Controller(meddler).ApproveMember(pendingId);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(MembershipStatus.Pending,
            (await _test.NewContext().StudyGroupMembers.SingleAsync(m => m.UserId == joiner.Id)).Status);
    }

    [Fact]
    public async Task Approving_past_the_capacity_is_refused()
    {
        // The capacity check on Join isn't enough on its own: requests made
        // while there was room can still outlive the space.
        await Controller(_creator).Create(NewGroup(max: 2));
        var groupId = TheGroup().Id;

        var second = Classmate("U2024002");
        var third = Classmate("U2024003");
        await Controller(second).Join(groupId);
        await Controller(third).Join(groupId);

        var ids = await _test.NewContext().StudyGroupMembers
            .Where(m => m.Status == MembershipStatus.Pending)
            .Select(m => m.Id).ToListAsync();

        await Controller(_creator).ApproveMember(ids[0]);

        var controller = Controller(_creator);
        await controller.ApproveMember(ids[1]);

        Assert.Equal("The group is already full — reject or remove someone first.",
            controller.TempData["Error"]);
        Assert.Equal(2, await _test.NewContext().StudyGroupMembers
            .CountAsync(m => m.Status == MembershipStatus.Approved));
    }

    [Fact]
    public async Task Approving_the_last_free_place_marks_the_group_Full()
    {
        await Controller(_creator).Create(NewGroup(max: 2));
        var joiner = Classmate("U2024002");
        await Controller(joiner).Join(TheGroup().Id);

        var pendingId = (await _test.NewContext().StudyGroupMembers
            .SingleAsync(m => m.UserId == joiner.Id)).Id;

        await Controller(_creator).ApproveMember(pendingId);

        Assert.Equal(StudyGroupStatus.Full, TheGroup().Status);
    }

    [Fact]
    public async Task Approving_an_already_approved_member_is_refused()
    {
        await Controller(_creator).Create(NewGroup());
        var creatorMembershipId = (await _test.NewContext().StudyGroupMembers.SingleAsync()).Id;

        var controller = Controller(_creator);
        await controller.ApproveMember(creatorMembershipId);

        Assert.Equal("That request is no longer pending.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task An_approved_member_is_notified()
    {
        await Controller(_creator).Create(NewGroup());
        var joiner = Classmate("U2024002");
        await Controller(joiner).Join(TheGroup().Id);

        var pendingId = (await _test.NewContext().StudyGroupMembers
            .SingleAsync(m => m.UserId == joiner.Id)).Id;

        await Controller(_creator).ApproveMember(pendingId);

        Assert.True(await _test.NewContext().Notifications.AnyAsync(n => n.UserId == joiner.Id));
    }

    // ---------- Uniqueness ----------

    [Fact]
    public async Task The_database_refuses_two_membership_rows_for_the_same_person()
    {
        await Controller(_creator).Create(NewGroup());
        var groupId = TheGroup().Id;

        using var context = _test.NewContext();
        context.StudyGroupMembers.Add(new StudyGroupMember
        {
            StudyGroupId = groupId, UserId = _creator.Id, Status = MembershipStatus.Pending
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
