using Microsoft.AspNetCore.Mvc;
using UniConnect.Controllers.Api;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Parity coverage for the mobile Study Groups API — MOBILE_STUDYGROUPS_PLAN.md §7.
///
/// The claim being defended is that the mobile client is a faithful mirror: it
/// must not be able to do anything the web refuses, or be refused anything the
/// web allows. Both entry points now call StudyGroupService, so these tests are
/// really asserting that the API layer translates the shared rules onto HTTP
/// correctly and doesn't quietly add or drop one.
///
/// Numbering follows the scenario table in the plan.
/// </summary>
public class StudyGroupApiParityTests : IDisposable
{
    private const string Course = "CSC301";
    private const string OtherCourse = "MAT202";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly StubHubContext<UniConnect.Hubs.StudyGroupHub> _hub = new();

    private readonly ApplicationUser _creator;
    private readonly ApplicationUser _student;

    public StudyGroupApiParityTests()
    {
        _test.Db.AddUniversity();

        _creator = _test.Db.AddUser("U2024001", fullName: "Creator");
        _student = _test.Db.AddUser("U2024002", fullName: "Student Two");

        _provider
            .WithCourse(Course, "Data Structures")
            .WithCourse(OtherCourse, "Discrete Maths")
            .Enroll(_creator.UniversityId, Course)
            .Enroll(_student.UniversityId, Course);

        // StudyGroup has a real composite FK to the local Courses table, so the
        // course must have been mirrored by the sync job before a group can
        // exist for it — the adapter's view alone isn't enough.
        _test.Db.AddCourse(Course, name: "Data Structures");
        _test.Db.AddCourse(OtherCourse, name: "Discrete Maths");
    }

    public void Dispose() => _test.Dispose();

    private StudyGroupsApiController Api(ApplicationUser user) =>
        new StudyGroupsApiController(
                ServiceHarness.StudyGroups(_test.Db, _provider, _hub),
                IdentityHarness.CreateUserManager(_test.Db))
            .SignedInApi(user, "Student");

    private static StudyGroupsApiController.CreateGroupRequest NewGroup(
        string course = Course, int max = 10, int min = 2) => new()
    {
        GroupName = "Test Group",
        CourseCode = course,
        MaxMembers = max,
        MinMembers = min
    };

    private async Task<int> CreateGroupAsync(int max = 10)
    {
        var created = await Api(_creator).Create(NewGroup(max: max));
        var payload = Assert.IsType<CreatedAtActionResult>(created);
        return Assert.IsType<StudyGroupsApiController.GroupSummary>(payload.Value).Id;
    }

    private static T Body<T>(IActionResult result) where T : class
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    // ---------- 1, 2: visibility ----------

