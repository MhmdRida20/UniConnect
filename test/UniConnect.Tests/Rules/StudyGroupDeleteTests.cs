using Microsoft.AspNetCore.Mvc;
using UniConnect.Controllers.Api;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Deleting a study group.
///
/// "Delete" archives rather than destroys: the group owns its members and its
/// whole message history, and the audit trail refers to it by id. These tests
/// pin both halves of that — who is allowed to do it, and that the group really
/// does disappear from where students look for it.
/// </summary>
public class StudyGroupDeleteTests : IDisposable
{
    private const string Course = "CSC301";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly StubHubContext<UniConnect.Hubs.StudyGroupHub> _hub = new();

    private readonly ApplicationUser _creator;
    private readonly ApplicationUser _member;

    public StudyGroupDeleteTests()
    {
        _test.Db.AddUniversity();

        _creator = _test.Db.AddUser("U2024001", fullName: "Creator");
        _member = _test.Db.AddUser("U2024002", fullName: "Member Two");

        _provider
            .WithCourse(Course, "Data Structures")
            .Enroll(_creator.UniversityId, Course)
            .Enroll(_member.UniversityId, Course);

        _test.Db.AddCourse(Course, name: "Data Structures");
    }

    public void Dispose() => _test.Dispose();

    private StudyGroupsApiController Api(ApplicationUser user) =>
        new StudyGroupsApiController(
                ServiceHarness.StudyGroups(_test.Db, _provider, _hub),
                IdentityHarness.CreateUserManager(_test.Db))
            .SignedInApi(user, "Student");

    private async Task<int> CreateGroupAsync()
    {
        var created = await Api(_creator).Create(new StudyGroupsApiController.CreateGroupRequest
        {
            GroupName = "Test Group",
            CourseCode = Course,
            MaxMembers = 10,
            MinMembers = 2
        });

        var payload = Assert.IsType<CreatedAtActionResult>(created);
        return Assert.IsType<StudyGroupsApiController.GroupSummary>(payload.Value).Id;
    }

    /// <summary>Adds the second student as an approved member.</summary>
    private async Task JoinAndApproveAsync(int groupId)
    {
        await Api(_member).Join(groupId);

        var membership = _test.Db.StudyGroupMembers
            .First(m => m.StudyGroupId == groupId && m.UserId == _member.Id);

        await Api(_creator).ApproveMember(membership.Id);
    }

    private StudyGroup Reload(int groupId) => _test.Db.StudyGroups.First(g => g.Id == groupId);

    // ---------- who may delete ----------

    [Fact]
    public async Task The_creator_can_delete_their_group()
    {
        var groupId = await CreateGroupAsync();

        var result = await Api(_creator).Delete(groupId);

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StudyGroupStatus.Archived, Reload(groupId).Status);
    }

    [Fact]
    public async Task A_member_who_did_not_create_the_group_cannot_delete_it()
    {
        var groupId = await CreateGroupAsync();
        await JoinAndApproveAsync(groupId);

        var result = await Api(_member).Delete(groupId);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(StudyGroupStatus.Active, Reload(groupId).Status);
    }

    [Fact]
    public async Task A_student_with_no_connection_to_the_group_cannot_delete_it()
    {
        var groupId = await CreateGroupAsync();

        var result = await Api(_member).Delete(groupId);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(StudyGroupStatus.Active, Reload(groupId).Status);
    }

    [Fact]
    public async Task Deleting_a_group_from_another_university_is_not_found()
    {
        var groupId = await CreateGroupAsync();

        _test.Db.AddUniversity("OTHER");
        var outsider = _test.Db.AddUser("U9999001", universityCode: "OTHER", fullName: "Outsider");

        // NotFound rather than Forbidden: another university's groups should not
        // be distinguishable from ones that do not exist.
        Assert.IsType<NotFoundResult>(await Api(outsider).Delete(groupId));
        Assert.Equal(StudyGroupStatus.Active, Reload(groupId).Status);
    }

    [Fact]
    public async Task Deleting_a_group_that_does_not_exist_is_not_found()
    {
        Assert.IsType<NotFoundResult>(await Api(_creator).Delete(4242));
    }

    [Fact]
    public async Task Deleting_twice_is_refused_rather_than_silently_succeeding()
    {
        var groupId = await CreateGroupAsync();
        await Api(_creator).Delete(groupId);

        var second = await Api(_creator).Delete(groupId);

        var bad = Assert.IsType<BadRequestObjectResult>(second);
        var error = Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value);
        Assert.Equal("ALREADY_DELETED", error.Code);
    }

    // ---------- what deleting actually does ----------

    [Fact]
    public async Task A_deleted_group_disappears_from_browse_for_everyone()
    {
        var groupId = await CreateGroupAsync();
        await JoinAndApproveAsync(groupId);

        await Api(_creator).Delete(groupId);

        var forCreator = Assert.IsType<List<StudyGroupsApiController.GroupSummary>>(
            Assert.IsType<OkObjectResult>(await Api(_creator).Index(null)).Value);
        var forMember = Assert.IsType<List<StudyGroupsApiController.GroupSummary>>(
            Assert.IsType<OkObjectResult>(await Api(_member).Index(null)).Value);

        Assert.Empty(forCreator);
        Assert.Empty(forMember);
    }

    [Fact]
    public async Task Deleting_keeps_the_chat_history_rather_than_destroying_it()
    {
        var groupId = await CreateGroupAsync();
        await Api(_creator).PostMessage(groupId, new StudyGroupsApiController.PostMessageRequest
        {
            Content = "worth keeping"
        });

        await Api(_creator).Delete(groupId);

        // The whole reason this archives instead of deleting: a hard delete
        // would cascade the conversation away with the group.
        Assert.Single(_test.Db.StudyGroupMessages.Where(m => m.StudyGroupId == groupId));
        Assert.Equal("worth keeping",
            _test.Db.StudyGroupMessages.First(m => m.StudyGroupId == groupId).Content);
    }

    [Fact]
    public async Task Members_are_told_when_the_group_they_are_in_is_deleted()
    {
        var groupId = await CreateGroupAsync();
        await JoinAndApproveAsync(groupId);

        var before = _test.Db.Notifications.Count(n => n.UserId == _member.Id);

        await Api(_creator).Delete(groupId);

        Assert.True(_test.Db.Notifications.Count(n => n.UserId == _member.Id) > before);
    }

    [Fact]
    public async Task The_creator_is_not_notified_about_their_own_deletion()
    {
        var groupId = await CreateGroupAsync();
        await JoinAndApproveAsync(groupId);

        var before = _test.Db.Notifications.Count(n => n.UserId == _creator.Id);

        await Api(_creator).Delete(groupId);

        Assert.Equal(before, _test.Db.Notifications.Count(n => n.UserId == _creator.Id));
    }
}
