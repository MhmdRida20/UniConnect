using Microsoft.AspNetCore.Mvc;
using UniConnect.Controllers;
using UniConnect.Controllers.Api;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;
using UniConnect.ViewModels;

namespace UniConnect.Tests.Rules;

/// <summary>
/// Field-level validation for creating a study group.
///
/// The web form gets these rules from StudyGroupCreateVM's DataAnnotations, so
/// they never used to exist below the controller. The mobile API binds a plain
/// DTO with no annotations, which meant a request with a blank name went
/// straight past them — these tests pin the rules to StudyGroupService, where
/// both entry points have to pass through.
/// </summary>
public class StudyGroupCreateValidationTests : IDisposable
{
    private const string Course = "CSC301";

    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();
    private readonly StubHubContext<UniConnect.Hubs.StudyGroupHub> _hub = new();
    private readonly ApplicationUser _student;

    public StudyGroupCreateValidationTests()
    {
        _test.Db.AddUniversity();
        _student = _test.Db.AddUser("U2024001", fullName: "Student One");
        _provider.WithCourse(Course, "Data Structures").Enroll(_student.UniversityId, Course);
        _test.Db.AddCourse(Course, name: "Data Structures");
    }

    public void Dispose() => _test.Dispose();

    private StudyGroupsApiController Api() =>
        new StudyGroupsApiController(
                ServiceHarness.StudyGroups(_test.Db, _provider, _hub),
                IdentityHarness.CreateUserManager(_test.Db))
            .SignedInApi(_student, "Student");

    private StudyGroupsController Web() =>
        new StudyGroupsController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _hub,
                _provider,
                ServiceHarness.AuditLog(_test.Db),
                ServiceHarness.Notifications(_test.Db),
                ServiceHarness.StudyGroups(_test.Db, _provider, _hub))
            .SignedInAs(_student, "Student");

    private static StudyGroupsApiController.CreateGroupRequest Request(
        string name = "Test Group",
        string course = Course,
        string? description = null,
        string? location = null,
        int max = 10,
        int min = 2) => new()
        {
            GroupName = name,
            CourseCode = course,
            Description = description,
            MeetingLocation = location,
            MaxMembers = max,
            MinMembers = min
        };

    private static StudyGroupsApiController.FieldErrorResponse Refusal(IActionResult result)
    {
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        return Assert.IsType<StudyGroupsApiController.FieldErrorResponse>(bad.Value);
    }

    private int GroupCount() => _test.Db.StudyGroups.Count();

    // ---------- the gap that prompted these tests ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Api_refuses_a_blank_group_name_and_creates_nothing(string name)
    {
        var refusal = Refusal(await Api().Create(Request(name: name)));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.GroupName));
        Assert.Equal(0, GroupCount());
    }

    [Fact]
    public async Task Api_refuses_a_group_name_longer_than_100_characters()
    {
        var refusal = Refusal(await Api().Create(Request(name: new string('x', 101))));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.GroupName));
        Assert.Equal(0, GroupCount());
    }

    [Fact]
    public async Task Api_accepts_a_group_name_of_exactly_100_characters()
    {
        var result = await Api().Create(Request(name: new string('x', 100)));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1, GroupCount());
    }

    [Fact]
    public async Task Api_refuses_a_missing_course_code()
    {
        var refusal = Refusal(await Api().Create(Request(course: "")));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.CourseCode));
        Assert.Equal(0, GroupCount());
    }

    [Fact]
    public async Task Api_refuses_a_description_longer_than_500_characters()
    {
        var refusal = Refusal(await Api().Create(Request(description: new string('x', 501))));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.Description));
        Assert.Equal(0, GroupCount());
    }

    [Fact]
    public async Task Api_refuses_a_meeting_location_longer_than_100_characters()
    {
        var refusal = Refusal(await Api().Create(Request(location: new string('x', 101))));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.MeetingLocation));
        Assert.Equal(0, GroupCount());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(51)]
    [InlineData(0)]
    public async Task Api_refuses_member_counts_outside_the_allowed_range(int max)
    {
        var refusal = Refusal(await Api().Create(Request(max: max, min: 2)));

        Assert.NotEmpty(refusal.Fields);
        Assert.Equal(0, GroupCount());
    }

    [Fact]
    public async Task Api_reports_every_broken_field_at_once_rather_than_one_at_a_time()
    {
        var refusal = Refusal(await Api().Create(Request(name: "", course: "", max: 99)));

        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.GroupName));
        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.CourseCode));
        Assert.Contains(refusal.Fields, f => f.Field == nameof(StudyGroupCreateVM.MaxMembers));
    }

    [Fact]
    public async Task Api_still_creates_a_group_when_every_field_is_valid()
    {
        var result = await Api().Create(Request(description: "Weekly revision", location: "Library 204"));

        Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(1, GroupCount());
    }

    // ---------- the web path ----------

    [Fact]
    public async Task Web_does_not_create_a_group_when_model_validation_failed()
    {
        var controller = Web();

        // What the model binder produces for a form submitted with no name.
        controller.ModelState.AddModelError(
            nameof(StudyGroupCreateVM.GroupName), "The Group Name field is required.");

        var result = await controller.Create(new StudyGroupCreateVM
        {
            GroupName = string.Empty,
            CourseCode = Course,
            MaxMembers = 10,
            MinMembers = 2
        });

        // Re-renders the form...
        Assert.IsType<ViewResult>(result);
        // ...and, crucially, did not insert the group on the way past.
        Assert.Equal(0, GroupCount());
    }
}