    [Fact]
    public async Task Browse_returns_only_groups_for_courses_the_student_is_enrolled_in()
    {
        await CreateGroupAsync();

        // A group for a course the student is NOT enrolled in.
        _provider.Enroll(_creator.UniversityId, OtherCourse);
        await Api(_creator).Create(NewGroup(course: OtherCourse));

        var groups = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_student).Index(null));

        Assert.Single(groups);
        Assert.Equal(Course, groups[0].CourseCode);
    }

    [Fact]
    public async Task A_group_from_another_university_cannot_be_fetched_directly()
    {
        var groupId = await CreateGroupAsync();

        // ApplicationUser has a real FK to University, so the second
        // institution has to exist before anyone can belong to it.
        _test.Db.AddUniversity("OTHER");
        var outsider = _test.Db.AddUser("X9999001", universityCode: "OTHER");
        _provider.Enroll(outsider.UniversityId, Course);

        var result = await Api(outsider).Details(groupId);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("CROSS_UNIVERSITY", Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value).Code);
    }

    // ---------- 3, 4, 5, 6: create ----------

    [Fact]
    public async Task Create_is_refused_above_the_university_member_ceiling()
    {
        // Default ceiling is 10 (UniversitySettings.MaxStudyGroupMembers).
        var result = await Api(_creator).Create(NewGroup(max: 25));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<StudyGroupsApiController.FieldErrorResponse>(bad.Value);
        Assert.Contains(body.Fields, f => f.Field == "MaxMembers" && f.Message.Contains("caps study groups at 10"));
    }

    [Fact]
    public async Task Create_is_refused_when_minimum_exceeds_maximum()
    {
        var result = await Api(_creator).Create(NewGroup(max: 4, min: 8));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<StudyGroupsApiController.FieldErrorResponse>(bad.Value);
        Assert.Contains(body.Fields, f => f.Field == "MinMembers");
    }

    [Fact]
    public async Task Create_is_refused_for_a_course_the_student_is_not_enrolled_in()
    {
        var result = await Api(_student).Create(NewGroup(course: OtherCourse));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<StudyGroupsApiController.FieldErrorResponse>(bad.Value);
        Assert.Contains(body.Fields, f => f.Field == "CourseCode" && f.Message.Contains("not enrolled"));
    }

    [Fact]
    public async Task The_creator_is_an_approved_member_of_their_own_group_immediately()
    {
        var groupId = await CreateGroupAsync();

        var detail = Body<StudyGroupsApiController.GroupDetailResponse>(await Api(_creator).Details(groupId));

        Assert.True(detail.AmCreator);
        Assert.True(detail.CanPost);
        Assert.False(detail.CanJoin);
        Assert.Equal(1, detail.ApprovedCount);
        Assert.Equal("Approved", detail.MyMembership!.Status);
    }

    [Fact]
    public async Task Create_is_refused_when_the_course_has_not_been_mirrored_locally_yet()
    {
        // Enrolled per the adapter, but the sync job hasn't written the Course
        // row — this used to reach the database and fail as a 500 (plan §3.2).
        _provider.WithCourse("NEW101").Enroll(_creator.UniversityId, "NEW101");

        var result = await Api(_creator).Create(NewGroup(course: "NEW101"));

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<StudyGroupsApiController.FieldErrorResponse>(bad.Value);
        Assert.Contains(body.Fields, f => f.Field == "CourseCode" && f.Message.Contains("syncing"));
    }

    // ---------- 7, 8: joining ----------

    [Fact]
    public async Task Joining_twice_is_refused_the_second_time()
    {
        var groupId = await CreateGroupAsync();

        Assert.IsType<OkObjectResult>(await Api(_student).Join(groupId));

        var second = await Api(_student).Join(groupId);
        var bad = Assert.IsType<BadRequestObjectResult>(second);
        Assert.Equal("ALREADY_PENDING", Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value).Code);
    }

    [Fact]
    public async Task Joining_a_full_group_is_refused_and_marks_it_full()
    {
        // Max 1 is impossible (Range is 2-50), so fill a 2-seat group.
        var groupId = await CreateGroupAsync(max: 2);

        await Api(_student).Join(groupId);
        var member = _test.Db.StudyGroupMembers.Single(m => m.UserId == _student.Id);
        await Api(_creator).ApproveMember(member.Id);

        var third = _test.Db.AddUser("U2024003", fullName: "Third");
        _provider.Enroll(third.UniversityId, Course);

        var result = await Api(third).Join(groupId);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("GROUP_FULL", Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value).Code);
        Assert.Equal(StudyGroupStatus.Full, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);
    }

    // ---------- 9, 10: approval ----------

    [Fact]
    public async Task Approving_as_a_non_creator_is_forbidden()
    {
        var groupId = await CreateGroupAsync();
        await Api(_student).Join(groupId);
        var member = _test.Db.StudyGroupMembers.Single(m => m.UserId == _student.Id);

        Assert.IsType<ForbidResult>(await Api(_student).ApproveMember(member.Id));
    }

    [Fact]
    public async Task Approving_the_last_seat_flips_the_group_to_full()
    {
        var groupId = await CreateGroupAsync(max: 2);
        await Api(_student).Join(groupId);
        var member = _test.Db.StudyGroupMembers.Single(m => m.UserId == _student.Id);

        Assert.IsType<OkObjectResult>(await Api(_creator).ApproveMember(member.Id));

        Assert.Equal(StudyGroupStatus.Full, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);
    }

    // ---------- 12, 13, 14: leaving ----------

    [Fact]
    public async Task When_the_creator_leaves_leadership_passes_to_the_longest_standing_member()
    {
        var groupId = await CreateGroupAsync();

        await Api(_student).Join(groupId);
        await Api(_creator).ApproveMember(_test.Db.StudyGroupMembers.Single(m => m.UserId == _student.Id).Id);

        var third = _test.Db.AddUser("U2024003", fullName: "Third");
        _provider.Enroll(third.UniversityId, Course);
        await Api(third).Join(groupId);
        await Api(_creator).ApproveMember(_test.Db.StudyGroupMembers.Single(m => m.UserId == third.Id).Id);

        Assert.IsType<OkObjectResult>(await Api(_creator).Leave(groupId));

        // _student joined before third, so leadership goes to _student.
        Assert.Equal(_student.Id, _test.Db.StudyGroups.Single(g => g.Id == groupId).CreatorId);
    }

    [Fact]
    public async Task When_the_last_member_leaves_the_group_is_archived()
    {
        var groupId = await CreateGroupAsync();

        Assert.IsType<OkObjectResult>(await Api(_creator).Leave(groupId));

        Assert.Equal(StudyGroupStatus.Archived, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);
    }

    [Fact]
    public async Task Leaving_a_full_group_returns_it_to_active()
    {
        var groupId = await CreateGroupAsync(max: 2);
        await Api(_student).Join(groupId);
        await Api(_creator).ApproveMember(_test.Db.StudyGroupMembers.Single(m => m.UserId == _student.Id).Id);
        Assert.Equal(StudyGroupStatus.Full, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);

        await Api(_student).Leave(groupId);

        Assert.Equal(StudyGroupStatus.Active, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);
    }

    // ---------- 15, 16, 17: chat ----------

    [Fact]
    public async Task Posting_as_a_pending_member_is_forbidden()
    {
        var groupId = await CreateGroupAsync();
        await Api(_student).Join(groupId);   // Pending, not approved

        var result = await Api(_student).PostMessage(
            groupId, new StudyGroupsApiController.PostMessageRequest { Content = "hello" });

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Posting_to_an_inactive_group_reactivates_it()
    {
        var groupId = await CreateGroupAsync();
        var group = _test.Db.StudyGroups.Single(g => g.Id == groupId);
        group.Status = StudyGroupStatus.Inactive;
        _test.Db.SaveChanges();

        var result = await Api(_creator).PostMessage(
            groupId, new StudyGroupsApiController.PostMessageRequest { Content = "anyone still here?" });

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StudyGroupStatus.Active, _test.Db.StudyGroups.Single(g => g.Id == groupId).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_message_is_refused(string content)
    {
        var groupId = await CreateGroupAsync();

        var result = await Api(_creator).PostMessage(
            groupId, new StudyGroupsApiController.PostMessageRequest { Content = content });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("EMPTY_MESSAGE", Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value).Code);
    }

    [Fact]
    public async Task A_message_over_1000_characters_is_refused()
    {
        var groupId = await CreateGroupAsync();

        var result = await Api(_creator).PostMessage(
            groupId, new StudyGroupsApiController.PostMessageRequest { Content = new string('x', 1001) });

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("MESSAGE_TOO_LONG", Assert.IsType<StudyGroupsApiController.ErrorResponse>(bad.Value).Code);
    }

    // ---------- paging (plan §3.4) ----------

    [Fact]
    public async Task Chat_history_is_paged_newest_first_and_walks_backwards()
    {
        var groupId = await CreateGroupAsync();

        for (var i = 1; i <= 5; i++)
        {
            await Api(_creator).PostMessage(
                groupId, new StudyGroupsApiController.PostMessageRequest { Content = $"message {i}" });
        }

        var newest = Body<List<StudyGroupsApiController.MessageDto>>(
            await Api(_creator).Messages(groupId, before: null, take: 2));

        // Returned oldest-first within the page, but it is the newest page.
        Assert.Equal(2, newest.Count);
        Assert.Equal("message 4", newest[0].Content);
        Assert.Equal("message 5", newest[1].Content);

        var older = Body<List<StudyGroupsApiController.MessageDto>>(
            await Api(_creator).Messages(groupId, before: newest[0].Id, take: 2));

        Assert.Equal("message 2", older[0].Content);
        Assert.Equal("message 3", older[1].Content);
    }

    [Fact]
    public async Task Chat_history_is_forbidden_to_a_non_member()
    {
        var groupId = await CreateGroupAsync();

        Assert.IsType<ForbidResult>(await Api(_student).Messages(groupId, null, 30));
    }

    // ---------- permission flags are the server's job ----------

    [Fact]
    public async Task Pending_requests_are_only_visible_to_the_creator()
    {
        var groupId = await CreateGroupAsync();
        await Api(_student).Join(groupId);

        var asCreator = Body<StudyGroupsApiController.GroupDetailResponse>(await Api(_creator).Details(groupId));
        Assert.Single(asCreator.Pending);

        var asRequester = Body<StudyGroupsApiController.GroupDetailResponse>(await Api(_student).Details(groupId));
        Assert.Empty(asRequester.Pending);
        Assert.False(asRequester.AmCreator);
        Assert.False(asRequester.CanPost);
        Assert.Equal("Pending", asRequester.MyMembership!.Status);
    }

    // ---------- membership on the browse list ----------
    //
    // The web's Index view works out "you're a member of this one" from the
    // member collection it already has loaded. A mobile client gets only the
    // summary, so the server has to say it — MyStatus is what carries it.

    [Fact]
    public async Task Browse_marks_the_creator_as_a_member_of_their_own_group()
    {
        await CreateGroupAsync();

        var groups = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_creator).Index(null));

        Assert.Equal("Approved", groups[0].MyStatus);
        Assert.True(groups[0].AmMember);
    }

    [Fact]
    public async Task Browse_reports_no_membership_for_a_student_who_has_not_joined()
    {
        await CreateGroupAsync();

        var groups = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_student).Index(null));

        Assert.Null(groups[0].MyStatus);
        Assert.False(groups[0].AmMember);
    }

    [Fact]
    public async Task Browse_reports_a_pending_request_as_pending_not_as_membership()
    {
        var groupId = await CreateGroupAsync();
        await Api(_student).Join(groupId);

        var groups = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_student).Index(null));

        Assert.Equal("Pending", groups[0].MyStatus);

        // The distinction matters: a pending request must not light up the
        // "Member" badge or count towards "N you're in".
        Assert.False(groups[0].AmMember);
    }

    [Fact]
    public async Task Membership_is_reported_per_caller_not_per_group()
    {
        var groupId = await CreateGroupAsync();
        await Api(_student).Join(groupId);
        await Api(_creator).ApproveMember(
            _test.Db.StudyGroupMembers.First(m => m.UserId == _student.Id && m.StudyGroupId == groupId).Id);

        var forCreator = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_creator).Index(null));
        var forStudent = Body<List<StudyGroupsApiController.GroupSummary>>(await Api(_student).Index(null));

        Assert.True(forCreator[0].AmMember);
        Assert.True(forStudent[0].AmMember);
    }
}
