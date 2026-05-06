using Microsoft.Extensions.AI;
using System.Numerics.Tensors;

namespace Memori.Embeddings;

/// <summary>
/// Deterministic, dependency-free embedding generator for tests and local demos.
/// </summary>
/// <remarks>
/// This implementation is not a semantic embedding model. It hashes tokens into a
/// fixed-size vector so recall paths can be exercised without a production model.
/// </remarks>
public sealed class DeterministicEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
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
        // Use TensorPrimitives for optimized L2 normalization
        var sumSquares = TensorPrimitives.SumOfSquares(vector);
        var norm = (float)Math.Sqrt(sumSquares);
        if (norm == 0)
            return;
        var scale = 1f / norm;
        TensorPrimitives.Multiply(vector, scale, vector);
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
    public void Dispose()
    { }

    public object? GetService(Type serviceType, object? serviceKey = null)
        => null;

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(values);

        var embeddings = values
            .Select(value => new Embedding<float>(generate(value ?? string.Empty, cancellationToken)))
            .ToArray();

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }
}
