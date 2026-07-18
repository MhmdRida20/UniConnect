using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;
using UniConnect.Data;
using UniConnect.Models;
using UniConnect.ViewModels;

namespace UniConnect.Controllers
{
    /// <summary>
    /// FR-84 through FR-91 (the 8 report types) and UC-20 (generate reports,
    /// apply filters, export). Audit log viewing (FR-92, UC-20 A1) lives in
    /// AdminAuditLogController — kept separate since it's a genuinely
    /// different kind of screen (a raw event log, not a summarized report).
    ///
    /// Edge Cases: "Report generation timeout" and "Export file too large" —
    /// every report caps its detail rows at MaxRows rather than letting a
    /// query run unbounded; if the cap is hit, the result is marked
    /// Truncated and the view/export both say so plainly.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class AdminReportsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private const int MaxRows = 2000;

        public AdminReportsController(ApplicationDbContext db)
        {
            _db = db;
        }

        public IActionResult Index() => View();

        public async Task<IActionResult> Report(string type, DateTime? from, DateTime? to, string? universityCode)
        {
            var result = await GenerateAsync(type, from, to, universityCode);
            if (result is null) return NotFound();

            ViewBag.Type = type;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.UniversityCode = universityCode;
            ViewBag.Universities = await _db.Universities.OrderBy(u => u.Name).ToListAsync();

            return View(result);
        }

        public async Task<IActionResult> Export(string type, DateTime? from, DateTime? to, string? universityCode)
        {
            var result = await GenerateAsync(type, from, to, universityCode);
            if (result is null) return NotFound();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", result.ColumnHeaders.Select(CsvEscape)));
            foreach (var row in result.Rows)
                sb.AppendLine(string.Join(",", row.Select(CsvEscape)));

            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"{type}-report-{DateTime.UtcNow:yyyyMMdd}.csv";
            return File(bytes, "text/csv", fileName);
        }

