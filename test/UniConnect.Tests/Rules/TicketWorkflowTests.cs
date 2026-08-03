using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UniConnect.Controllers;
using UniConnect.Hubs;
using UniConnect.Models;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Rules;

/// <summary>
/// UC-06 — the staff side of complaints and ticketing.
///
/// The department scoping is the part that matters: staff are bound to one
/// department by a string on their account, matched against the ticket's
/// category name. That is a fragile way to express a permission boundary, so
/// it's worth having tests that prove it actually holds — including for a
/// direct URL to a ticket in someone else's queue.
/// </summary>
public class TicketWorkflowTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly StubHubContext<TicketHub> _hub = new();
    private readonly ApplicationUser _student;
    private readonly ApplicationUser _itStaff;
    private readonly ApplicationUser _financeStaff;
    private readonly TicketCategory _itCategory;
    private readonly TicketCategory _financeCategory;

    public TicketWorkflowTests()
    {
        _test.Db.AddUniversity();
        _student = _test.Db.AddUser("U2024001", fullName: "Sam Student");
        _itStaff = _test.Db.AddUser("STAFF-IT", department: "IT", fullName: "Iris IT");
        _financeStaff = _test.Db.AddUser("STAFF-FIN", department: "Finance", fullName: "Fred Finance");

        _itCategory = AddCategory("IT");
        _financeCategory = AddCategory("Finance");
    }

    public void Dispose() => _test.Dispose();

    private TicketCategory AddCategory(string name)
    {
        var category = new TicketCategory { UniversityCode = TestData.DefaultUniversity, Name = name };
        _test.Db.TicketCategories.Add(category);
        _test.Db.SaveChanges();
        return category;
    }

    private Ticket AddTicket(
        TicketCategory? category = null,
        TicketStatus status = TicketStatus.Open,
        TicketPriority priority = TicketPriority.Medium,
        string? assignedStaffId = null,
        int createdMinutesAgo = 0)
    {
        var ticket = new Ticket
        {
            UniversityCode = TestData.DefaultUniversity,
            SubmitterId = _student.Id,
            CategoryId = (category ?? _itCategory).Id,
            Title = "Projector won't turn on",
            Description = "Room 204's projector is dead.",
            Priority = priority,
            Status = status,
            AssignedStaffId = assignedStaffId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-createdMinutesAgo)
        };
        _test.Db.Tickets.Add(ticket);
        _test.Db.SaveChanges();
        return ticket;
    }

    private StaffTicketsController Controller(ApplicationUser staff) =>
        new StaffTicketsController(
                _test.Db,
                IdentityHarness.CreateUserManager(_test.Db),
                _hub,
                ServiceHarness.AuditLog(_test.Db),
                ServiceHarness.Notifications(_test.Db))
            .SignedInAs(staff, "DepartmentStaff");

    // ---------- Department scoping ----------

    [Fact]
    public async Task The_queue_shows_only_this_departments_tickets()
    {
        var mine = AddTicket(_itCategory);
        AddTicket(_financeCategory);

        var result = await Controller(_itStaff).Index(null);
        var queue = result.ShouldBeViewWithModel<List<Ticket>>();

        Assert.Equal(new[] { mine.Id }, queue.Select(t => t.Id));
    }

    [Fact]
    public async Task Opening_another_departments_ticket_by_direct_url_is_forbidden()
    {
        // E1 of UC-06 — the queue already filters, so this is the only way in.
        var financeTicket = AddTicket(_financeCategory);

        var result = await Controller(_itStaff).Details(financeTicket.Id);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Responding_to_another_departments_ticket_is_forbidden()
    {
        var financeTicket = AddTicket(_financeCategory);

        var result = await Controller(_itStaff).Respond(financeTicket.Id, "Looking into it", null);

        Assert.IsType<ForbidResult>(result);
        Assert.Empty(_test.NewContext().TicketResponses);
    }

    [Fact]
    public async Task Picking_up_another_departments_ticket_is_forbidden()
    {
        var financeTicket = AddTicket(_financeCategory);

        var result = await Controller(_itStaff).PickUp(financeTicket.Id);

        Assert.IsType<ForbidResult>(result);
        Assert.Null((await _test.NewContext().Tickets.SingleAsync(t => t.Id == financeTicket.Id)).AssignedStaffId);
    }

    [Fact]
    public async Task A_staff_account_with_no_department_is_forbidden_everywhere()
    {
        // Belt and braces: the role alone shouldn't be enough if the account
        // was never assigned to a department.
        var unassigned = _test.Db.AddUser("STAFF-NONE", department: null);
        var ticket = AddTicket();

        Assert.IsType<ForbidResult>(await Controller(unassigned).Index(null));
        Assert.IsType<ForbidResult>(await Controller(unassigned).Details(ticket.Id));
        Assert.IsType<ForbidResult>(await Controller(unassigned).PickUp(ticket.Id));
    }

    // ---------- Queue ordering and filtering ----------

    [Fact]
    public async Task The_queue_puts_urgent_first_then_oldest_within_a_priority()
    {
        var oldMedium = AddTicket(priority: TicketPriority.Medium, createdMinutesAgo: 500);
        var newMedium = AddTicket(priority: TicketPriority.Medium, createdMinutesAgo: 10);
        var urgent = AddTicket(priority: TicketPriority.Urgent, createdMinutesAgo: 1);

        var queue = (await Controller(_itStaff).Index(null)).ShouldBeViewWithModel<List<Ticket>>();

        Assert.Equal(new[] { urgent.Id, oldMedium.Id, newMedium.Id }, queue.Select(t => t.Id));
    }

    [Fact]
    public async Task The_queue_can_be_filtered_by_status()
    {
        var open = AddTicket(status: TicketStatus.Open);
        AddTicket(status: TicketStatus.Resolved);

        var queue = (await Controller(_itStaff).Index(nameof(TicketStatus.Open)))
            .ShouldBeViewWithModel<List<Ticket>>();

        Assert.Equal(new[] { open.Id }, queue.Select(t => t.Id));
    }

    [Fact]
    public async Task An_unrecognised_status_filter_is_ignored_rather_than_emptying_the_queue()
    {
        AddTicket(status: TicketStatus.Open);
        AddTicket(status: TicketStatus.Resolved);

        var queue = (await Controller(_itStaff).Index("NotAStatus")).ShouldBeViewWithModel<List<Ticket>>();

        Assert.Equal(2, queue.Count);
    }

    // ---------- Responding ----------

    [Fact]
    public async Task A_response_without_a_status_change_records_no_transition()
    {
        var ticket = AddTicket();

        await Controller(_itStaff).Respond(ticket.Id, "  On my way.  ", null);

        var response = await _test.NewContext().TicketResponses.SingleAsync();
        Assert.Equal("On my way.", response.Content);      // trimmed
        Assert.Null(response.PreviousStatus);
        Assert.Null(response.NewStatus);
    }

    [Fact]
    public async Task A_status_change_is_recorded_on_the_response_and_audited()
    {
        // The transition history is what makes a ticket auditable after the
        // fact, so both halves have to be written.
        var ticket = AddTicket(status: TicketStatus.Open);

        await Controller(_itStaff).Respond(ticket.Id, "Fixed the cable.", TicketStatus.Resolved);

        using var verify = _test.NewContext();
        var response = await verify.TicketResponses.SingleAsync();
        Assert.Equal(TicketStatus.Open, response.PreviousStatus);
        Assert.Equal(TicketStatus.Resolved, response.NewStatus);
        Assert.Equal(TicketStatus.Resolved, (await verify.Tickets.SingleAsync(t => t.Id == ticket.Id)).Status);

        var audit = await verify.AuditLogs.SingleAsync(a => a.Action == "TicketStatusChanged");
        Assert.Equal("Open -> Resolved", audit.Details);
    }

    [Fact]
    public async Task Setting_the_status_to_what_it_already_is_records_no_transition()
    {
        var ticket = AddTicket(status: TicketStatus.InProgress);

        await Controller(_itStaff).Respond(ticket.Id, "Still working on it.", TicketStatus.InProgress);

        var response = await _test.NewContext().TicketResponses.SingleAsync();
        Assert.Null(response.PreviousStatus);
        Assert.False(await _test.NewContext().AuditLogs.AnyAsync(a => a.Action == "TicketStatusChanged"));
    }

    [Fact]
    public async Task Responding_claims_an_unassigned_ticket()
    {
        // Matches the "another staff member can pick up an unavailable
        // colleague's ticket" edge case — replying is itself a claim.
        var ticket = AddTicket();

        await Controller(_itStaff).Respond(ticket.Id, "Taking this one.", null);

        Assert.Equal(_itStaff.Id, (await _test.NewContext().Tickets.SingleAsync(t => t.Id == ticket.Id)).AssignedStaffId);
    }

    [Fact]
    public async Task Responding_does_not_steal_a_ticket_from_the_colleague_who_holds_it()
    {
        var colleague = _test.Db.AddUser("STAFF-IT2", department: "IT");
        var ticket = AddTicket(assignedStaffId: colleague.Id);

        await Controller(_itStaff).Respond(ticket.Id, "Adding a note.", null);

        Assert.Equal(colleague.Id, (await _test.NewContext().Tickets.SingleAsync(t => t.Id == ticket.Id)).AssignedStaffId);
    }

    [Fact]
    public async Task An_empty_response_is_refused()
    {
        var ticket = AddTicket();
        var controller = Controller(_itStaff);

        await controller.Respond(ticket.Id, "   ", TicketStatus.Resolved);

        Assert.Equal("Please enter a response.", controller.TempData["Error"]);
        Assert.Empty(_test.NewContext().TicketResponses);
        Assert.Equal(TicketStatus.Open, (await _test.NewContext().Tickets.SingleAsync(t => t.Id == ticket.Id)).Status);
    }

    [Fact]
    public async Task The_student_is_notified_of_a_response()
    {
        var ticket = AddTicket();

        await Controller(_itStaff).Respond(ticket.Id, "We're on it.", null);

        var notification = await _test.NewContext().Notifications.SingleAsync();
        Assert.Equal(_student.Id, notification.UserId);
        Assert.Equal("New response on your ticket", notification.Title);
    }

    [Fact]
    public async Task A_status_change_tells_the_student_what_it_changed_to()
    {
        var ticket = AddTicket();

        await Controller(_itStaff).Respond(ticket.Id, "All sorted.", TicketStatus.Resolved);

        var notification = await _test.NewContext().Notifications.SingleAsync();
        Assert.Equal("Ticket status updated", notification.Title);
        Assert.Contains("Resolved", notification.Message);
    }

    // ---------- Assignment ----------

    [Fact]
    public async Task Picking_up_a_ticket_in_your_own_department_assigns_it_to_you()
    {
        var ticket = AddTicket();

        await Controller(_itStaff).PickUp(ticket.Id);

        Assert.Equal(_itStaff.Id, (await _test.NewContext().Tickets.SingleAsync(t => t.Id == ticket.Id)).AssignedStaffId);
    }

    [Fact]
    public async Task Acting_on_a_ticket_that_does_not_exist_is_a_404()
    {
        Assert.IsType<NotFoundResult>(await Controller(_itStaff).PickUp(9999));
        Assert.IsType<NotFoundResult>(await Controller(_itStaff).Details(9999));
    }
}
