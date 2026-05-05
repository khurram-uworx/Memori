using Memori.Abstractions;

namespace Memori.Embeddings;

/// <summary>
/// Embedding generator that intentionally returns no vectors.
/// </summary>
/// <remarks>
/// Use this when an application wants Memori to rely on lexical recall only.
/// </remarks>
public sealed class NullEmbeddingGenerator : IMemoriEmbeddingGenerator
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<float>?> GenerateEmbeddingAsync(string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);

        return ValueTask.FromResult<IReadOnlyList<float>?>(null);
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<IReadOnlyList<float>?>> GenerateEmbeddingsAsync(IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(texts);

        var embeddings = new IReadOnlyList<float>?[texts.Count];
        return ValueTask.FromResult<IReadOnlyList<IReadOnlyList<float>?>>(embeddings);
    }
}
