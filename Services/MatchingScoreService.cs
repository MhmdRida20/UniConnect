using Microsoft.EntityFrameworkCore;
using UniConnect.Adapters;
using UniConnect.Data;
using UniConnect.Models;

namespace UniConnect.Services
{
    public record MatchingScoreResult(int Score, bool CourseDataAvailable);

    /// <summary>
    /// A shared reference "vocabulary" used to weigh terms by how
    /// distinctive they are — see TextSimilarity.BuildIdf. Built ONCE per
    /// request (not once per internship shown) via BuildCorpusAsync, then
    /// passed into every CalculateAsync call, since the corpus doesn't
    /// depend on which student or which specific internship is being
    /// scored — rebuilding it per-listing on the browse page would be a
    /// wasteful, redundant database query repeated for every single result.
    /// </summary>
    public class MatchingCorpus
    {
        public Dictionary<string, double> ListIdf { get; set; } = new();
        public Dictionary<string, double> WordIdf { get; set; } = new();
    }

    /// <summary>
    /// FR-41: "The system shall calculate a matching score for each
    /// student-internship pair," based on: student skills vs. required
    /// skills, completed courses vs. recommended courses, declared major vs.
    /// relevant majors, career interests vs. internship field, and location
    /// preferences.
    ///
    /// Every one of those five comparisons is done with the SAME technique
    /// — TF-IDF + cosine similarity (see TextSimilarity.cs) — rather than
    /// five different ad-hoc string checks. This is what "a simple AI model
    /// doing the comparisons" means here concretely: a real, standard
    /// information-retrieval technique, applied uniformly.
    ///
    /// Weighted 0-100: Skills 35, Courses 25, Major 20, Interests 10,
    /// Location 10.
    ///
    /// Edge Case ("Adapter unavailable for course data"): if the adapter
    /// call for COURSES fails, that component is dropped and the remaining
    /// weights are scaled up proportionally to still sum to 100 — a
    /// "partial matching score excluding courses," not a broken/zero one.
    ///
    /// Major and Skills/Courses with nothing specified are still handled as
    /// NEUTRAL (full credit) — a student with no major on file, or a
    /// posting with no required skills, is never penalized or flagged; see
    /// each section below.
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

        /// <summary>
        /// Builds the shared IDF "vocabulary" from every internship
        /// currently in the system — two separate corpora, since list-type
        /// fields (skills/courses/majors) and free-text fields
        /// (title/description/location) are tokenized differently (see
        /// TextSimilarity) and shouldn't be mixed into one vocabulary.
        /// Call this ONCE per request; reuse the result across every
        /// CalculateAsync call in that same request.
        /// </summary>
        public async Task<MatchingCorpus> BuildCorpusAsync()
        {
            var internships = await _db.Internships
                .Select(i => new { i.RequiredSkills, i.RecommendedCourses, i.RelevantMajors, i.Title, i.Description, i.Location })
                .ToListAsync();

            var listDocs = new List<List<string>>();
            var wordDocs = new List<List<string>>();

            foreach (var i in internships)
            {
                var listTokens = TextSimilarity.TokenizeAsItems(i.RequiredSkills)
                    .Concat(TextSimilarity.TokenizeAsItems(i.RecommendedCourses))
                    .Concat(TextSimilarity.TokenizeAsItems(i.RelevantMajors))
                    .ToList();
                if (listTokens.Count > 0) listDocs.Add(listTokens);

                var wordTokens = TextSimilarity.TokenizeAsWords($"{i.Title} {i.Description} {i.Location}");
                if (wordTokens.Count > 0) wordDocs.Add(wordTokens);
            }

            return new MatchingCorpus
            {
                ListIdf = TextSimilarity.BuildIdf(listDocs),
                WordIdf = TextSimilarity.BuildIdf(wordDocs)
            };
        }

