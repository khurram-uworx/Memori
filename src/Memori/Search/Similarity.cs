namespace Memori.Search;

/// <summary>
/// Utility methods for in-process recall scoring.
/// </summary>
public static class Similarity
{
    /// <summary>
    /// Computes cosine similarity for two vectors.
    /// </summary>
    public static double Cosine(ReadOnlySpan<float> left, IReadOnlyList<float> right)
    {
        if (left.Length == 0 || left.Length != right.Count)
            return 0;

        // Use TensorPrimitives for optimized cosine similarity
        if (right is float[] arr)
            return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(left, arr);

        // Fallback: copy to array for span access
        var tmp = new float[right.Count];
        for (int i = 0; i < right.Count; i++)
            tmp[i] = right[i];
        return System.Numerics.Tensors.TensorPrimitives.CosineSimilarity(left, tmp);
    }

    /// <summary>
    /// Computes a simple token-overlap lexical score in the range 0..1.
    /// </summary>
    public static double LexicalScore(string query, string content)
    {
        var queryTerms = Tokenize(query);

        if (queryTerms.Count == 0)
            return 0;

        var contentTerms = Tokenize(content);

        if (contentTerms.Count == 0)
            return 0;

        var overlap = queryTerms.Count(contentTerms.Contains);
        return (double)overlap / queryTerms.Count;
    }

    /// <summary>
    /// Combines dense and lexical scores into a single rank score.
    /// </summary>
    public static double RankScore(double similarity, double lexicalScore, bool hasDenseSignal)
    {
        return hasDenseSignal
            ? (similarity * 0.7) + (lexicalScore * 0.3)
            : lexicalScore;
    }

    /// <summary>
    /// Tokenizes text into normalized alphanumeric terms.
    /// </summary>
    public static HashSet<string> Tokenize(string text)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = -1;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]))
            {
                if (start < 0)
                    start = i;

                continue;
            }

            if (start < 0)
                continue;

            var token = text[start..i];

            if (token.Length > 1)
                terms.Add(token);

            start = -1;
        }

        return terms;
    }
}
