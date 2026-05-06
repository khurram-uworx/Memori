using Memori.Abstractions;

namespace Memori.Embeddings;

/// <summary>
/// Deterministic, dependency-free embedding generator for tests and local demos.
/// </summary>
/// <remarks>
/// This implementation is not a semantic embedding model. It hashes tokens into a
/// fixed-size vector so recall paths can be exercised without a production model.
/// </remarks>
public sealed class DeterministicEmbeddingGenerator : IMemoriEmbeddingGenerator
{
    /// <summary>
    /// Default vector size used by the deterministic generator.
    /// </summary>
    public const int DefaultDimensions = 64;

    static IEnumerable<string> tokenize(string text)
    {
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

            if (i - start > 1)
                yield return text[start..i].ToUpperInvariant();

            start = -1;
        }
    }

    static uint stableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;

        foreach (var c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return hash;
    }

    static void normalize(float[] vector)
    {
        double norm = 0;

        foreach (var value in vector)
            norm += value * value;

        if (norm == 0)
            return;

        var scale = 1 / Math.Sqrt(norm);

        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] * scale);
    }

    readonly int dimensions;

    /// <summary>
    /// Creates a deterministic embedding generator.
    /// </summary>
    /// <param name="dimensions">Number of dimensions in generated vectors.</param>
    public DeterministicEmbeddingGenerator(int dimensions = DefaultDimensions)
    {
        if (dimensions <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimensions), "Dimensions must be greater than zero.");

        this.dimensions = dimensions;
    }

    float[] generate(string text, CancellationToken cancellationToken)
    {
        var vector = new float[dimensions];

        foreach (var token in tokenize(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = stableHash(token);
            var index = (int)(hash % (uint)dimensions);
            var sign = (hash & 0x8000_0000) == 0 ? 1f : -1f;
            vector[index] += sign;
        }

        normalize(vector);
        return vector;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<float>?> GenerateEmbeddingAsync(string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);

        return ValueTask.FromResult<IReadOnlyList<float>?>(generate(text, cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<IReadOnlyList<float>?>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(texts);

        var embeddings = new IReadOnlyList<float>?[texts.Count];
        for (var i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            embeddings[i] = generate(texts[i] ?? string.Empty, cancellationToken);
        }

        return ValueTask.FromResult<IReadOnlyList<IReadOnlyList<float>?>>(embeddings);
    }
}
