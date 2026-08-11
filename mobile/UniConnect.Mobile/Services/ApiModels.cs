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

    /// <summary>0–1 for the ProgressBar standing in for .sg-progress.</summary>
    public double MemberProgress =>
        MaxMembers > 0 ? Math.Clamp((double)ApprovedCount / MaxMembers, 0, 1) : 0;

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
