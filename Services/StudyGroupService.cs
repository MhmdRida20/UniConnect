using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Hubs;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// Every Study Groups business rule, in one place, so the web controller and
    /// the mobile API run identical logic rather than two implementations that
    /// drift apart. Same pattern as AttendanceSubmissionService.
    ///
    /// This layer returns outcomes, never IActionResult — the web controller
    /// turns them into redirects with TempData, the API turns them into status
    /// codes with JSON. Neither re-decides anything.
    ///
    /// The permission flags on <see cref="StudyGroupDetail"/> (AmCreator, CanJoin,
    /// CanPost) are computed HERE for the same reason: a client that re-derives
    /// them from the member list will eventually disagree with the server.
    /// </summary>
    public class StudyGroupService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUniversityProviderResolver _providerResolver;
        private readonly IHubContext<StudyGroupHub> _hub;
        private readonly NotificationService _notifications;
        private readonly AuditLogService _auditLog;

        public StudyGroupService(
            ApplicationDbContext db,
            IUniversityProviderResolver providerResolver,
            IHubContext<StudyGroupHub> hub,
            NotificationService notifications,
            AuditLogService auditLog)
        {
            _db = db;
            _providerResolver = providerResolver;
            _hub = hub;
            _notifications = notifications;
            _auditLog = auditLog;
        }

        // ---------- outcome plumbing ----------

        public enum Outcome
        {
            Success,
            NotFound,
            Forbidden,
            /// <summary>A rule said no. Message is user-facing and identical on both clients.</summary>
            Refused,
            /// <summary>Optimistic concurrency lost — the caller should re-read and retry.</summary>
            Concurrency
        }

        public record Result(Outcome Outcome, string? Message = null, string? Code = null)
        {
            public bool Ok => Outcome == Outcome.Success;
            public static Result Success(string? message = null) => new(Outcome.Success, message);
            public static Result NotFound() => new(Outcome.NotFound);
            public static Result Forbidden() => new(Outcome.Forbidden);
            public static Result Refused(string message, string code) => new(Outcome.Refused, message, code);
            public static Result Concurrency(string message) =>
                new(Outcome.Concurrency, message, "CONCURRENCY_RETRY");
        }

        /// <summary>A field-level validation failure, so the web can populate ModelState.</summary>
        public record FieldError(string Field, string Message);

        // ---------- broadcasts ----------

        private Task BroadcastGroupUpdated(int groupId)
            => _hub.Clients.Group($"group-{groupId}").SendAsync("GroupUpdated");

        private Task BroadcastListChanged()
            => _hub.Clients.Group("study-groups-lobby").SendAsync("StudyGroupListChanged");

        // ---------- read ----------

        /// <summary>
        /// Groups for courses this student is enrolled in, at their own
        /// university, excluding Archived. Course codes can coincide across
        /// universities now that each has its own catalog, which is why
        /// UniversityCode is part of the filter and not just the course code.
        /// </summary>
        public async Task<List<StudyGroup>> GetVisibleGroupsAsync(ApplicationUser user, string? courseCode = null)
        {
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            var myCourses = await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);
            var myCourseCodes = myCourses.Select(c => c.CourseCode).ToList();

            var query = _db.StudyGroups
                .Include(g => g.Course)
                .Include(g => g.Creator)
                .Include(g => g.Members)
                .Where(g => g.UniversityCode == user.UniversityCode
                            && myCourseCodes.Contains(g.CourseCode)
                            && g.Status != StudyGroupStatus.Archived);

            if (!string.IsNullOrWhiteSpace(courseCode))
                query = query.Where(g => g.CourseCode == courseCode);

            return await query.OrderByDescending(g => g.CreatedAt).ToListAsync();
        }

        public async Task<List<UniversityCourseDto>> GetMyCoursesAsync(ApplicationUser user)
        {
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            return await provider.GetEnrolledCoursesAsync(user.UniversityCode, user.UniversityId);
        }

        /// <summary>Everything one group's screen needs, including the caller's own state.</summary>
        public record StudyGroupDetail(
            StudyGroup Group,
            StudyGroupMember? MyMembership,
            bool AmCreator,
            bool CanJoin,
            bool CanPost,
            int ApprovedCount);

        /// <summary>
        /// Loads a group with the caller's permissions resolved. Chat history is
        /// deliberately NOT included — see GetMessagesAsync, which pages it.
        /// </summary>
        public async Task<(Result Result, StudyGroupDetail? Detail)> GetDetailAsync(ApplicationUser user, int id)
        {
            var group = await _db.StudyGroups
                .Include(g => g.Course)
                .Include(g => g.Creator)
                .Include(g => g.Members).ThenInclude(m => m.User)
                .FirstOrDefaultAsync(g => g.Id == id);

            if (group is null) return (Result.NotFound(), null);

            // A group belongs to exactly one university — never let a match on
            // course code alone leak across universities.
            if (group.UniversityCode != user.UniversityCode)
                return (Result.Refused("This group doesn't belong to your university.", "CROSS_UNIVERSITY"), null);

            // FR-49 — must be enrolled in the course to view.
            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            if (!await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, group.CourseCode))
                return (Result.Refused("You must be enrolled in the course to view this group.", "NOT_ENROLLED"), null);

            var myMembership = group.Members.FirstOrDefault(m => m.UserId == user.Id);
            var approvedCount = group.Members.Count(m => m.Status == MembershipStatus.Approved);

            var canJoin = myMembership is null
                          && group.Status != StudyGroupStatus.Archived
                          && approvedCount < group.MaxMembers;

            return (Result.Success(), new StudyGroupDetail(
                group,
                myMembership,
                AmCreator: group.CreatorId == user.Id,
                CanJoin: canJoin,
                CanPost: myMembership?.Status == MembershipStatus.Approved,
                ApprovedCount: approvedCount));
        }

        /// <summary>
        /// Paged newest-first, because Details used to load every message a group
        /// had ever produced. Pass the oldest id already held as `before` to walk
        /// backwards.
        /// </summary>
        public async Task<(Result Result, List<StudyGroupMessage> Messages)> GetMessagesAsync(
            ApplicationUser user, int groupId, int? before = null, int take = 30)
        {
            take = Math.Clamp(take, 1, 100);

            var isMember = await _db.StudyGroupMembers.AnyAsync(
                m => m.StudyGroupId == groupId && m.UserId == user.Id
                  && m.Status == MembershipStatus.Approved);
            if (!isMember) return (Result.Forbidden(), new List<StudyGroupMessage>());

            var query = _db.StudyGroupMessages
                .Include(m => m.Sender)
                .Where(m => m.StudyGroupId == groupId);

            if (before is not null) query = query.Where(m => m.Id < before);

            var messages = await query
                .OrderByDescending(m => m.Id)
                .Take(take)
                .ToListAsync();

            return (Result.Success(), messages);
        }

        // ---------- create ----------

        public record CreateRequest(
            string GroupName, string? Description, string CourseCode,
            int MaxMembers, int MinMembers, string? MeetingLocation);

        /// <summary>
        /// Validates and creates. Field errors come back as a list so the web can
        /// push them into ModelState and the API can return them as a 400 body —
        /// same rules, same messages, two presentations.
        /// </summary>
        public async Task<(List<FieldError> Errors, StudyGroup? Group)> CreateAsync(
            ApplicationUser user, CreateRequest request)
        {
            var errors = new List<FieldError>();

            // Field-shape rules. The web form never reaches the service with
            // these violated, because StudyGroupCreateVM's annotations fail
            // ModelState first — but the mobile API binds a plain DTO with no
            // annotations, so without these a request with a blank name would
            // create a nameless group. Both callers now pass through the same
            // checks, which is the point of this class.
            var name = request.GroupName?.Trim() ?? string.Empty;
            if (name.Length == 0)
                errors.Add(new FieldError(nameof(request.GroupName), "Group name is required."));
            else if (name.Length > 100)
                errors.Add(new FieldError(nameof(request.GroupName), "Group name cannot exceed 100 characters."));

            if (string.IsNullOrWhiteSpace(request.CourseCode))
                errors.Add(new FieldError(nameof(request.CourseCode), "Please choose a course."));

            if ((request.Description?.Trim().Length ?? 0) > 500)
                errors.Add(new FieldError(nameof(request.Description), "Description cannot exceed 500 characters."));

            if ((request.MeetingLocation?.Trim().Length ?? 0) > 100)
                errors.Add(new FieldError(nameof(request.MeetingLocation), "Meeting location cannot exceed 100 characters."));

            if (request.MinMembers is < 2 or > 50)
                errors.Add(new FieldError(nameof(request.MinMembers), "Minimum members must be between 2 and 50."));

            if (request.MaxMembers is < 2 or > 50)
                errors.Add(new FieldError(nameof(request.MaxMembers), "Maximum members must be between 2 and 50."));

            // Stop here rather than asking the adapter whether the student is
            // enrolled in a course code we already know is missing.
            if (errors.Count > 0) return (errors, null);

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);

            // E1 of FR-46: must be enrolled in the course.
            if (!await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, request.CourseCode))
                errors.Add(new FieldError(nameof(request.CourseCode), "You are not enrolled in this course."));

            if (request.MinMembers > request.MaxMembers)
                errors.Add(new FieldError(nameof(request.MinMembers), "Minimum members cannot exceed maximum members."));

            // FR-11: the university's ceiling. A student may choose a SMALLER max
            // for their own group, never a larger one. This is a database lookup,
            // not a validation attribute — easy to miss when porting.
            var settings = await _db.UniversitySettings.FindAsync(user.UniversityCode);
            var maxAllowed = settings?.MaxStudyGroupMembers ?? 10;
            if (request.MaxMembers > maxAllowed)
                errors.Add(new FieldError(nameof(request.MaxMembers),
                    $"Your university caps study groups at {maxAllowed} members."));

            // StudyGroup has a composite FK to Courses (UniversityCode, CourseCode).
            // The adapter can confirm enrollment while UniversityApiSyncRunner has
            // not yet mirrored the course row locally, in which case the insert
            // fails at the database as a 500 long after validation passed.
            else if (errors.Count == 0 &&
                     !await _db.Courses.AnyAsync(c => c.UniversityCode == user.UniversityCode
                                                   && c.CourseCode == request.CourseCode))
                errors.Add(new FieldError(nameof(request.CourseCode),
                    "That course hasn't finished syncing from your university yet — please try again shortly."));

            if (errors.Count > 0) return (errors, null);

            var group = new StudyGroup
            {
                // Already trimmed and length-checked above.
                GroupName = name,
                Description = request.Description?.Trim(),
                // Taken from the creator, never from the client.
                UniversityCode = user.UniversityCode,
                CourseCode = request.CourseCode,
                CreatorId = user.Id,
                MaxMembers = request.MaxMembers,
                MinMembers = request.MinMembers,
                MeetingLocation = request.MeetingLocation?.Trim(),
                Status = StudyGroupStatus.Active,
                CreatedAt = DateTime.UtcNow
            };
            _db.StudyGroups.Add(group);
            await _db.SaveChangesAsync();

            // The creator is a member from the outset, already approved.
            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = group.Id,
                UserId = user.Id,
                Status = MembershipStatus.Approved,
                JoinedAt = DateTime.UtcNow
            });
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(
                "StudyGroupCreated", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "StudyGroup", entityId: group.Id.ToString(),
                details: $"{group.GroupName} ({group.CourseCode})");

            await BroadcastListChanged();
            return (errors, group);
        }

        // ---------- membership ----------

        public async Task<Result> JoinAsync(ApplicationUser user, int groupId)
        {
            var group = await _db.StudyGroups.Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == groupId);
            if (group is null) return Result.NotFound();

            if (group.UniversityCode != user.UniversityCode)
                return Result.Refused("This group doesn't belong to your university.", "CROSS_UNIVERSITY");

            var provider = await _providerResolver.GetProviderAsync(user.UniversityCode);
            if (!await provider.IsEnrolledAsync(user.UniversityCode, user.UniversityId, group.CourseCode))
                return Result.Refused("You are not enrolled in this course.", "NOT_ENROLLED");

            var existing = group.Members.FirstOrDefault(m => m.UserId == user.Id);
            if (existing is not null)
            {
                return existing.Status switch
                {
                    MembershipStatus.Approved => Result.Refused("You are already in this group.", "ALREADY_MEMBER"),
                    MembershipStatus.Pending => Result.Refused("You already have a pending request for this group.", "ALREADY_PENDING"),
                    _ => Result.Refused("You can't request to join right now.", "CANNOT_JOIN")
                };
            }

            // E1 — no point creating a request for a group that is already full.
            var approvedCount = group.Members.Count(m => m.Status == MembershipStatus.Approved);
            if (approvedCount >= group.MaxMembers)
            {
                group.Status = StudyGroupStatus.Full;
                await _db.SaveChangesAsync();
                return Result.Refused("This study group is already full.", "GROUP_FULL");
            }

            _db.StudyGroupMembers.Add(new StudyGroupMember
            {
                StudyGroupId = groupId,
                UserId = user.Id,
                Status = MembershipStatus.Pending,
                JoinedAt = DateTime.UtcNow
            });

            // A new request is activity — wake the group back up if it had gone quiet.
            if (group.Status == StudyGroupStatus.Inactive)
                group.Status = StudyGroupStatus.Active;

            await _db.SaveChangesAsync();
            await BroadcastGroupUpdated(groupId);
            await BroadcastListChanged();

            await _notifications.NotifyAsync(
                group.CreatorId, "New join request",
                $"{user.FullName} wants to join \"{group.GroupName}\".",
                $"/StudyGroups/Details/{groupId}");

            return Result.Success("Your request to join has been sent to the group creator.");
        }

        public async Task<(Result Result, int GroupId)> ApproveMemberAsync(ApplicationUser user, int memberId)
        {
            var membership = await _db.StudyGroupMembers.Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return (Result.NotFound(), 0);

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return (Result.Forbidden(), group.Id);

            if (membership.Status != MembershipStatus.Pending)
                return (Result.Refused("That request is no longer pending.", "NOT_PENDING"), group.Id);

            var approvedCount = await _db.StudyGroupMembers.CountAsync(
                m => m.StudyGroupId == group.Id && m.Status == MembershipStatus.Approved);
            if (approvedCount >= group.MaxMembers)
                return (Result.Refused("The group is already full — reject or remove someone first.", "GROUP_FULL"), group.Id);

            membership.Status = MembershipStatus.Approved;
            if (approvedCount + 1 >= group.MaxMembers)
                group.Status = StudyGroupStatus.Full;
            else if (group.Status == StudyGroupStatus.Inactive)
                group.Status = StudyGroupStatus.Active;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                // StudyGroup carries a [Timestamp] RowVersion precisely for this:
                // two approvals racing for the last seat must not both succeed.
                // Don't guess at the outcome — ask the caller to retry against
                // current data. The mobile client must surface this, not swallow
                // it or auto-retry.
                return (Result.Concurrency(
                    "This group changed while you were approving that request — please check the group and try again."),
                    group.Id);
            }

            await _auditLog.LogAsync(
                "StudyGroupMembershipApproved", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "StudyGroup", entityId: group.Id.ToString(),
                details: $"Member: {membership.UserId}");

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            await _notifications.NotifyAsync(
                membership.UserId, "Study group request approved",
                $"You're now a member of \"{group.GroupName}\".",
                $"/StudyGroups/Details/{group.Id}");

            return (Result.Success("Member approved."), group.Id);
        }

        public async Task<(Result Result, int GroupId)> RejectMemberAsync(ApplicationUser user, int memberId)
        {
            var membership = await _db.StudyGroupMembers.Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return (Result.NotFound(), 0);

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return (Result.Forbidden(), group.Id);

            if (membership.Status != MembershipStatus.Pending)
                return (Result.Refused("That request is no longer pending.", "NOT_PENDING"), group.Id);

            // Removed outright rather than leaving a Rejected row behind — the
            // requester is free to ask again later.
            var rejectedUserId = membership.UserId;
            _db.StudyGroupMembers.Remove(membership);
            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            await _notifications.NotifyAsync(
                rejectedUserId, "Study group request declined",
                $"Your request to join \"{group.GroupName}\" was declined.",
                "/StudyGroups/Index");

            return (Result.Success("Request rejected."), group.Id);
        }

        public async Task<(Result Result, int GroupId)> RemoveMemberAsync(ApplicationUser user, int memberId)
        {
            var membership = await _db.StudyGroupMembers.Include(m => m.StudyGroup)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return (Result.NotFound(), 0);

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return (Result.Forbidden(), group.Id);

            if (membership.UserId == group.CreatorId)
                return (Result.Refused(
                    "The creator can't remove themselves this way — transfer leadership first, or leave the group.",
                    "CREATOR_SELF_REMOVE"), group.Id);

            _db.StudyGroupMembers.Remove(membership);

            // A seat just opened up — the group can't still be Full.
            if (group.Status == StudyGroupStatus.Full)
                group.Status = StudyGroupStatus.Active;

            await _db.SaveChangesAsync();
            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            return (Result.Success("Member removed from the group."), group.Id);
        }

        public async Task<(Result Result, int GroupId)> TransferLeadershipAsync(ApplicationUser user, int memberId)
        {
            var membership = await _db.StudyGroupMembers
                .Include(m => m.StudyGroup).Include(m => m.User)
                .FirstOrDefaultAsync(m => m.Id == memberId);
            if (membership?.StudyGroup is null) return (Result.NotFound(), 0);

            var group = membership.StudyGroup;
            if (group.CreatorId != user.Id) return (Result.Forbidden(), group.Id);

            if (membership.Status != MembershipStatus.Approved)
                return (Result.Refused("Only an approved member can become the group leader.", "NOT_APPROVED"), group.Id);

            group.CreatorId = membership.UserId;
            await _db.SaveChangesAsync();

            await BroadcastGroupUpdated(group.Id);
            await BroadcastListChanged();

            return (Result.Success(
                $"Leadership transferred to {membership.User?.FullName ?? "the selected member"}."), group.Id);
        }

        /// <summary>
        /// Leaves a group, or withdraws a pending request — the same "end my
        /// relationship with this group" action either way, but with different
        /// consequences and different messages.
        /// </summary>
        /// <summary>
        /// Deletes a group. Only the creator may do it.
        ///
        /// The group is archived rather than physically removed: StudyGroup is
        /// the parent of its members and its whole message history, so a real
        /// delete would take an entire conversation with it and leave the audit
        /// trail pointing at a row that no longer exists. Archived is already
        /// how this codebase retires a group — LeaveAsync archives one when its
        /// last member leaves — and GetVisibleGroupsAsync already filters
        /// archived groups out of browse, so nothing else has to change for it
        /// to disappear.
        /// </summary>
        public async Task<Result> DeleteAsync(ApplicationUser user, int groupId)
        {
            var group = await _db.StudyGroups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group is null) return Result.NotFound();

            // Never let one university's student touch another's group.
            if (group.UniversityCode != user.UniversityCode) return Result.NotFound();

            if (group.CreatorId != user.Id) return Result.Forbidden();

            if (group.Status == StudyGroupStatus.Archived)
                return Result.Refused("This group has already been deleted.", "ALREADY_DELETED");

            // Everyone who was in it deserves to be told, and the creator's own
            // notification would be noise.
            var membersToNotify = group.Members
                .Where(m => m.Status == MembershipStatus.Approved && m.UserId != user.Id)
                .Select(m => m.UserId)
                .ToList();

            var groupName = group.GroupName;

            group.Status = StudyGroupStatus.Archived;

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Concurrency("Someone else changed this group while you were deleting it. Please try again.");
            }

            foreach (var memberId in membersToNotify)
            {
                await _notifications.NotifyAsync(
                    memberId, "Study group deleted",
                    $"\"{groupName}\" was deleted by its creator.",
                    "/StudyGroups/Index");
            }

            await _auditLog.LogAsync(
                "StudyGroupDeleted", userId: user.Id, universityCode: user.UniversityCode,
                entityType: "StudyGroup", entityId: group.Id.ToString(),
                details: $"{groupName} ({group.CourseCode})");

            await BroadcastGroupUpdated(groupId);
            await BroadcastListChanged();

            return Result.Success("Study group deleted.");
        }

        public async Task<Result> LeaveAsync(ApplicationUser user, int groupId)
        {
            var membership = await _db.StudyGroupMembers
                .FirstOrDefaultAsync(m => m.StudyGroupId == groupId && m.UserId == user.Id);
            if (membership is null) return Result.NotFound();

            var group = await _db.StudyGroups.FindAsync(groupId);
            var wasCreator = group is not null && group.CreatorId == user.Id
                             && membership.Status == MembershipStatus.Approved;
            var wasPendingRequest = membership.Status == MembershipStatus.Pending;

            _db.StudyGroupMembers.Remove(membership);
            await _db.SaveChangesAsync();

            if (group is not null && !wasPendingRequest)
            {
                var remainingApproved = await _db.StudyGroupMembers
                    .Where(m => m.StudyGroupId == groupId && m.Status == MembershipStatus.Approved)
                    .OrderBy(m => m.JoinedAt)
                    .ToListAsync();

                if (!remainingApproved.Any())
                {
                    group.Status = StudyGroupStatus.Archived;
                }
                else if (wasCreator)
                {
                    // Leadership passes to the longest-standing remaining member.
                    group.CreatorId = remainingApproved.First().UserId;
                    if (group.Status == StudyGroupStatus.Full && remainingApproved.Count < group.MaxMembers)
                        group.Status = StudyGroupStatus.Active;
                }
                else if (group.Status == StudyGroupStatus.Full)
                {
                    group.Status = StudyGroupStatus.Active;   // a seat opened up
                }

                await _db.SaveChangesAsync();
            }

            await BroadcastGroupUpdated(groupId);
            await BroadcastListChanged();

            return Result.Success(
                wasPendingRequest ? "Your request was withdrawn."
                : wasCreator ? "You left the group. Leadership was passed to the longest-standing member."
                : "You left the group.");
        }

        // ---------- chat ----------

        public async Task<(Result Result, StudyGroupMessage? Message)> PostMessageAsync(
            ApplicationUser user, int groupId, string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return (Result.Refused("A message can't be empty.", "EMPTY_MESSAGE"), null);

            var trimmed = content.Trim();
            if (trimmed.Length > 1000)
                return (Result.Refused("A message can't be longer than 1000 characters.", "MESSAGE_TOO_LONG"), null);

            var isMember = await _db.StudyGroupMembers.AnyAsync(
                m => m.StudyGroupId == groupId && m.UserId == user.Id
                  && m.Status == MembershipStatus.Approved);
            if (!isMember) return (Result.Forbidden(), null);

            var message = new StudyGroupMessage
            {
                StudyGroupId = groupId,
                SenderId = user.Id,
                Content = trimmed,
                SentAt = DateTime.UtcNow
            };
            _db.StudyGroupMessages.Add(message);

            // A message is activity — wake the group back up if it had gone quiet.
            var group = await _db.StudyGroups.FindAsync(groupId);
            var reactivated = false;
            if (group is not null && group.Status == StudyGroupStatus.Inactive)
            {
                group.Status = StudyGroupStatus.Active;
                reactivated = true;
            }

            await _db.SaveChangesAsync();

            await _hub.Clients.Group($"group-{groupId}").SendAsync("ReceiveMessage", new
            {
                senderName = user.FullName,
                senderId = user.Id,
                content = message.Content,
                // `sentAt` is a server-formatted display string, which was fine
                // when a browser was the only client. A native client can't
                // re-format, localise or sort by it, and it is culture-dependent
                // on the server. sentAtUtc is ADDED rather than replacing it, so
                // wwwroot/js/pages/study-group-details.js keeps working untouched.
                sentAt = message.SentAt.ToString("MMM dd, HH:mm"),
                sentAtUtc = message.SentAt,
                messageId = message.Id
            });

            if (reactivated) await BroadcastListChanged();

            return (Result.Success(), message);
        }
    }
}
