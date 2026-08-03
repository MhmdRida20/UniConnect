using Microsoft.Extensions.Logging.Abstractions;
using UniConnect.Services;
using UniConnect.Tests.Infrastructure;

namespace UniConnect.Tests.Unit;

/// <summary>
/// FR-41 — the student/internship matching score.
///
/// This number is shown to students as a percentage on every listing and is
/// stored on their applications, so its behaviour is user-visible in a way most
/// internals aren't. The cases below pin down the two things easiest to break
/// by accident: the weighting arithmetic, and the "missing data is neutral, not
/// a penalty" rule the service documents but doesn't apply uniformly.
/// </summary>
public class MatchingScoreServiceTests : IDisposable
{
    private readonly TestDatabase _test = new();
    private readonly FakeUniversityProvider _provider = new();

    private MatchingScoreService Service() =>
        new(_test.Db, _provider, NullLogger<MatchingScoreService>.Instance);

    public void Dispose() => _test.Dispose();

    // A student and posting that agree on every one of the five factors.
    private (UniConnect.Models.ApplicationUser Student, UniConnect.Models.Internship Internship) PerfectMatch()
    {
        _test.Db.AddUniversity();
        var student = _test.Db.AddUser("U2024001");
        var careersAccount = _test.Db.AddUser("CAREERS");
        var company = _test.Db.AddCompany(careersAccount);

        var internship = _test.Db.AddInternship(
            company,
            title: "Backend Development",
            description: "",
            requiredSkills: "C#, SQL",
            recommendedCourses: "CSC301",
            relevantMajors: "Computer Science",
            location: "Beirut");

        _test.Db.AddSkills(student, "C#", "SQL");
        _test.Db.AddCareerProfile(student, interests: "Backend Development", preferredLocation: "Beirut");

        _provider.WithCourse("CSC301").Enroll(student.UniversityId, "CSC301");

        return (student, internship);
    }

    [Fact]
    public async Task Perfect_match_scores_100()
    {
        // Also proves the five weights still sum to 100 — if anyone edits one
        // constant without the others, this is what catches it.
        var (student, internship) = PerfectMatch();
        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        var result = await service.CalculateAsync(student, internship, "Computer Science", corpus);

        Assert.Equal(100, result.Score);
        Assert.True(result.CourseDataAvailable);
    }

    [Fact]
    public async Task Adapter_failure_excludes_courses_and_still_totals_100()
    {
        // The documented edge case: "partial matching score excluding courses,"
        // not a broken or artificially deflated one. A student who would have
        // scored 100 must still score 100 when the registrar is unreachable.
        var (student, internship) = PerfectMatch();
        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        _provider.FailWith = new HttpRequestException("registrar unreachable");

        var result = await service.CalculateAsync(student, internship, "Computer Science", corpus);

        Assert.False(result.CourseDataAvailable);
        Assert.Equal(100, result.Score);
    }

    [Fact]
    public async Task Adapter_failure_is_swallowed_rather_than_thrown()
    {
        // Browsing internships must not 500 because a partner API is down.
        var (student, internship) = PerfectMatch();
        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        _provider.FailWith = new TimeoutException();

        var exception = await Record.ExceptionAsync(() =>
            service.CalculateAsync(student, internship, "Computer Science", corpus));

        Assert.Null(exception);
    }

    [Fact]
    public async Task A_posting_with_no_required_skills_gives_full_skill_credit()
    {
        // "A posting with no required skills is never penalised" — a student
        // with an empty skill list should lose nothing on that factor.
        _test.Db.AddUniversity();
        var student = _test.Db.AddUser("U2024001");
        var company = _test.Db.AddCompany(_test.Db.AddUser("CAREERS"));

        var unspecified = _test.Db.AddInternship(company, title: "Open Role", requiredSkills: null);
        var demanding = _test.Db.AddInternship(company, title: "Open Role", requiredSkills: "Fortran, COBOL");

        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        var withoutRequirements = await service.CalculateAsync(student, unspecified, null, corpus);
        var withRequirements = await service.CalculateAsync(student, demanding, null, corpus);

        // Skills carry 35 of the 100 points, and it's the only factor differing.
        Assert.Equal(35, withoutRequirements.Score - withRequirements.Score);
    }