        /// <param name="studentMajor">
        /// The student's declared major, or null if they have none on file
        /// (or it couldn't be retrieved) — the caller fetches this once via
        /// IUniversityProvider.GetStudentInfoAsync and passes it in.
        /// </param>
        /// <param name="corpus">Built once per request via BuildCorpusAsync — see that method's comment.</param>
        public async Task<MatchingScoreResult> CalculateAsync(
            ApplicationUser student, Internship internship, string? studentMajor, MatchingCorpus corpus)
        {
            var profile = await _db.CareerProfiles.FirstOrDefaultAsync(p => p.UserId == student.Id);
            var skills = await _db.StudentSkills.Where(s => s.UserId == student.Id).ToListAsync();

            // ---------- Skills (TF-IDF + cosine similarity) ----------
            var requiredSkillTokens = TextSimilarity.TokenizeAsItems(internship.RequiredSkills);
            double skillsScore;
            if (requiredSkillTokens.Count > 0)
            {
                var studentSkillTokens = skills.Select(s => s.SkillName.Trim().ToLowerInvariant()).ToList();
                var requiredVec = TextSimilarity.ComputeVector(requiredSkillTokens, corpus.ListIdf);
                var studentVec = TextSimilarity.ComputeVector(studentSkillTokens, corpus.ListIdf);
                skillsScore = TextSimilarity.CosineSimilarity(requiredVec, studentVec);
            }
            else
            {
                skillsScore = 1; // no specific skills required — don't penalize
            }

            // ---------- Courses (TF-IDF + cosine similarity; adapter-backed — Edge Case) ----------
            var recommendedCourseTokens = TextSimilarity.TokenizeAsItems(internship.RecommendedCourses);
            double coursesScore = 0;
            var courseDataAvailable = true;

            if (recommendedCourseTokens.Count > 0)
            {
                try
                {
                    var provider = await _resolver.GetProviderAsync(student.UniversityCode);
                    var completedCourses = await provider.GetEnrolledCoursesAsync(student.UniversityCode, student.UniversityId);
                    var completedTokens = completedCourses.Select(c => c.CourseCode.Trim().ToLowerInvariant()).ToList();

                    var recommendedVec = TextSimilarity.ComputeVector(recommendedCourseTokens, corpus.ListIdf);
                    var completedVec = TextSimilarity.ComputeVector(completedTokens, corpus.ListIdf);
                    coursesScore = TextSimilarity.CosineSimilarity(recommendedVec, completedVec);
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

            // ---------- Major (TF-IDF + cosine similarity) ----------
            // Neutral (full credit) if the posting doesn't specify relevant
            // majors, OR the student's major isn't known — never penalized,
            // never flagged. A missing major is a normal, common state
            // (e.g. a first-year undeclared student), not a system failure.
            var relevantMajorTokens = TextSimilarity.TokenizeAsItems(internship.RelevantMajors);
            double majorScore;
            if (relevantMajorTokens.Count == 0 || string.IsNullOrWhiteSpace(studentMajor))
            {
                majorScore = 1;
            }
            else
            {
                var studentMajorTokens = TextSimilarity.TokenizeAsItems(studentMajor);
                var relevantVec = TextSimilarity.ComputeVector(relevantMajorTokens, corpus.ListIdf);
                var studentVec = TextSimilarity.ComputeVector(studentMajorTokens, corpus.ListIdf);
                majorScore = TextSimilarity.CosineSimilarity(relevantVec, studentVec);
            }

            // ---------- Career interests vs. internship "field" (TF-IDF + cosine similarity) ----------
            double interestsScore = 0;
            if (!string.IsNullOrWhiteSpace(profile?.CareerInterests))
            {
                var interestTokens = TextSimilarity.TokenizeAsWords(profile.CareerInterests);
                var postingTokens = TextSimilarity.TokenizeAsWords($"{internship.Title} {internship.Description}");
                var interestVec = TextSimilarity.ComputeVector(interestTokens, corpus.WordIdf);
                var postingVec = TextSimilarity.ComputeVector(postingTokens, corpus.WordIdf);
                interestsScore = TextSimilarity.CosineSimilarity(interestVec, postingVec);
            }

            // ---------- Location preference (TF-IDF + cosine similarity) ----------
            double locationScore = 0;
            if (!string.IsNullOrWhiteSpace(profile?.PreferredLocation))
            {
                var preferredTokens = TextSimilarity.TokenizeAsWords(profile.PreferredLocation);
                var locationTokens = TextSimilarity.TokenizeAsWords(internship.Location);
                var preferredVec = TextSimilarity.ComputeVector(preferredTokens, corpus.WordIdf);
                var locationVec = TextSimilarity.ComputeVector(locationTokens, corpus.WordIdf);
                locationScore = TextSimilarity.CosineSimilarity(preferredVec, locationVec);
            }

            // ---------- Combine, redistributing the course weight if unavailable ----------
            int skillsW = SkillsWeight, coursesW = CoursesWeight, majorW = MajorWeight,
                interestsW = InterestsWeight, locationW = LocationWeight;

            if (!courseDataAvailable)
            {
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
    }
}
