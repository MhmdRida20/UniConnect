namespace UniConnect.Services
{
    /// <summary>
    /// A small, self-contained TF-IDF + cosine similarity engine — the
    /// "simple AI model" (per the instructor's suggestion) used to compare
    /// every factor in the Internship matching score (FR-41).
    ///
    /// This is a genuine, standard information-retrieval/NLP technique —
    /// the same core idea early search engines used to rank documents —
    /// not a neural network and not "trained" in the deep-learning sense,
    /// but legitimate, defensible machine learning coursework. It runs
    /// entirely inside the app: no external API, no cost, no network
    /// dependency, fully deterministic.
    ///
    /// The two ideas involved:
    ///   TF-IDF  — weighs words by how DISTINCTIVE they are. A word that
    ///             appears in almost every internship posting (e.g. "the",
    ///             or even something like "team") tells you little; a word
    ///             that appears in only a handful of postings (e.g. a
    ///             specific technical skill) is a much stronger signal, and
    ///             gets weighted higher.
    ///   Cosine similarity — measures how alike two pieces of text are by
    ///             treating each as a vector of weighted words and
    ///             measuring the angle between them. 1.0 = essentially the
    ///             same content; 0 = no shared vocabulary at all.
    /// </summary>
    public static class TextSimilarity
    {
        /// <summary>
        /// Splits a comma-separated list (e.g. "C#, SQL, Node.js") into
        /// individual ITEMS, each treated as one whole token — preserves
        /// compound/special-character terms like "C#" and "Node.js" intact
        /// rather than breaking them into meaningless sub-words. Used for
        /// Skills, Courses, and Major — all structured lists, not prose.
        /// </summary>
        public static List<string> TokenizeAsItems(string? commaSeparated) =>
            string.IsNullOrWhiteSpace(commaSeparated)
                ? new List<string>()
                : commaSeparated
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => s.ToLowerInvariant())
                    .ToList();

        /// <summary>
        /// Splits free text into individual WORDS — used for Career
        /// Interests and Location, since those are prose, not lists.
        /// Keeps '#', '+', and '.' as part of a word (so "C#" or ".NET"
        /// survive intact) rather than treating them as separators.
        /// </summary>
        public static List<string> TokenizeAsWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return new List<string>();
            return System.Text.RegularExpressions.Regex
                .Split(text.ToLowerInvariant(), @"[^a-z0-9#\+\.]+")
                .Where(w => w.Length > 1)
                .ToList();
        }

        /// <summary>
        /// Builds an IDF (Inverse Document Frequency) map from a corpus of
        /// already-tokenized documents. Terms appearing in FEW documents
        /// get a HIGH weight (distinctive, informative); terms appearing in
        /// MOST documents get a LOW weight (common, less useful for telling
        /// two things apart). Uses the standard smoothed formula
        /// (idf = ln((N+1)/(df+1)) + 1) so it stays well-defined even for a
        /// very small corpus (e.g. only one or two internships posted so
        /// far) rather than producing negative or undefined values.
        /// </summary>
        public static Dictionary<string, double> BuildIdf(List<List<string>> corpus)
        {
            var documentCount = Math.Max(1, corpus.Count);
            var documentFrequency = new Dictionary<string, int>();

            foreach (var doc in corpus)
            {
                foreach (var term in doc.Distinct())
                    documentFrequency[term] = documentFrequency.GetValueOrDefault(term) + 1;
            }

            return documentFrequency.ToDictionary(
                kv => kv.Key,
                kv => Math.Log((documentCount + 1) / (double)(kv.Value + 1)) + 1);
        }

        /// <summary>
        /// Converts a tokenized document into a TF-IDF weighted vector —
        /// term frequency (how often a word appears IN THIS document)
        /// multiplied by its IDF weight (how distinctive that word is
        /// ACROSS the whole corpus). A term never seen in the corpus at all
        /// falls back to a neutral IDF of 1, so genuinely new/unseen terms
        /// still count for something rather than being silently dropped.
        /// </summary>
        public static Dictionary<string, double> ComputeVector(List<string> tokens, Dictionary<string, double> idf)
        {
            var vector = new Dictionary<string, double>();
            if (tokens.Count == 0) return vector;

            var termFrequency = new Dictionary<string, int>();
            foreach (var t in tokens)
                termFrequency[t] = termFrequency.GetValueOrDefault(t) + 1;

            foreach (var (term, count) in termFrequency)
            {
                var tf = count / (double)tokens.Count;
                var idfWeight = idf.GetValueOrDefault(term, 1.0);
                vector[term] = tf * idfWeight;
            }
            return vector;
        }

        /// <summary>
        /// Cosine similarity — the angle between two weighted word-vectors.
        /// 1.0 means essentially the same content; 0 means no shared
        /// vocabulary at all. Returns 0 (not an error) if either side has
        /// no usable text, rather than dividing by zero or throwing.
        /// </summary>
        public static double CosineSimilarity(Dictionary<string, double> a, Dictionary<string, double> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0;

            double dotProduct = 0;
            foreach (var (term, weight) in a)
            {
                if (b.TryGetValue(term, out var otherWeight))
                    dotProduct += weight * otherWeight;
            }

            var magnitudeA = Math.Sqrt(a.Values.Sum(v => v * v));
            var magnitudeB = Math.Sqrt(b.Values.Sum(v => v * v));
            if (magnitudeA == 0 || magnitudeB == 0) return 0;

            return Math.Clamp(dotProduct / (magnitudeA * magnitudeB), 0, 1);
        }
    }
}
