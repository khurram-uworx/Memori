using Memori.Abstractions;

namespace Memori.Augmentation;

/// <summary>
/// No-op augmentation client used when no augmentation service is configured.
/// </summary>
public sealed class NullAugmentationClient : IAugmentationClient
{
    /// <inheritdoc />
    public ValueTask<AugmentationResult?> AugmentAsync(
        AugmentationInput context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ValueTask.FromResult<AugmentationResult?>(null);
    }
}
