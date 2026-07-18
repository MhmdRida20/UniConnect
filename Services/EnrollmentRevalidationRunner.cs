using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Edge Case (Study Group Edge Cases — "Adapter enrollment changes"):
    /// "A student drops a course after joining a study group for that course
    /// → the system shall remove or flag the student's membership when the
    /// enrollment is no longer valid."
    ///
    /// The actual re-check-and-remove logic, extracted into its own
    /// injectable service so both EnrollmentRevalidationService (the
    /// periodic background job) and the admin's simulator (which triggers
    /// an immediate check right after dropping a student from a course, for
    /// a tight test/demo loop) call the exact same code path.
    ///
    /// Removed members are directly notified WHY (see NotificationService)
    /// — the group doesn't just silently lose them with no explanation.
    /// </summary>
    public class EnrollmentRevalidationRunner
    {
        private readonly ApplicationDbContext _db;
        private readonly IUniversityProviderResolver _resolver;
        private readonly IHubContext<StudyGroupHub> _hub;
        private readonly NotificationService _notifications;
        private readonly ILogger<EnrollmentRevalidationRunner> _logger;

        public EnrollmentRevalidationRunner(
            ApplicationDbContext db,
            IUniversityProviderResolver resolver,
            IHubContext<StudyGroupHub> hub,
            NotificationService notifications,
            ILogger<EnrollmentRevalidationRunner> logger)
        {
            _db = db;
            _resolver = resolver;
            _hub = hub;
            _notifications = notifications;
            _logger = logger;
        }

        public async Task RevalidateAsync(CancellationToken ct = default)
        {
            var approvedMembers = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup)
                .Include(m => m.User)
                .Where(m => m.Status == MembershipStatus.Approved && m.StudyGroup != null)
                .ToListAsync(ct);

            // (userId, groupId, groupName, courseCode) for each removal, so we
            // can notify them AFTER the group succession logic below has run.
            var removals = new List<(string UserId, int GroupId, string GroupName, string CourseCode)>();

            foreach (var universityGroup in approvedMembers.GroupBy(m => m.StudyGroup!.UniversityCode))
            {
                var provider = await _resolver.GetProviderAsync(universityGroup.Key);

                foreach (var membership in universityGroup)
                {
                    var group = membership.StudyGroup!;
                    if (membership.User is null) continue;

                    bool stillEnrolled;
                    try
                    {
                        stillEnrolled = await provider.IsEnrolledAsync(
                            group.UniversityCode, membership.User.UniversityId, group.CourseCode);
                    }
                    catch (Exception ex)
                    {
                        // Adapter unavailable for this specific check — don't
                        // remove anyone based on an inconclusive result.
                        _logger.LogWarning(ex,
                            "Could not verify enrollment for user {UserId} in course {Course} — skipping this check.",
                            membership.UserId, group.CourseCode);
                        continue;
                    }

                    if (stillEnrolled) continue;

                    _db.StudyGroupMembers.Remove(membership);
                    removals.Add((membership.UserId, group.Id, group.GroupName, group.CourseCode));

                    _logger.LogInformation(
                        "Removed user {UserId} from study group {GroupId} — no longer enrolled in {Course}.",
                        membership.UserId, group.Id, group.CourseCode);
                }
            }

            if (removals.Count == 0) return;

            await _db.SaveChangesAsync(ct);

            // Succession/archival for any group that just lost a member —
            // same rule as a normal Leave().
            foreach (var groupId in removals.Select(r => r.GroupId).Distinct())
            {
                var group = await _db.StudyGroups.FindAsync(new object[] { groupId }, ct);
                if (group is null) continue;

                var remaining = await _db.StudyGroupMembers
                    .Where(m => m.StudyGroupId == groupId && m.Status == MembershipStatus.Approved)
                    .OrderBy(m => m.JoinedAt)
                    .ToListAsync(ct);

                if (!remaining.Any())
                    group.Status = StudyGroupStatus.Archived;
                else if (!remaining.Any(m => m.UserId == group.CreatorId))
                    group.CreatorId = remaining.First().UserId;

                await _hub.Clients.Group($"group-{groupId}").SendAsync("GroupUpdated", cancellationToken: ct);
            }

            await _db.SaveChangesAsync(ct);

            // Tell each removed student directly why — not just a group
            // that silently changed underneath them.
            foreach (var (userId, _, groupName, courseCode) in removals)
            {
                await _notifications.NotifyAsync(
                    userId,
                    "Removed from a study group",
                    $"You were removed from \"{groupName}\" because you're no longer enrolled in {courseCode}.",
                    "/StudyGroups");
            }
        }
    }
}
