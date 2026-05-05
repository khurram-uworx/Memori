using Memori.Abstractions;
using Microsoft.Extensions.AI;

namespace Memori.Embeddings;

/// <summary>
/// Adapts Microsoft.Extensions.AI embedding generators to Memori's embedding abstraction.
/// </summary>
public sealed class MicrosoftEmbeddingGeneratorAdapter : IMemoriEmbeddingGenerator
{
    readonly IEmbeddingGenerator<string, Embedding<float>> inner;
    readonly EmbeddingGenerationOptions? options;

    /// <summary>
    /// Creates an adapter for a Microsoft.Extensions.AI embedding generator.
    /// </summary>
    public MicrosoftEmbeddingGeneratorAdapter(IEmbeddingGenerator<string, Embedding<float>> inner,
        EmbeddingGenerationOptions? options = null)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.options = options;
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<float>?> GenerateEmbeddingAsync(string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var embedding = await inner.GenerateAsync(text, options, cancellationToken)
            .ConfigureAwait(false);
        return embedding.Vector.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<IReadOnlyList<float>?>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var embeddings = await inner.GenerateAsync(texts, options, cancellationToken)
            .ConfigureAwait(false);

        var vectors = new IReadOnlyList<float>?[embeddings.Count];
        for (var i = 0; i < embeddings.Count; i++)
        {
            vectors[i] = embeddings[i].Vector.ToArray();
        }

        return vectors;
    }
}
