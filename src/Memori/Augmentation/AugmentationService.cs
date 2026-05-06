using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;

namespace Memori.Augmentation;

/// <summary>
/// Coordinates durable augmentation writes for captured conversations.
/// </summary>
public sealed class AugmentationService
{
    readonly IStorage storage;
    readonly IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator;
    readonly IAugmentationClient augmentationClient;
    readonly MemoriOptions options;
    readonly object gate = new();
    readonly List<Task> pendingTasks = [];

    /// <summary>
    /// Creates a new augmentation service.
    /// </summary>
    public AugmentationService(
        IStorage storage,
        IAugmentationClient augmentationClient,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.augmentationClient = augmentationClient ?? throw new ArgumentNullException(nameof(augmentationClient));
        this.embeddingGenerator = embeddingGenerator;
        this.options = options ?? new MemoriOptions();
        this.options.Validate();
    }

    /// <summary>
    /// Queues augmentation work or runs it inline depending on configuration.
    /// </summary>
    public ValueTask EnqueueAsync(AugmentationInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!options.EnableAugmentation)
            return ValueTask.CompletedTask;

        if (!options.RunAugmentationInBackground)
            return RunAugmentationAsync(input, cancellationToken);

        var task = RunAugmentationAsync(input, cancellationToken).AsTask();
        lock (gate)
        {
            pendingTasks.Add(task);
        }

        _ = task.ContinueWith(static (completed, state) =>
        {
            var self = (AugmentationService)state!;
            lock (self.gate)
            {
                self.pendingTasks.Remove(completed);
            }
        }, this, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Waits for all queued augmentation tasks to complete.
    /// </summary>
    public async ValueTask WaitForAugmentationAsync(CancellationToken cancellationToken = default)
    {
        Task[] tasks;
        lock (gate)
        {
            tasks = pendingTasks.ToArray();
        }

        if (tasks.Length == 0)
            return;

        await Task.WhenAll(tasks).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    async ValueTask RunAugmentationAsync(AugmentationInput input, CancellationToken cancellationToken)
    {
        var result = await augmentationClient.AugmentAsync(input, cancellationToken).ConfigureAwait(false);

        if (result is null)
            return;

        if (!string.IsNullOrWhiteSpace(result.ConversationSummary))
        {
            await storage.UpdateConversationSummaryAsync(
                    input.ConversationId,
                    result.ConversationSummary!,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        if (result.Facts is { Count: > 0 })
        {
            var facts = await maybeEmbedFactsAsync(result.Facts, cancellationToken).ConfigureAwait(false);

            if (facts.Count > 0)
                await storage.AddFactsAsync(input.EntityId, facts, input.ConversationId, cancellationToken)
                    .ConfigureAwait(false);
        }

        if (result.SemanticTriples is { Count: > 0 })
            await storage.AddSemanticTriplesAsync(input.EntityId, result.SemanticTriples, cancellationToken)
                .ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(input.ProcessId) && result.ProcessAttributes is { Count: > 0 })
            await storage.AddProcessAttributesAsync(input.ProcessId, result.ProcessAttributes, cancellationToken)
                .ConfigureAwait(false);
    }

    async ValueTask<IReadOnlyList<NewMemoryFact>> maybeEmbedFactsAsync(
        IReadOnlyList<NewMemoryFact> facts,
        CancellationToken cancellationToken)
    {
        if (embeddingGenerator is null)
            return facts;

        var output = new List<NewMemoryFact>(facts.Count);
        foreach (var fact in facts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(fact.Content) && fact.Embedding is null)
            {
                var embedding = await embeddingGenerator.GenerateAsync(fact.Content, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                output.Add(new NewMemoryFact(
                    fact.Content,
                    embedding.Vector.ToArray(),
                    fact.Summaries,
                    fact.CreatedAt));
            }
            else
                output.Add(fact);
        }

        return output;
    }
}
