using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    /// <summary>
    /// The student-facing half of Internships & Career Matching (UC-07):
    /// browse, view, apply, track and withdraw.
    ///
    /// The rules used to live in InternshipsController. They moved here when the
    /// mobile app needed the same feature: an API controller that re-implemented
    /// the deadline, duplicate, positions-filled and cross-university guards
    /// would be six chances for the two clients to disagree about who may apply
    /// to what. Both controllers now call this and only translate the outcome.
    /// </summary>
    public class InternshipService
    {
        private readonly ApplicationDbContext _db;
        private readonly MatchingScoreService _matching;
        private readonly NotificationService _notifications;
        private readonly AuditLogService _auditLog;
        private readonly IUniversityProviderResolver _resolver;
        private readonly ILogger<InternshipService> _logger;

        public InternshipService(
            ApplicationDbContext db,
            MatchingScoreService matching,
            NotificationService notifications,
            AuditLogService auditLog,
            IUniversityProviderResolver resolver,
            ILogger<InternshipService> logger)
        {
            _db = db;
            _matching = matching;
            _notifications = notifications;
            _auditLog = auditLog;
            _resolver = resolver;
            _logger = logger;
        }

        // ---------- outcomes ----------

        public enum Outcome { Success, NotFound, Refused }

        public record Result(Outcome Outcome, string? Message = null, string? Code = null)
        {
            public bool Ok => Outcome == Outcome.Success;
            public static Result Success(string? message = null) => new(Outcome.Success, message);
            public static Result NotFound() => new(Outcome.NotFound);
            public static Result Refused(string message, string code) => new(Outcome.Refused, message, code);
        }

        /// <summary>An internship plus everything the caller needs to render it.</summary>
        public record ScoredInternship(Internship Internship, int Score, bool CourseDataAvailable);

        /// <summary>What a student may currently do with one listing.</summary>
        public record InternshipDetail(
            Internship Internship,
            int Score,
            bool CourseDataAvailable,
            InternshipApplication? MyApplication,
            bool PositionsFilled,
            bool DeadlinePassed,
            bool HasCv,
            int SkillCount)
        {
            /// <summary>
            /// Whether the in-app apply form should be offered at all. Mirrors
            /// exactly what ApplyAsync will accept, so the UI never shows a
            /// button that is guaranteed to be refused.
            /// </summary>
            public bool CanApply =>
                MyApplication is null
                && Internship.IsActive
                && Internship.PostingMode == InternshipPostingMode.FullApplication
                && !PositionsFilled
                && !DeadlinePassed;

            /// <summary>Listings the employer handles through their own link or inbox.</summary>
            public bool IsExternal => Internship.PostingMode == InternshipPostingMode.ListingOnly;
        }

        // ---------- browse ----------

        /// <summary>
        /// Live listings for the student's university, scored and ordered best
        /// first. Ties break on recency, matching the web's ordering.
        /// </summary>
        public async Task<List<ScoredInternship>> BrowseAsync(
            ApplicationUser user,
            string? skill = null,
            string? location = null,
            int? maxDuration = null,
            bool myMajorOnly = false)
        {
            var query = _db.Internships
                .Include(i => i.Company)
                .Where(i => i.IsActive && i.ApplicationDeadline >= DateTime.Today
                            && i.Company!.UniversityCode == user.UniversityCode);

            if (!string.IsNullOrWhiteSpace(skill))
                query = query.Where(i => i.RequiredSkills != null && i.RequiredSkills.Contains(skill));
            if (!string.IsNullOrWhiteSpace(location))
                query = query.Where(i => i.Location.Contains(location));
            if (maxDuration.HasValue)
                query = query.Where(i => i.DurationWeeks == null || i.DurationWeeks <= maxDuration);

            var internships = await query.ToListAsync();

            // Both are fetched once for the whole request, never inside the
            // scoring loop below: the student's major cannot change between two
            // listings, and the corpus is shared by every score.
            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();

            if (myMajorOnly && !string.IsNullOrWhiteSpace(studentMajor))
                internships = internships.Where(i => IsRelevantTo(i, studentMajor)).ToList();

            var scored = new List<ScoredInternship>();
            foreach (var internship in internships)
            {
                var result = await _matching.CalculateAsync(user, internship, studentMajor, corpus);
                scored.Add(new ScoredInternship(internship, result.Score, result.CourseDataAvailable));
            }

            return scored
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Internship.CreatedAt)
                .ToList();
        }

        /// <summary>
        /// Whether "my major only" should keep this listing. A posting that
        /// names no majors is open to everyone, so it stays — the filter is
        /// meant to hide postings aimed at OTHER majors, not open ones.
        /// </summary>
        private static bool IsRelevantTo(Internship internship, string studentMajor)
        {
            if (string.IsNullOrWhiteSpace(internship.RelevantMajors)) return true;

            var majors = internship.RelevantMajors
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return majors.Length == 0
                || majors.Any(m => string.Equals(m, studentMajor, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// True when the student's best match is poor and their career profile
        /// is thin — the case where the useful advice is "fill in your profile"
        /// rather than "no results".
        /// </summary>
        public async Task<bool> ShouldSuggestProfileUpdateAsync(ApplicationUser user, List<ScoredInternship> scored)
        {
            if (scored.Count == 0 || scored.Max(x => x.Score) >= 20) return false;

            var hasProfile = await _db.CareerProfiles.AnyAsync(p => p.UserId == user.Id);
            var hasSkills = await _db.StudentSkills.AnyAsync(s => s.UserId == user.Id);
            return !hasProfile || !hasSkills;
        }

        // ---------- one listing ----------

        /// <summary>
        /// A listing with the caller's standing on it, or null if it does not
        /// exist or belongs to another university. The two are deliberately
        /// indistinguishable: another university's postings should not be
        /// discoverable by probing ids.
        /// </summary>
        public async Task<InternshipDetail?> GetDetailAsync(ApplicationUser user, int internshipId)
        {
            var internship = await _db.Internships
                .Include(i => i.Company)
                .FirstOrDefaultAsync(i => i.Id == internshipId);

            if (internship is null || internship.Company?.UniversityCode != user.UniversityCode)
                return null;

            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();
            var score = await _matching.CalculateAsync(user, internship, studentMajor, corpus);

            var mine = await _db.InternshipApplications
                .FirstOrDefaultAsync(a => a.InternshipId == internshipId && a.UserId == user.Id);

            var accepted = await _db.InternshipApplications.CountAsync(
                a => a.InternshipId == internshipId && a.Status == InternshipApplicationStatus.Accepted);

            var careerProfile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id);
            var skillCount = await _db.StudentSkills.CountAsync(s => s.UserId == user.Id);

            return new InternshipDetail(
                internship,
                score.Score,
                score.CourseDataAvailable,
                mine,
                PositionsFilled: accepted >= internship.NumberOfPositions,
                DeadlinePassed: internship.ApplicationDeadline < DateTime.Today,
                HasCv: !string.IsNullOrWhiteSpace(careerProfile?.CvFilePath),
                SkillCount: skillCount);
        }

        // ---------- apply ----------

        /// <summary>
        /// Applies on the student's behalf. Every refusal carries a stable code
        /// so a client can react without matching on English.
        /// </summary>
        public async Task<(Result Result, InternshipApplication? Application)> ApplyAsync(
            ApplicationUser user, int internshipId, string? coverMessage)
        {
            var internship = await _db.Internships
                .Include(i => i.Company)
                .FirstOrDefaultAsync(i => i.Id == internshipId);

            if (internship is null || internship.Company?.UniversityCode != user.UniversityCode)
                return (Result.NotFound(), null);

            if (internship.PostingMode == InternshipPostingMode.ListingOnly)
                return (Result.Refused(
                    "This listing accepts applications through the employer's own link/email, not in UniConnect.",
                    "EXTERNAL_LISTING"), null);

            if (!internship.IsActive)
                return (Result.Refused("This internship is no longer accepting applications.", "INACTIVE"), null);

            if (internship.ApplicationDeadline < DateTime.Today)
                return (Result.Refused("The application deadline for this internship has passed.", "DEADLINE_PASSED"), null);

            if (await _db.InternshipApplications.AnyAsync(a => a.InternshipId == internshipId && a.UserId == user.Id))
                return (Result.Refused("You've already applied to this internship.", "ALREADY_APPLIED"), null);

            var accepted = await _db.InternshipApplications.CountAsync(
                a => a.InternshipId == internshipId && a.Status == InternshipApplicationStatus.Accepted);
            if (accepted >= internship.NumberOfPositions)
                return (Result.Refused("All positions for this internship have already been filled.", "POSITIONS_FILLED"), null);

            var studentMajor = await GetStudentMajorAsync(user);
            var corpus = await _matching.BuildCorpusAsync();
            var score = await _matching.CalculateAsync(user, internship, studentMajor, corpus);

            var application = new InternshipApplication
            {
                InternshipId = internshipId,
                UserId = user.Id,
                CoverMessage = string.IsNullOrWhiteSpace(coverMessage) ? null : coverMessage.Trim(),
                MatchingScore = score.Score,
                Status = InternshipApplicationStatus.Submitted,
                AppliedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            try
            {
                _db.InternshipApplications.Add(application);
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // The unique index caught a race between two near-simultaneous
                // submits that the check above could not see.
                return (Result.Refused("You've already applied to this internship.", "ALREADY_APPLIED"), null);
            }

            if (internship.Company is not null)
            {
                await _notifications.NotifyAsync(
                    internship.Company.UserId,
                    "New internship application",
                    $"{user.FullName} applied to \"{internship.Title}\" (matching score: {score.Score}).",
                    "/Company/Applications?internshipId=" + internshipId);
            }

            await _auditLog.LogAsync(
                "InternshipApplicationSubmitted",
                userId: user.Id,
                universityCode: user.UniversityCode,
                entityType: "InternshipApplication",
                entityId: application.Id.ToString(),
                details: $"Internship: {internship.Title}, score: {score.Score}");

            return (Result.Success("Application submitted!"), application);
        }

        // ---------- track ----------

        public async Task<List<InternshipApplication>> MyApplicationsAsync(ApplicationUser user) =>
            await _db.InternshipApplications
                .Include(a => a.Internship).ThenInclude(i => i!.Company)
                .Where(a => a.UserId == user.Id)
                .OrderByDescending(a => a.AppliedAt)
                .ToListAsync();

        /// <summary>
        /// Statuses a student can no longer walk back from. Accepted and
        /// Rejected are the employer's decision, and Withdrawn is already done.
        /// </summary>
        private static readonly InternshipApplicationStatus[] Terminal =
        {
            InternshipApplicationStatus.Accepted,
            InternshipApplicationStatus.Rejected,
            InternshipApplicationStatus.Withdrawn
        };

        public async Task<Result> WithdrawAsync(ApplicationUser user, int applicationId)
        {
            var application = await _db.InternshipApplications
                .FirstOrDefaultAsync(a => a.Id == applicationId && a.UserId == user.Id);

            if (application is null) return Result.NotFound();

            if (Terminal.Contains(application.Status))
                return Result.Refused("This application can no longer be withdrawn.", "NOT_WITHDRAWABLE");

            application.Status = InternshipApplicationStatus.Withdrawn;
            application.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Result.Success("Application withdrawn.");
        }

        // ---------- shared ----------

        /// <summary>
        /// The student's major, or null when it is unknown. Null is treated as
        /// neutral everywhere it is used — never as a penalty — so an adapter
        /// failure degrades the score rather than breaking the page.
        /// </summary>
        public async Task<string?> GetStudentMajorAsync(ApplicationUser user)
        {
            try
            {
                var provider = await _resolver.GetProviderAsync(user.UniversityCode);
                var info = await provider.GetStudentInfoAsync(user.UniversityCode, user.UniversityId);
                return info?.Major;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not retrieve major for {User} — major-based matching will be neutral for this request.",
                    user.Id);
                return null;
            }
        }
    }
}
