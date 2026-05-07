using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Search;
using Microsoft.Extensions.AI;

namespace Memori;

/// <summary>
/// Main Memori facade for attribution, sessions, and durable conversation capture.
/// </summary>
public sealed class Memori
{
    readonly IStorage storage;
    readonly MemoriOptions options;
    readonly MemorySearchService memorySearchService;
    readonly AugmentationService? augmentationService;
    readonly object gate = new();

    Attribution? attribution;
    string? sessionId;

    internal MemoriOptions Options => options;

    /// <summary>
    /// Gets the current attribution context, if one has been configured.
    /// </summary>
    public Attribution? CurrentAttribution
    {
        get
        {
            lock (gate)
            {
                return attribution;
            }
        }
    }

    /// <summary>
    /// Gets the active session identifier, if one has been configured.
    /// </summary>
    public string? CurrentSessionId
    {
        get
        {
            lock (gate)
            {
                return sessionId;
            }
        }
    }

    /// <summary>
    /// Creates a new Memori facade.
    /// </summary>
    public Memori(
        IStorage storage,
        MemoriOptions? options = null,
        string? sessionId = null,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
        this.options = options ?? new MemoriOptions();
        this.options.Validate();
        memorySearchService = new MemorySearchService(storage, embeddingGenerator, this.options);
        this.sessionId = sessionId;
        augmentationService = augmentationClient is null
            ? null
            : new AugmentationService(storage, augmentationClient, embeddingGenerator, this.options);
    }

    /// <summary>
    /// Sets the current attribution context used for subsequent captures.
    /// </summary>
    public Attribution Attribution(string entityId, string? processId = null)
    {
        var value = new Attribution(entityId, processId);
        lock (gate)
        {
            attribution = value;
        }

        return value;
    }

    /// <summary>
    /// Creates and sets a new session identifier.
    /// </summary>
    public string NewSession()
    {
        var value = Guid.NewGuid().ToString("N");
        lock (gate)
        {
            sessionId = value;
        }

        return value;
    }

    /// <summary>
    /// Sets the active session identifier.
    /// </summary>
    public void SetSession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("Value cannot be empty.", nameof(sessionId));

        lock (gate)
        {
            this.sessionId = sessionId;
        }
    }

    /// <summary>
    /// Resumes an externally managed session identifier.
    /// </summary>
    /// <remarks>
    /// This only changes conversation grouping for capture/history. Recall and memory deletion remain scoped
    /// to the current attribution entity.
    /// </remarks>
    public void ResumeSession(string sessionId) => SetSession(sessionId);

    /// <summary>
    /// Clears the current attribution context.
    /// </summary>
    public void ClearAttribution()
    {
        lock (gate)
        {
            attribution = null;
        }
    }

    /// <summary>
    /// Clears the active session identifier.
    /// </summary>
    public void ClearSession()
    {
        lock (gate)
        {
            sessionId = null;
        }
    }

    /// <summary>
    /// Captures a completed conversation turn to durable storage.
    /// </summary>
    /// <remarks>
    /// When no attribution has been configured, capture is intentionally a no-op.
    /// </remarks>
    public async ValueTask CaptureAsync(
        IReadOnlyList<ConversationMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
            return;

        Attribution? currentAttribution;
        string? currentSessionId;
        lock (gate)
        {
            currentAttribution = attribution;
            currentSessionId = sessionId;
        }

        if (currentAttribution is null)
            return;

        currentSessionId ??= NewSession();

        var entityId = await storage.GetOrCreateEntityAsync(currentAttribution.EntityId, cancellationToken)
            .ConfigureAwait(false);
        var processId = currentAttribution.ProcessId is null
            ? null
            : await storage.GetOrCreateProcessAsync(currentAttribution.ProcessId, cancellationToken)
                .ConfigureAwait(false);
        var resolvedSessionId = await storage.GetOrCreateSessionAsync(
                currentSessionId,
                entityId,
                processId,
                cancellationToken)
            .ConfigureAwait(false);
        var conversation = await storage.GetOrCreateConversationAsync(
                resolvedSessionId,
                options.SessionTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        var capturedMessages = normalizeMessages(messages, options.StripSystemMessagesOnCapture);

        if (capturedMessages.Count == 0)
            return;

        await storage.AppendMessagesAsync(conversation.Id, capturedMessages, cancellationToken)
            .ConfigureAwait(false);

        if (augmentationService is not null)
        {
            var input = new AugmentationInput(
                entityId,
                processId,
                conversation.Id,
                capturedMessages);
            await augmentationService.EnqueueAsync(input, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Waits for background augmentation tasks to finish.
    /// </summary>
    public ValueTask WaitForAugmentationAsync(CancellationToken cancellationToken = default)
        => augmentationService is null
            ? ValueTask.CompletedTask
            : augmentationService.WaitForAugmentationAsync(cancellationToken);

    /// <summary>
    /// Recalls relevant facts for the current attribution context.
    /// </summary>
    public async ValueTask<IReadOnlyList<RecallResult>> RecallAsync(
        string query,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Attribution? currentAttribution;
        lock (gate)
        {
            currentAttribution = attribution;
        }

        if (currentAttribution is null)
            return Array.Empty<RecallResult>();

        return await memorySearchService.RecallAsync(
                currentAttribution.EntityId,
                query,
                limit,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Formats recalled memories into prompt context using the configured options.
    /// </summary>
    public string FormatPromptContext(IReadOnlyList<RecallResult> results)
        => memorySearchService.FormatPromptContext(results);

    /// <summary>
    /// Builds structured prompt context using the configured options.
    /// </summary>
    public PromptContext BuildPromptContext(IReadOnlyList<RecallResult> results)
        => memorySearchService.BuildPromptContext(results);

    /// <summary>
    /// Deletes durable memories for the current attribution context.
    /// </summary>
    public async ValueTask DeleteEntityMemoriesAsync(CancellationToken cancellationToken = default)
    {
        Attribution? currentAttribution;
        lock (gate)
        {
            currentAttribution = attribution;
        }

        if (currentAttribution is null)
            return;

        var entityId = await storage.GetOrCreateEntityAsync(currentAttribution.EntityId, cancellationToken)
            .ConfigureAwait(false);

        await storage.DeleteEntityMemoriesAsync(entityId, cancellationToken).ConfigureAwait(false);
    }

    static IReadOnlyList<ConversationMessage> normalizeMessages(
        IReadOnlyList<ConversationMessage> messages,
        bool stripSystemMessages)
    {
        if (!stripSystemMessages)
            return messages;

        var filtered = new List<ConversationMessage>(messages.Count);

        foreach (var message in messages)
            if (!string.Equals(message.Role, ConversationRoles.System, StringComparison.OrdinalIgnoreCase))
                filtered.Add(message);

        return filtered;
    }
}