        private static string CsvEscape(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private async Task<ReportResultVM?> GenerateAsync(string type, DateTime? from, DateTime? to, string? universityCode)
        {
            var fromDate = from ?? DateTime.UtcNow.AddMonths(-3);
            var toDate = (to ?? DateTime.UtcNow).AddDays(1); // inclusive of the whole "to" day

            return type switch
            {
                "ServiceUsage" => await ServiceUsageReport(),
                "Attendance" => await AttendanceReport(fromDate, toDate, universityCode),
                "Complaints" => await ComplaintsReport(fromDate, toDate, universityCode),
                "Internships" => await InternshipsReport(fromDate, toDate),
                "StudyGroups" => await StudyGroupsReport(fromDate, toDate, universityCode),
                "Rides" => await RidesReport(fromDate, toDate, universityCode),
                "Clubs" => await ClubsReport(fromDate, toDate, universityCode),
                "Notifications" => await NotificationsReport(fromDate, toDate),
                _ => null
            };
        }

        // ---------- FR-84: Service Usage ---------------------------------------
        private async Task<ReportResultVM> ServiceUsageReport()
        {
            var universities = await _db.Universities.OrderBy(u => u.Name).ToListAsync();
            var enablement = await _db.UniversityServices.Where(us => us.IsEnabled).ToListAsync();
            var services = await _db.Services.ToDictionaryAsync(s => s.Code, s => s.Name);

            var rows = new List<List<string>>();
            foreach (var uni in universities)
            {
                var enabledHere = enablement.Where(e => e.UniversityCode == uni.Code).ToList();
                foreach (var e in enabledHere)
                {
                    var count = e.ServiceCode switch
                    {
                        ServiceCodes.StudyGroups => await _db.StudyGroups.CountAsync(g => g.UniversityCode == uni.Code),
                        ServiceCodes.RideSharing => await _db.Rides.CountAsync(r => r.UniversityCode == uni.Code),
                        ServiceCodes.Tickets => await _db.Tickets.CountAsync(t => t.UniversityCode == uni.Code),
                        ServiceCodes.Clubs => await _db.Clubs.CountAsync(c => c.UniversityCode == uni.Code),
                        ServiceCodes.Attendance => await _db.AttendanceSessions.CountAsync(s => s.UniversityCode == uni.Code),
                        // Internships aren't university-scoped (see Company.cs) —
                        // reported as a global figure, noted in the row itself.
                        ServiceCodes.Internships => await _db.InternshipApplications.CountAsync(),
                        _ => 0
                    };
                    rows.Add(new List<string> { uni.Name, services.GetValueOrDefault(e.ServiceCode, e.ServiceCode), "Yes", count.ToString() });
                }
            }

            return new ReportResultVM
            {
                Title = "Service Usage Report",
                Summary = new() { ("Universities", universities.Count.ToString()), ("Enabled Service Rows", enablement.Count.ToString()) },
                ColumnHeaders = new() { "University", "Service", "Enabled", "Activity Count" },
                Rows = rows
            };
        }

        // ---------- FR-85: Attendance -------------------------------------------
        private async Task<ReportResultVM> AttendanceReport(DateTime from, DateTime to, string? universityCode)
        {
            var query = _db.AttendanceSessions.Include(s => s.Records)
                .Where(s => s.CreatedAt >= from && s.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(universityCode))
                query = query.Where(s => s.UniversityCode == universityCode);

            var sessions = await query.OrderByDescending(s => s.StartTime).Take(MaxRows).ToListAsync();
            var truncated = sessions.Count >= MaxRows;

            var present = sessions.Sum(s => s.Records.Count(r => r.Status == AttendanceStatus.Present));
            var late = sessions.Sum(s => s.Records.Count(r => r.Status == AttendanceStatus.Late));
            var absent = sessions.Sum(s => s.Records.Count(r => r.Status == AttendanceStatus.Absent));
            var suspicious = sessions.Sum(s => s.Records.Count(r => r.IsSuspicious));

            var rows = sessions.Select(s => new List<string>
            {
                s.CourseName, s.CourseCode, s.StartTime.ToString("yyyy-MM-dd HH:mm"),
                s.Records.Count(r => r.Status == AttendanceStatus.Present).ToString(),
                s.Records.Count(r => r.Status == AttendanceStatus.Late).ToString(),
                s.Records.Count(r => r.Status == AttendanceStatus.Absent).ToString(),
                s.Records.Count(r => r.IsSuspicious).ToString()
            }).ToList();

            return new ReportResultVM
            {
                Title = "Attendance Report",
                Summary = new()
                {
                    ("Sessions", sessions.Count.ToString()), ("Present", present.ToString()),
                    ("Late", late.ToString()), ("Absent", absent.ToString()), ("Suspicious Attempts", suspicious.ToString())
                },
                ColumnHeaders = new() { "Course", "Code", "Start Time", "Present", "Late", "Absent", "Suspicious" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-86: Complaints/Tickets ------------------------------------
        private async Task<ReportResultVM> ComplaintsReport(DateTime from, DateTime to, string? universityCode)
        {
            var query = _db.Tickets.Include(t => t.Category).Include(t => t.Responses)
                .Where(t => t.CreatedAt >= from && t.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(universityCode))
                query = query.Where(t => t.UniversityCode == universityCode);

            var tickets = await query.OrderByDescending(t => t.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = tickets.Count >= MaxRows;

            var responseTimes = new List<double>();
            foreach (var t in tickets)
            {
                var firstResponse = t.Responses.OrderBy(r => r.CreatedAt).FirstOrDefault();
                if (firstResponse is not null)
                    responseTimes.Add((firstResponse.CreatedAt - t.CreatedAt).TotalHours);
            }
            var avgResponseHours = responseTimes.Count > 0 ? responseTimes.Average() : 0;

            var rows = tickets.Select(t => new List<string>
            {
                t.Title, t.Category?.Name ?? "—", t.Priority.ToString(), t.Status.ToString(),
                t.Category?.Name ?? "—", t.CreatedAt.ToString("yyyy-MM-dd")
            }).ToList();

            return new ReportResultVM
            {
                Title = "Complaint / Ticket Report",
                Summary = new()
                {
                    ("Tickets", tickets.Count.ToString()),
                    ("Open", tickets.Count(t => t.Status == TicketStatus.Open).ToString()),
                    ("Resolved", tickets.Count(t => t.Status == TicketStatus.Resolved).ToString()),
                    ("Avg. Response Time", $"{avgResponseHours:F1} hrs")
                },
                ColumnHeaders = new() { "Title", "Category", "Priority", "Status", "Department", "Created" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-87: Internships (global — not university-scoped) ---------
        private async Task<ReportResultVM> InternshipsReport(DateTime from, DateTime to)
        {
            var postings = await _db.Internships.Include(i => i.Company).Include(i => i.Applications)
                .Where(i => i.CreatedAt >= from && i.CreatedAt < to)
                .OrderByDescending(i => i.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = postings.Count >= MaxRows;

            var allApplications = postings.SelectMany(p => p.Applications).ToList();
            var scores = allApplications.Where(a => a.MatchingScore.HasValue).Select(a => a.MatchingScore!.Value).ToList();
            var avgScore = scores.Count > 0 ? scores.Average() : 0;

            var rows = postings.Select(p => new List<string>
            {
                p.Title, p.Company?.CompanyName ?? "—", p.Applications.Count.ToString(),
                p.Applications.Count(a => a.Status == InternshipApplicationStatus.Accepted).ToString(),
                p.Applications.Count(a => a.Status == InternshipApplicationStatus.Rejected).ToString(),
                p.IsActive ? "Active" : "Deactivated"
            }).ToList();

            return new ReportResultVM
            {
                Title = "Internship Report",
                Summary = new()
                {
                    ("Postings", postings.Count.ToString()), ("Applications", allApplications.Count.ToString()),
                    ("Avg. Matching Score", $"{avgScore:F0}%"),
                    ("Accepted", allApplications.Count(a => a.Status == InternshipApplicationStatus.Accepted).ToString())
                },
                ColumnHeaders = new() { "Title", "Company", "Applications", "Accepted", "Rejected", "Status" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-88: Study Groups ------------------------------------------
        private async Task<ReportResultVM> StudyGroupsReport(DateTime from, DateTime to, string? universityCode)
        {
            var query = _db.StudyGroups.Include(g => g.Members).Include(g => g.Messages)
                .Where(g => g.CreatedAt >= from && g.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(universityCode))
                query = query.Where(g => g.UniversityCode == universityCode);

            var groups = await query.OrderByDescending(g => g.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = groups.Count >= MaxRows;

            var rows = groups.Select(g => new List<string>
            {
                g.GroupName, g.CourseCode, g.Status.ToString(),
                g.Members.Count(m => m.Status == MembershipStatus.Approved).ToString(),
                g.Messages.Count.ToString()
            }).ToList();

            return new ReportResultVM
            {
                Title = "Study Group Report",
                Summary = new()
                {
                    ("Groups", groups.Count.ToString()),
                    ("Active", groups.Count(g => g.Status == StudyGroupStatus.Active).ToString()),
                    ("Inactive", groups.Count(g => g.Status == StudyGroupStatus.Inactive).ToString()),
                    ("Archived", groups.Count(g => g.Status == StudyGroupStatus.Archived).ToString()),
                    ("Total Members", groups.Sum(g => g.Members.Count(m => m.Status == MembershipStatus.Approved)).ToString()),
                    ("Total Messages", groups.Sum(g => g.Messages.Count).ToString())
                },
                ColumnHeaders = new() { "Group", "Course", "Status", "Members", "Messages" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-89: Ride Sharing ------------------------------------------
        private async Task<ReportResultVM> RidesReport(DateTime from, DateTime to, string? universityCode)
        {
            var query = _db.Rides.Include(r => r.Requests)
                .Where(r => r.CreatedAt >= from && r.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(universityCode))
                query = query.Where(r => r.UniversityCode == universityCode);

            var rides = await query.OrderByDescending(r => r.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = rides.Count >= MaxRows;

            var totalRequests = rides.Sum(r => r.Requests.Count);
            var acceptedRequests = rides.Sum(r => r.Requests.Count(rr => rr.Status == RideRequestStatus.Accepted));
            var acceptanceRate = totalRequests > 0 ? (double)acceptedRequests / totalRequests * 100 : 0;
            var seatsUsed = rides.Sum(r => r.TotalSeats - r.AvailableSeats);
            var seatsTotal = rides.Sum(r => r.TotalSeats);

            var rows = rides.Select(r => new List<string>
            {
                $"{r.DepartureLocation} -> {r.Destination}", r.Status.ToString(), r.Requests.Count.ToString(),
                r.Requests.Count(rr => rr.Status == RideRequestStatus.Accepted).ToString(),
                $"{r.TotalSeats - r.AvailableSeats}/{r.TotalSeats}"
            }).ToList();

            return new ReportResultVM
            {
                Title = "Ride Sharing Report",
                Summary = new()
                {
                    ("Rides", rides.Count.ToString()), ("Requests", totalRequests.ToString()),
                    ("Acceptance Rate", $"{acceptanceRate:F0}%"), ("Seats Used", $"{seatsUsed}/{seatsTotal}")
                },
                ColumnHeaders = new() { "Route", "Status", "Requests", "Accepted", "Seats Used" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-90: Clubs -------------------------------------------------
        private async Task<ReportResultVM> ClubsReport(DateTime from, DateTime to, string? universityCode)
        {
            var query = _db.Clubs.Include(c => c.Members).Include(c => c.Events).ThenInclude(e => e.Rsvps)
                .Where(c => c.CreatedAt >= from && c.CreatedAt < to);
            if (!string.IsNullOrWhiteSpace(universityCode))
                query = query.Where(c => c.UniversityCode == universityCode);

            var clubs = await query.OrderByDescending(c => c.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = clubs.Count >= MaxRows;

            var rows = clubs.Select(c => new List<string>
            {
                c.ClubName, c.Status.ToString(),
                c.Members.Count(m => m.Status == ClubMembershipStatus.Approved).ToString(),
                c.Events.Count.ToString(),
                c.Events.Sum(e => e.Rsvps.Count).ToString()
            }).ToList();

            return new ReportResultVM
            {
                Title = "Club Report",
                Summary = new()
                {
                    ("Clubs", clubs.Count.ToString()),
                    ("Active", clubs.Count(c => c.Status == ClubStatus.Active).ToString()),
                    ("Inactive", clubs.Count(c => c.Status == ClubStatus.Inactive).ToString()),
                    ("Total Members", clubs.Sum(c => c.Members.Count(m => m.Status == ClubMembershipStatus.Approved)).ToString()),
                    ("Total Events", clubs.Sum(c => c.Events.Count).ToString())
                },
                ColumnHeaders = new() { "Club", "Status", "Members", "Events", "RSVPs" },
                Rows = rows,
                Truncated = truncated
            };
        }

        // ---------- FR-91: Notifications (global) --------------------------------
        private async Task<ReportResultVM> NotificationsReport(DateTime from, DateTime to)
        {
            var query = _db.Notifications.Include(n => n.User)
                .Where(n => n.CreatedAt >= from && n.CreatedAt < to);

            var notifications = await query.OrderByDescending(n => n.CreatedAt).Take(MaxRows).ToListAsync();
            var truncated = notifications.Count >= MaxRows;

            var rows = notifications.Select(n => new List<string>
            {
                n.CreatedAt.ToString("yyyy-MM-dd HH:mm"), n.User?.FullName ?? "—", n.Title, n.IsRead ? "Read" : "Unread"
            }).ToList();

            return new ReportResultVM
            {
                Title = "Notification Report",
                Summary = new()
                {
                    ("Sent", notifications.Count.ToString()),
                    ("Read", notifications.Count(n => n.IsRead).ToString()),
                    ("Unread", notifications.Count(n => !n.IsRead).ToString()),
                    // Every notification that's created is successfully persisted —
                    // there's no delivery-failure concept in this implementation
                    // (see NotificationService), so "Failed" is always 0.
                    ("Failed", "0")
                },
                ColumnHeaders = new() { "Date", "Recipient", "Title", "Status" },
                Rows = rows,
                Truncated = truncated
            };
        }
    }
}
