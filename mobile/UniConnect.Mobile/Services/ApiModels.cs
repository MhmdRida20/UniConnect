namespace UniConnect.Mobile.Services;

// Client-side mirrors of the DTOs in Controllers/Api. Kept as plain classes
// with the same property names so System.Text.Json binds them with no
// attributes: ASP.NET Core serialises camelCase, and the deserialiser here is
// configured case-insensitively.
//
// These are deliberately hand-written rather than shared with the web project.
// A project reference would drag EF Core, Identity and the whole server stack
// into the app; the DTOs are small and their shapes are the actual contract.

/// <summary>What POST /api/auth/login returns on success.</summary>
public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string UniversityCode { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

/// <summary>
/// The failure shape used by the auth endpoints: `error` is a machine code and
/// `message` is the sentence to show. Note this is NOT the shape the Study
/// Groups endpoints use — see <see cref="ApiError"/>.
/// </summary>
public class AuthError
{
    public string? Error { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// The failure shape used by the Study Groups endpoints: `error` is the
/// sentence to show and `code` is the machine-readable reason.
/// </summary>
public class ApiError
{
    public string? Error { get; set; }
    public string? Code { get; set; }
}

public class GroupSummary
{
    public int Id { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public string? CourseName { get; set; }
    public string Status { get; set; } = string.Empty;
    public int MaxMembers { get; set; }
    public int MinMembers { get; set; }
    public int ApprovedCount { get; set; }
    public string? MeetingLocation { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatorName { get; set; }

    /// <summary>The caller's membership status, or null if they have none.</summary>
    public string? MyStatus { get; set; }

    public bool AmMember => MyStatus == "Approved";
    public bool AmPending => MyStatus == "Pending";

    // Read-only helpers so the XAML can bind one value instead of stitching
    // strings together in the template.
    public string CourseLine =>
        string.IsNullOrWhiteSpace(CourseName) ? CourseCode : $"{CourseCode} · {CourseName}";

    public string MembersLine => $"{ApprovedCount} / {MaxMembers}";

    public bool HasMeetingLocation => !string.IsNullOrWhiteSpace(MeetingLocation);
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    // Status is sent as the enum's name, so these compare against those names.
    public bool IsFull => Status == "Full";
    public bool IsInactive => Status == "Inactive";

    /// <summary>Deleted, in the sense the app means it — see StudyGroupService.DeleteAsync.</summary>
    public bool IsArchived => Status == "Archived";

    /// <summary>
    /// The card's status pill, following the same three-way choice the web
    /// makes in Views/StudyGroups/Index.cshtml: inactive, full, or seats left.
    /// </summary>
    public string StatusPillText =>
        IsInactive ? "Inactive"
        : IsFull ? "Full"
        : $"{Math.Max(0, MaxMembers - ApprovedCount)} seat{(MaxMembers - ApprovedCount == 1 ? "" : "s")} left";

    /// <summary>0–1 for the seats-taken bar.</summary>
    public double MemberProgress =>
        MaxMembers > 0 ? Math.Clamp((double)ApprovedCount / MaxMembers, 0, 1) : 0;

    /// <summary>
    /// The bar's empty remainder. The bar is a two-column Grid weighted by these
    /// two proportions rather than a ProgressBar, which cannot be given a
    /// height or a corner radius consistently across platforms.
    /// </summary>
    public double MemberRemaining => 1 - MemberProgress;

    public int SeatsLeft => Math.Max(0, MaxMembers - ApprovedCount);

    /// <summary>"Seats left: 2/6", with the count spelled out when there are none.</summary>
    public string SeatsLine =>
        IsFull || SeatsLeft == 0
            ? $"Seats left: 0/{MaxMembers} (Full)"
            : $"Seats left: {SeatsLeft}/{MaxMembers}";

    public string CreatorInitials => Avatar.Initials(CreatorName);

    /// <summary>The first few approved members, newest joiners last.</summary>
    public List<string> MemberNames { get; set; } = new();

    /// <summary>How many avatars the card draws before collapsing the rest.</summary>
    private const int AvatarsShown = 3;

    /// <summary>
    /// Initials for the stacked avatar row.
    ///
    /// Falls back to the creator when the server sends no names, so the row
    /// never collapses to nothing and leaves the card's bottom edge lopsided.
    /// That happens for a group whose only approved member has left: the
    /// creator is still on the group, just no longer in its membership.
    /// </summary>
    public List<string> AvatarInitials =>
        MemberNames.Count > 0
            ? MemberNames.Take(AvatarsShown).Select(Avatar.Initials).ToList()
            : new List<string> { CreatorInitials };

    /// <summary>"+2" for members beyond the ones with an avatar.</summary>
    public string OtherMembersLabel => $"+{ApprovedCount - AvatarsShown}";

    public bool HasOtherMembers => ApprovedCount > AvatarsShown;

    /// <summary>
    /// Filling up and worth a look. Derived rather than editorial — UniConnect
    /// has no ratings, so a "Top Rated" badge would be decoration with nothing
    /// behind it; "Popular" at least means something a student can act on.
    /// </summary>
    public bool IsPopular => !IsFull && ApprovedCount >= 2 && MemberProgress >= 0.7;

    // The card carries one badge. Precedence runs from what the student has
    // done (joined, waiting) to what the group is (full, filling up), with the
    // seat count as the fallback.
    public bool ShowFullPill => !AmMember && !AmPending && IsFull;
    public bool ShowPopularPill => !AmMember && !AmPending && !IsFull && IsPopular;
    public bool ShowSeatPill => !AmMember && !AmPending && !IsFull && !IsPopular;

    public string ViewActionText => AmMember ? "Go to Group" : "View Group";

    public string DescriptionOrPlaceholder =>
        HasDescription ? Description! : "No description provided.";

    public string CreatorLabel => string.IsNullOrWhiteSpace(CreatorName) ? "—" : CreatorName!;

    public string MeetingLocationOrPlaceholder =>
        HasMeetingLocation ? MeetingLocation! : "Not set";

    /// <summary>Everything the list search matches against, mirroring data-search on the web.</summary>
    public string SearchKey =>
        $"{GroupName} {CourseCode} {CourseName} {CreatorName}".ToLowerInvariant();
}

public class MemberDto
{
    public int MemberId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime JoinedAt { get; set; }

    // Filled in by the details page once it knows who is signed in and who
    // created the group — the server sends the same member row to everyone.
    public bool IsSelf { get; set; }
    public bool IsGroupCreator { get; set; }

    /// <summary>Whether to offer "make leader" and "remove" on this row.</summary>
    public bool ShowManageActions { get; set; }

    public string Initials => Avatar.Initials(FullName);

    public string DisplayName => IsSelf ? $"{FullName} (You)" : FullName;

    public string JoinedLabel => $"Joined {ToLocal(JoinedAt):MMM dd}";

    public string RequestedLabel => $"Requested {ToLocal(JoinedAt):MMM dd, HH:mm}";

    // JSON hands back an unspecified-kind DateTime; the server stores UTC.
    private static DateTime ToLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
}

public class MyMembershipDto
{
    public int MemberId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class GroupDetail : GroupSummary
{
    public string CreatorId { get; set; } = string.Empty;
    public MyMembershipDto? MyMembership { get; set; }
    public bool AmCreator { get; set; }
    public bool CanJoin { get; set; }
    public bool CanPost { get; set; }
    public List<MemberDto> Members { get; set; } = new();
    public List<MemberDto> Pending { get; set; } = new();

    /// <summary>
    /// Leaving is offered to approved members who did not create the group —
    /// the creator has to transfer leadership first, which the server enforces.
    /// A pending request is withdrawn instead (same endpoint, different
    /// wording), and a rejected one leaves nothing to do.
    /// </summary>
    public bool CanLeave => AmMember && !AmCreator;
}

public class ActionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class CourseDto
{
    public string CourseCode { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int Credits { get; set; }

    /// <summary>What the course picker shows.</summary>
    public string Display => $"{CourseCode} · {CourseName}";
}

public class MessageDto
{
    public int Id { get; set; }
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime SentAtUtc { get; set; }

    // Set by the client after loading, since only it knows who is signed in.
    public bool IsMine { get; set; }

    /// <summary>
    /// The server sends UTC; students think in their own time. The web renders
    /// "MMM dd, HH:mm" in browser-local time, so this matches it.
    /// </summary>
    public string SentAtLocal =>
        DateTime.SpecifyKind(SentAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("MMM dd, HH:mm");

    public string SenderLabel => IsMine ? "You" : SenderName;

    public string Initials => Avatar.Initials(SenderName);
}

/// <summary>The 400 body from POST /api/study-groups when fields are wrong.</summary>
public class FieldErrorResponse
{
    public string Error { get; set; } = "Please correct the highlighted fields.";
    public List<FieldErrorItem> Fields { get; set; } = new();
}

public class FieldErrorItem
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>What the app sends to create a group.</summary>
public class CreateGroupRequest
{
    public string GroupName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int MaxMembers { get; set; } = 10;
    public int MinMembers { get; set; } = 2;
    public string? MeetingLocation { get; set; }
}

// ===== Notifications =====

/// <summary>
/// One notification. <see cref="WasUnread"/> is the server's answer to "was
/// this new when you opened the list" — fetching the list marks everything in
/// it read, so it cannot be re-derived from the row afterwards.
/// </summary>
public class NotificationDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    /// <summary>A web path such as "/StudyGroups/Details/5", or null.</summary>
    public string? Link { get; set; }

    public DateTime CreatedAt { get; set; }
    public bool WasUnread { get; set; }

    private DateTime LocalCreatedAt =>
        DateTime.SpecifyKind(CreatedAt, DateTimeKind.Utc).ToLocalTime();

    /// <summary>
    /// Relative for anything recent, absolute once it stops being useful to say
    /// "4 days ago" — which is roughly where a student starts wanting the date.
    /// </summary>
    public string WhenLabel
    {
        get
        {
            var age = DateTime.Now - LocalCreatedAt;

            if (age.TotalMinutes < 1) return "Just now";
            if (age.TotalMinutes < 60) return $"{(int)age.TotalMinutes}m ago";
            if (age.TotalHours < 24) return $"{(int)age.TotalHours}h ago";
            if (age.TotalDays < 7) return $"{(int)age.TotalDays}d ago";

            return LocalCreatedAt.ToString("MMM dd, yyyy");
        }
    }

    public bool HasLink => !string.IsNullOrWhiteSpace(Link);
}

/// <summary>The unread badge's count. Reading it does not mark anything read.</summary>
public class UnreadCountDto
{
    public int Count { get; set; }
}

// ===== Internships =====

/// <summary>One listing on the browse screen.</summary>
public class InternshipSummary
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public int? DurationWeeks { get; set; }
    public DateTime ApplicationDeadline { get; set; }
    public int NumberOfPositions { get; set; }
    public string? RequiredSkills { get; set; }
    public string PostingMode { get; set; } = string.Empty;
    public int MatchingScore { get; set; }
    public bool CourseDataAvailable { get; set; }

    /// <summary>The caller's application status, or null if they have not applied.</summary>
    public string? MyApplicationStatus { get; set; }

    public bool HaveApplied => !string.IsNullOrEmpty(MyApplicationStatus);

    /// <summary>Applications the employer takes through their own link or inbox.</summary>
    public bool IsExternal => PostingMode == "ListingOnly";

    public string MatchLabel => $"{MatchingScore}% match";

    /// <summary>
    /// Shown when the student's course history could not be read, which makes
    /// the score weaker than it looks. Saying so beats presenting a number that
    /// reads as authoritative.
    /// </summary>
    public bool ScoreIsPartial => !CourseDataAvailable;

    public string DurationLabel =>
        DurationWeeks is null or <= 0 ? "Duration not stated" : $"{DurationWeeks} weeks";

    public string DeadlineLabel => $"Apply by {ApplicationDeadline:MMM dd, yyyy}";

    /// <summary>
    /// Days left, for the urgency pill. The deadline is a date, so this counts
    /// whole days from today rather than from the current instant.
    /// </summary>
    public int DaysLeft => (ApplicationDeadline.Date - DateTime.Today).Days;

    public bool ClosingSoon => DaysLeft is >= 0 and <= 7;

    public string ClosingSoonLabel => DaysLeft switch
    {
        0 => "Closes today",
        1 => "1 day left",
        _ => $"{DaysLeft} days left"
    };

    public string PositionsLabel =>
        NumberOfPositions == 1 ? "1 position" : $"{NumberOfPositions} positions";

    public bool HasSkills => !string.IsNullOrWhiteSpace(RequiredSkills);

    /// <summary>Required skills as chips; the server stores them comma-separated.</summary>
    public List<string> SkillList =>
        string.IsNullOrWhiteSpace(RequiredSkills)
            ? new List<string>()
            : RequiredSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public string SearchKey => $"{Title} {CompanyName} {Location} {RequiredSkills}".ToLowerInvariant();
}

/// <summary>A listing plus what the caller may do with it.</summary>
public class InternshipDetail : InternshipSummary
{
    public string Description { get; set; } = string.Empty;
    public string? RecommendedCourses { get; set; }
    public string? RelevantMajors { get; set; }
    public string? ExternalApplyUrl { get; set; }
    public string? ExternalApplyEmail { get; set; }

    public bool CanApply { get; set; }
    // The server also sends IsExternal, but the base class already derives it
    // from PostingMode and the two always agree — a read-only property is
    // skipped by the deserialiser, so there is nothing to bind it to anyway.
    public bool PositionsFilled { get; set; }
    public bool DeadlinePassed { get; set; }
    public bool HasCv { get; set; }
    public int SkillCount { get; set; }
    public int? MyApplicationId { get; set; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public bool HasRecommendedCourses => !string.IsNullOrWhiteSpace(RecommendedCourses);
    public bool HasExternalUrl => !string.IsNullOrWhiteSpace(ExternalApplyUrl);
    public bool HasExternalEmail => !string.IsNullOrWhiteSpace(ExternalApplyEmail);

    /// <summary>
    /// Why the apply form is not being offered. Only consulted when CanApply is
    /// false, and ordered the way a student would ask: what I did, then what
    /// happened to the listing.
    /// </summary>
    public string CannotApplyReason =>
        HaveApplied ? $"You applied to this internship. Status: {MyApplicationStatus}."
        : IsExternal ? "This employer takes applications on their own site."
        : DeadlinePassed ? "The application deadline has passed."
        : PositionsFilled ? "All positions have been filled."
        : "This internship is not accepting applications.";

    /// <summary>
    /// A nudge, not a blocker — the server accepts an application either way,
    /// but a CV is what gets forwarded if the employer shortlists you.
    /// </summary>
    public bool ShouldPromptForCv => CanApply && !HasCv;
}

/// <summary>One row on "My applications".</summary>
public class ApplicationSummary
{
    public int Id { get; set; }
    public int InternshipId { get; set; }
    public string InternshipTitle { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? MatchingScore { get; set; }
    public DateTime AppliedAt { get; set; }
    public string? CoverMessage { get; set; }
    public bool CanWithdraw { get; set; }

    public string AppliedLabel =>
        $"Applied {DateTime.SpecifyKind(AppliedAt, DateTimeKind.Utc).ToLocalTime():MMM dd, yyyy}";

    public string ScoreLabel => MatchingScore is null ? "—" : $"{MatchingScore}% match";

    public bool HasCoverMessage => !string.IsNullOrWhiteSpace(CoverMessage);

    // Status drives the pill's colour. The names come from the server's enum.
    public bool IsAccepted => Status == "Accepted";
    public bool IsRejected => Status == "Rejected";
    public bool IsWithdrawn => Status == "Withdrawn";
    public bool IsShortlisted => Status == "Shortlisted";
    public bool IsInProgress => !IsAccepted && !IsRejected && !IsWithdrawn;
}

/// <summary>
/// Two-letter monograms for member and message avatars — the same rule as the
/// Initials() local function in Views/StudyGroups/Details.cshtml.
/// </summary>
public static class Avatar
{
    public static string Initials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? parts[0][..1].ToUpperInvariant()
            : (parts[0][..1] + parts[^1][..1]).ToUpperInvariant();
    }
}
