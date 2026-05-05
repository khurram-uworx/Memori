namespace Memori.Abstractions;

/// <summary>
/// Generates vector embeddings for text used by Memori recall and augmentation.
/// </summary>
/// <remarks>
/// Implementations should be safe for concurrent use. Returning <see langword="null"/>
/// is allowed and means Memori should rely on lexical recall for that input.
/// </remarks>
public interface IMemoriEmbeddingGenerator
{
    /// <summary>
    /// Generates one embedding vector for text.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An embedding vector, or <see langword="null"/> when embeddings are unavailable.</returns>
    ValueTask<IReadOnlyList<float>?> GenerateEmbeddingAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple text values.
    /// </summary>
    /// <param name="texts">Text values to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Embedding vectors in the same order as <paramref name="texts"/>.</returns>
    ValueTask<IReadOnlyList<IReadOnlyList<float>?>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