    [Fact]
    public async Task An_unknown_student_major_is_neutral_not_a_penalty()
    {
        // A first-year undeclared student is a normal state, not a bad match.
        _test.Db.AddUniversity();
        var student = _test.Db.AddUser("U2024001");
        var company = _test.Db.AddCompany(_test.Db.AddUser("CAREERS"));
        var internship = _test.Db.AddInternship(company, relevantMajors: "Computer Science");

        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        var unknownMajor = await service.CalculateAsync(student, internship, null, corpus);
        var wrongMajor = await service.CalculateAsync(student, internship, "Basket Weaving", corpus);

        // Major carries 20 points.
        Assert.Equal(20, unknownMajor.Score - wrongMajor.Score);
    }

    [Fact]
    public async Task Matching_skills_score_higher_than_unrelated_ones()
    {
        _test.Db.AddUniversity();
        var matching = _test.Db.AddUser("U2024001");
        var unrelated = _test.Db.AddUser("U2024002");
        var company = _test.Db.AddCompany(_test.Db.AddUser("CAREERS"));
        var internship = _test.Db.AddInternship(company, requiredSkills: "C#, SQL, Docker");

        _test.Db.AddSkills(matching, "C#", "SQL", "Docker");
        _test.Db.AddSkills(unrelated, "Welding", "Carpentry");

        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        var good = await service.CalculateAsync(matching, internship, null, corpus);
        var poor = await service.CalculateAsync(unrelated, internship, null, corpus);

        Assert.True(good.Score > poor.Score, $"{good.Score} should beat {poor.Score}");
    }

    [Fact]
    public async Task A_student_with_no_career_profile_still_gets_a_usable_score()
    {
        // Documents a real asymmetry: unlike skills, courses and major, the
        // interests and location factors are NOT neutral when absent — they
        // score zero. So an empty profile costs 20 points even on an otherwise
        // perfect match. That is deliberate (it's what drives the "improve your
        // profile" prompt), but it is worth pinning down rather than
        // rediscovering.
        var (student, internship) = PerfectMatch();
        _test.Db.CareerProfiles.RemoveRange(_test.Db.CareerProfiles);
        _test.Db.SaveChanges();

        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        var result = await service.CalculateAsync(student, internship, "Computer Science", corpus);

        Assert.Equal(80, result.Score);
    }

    [Fact]
    public async Task Score_always_lands_between_0_and_100()
    {
        _test.Db.AddUniversity();
        var company = _test.Db.AddCompany(_test.Db.AddUser("CAREERS"));

        var loaded = _test.Db.AddUser("U2024001");
        _test.Db.AddSkills(loaded, "C#", "C#", "SQL", "Docker", "Kubernetes");
        _test.Db.AddCareerProfile(loaded, interests: "backend backend backend", preferredLocation: "Beirut");

        var bare = _test.Db.AddUser("U2024002");

        var internships = new[]
        {
            _test.Db.AddInternship(company, title: "Backend", requiredSkills: "C#", location: "Beirut"),
            _test.Db.AddInternship(company, title: "Frontend", requiredSkills: null, location: "Tripoli"),
            _test.Db.AddInternship(company, title: "Data", requiredSkills: "R, Stata", recommendedCourses: "MAT202")
        };

        var service = Service();
        var corpus = await service.BuildCorpusAsync();

        foreach (var student in new[] { loaded, bare })
        foreach (var internship in internships)
        {
            var result = await service.CalculateAsync(student, internship, "Computer Science", corpus);
            Assert.InRange(result.Score, 0, 100);
        }
    }

    // ---------- BuildCorpusAsync ----------

    [Fact]
    public async Task BuildCorpus_collects_terms_from_every_posting()
    {
        _test.Db.AddUniversity();
        var company = _test.Db.AddCompany(_test.Db.AddUser("CAREERS"));
        _test.Db.AddInternship(company, title: "Backend", requiredSkills: "C#");
        _test.Db.AddInternship(company, title: "Analytics", requiredSkills: "Python");

        var corpus = await Service().BuildCorpusAsync();

        Assert.Contains("c#", corpus.ListIdf.Keys);
        Assert.Contains("python", corpus.ListIdf.Keys);
        Assert.Contains("backend", corpus.WordIdf.Keys);
        Assert.Contains("analytics", corpus.WordIdf.Keys);
    }

    [Fact]
    public async Task BuildCorpus_handles_an_empty_catalogue()
    {
        // A freshly provisioned university has no postings yet; scoring must
        // not depend on there already being a corpus.
        var corpus = await Service().BuildCorpusAsync();

        Assert.Empty(corpus.ListIdf);
        Assert.Empty(corpus.WordIdf);
    }
}
