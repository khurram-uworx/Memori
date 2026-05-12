using Microsoft.Extensions.AI;
using System.Numerics.Tensors;
using System.Text.RegularExpressions;

namespace Memori.Embeddings;

/// <summary>
/// Character n-gram embedding generator for demos and prototyping.
/// </summary>
/// <remarks>
/// Uses character n-grams [2,3,4] with multi-hash (4 per n-gram) into a 1536-dimensional
/// space with L2 normalization. Produces semantically meaningful similarity without requiring
/// an external model.
/// </remarks>
public sealed partial class NgramEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    const int Dimension = 1536;
    const int HashesPerNgram = 4;
    static readonly int[] NgramLengths = [2, 3, 4];

    static readonly EmbeddingGeneratorMetadata Metadata = new("ngram-v1", defaultModelDimensions: 1536);

    static float[] generate(string text, CancellationToken cancellationToken)
    {
        if (text.Length < 2)
            return new float[Dimension];

        var cleaned = WhitespaceRegex().Replace(text, " ");
        var embedding = new float[Dimension];

        foreach (var len in NgramLengths)
            for (int i = 0; i <= cleaned.Length - len; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var hash = hashNgram(cleaned, i, len);

                for (int h = 0; h < HashesPerNgram; h++)
                {
                    var combined = HashCode.Combine(hash, h);
                    var bucket = Math.Abs(combined) % Dimension;
                    embedding[bucket] += (combined & 1) == 0 ? 1f : -1f;
                }
            }

        var norm = MathF.Sqrt(TensorPrimitives.SumOfSquares(embedding));
        if (norm > 0)
            TensorPrimitives.Divide(embedding, norm, embedding);

        return embedding;
    }

    static int hashNgram(string text, int start, int length)
    {
        var hash = length;

        for (int i = 0; i < length; i++)
            hash = hash * 31 + text[start + i];

        return hash;
    }

    /// <inheritdoc />
    public void Dispose()
    { }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(EmbeddingGeneratorMetadata)
        ? Metadata
        : serviceType?.IsInstanceOfType(this) == true
            ? this
            : null;

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(values);

        var results = new GeneratedEmbeddings<Embedding<float>>();
        foreach (var text in values)
            results.Add(new Embedding<float>(generate(text ?? string.Empty, cancellationToken)));
        return Task.FromResult(results);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
