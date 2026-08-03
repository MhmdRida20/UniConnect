using UniConnect.Services;

namespace UniConnect.Tests.Unit;

/// <summary>
/// The TF-IDF engine underneath every internship matching score (FR-41).
///
/// Pure functions with no dependencies, so these are the cheapest tests in the
/// suite and they protect the component whose output is shown to students as a
/// percentage — a silent change here quietly rescores every listing.
/// </summary>
public class TextSimilarityTests
{
    // ---------- TokenizeAsItems: structured, comma-separated lists ----------

    [Fact]
    public void TokenizeAsItems_keeps_compound_terms_whole()
    {
        // The whole reason list fields aren't word-tokenized: splitting on
        // punctuation would turn "C#" into "c" and "Node.js" into "node"+"js",
        // which then match unrelated postings.
        var tokens = TextSimilarity.TokenizeAsItems("C#, SQL, Node.js, ASP.NET Core");

        Assert.Equal(new[] { "c#", "sql", "node.js", "asp.net core" }, tokens);
    }

    [Fact]
    public void TokenizeAsItems_trims_and_drops_empty_entries()
    {
        var tokens = TextSimilarity.TokenizeAsItems("  Java ,, , Python  ");

        Assert.Equal(new[] { "java", "python" }, tokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TokenizeAsItems_returns_empty_for_no_input(string? input)
    {
        Assert.Empty(TextSimilarity.TokenizeAsItems(input));
    }

    // ---------- TokenizeAsWords: free prose ----------

    [Fact]
    public void TokenizeAsWords_preserves_hash_plus_and_dot_inside_words()
    {
        var tokens = TextSimilarity.TokenizeAsWords("Backend work in C# and .NET, plus C++");

        Assert.Contains("c#", tokens);
        Assert.Contains(".net", tokens);
        Assert.Contains("c++", tokens);
    }

    [Fact]
    public void TokenizeAsWords_drops_single_character_tokens()
    {
        // Single letters carry no signal and would otherwise inflate every
        // vector's magnitude.
        var tokens = TextSimilarity.TokenizeAsWords("a data analysis role");

        Assert.DoesNotContain("a", tokens);
        Assert.Equal(new[] { "data", "analysis", "role" }, tokens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TokenizeAsWords_returns_empty_for_no_input(string? input)
    {
        Assert.Empty(TextSimilarity.TokenizeAsWords(input));
    }

    // ---------- BuildIdf ----------

    [Fact]
    public void BuildIdf_weights_rare_terms_above_common_ones()
    {
        // "python" is in every document; "cobol" in exactly one. The whole
        // point of IDF is that the second is the more informative signal.
        var corpus = new List<List<string>>
        {
            new() { "python", "sql" },
            new() { "python", "java" },
            new() { "python", "cobol" }
        };

        var idf = TextSimilarity.BuildIdf(corpus);

        Assert.True(idf["cobol"] > idf["python"]);
    }

    [Fact]
    public void BuildIdf_stays_positive_on_a_single_document_corpus()
    {
        // The smoothed formula exists so a brand-new deployment with one
        // internship posted doesn't produce zero or negative weights.
        var idf = TextSimilarity.BuildIdf(new List<List<string>> { new() { "c#", "sql" } });

        Assert.All(idf.Values, weight => Assert.True(weight > 0, $"weight was {weight}"));
    }

    [Fact]
    public void BuildIdf_counts_a_repeated_term_once_per_document()
    {
        var repeated = TextSimilarity.BuildIdf(new List<List<string>> { new() { "sql", "sql", "sql" } });
        var single = TextSimilarity.BuildIdf(new List<List<string>> { new() { "sql" } });

        Assert.Equal(single["sql"], repeated["sql"], 10);
    }

    [Fact]
    public void BuildIdf_returns_empty_for_an_empty_corpus()
    {
        Assert.Empty(TextSimilarity.BuildIdf(new List<List<string>>()));
    }

    // ---------- ComputeVector ----------

    [Fact]
    public void ComputeVector_returns_empty_for_no_tokens()
    {
        Assert.Empty(TextSimilarity.ComputeVector(new List<string>(), new Dictionary<string, double>()));
    }

    [Fact]
    public void ComputeVector_gives_an_unseen_term_neutral_weight()
    {
        // A genuinely new skill nobody has posted about yet should still count
        // for something rather than being silently dropped.
        var vector = TextSimilarity.ComputeVector(new List<string> { "rust" }, new Dictionary<string, double>());

        Assert.Equal(1.0, vector["rust"], 10);   // tf = 1/1, idf falls back to 1
    }

    [Fact]
    public void ComputeVector_scales_term_frequency_by_document_length()
    {
        var vector = TextSimilarity.ComputeVector(
            new List<string> { "sql", "sql", "java", "python" },
            new Dictionary<string, double>());

        Assert.Equal(0.5, vector["sql"], 10);    // 2 of 4
        Assert.Equal(0.25, vector["java"], 10);  // 1 of 4
    }

    // ---------- CosineSimilarity ----------

    [Fact]
    public void CosineSimilarity_is_one_for_identical_content()
    {
        var idf = TextSimilarity.BuildIdf(new List<List<string>> { new() { "c#", "sql" }, new() { "java" } });
        var vector = TextSimilarity.ComputeVector(new List<string> { "c#", "sql" }, idf);

        Assert.Equal(1.0, TextSimilarity.CosineSimilarity(vector, vector), 10);
    }

    [Fact]
    public void CosineSimilarity_is_zero_when_nothing_overlaps()
    {
        var idf = new Dictionary<string, double>();
        var a = TextSimilarity.ComputeVector(new List<string> { "c#", "sql" }, idf);
        var b = TextSimilarity.ComputeVector(new List<string> { "welding", "carpentry" }, idf);

        Assert.Equal(0, TextSimilarity.CosineSimilarity(a, b));
    }

    [Fact]
    public void CosineSimilarity_returns_zero_rather_than_dividing_by_zero()
    {
        var populated = TextSimilarity.ComputeVector(new List<string> { "c#" }, new Dictionary<string, double>());
        var empty = new Dictionary<string, double>();

        Assert.Equal(0, TextSimilarity.CosineSimilarity(populated, empty));
        Assert.Equal(0, TextSimilarity.CosineSimilarity(empty, populated));
        Assert.Equal(0, TextSimilarity.CosineSimilarity(empty, empty));
    }

    [Fact]
    public void CosineSimilarity_ranks_partial_overlap_between_none_and_all()
    {
        var idf = new Dictionary<string, double>();
        var required = TextSimilarity.ComputeVector(new List<string> { "c#", "sql", "docker" }, idf);
        var partial = TextSimilarity.ComputeVector(new List<string> { "c#", "sql" }, idf);
        var none = TextSimilarity.ComputeVector(new List<string> { "welding" }, idf);

        var partialScore = TextSimilarity.CosineSimilarity(required, partial);

        Assert.True(partialScore > TextSimilarity.CosineSimilarity(required, none));
        Assert.True(partialScore < 1.0);
    }

    [Fact]
    public void CosineSimilarity_never_leaves_the_zero_to_one_range()
    {
        var idf = TextSimilarity.BuildIdf(new List<List<string>>
        {
            new() { "c#" }, new() { "c#", "sql" }, new() { "java", "sql", "docker" }
        });

        List<string>[] documents =
        {
            new() { "c#" },
            new() { "c#", "c#", "c#" },
            new() { "sql", "docker" },
            new() { "java" }
        };

        foreach (var left in documents)
        foreach (var right in documents)
        {
            var score = TextSimilarity.CosineSimilarity(
                TextSimilarity.ComputeVector(left, idf),
                TextSimilarity.ComputeVector(right, idf));

            Assert.InRange(score, 0.0, 1.0);
        }
    }
}
