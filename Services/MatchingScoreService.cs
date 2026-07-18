using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    public record MatchingScoreResult(int Score, bool CourseDataAvailable);

    /// <summary>
    /// FR-41: "The system shall calculate a matching score for each
    /// student-internship pair," based on: student skills vs. required
    /// skills, completed courses vs. recommended courses, declared major vs.
    /// relevant majors, career interests vs. internship field, and location
    /// preferences.
    ///
    /// Weighted 0-100: Skills 35, Courses 25, Major 20, Interests 10,
    /// Location 10.
    ///
    /// Edge Case ("Adapter unavailable for course data"): if the adapter
    /// call for COURSES fails, that component is dropped and the remaining
    /// weights are scaled up proportionally to still sum to 100 — a
    /// "partial matching score excluding courses," not a broken/zero one.
    ///
    /// Major is handled differently on purpose: a student with no major on
    /// file (undeclared, or the adapter simply doesn't have it) is treated
    /// as NEUTRAL — full credit for that component, same as "no skills were
    /// required" — rather than penalized or flagged. This is a deliberate
    /// choice: a missing major is a normal, common state (e.g. a first-year
    /// undeclared student), not a system failure worth surfacing to anyone.
    /// The caller (InternshipsController) is responsible for fetching the
    /// student's major ONCE per request via the adapter and passing it in —
    /// not re-fetched here — since this method is called once PER LISTING
    /// on the browse page, and re-fetching the same student's major on every
    /// single one of those calls would be a redundant, wasteful adapter call.
    /// </summary>
    public class MatchingScoreService
    {
        private readonly ApplicationDbContext _db;
        private readonly IUniversityProviderResolver _resolver;
        private readonly ILogger<MatchingScoreService> _logger;

        private const int SkillsWeight = 35;
        private const int CoursesWeight = 25;
        private const int MajorWeight = 20;
        private const int InterestsWeight = 10;
        private const int LocationWeight = 10;
        // SkillsWeight + CoursesWeight + MajorWeight + InterestsWeight + LocationWeight == 100

        public MatchingScoreService(
            ApplicationDbContext db, IUniversityProviderResolver resolver, ILogger<MatchingScoreService> logger)
        {
            _db = db;
            _resolver = resolver;
            _logger = logger;
        }

        /// <param name="studentMajor">
        /// The student's declared major, or null if they have none on file
        /// (or it couldn't be retrieved) — the caller fetches this once via
        /// IUniversityProvider.GetStudentInfoAsync and passes it in.
        /// </param>
        public async Task<MatchingScoreResult> CalculateAsync(ApplicationUser student, Internship internship, string? studentMajor)
        {
            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == student.Id);
            var skills = await _db.StudentSkills.Where(s => s.UserId == student.Id).ToListAsync();

            // ---------- Skills ----------
            var requiredSkills = ParseList(internship.RequiredSkills);
            double skillsScore;
            if (requiredSkills.Count > 0)
            {
                var studentSkillNames = skills.Select(s => s.SkillName.Trim().ToLowerInvariant()).ToHashSet();
                var matched = requiredSkills.Count(rs => studentSkillNames.Contains(rs.ToLowerInvariant()));
                skillsScore = (double)matched / requiredSkills.Count;
            }
            else
            {
                skillsScore = 1; // no specific skills required — don't penalize
            }

            // ---------- Courses (adapter-backed; can fail — Edge Case) ----------
            var recommendedCourses = ParseList(internship.RecommendedCourses);
            double coursesScore = 0;
            var courseDataAvailable = true;

            if (recommendedCourses.Count > 0)
            {
                try
                {
                    var provider = await _resolver.GetProviderAsync(student.UniversityCode);
                    var completedCourses = await provider.GetEnrolledCoursesAsync(student.UniversityCode, student.UniversityId);
                    var completedCodes = completedCourses.Select(c => c.CourseCode.Trim().ToUpperInvariant()).ToHashSet();
                    var matched = recommendedCourses.Count(rc => completedCodes.Contains(rc.ToUpperInvariant()));
                    coursesScore = (double)matched / recommendedCourses.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Adapter unavailable for course data — {User} matching score will exclude the course component.",
                        student.Id);
                    courseDataAvailable = false;
                }
            }
            else
            {
                coursesScore = 1; // no specific courses recommended
            }

            // ---------- Major ----------
            // Neutral (full credit) if the posting doesn't specify relevant
            // majors, OR the student's major isn't known — never penalized,
            // never flagged. See the class-level comment for why.
            var relevantMajors = ParseList(internship.RelevantMajors);
            double majorScore;
            if (relevantMajors.Count == 0 || string.IsNullOrWhiteSpace(studentMajor))
            {
                majorScore = 1;
            }
            else
            {
                majorScore = relevantMajors.Any(m => string.Equals(m.Trim(), studentMajor.Trim(), StringComparison.OrdinalIgnoreCase))
                    ? 1 : 0;
            }

            // ---------- Career interests vs. internship "field" ----------
            // No dedicated "field/category" attribute exists on Internship in
            // the schema — interpreted as keyword overlap between the
            // student's free-text interests and the posting's title/description,
            // which is what "field" realistically maps to without that column.
            double interestsScore = 0;
            if (!string.IsNullOrWhiteSpace(profile?.CareerInterests))
            {
                var interestWords = ParseWords(profile.CareerInterests);
                var postingText = $"{internship.Title} {internship.Description}".ToLowerInvariant();
                if (interestWords.Count > 0)
                {
                    var matched = interestWords.Count(w => postingText.Contains(w));
                    interestsScore = Math.Min(1.0, (double)matched / interestWords.Count);
                }
            }

            // ---------- Location preference ----------
            double locationScore = 0;
            if (!string.IsNullOrWhiteSpace(profile?.PreferredLocation))
            {
                locationScore = internship.Location.Contains(profile.PreferredLocation, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }

            // ---------- Combine, redistributing the course weight if unavailable ----------
            int skillsW = SkillsWeight, coursesW = CoursesWeight, majorW = MajorWeight,
                interestsW = InterestsWeight, locationW = LocationWeight;

            if (!courseDataAvailable)
            {
                // Scale the remaining four weights up proportionally so they
                // still sum to 100, rather than just losing 25 points outright.
                var remainingTotal = SkillsWeight + MajorWeight + InterestsWeight + LocationWeight;
                skillsW = (int)Math.Round(SkillsWeight * 100.0 / remainingTotal);
                majorW = (int)Math.Round(MajorWeight * 100.0 / remainingTotal);
                interestsW = (int)Math.Round(InterestsWeight * 100.0 / remainingTotal);
                locationW = 100 - skillsW - majorW - interestsW; // remainder absorbs any rounding
                coursesW = 0;
            }

            var total = skillsScore * skillsW + coursesScore * coursesW + majorScore * majorW
                      + interestsScore * interestsW + locationScore * locationW;
            var finalScore = Math.Clamp((int)Math.Round(total), 0, 100);

            return new MatchingScoreResult(finalScore, courseDataAvailable);
        }

        private static List<string> ParseList(string? commaSeparated) =>
            string.IsNullOrWhiteSpace(commaSeparated)
                ? new List<string>()
                : commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static List<string> ParseWords(string freeText) =>
            freeText.ToLowerInvariant()
                .Split(new[] { ' ', ',', '.', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(w => w.Length > 3) // skip short/common words for a slightly cleaner signal
                .Distinct()
                .ToList();
    }
}
