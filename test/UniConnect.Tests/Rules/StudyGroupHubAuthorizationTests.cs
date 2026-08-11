using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// The live channel must not hand out what the REST API refuses.
///
/// StudyGroupService.GetMessagesAsync forbids chat history to a non-member, and
/// there is a test for it — but the hub used to accept anonymous connections
/// and add any caller to any group on request, so the same messages were one
/// JoinGroup call away for anybody. These tests pin the fix.
/// </summary>
public class StudyGroupHubAuthorizationTests : IDisposable
{
    private const string Course = "CSC301";

    private readonly TestDatabase _test = new();
    private readonly ApplicationUser _member;
    private readonly ApplicationUser _outsider;
    private readonly int _groupId;

    public StudyGroupHubAuthorizationTests()
    {
        _test.Db.AddUniversity();
        _test.Db.AddCourse(Course, name: "Data Structures");

        _member = _test.Db.AddUser("U2024001", fullName: "Member One");
        _outsider = _test.Db.AddUser("U2024002", fullName: "Outsider");

        var group = new StudyGroup
        {
            GroupName = "Test Group",
            UniversityCode = _member.UniversityCode,
            CourseCode = Course,
            CreatorId = _member.Id,
            MaxMembers = 10,
            MinMembers = 2,
            Status = StudyGroupStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _test.Db.StudyGroups.Add(group);
        _test.Db.SaveChanges();
        _groupId = group.Id;

        _test.Db.StudyGroupMembers.Add(new StudyGroupMember
        {
            StudyGroupId = _groupId,
            UserId = _member.Id,
            Status = MembershipStatus.Approved,
            JoinedAt = DateTime.UtcNow
        });
        _test.Db.SaveChanges();
    }

    public void Dispose() => _test.Dispose();

    private (StudyGroupHub Hub, RecordingGroupManager Groups) HubFor(string? userId)
    {
        var groups = new RecordingGroupManager();
        var hub = new StudyGroupHub(_test.Db)
        {
            Context = new FakeHubCallerContext(userId),
            Groups = groups
        };
        return (hub, groups);
    }

    [Fact]
    public void Hub_requires_an_authenticated_caller()
    {
        // The connection itself is refused before any method runs, which is
        // what keeps an anonymous client from reaching JoinGroup at all.
        Assert.NotNull(typeof(StudyGroupHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .FirstOrDefault());
    }

    [Fact]
    public void Hub_accepts_both_the_cookie_and_the_bearer_scheme()
    {
        // A bare [Authorize] uses the application default, which AddIdentity
        // sets to the Identity cookie — that let browsers connect and rejected
        // the mobile app's bearer token, leaving its chat stuck on "Offline".
        var authorize = typeof(StudyGroupHub)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        var schemes = (authorize.AuthenticationSchemes ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Contains(IdentityConstants.ApplicationScheme, schemes);
        Assert.Contains(JwtBearerDefaults.AuthenticationScheme, schemes);
    }

    [Fact]
    public async Task An_approved_member_may_subscribe_to_the_group()
    {
        var (hub, groups) = HubFor(_member.Id);

        await hub.JoinGroup(_groupId);

        Assert.Contains($"group-{_groupId}", groups.Added);
    }

    [Fact]
    public async Task A_student_who_is_not_a_member_is_refused()
    {
        var (hub, groups) = HubFor(_outsider.Id);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinGroup(_groupId));
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task A_pending_request_does_not_grant_the_live_feed()
    {
        _test.Db.StudyGroupMembers.Add(new StudyGroupMember
        {
            StudyGroupId = _groupId,
            UserId = _outsider.Id,
            Status = MembershipStatus.Pending,
            JoinedAt = DateTime.UtcNow
        });
        _test.Db.SaveChanges();

        var (hub, groups) = HubFor(_outsider.Id);

        // Waiting for approval must not mean reading the chat in the meantime.
        await Assert.ThrowsAsync<HubException>(() => hub.JoinGroup(_groupId));
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task A_caller_with_no_identity_is_refused()
    {
        var (hub, groups) = HubFor(null);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinGroup(_groupId));
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Subscribing_to_a_group_that_does_not_exist_is_refused()
    {
        var (hub, groups) = HubFor(_member.Id);

        await Assert.ThrowsAsync<HubException>(() => hub.JoinGroup(_groupId + 9999));
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task Leaving_stays_open_because_it_grants_nothing()
    {
        var (hub, groups) = HubFor(_outsider.Id);

        await hub.LeaveGroup(_groupId);

        Assert.Contains($"group-{_groupId}", groups.Removed);
    }

    // ---- minimal SignalR doubles ----

    private sealed class RecordingGroupManager : IGroupManager
    {
        public List<string> Added { get; } = new();
        public List<string> Removed { get; } = new();

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Added.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add(groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly string? _userId;

        public FakeHubCallerContext(string? userId) => _userId = userId;

        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => _userId;

        public override ClaimsPrincipal? User => _userId is null
            ? null
            : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, _userId) }, "Test"));

        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;

        public override void Abort() { }
    }
}
