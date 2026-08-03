using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Tests.Unit;

/// <summary>
/// The data-annotation rules on the create forms.
///
/// These are the first line of defence on every POST — ModelState.IsValid gates
/// the controller body — and they're easy to weaken by accident when a form is
/// restyled, which this project has been doing a lot of.
/// </summary>
public class ViewModelValidationTests
{
    private static IReadOnlyList<ValidationResult> Validate(object model)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    private static bool HasErrorOn(object model, string member) =>
        Validate(model).Any(r => r.MemberNames.Contains(member));

    // ---------- StudyGroupCreateVM ----------

    [Fact]
    public void Study_group_defaults_are_valid()
    {
        var vm = new StudyGroupCreateVM { GroupName = "Calculus Crew", CourseCode = "MAT202" };

        Assert.Empty(Validate(vm));
    }

    [Theory]
    [InlineData(1)]     // a "group" of one isn't a group
    [InlineData(0)]
    [InlineData(51)]    // FR-20 caps the size
    public void Study_group_size_outside_2_to_50_is_rejected(int size)
    {
        var vm = new StudyGroupCreateVM { GroupName = "G", CourseCode = "MAT202", MaxMembers = size };

        Assert.True(HasErrorOn(vm, nameof(StudyGroupCreateVM.MaxMembers)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    public void Study_group_size_boundaries_are_inclusive(int size)
    {
        var vm = new StudyGroupCreateVM
        {
            GroupName = "G", CourseCode = "MAT202", MaxMembers = size, MinMembers = 2
        };

        Assert.False(HasErrorOn(vm, nameof(StudyGroupCreateVM.MaxMembers)));
    }

    [Fact]
    public void Study_group_requires_a_name_and_a_course()
    {
        var vm = new StudyGroupCreateVM();

        Assert.True(HasErrorOn(vm, nameof(StudyGroupCreateVM.GroupName)));
        Assert.True(HasErrorOn(vm, nameof(StudyGroupCreateVM.CourseCode)));
    }

    // ---------- AttendanceSessionCreateVM ----------

    [Fact]
    public void Attendance_session_requires_classroom_coordinates()
    {
        // Without them the GPS radius check has nothing to measure against, so
        // the whole point of the feature is lost.
        var vm = new AttendanceSessionCreateVM { CourseCode = "CSC301" };

        Assert.True(HasErrorOn(vm, nameof(AttendanceSessionCreateVM.ClassroomLat)));
        Assert.True(HasErrorOn(vm, nameof(AttendanceSessionCreateVM.ClassroomLng)));
    }

    [Theory]
    [InlineData(9)]      // too tight to be usable indoors
    [InlineData(1001)]   // wide enough to cover the next building
    public void Attendance_gps_radius_outside_10_to_1000_is_rejected(int radius)
    {
        var vm = new AttendanceSessionCreateVM
        {
            CourseCode = "CSC301", ClassroomLat = 33.9, ClassroomLng = 35.5, GpsRadiusMeters = radius
        };

        Assert.True(HasErrorOn(vm, nameof(AttendanceSessionCreateVM.GpsRadiusMeters)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void Attendance_grace_period_outside_0_to_60_is_rejected(int grace)
    {
        var vm = new AttendanceSessionCreateVM
        {
            CourseCode = "CSC301", ClassroomLat = 33.9, ClassroomLng = 35.5, GracePeriodMinutes = grace
        };

        Assert.True(HasErrorOn(vm, nameof(AttendanceSessionCreateVM.GracePeriodMinutes)));
    }

    [Fact]
    public void Attendance_session_with_everything_set_is_valid()
    {
        var vm = new AttendanceSessionCreateVM
        {
            CourseCode = "CSC301", ClassroomLat = 33.8938, ClassroomLng = 35.5018
        };

        Assert.Empty(Validate(vm));
    }

    // ---------- RideCreateVM ----------

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Ride_seat_count_outside_1_to_8_is_rejected(int seats)
    {
        var vm = new RideCreateVM
        {
            DepartureLocation = "Hamra", Destination = "Main Gate", VehicleId = 1, TotalSeats = seats
        };

        Assert.True(HasErrorOn(vm, nameof(RideCreateVM.TotalSeats)));
    }

    [Fact]
    public void Ride_requires_both_ends_of_the_journey()
    {
        var vm = new RideCreateVM { VehicleId = 1 };

        Assert.True(HasErrorOn(vm, nameof(RideCreateVM.DepartureLocation)));
        Assert.True(HasErrorOn(vm, nameof(RideCreateVM.Destination)));
    }

    // ---------- TicketCreateVM ----------

    [Fact]
    public void Ticket_requires_a_title_and_description()
    {
        var vm = new TicketCreateVM { CategoryId = 1 };

        Assert.True(HasErrorOn(vm, nameof(TicketCreateVM.Title)));
        Assert.True(HasErrorOn(vm, nameof(TicketCreateVM.Description)));
    }

    [Fact]
    public void Ticket_description_is_capped_at_2000_characters()
    {
        var vm = new TicketCreateVM
        {
            CategoryId = 1,
            Title = "Broken projector",
            Description = new string('x', 2001),
            Priority = TicketPriority.Medium
        };

        Assert.True(HasErrorOn(vm, nameof(TicketCreateVM.Description)));
    }

    // ---------- The datetime edit format ----------

    [Theory]
    [InlineData(typeof(AttendanceSessionCreateVM), nameof(AttendanceSessionCreateVM.StartTime))]
    [InlineData(typeof(AttendanceSessionCreateVM), nameof(AttendanceSessionCreateVM.EndTime))]
    [InlineData(typeof(RideCreateVM), nameof(RideCreateVM.DepartureTime))]
    [InlineData(typeof(ClubEventCreateVM), nameof(ClubEventCreateVM.EventDateTime))]
    public void Datetime_fields_render_in_the_format_the_browser_input_accepts(Type vmType, string property)
    {
        // <input type="datetime-local"> only accepts yyyy-MM-ddTHH:mm. Without
        // this attribute the model value renders with seconds and an AM/PM
        // suffix, and the control comes up blank.
        var attribute = vmType.GetProperty(property)!.GetCustomAttribute<DisplayFormatAttribute>();

        Assert.NotNull(attribute);
        Assert.True(attribute.ApplyFormatInEditMode);

        var format = attribute.DataFormatString;
        Assert.Equal("{0:yyyy-MM-ddTHH:mm}", format);

        // And prove the format actually produces what the control expects.
        var rendered = string.Format(CultureInfo.InvariantCulture, format!,
            new DateTime(2026, 8, 3, 13, 48, 55));
        Assert.Equal("2026-08-03T13:48", rendered);
    }
}
