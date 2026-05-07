using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Search;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System.Linq.Expressions;

namespace Memori;

/// <summary>
/// Main Memori facade for attribution, sessions, and durable conversation capture.
/// </summary>
public sealed class Memori
{
    readonly IConversationStorage conversationStorage;
    readonly VectorStoreCollection<string, MemoryFactRecord> factCollection;
    readonly MemoriOptions options;
    readonly MemorySearchService memorySearchService;
    readonly AugmentationService? augmentationService;
    readonly object gate = new();

    Attribution? attribution;
    string? sessionId;
    string? scope;

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
    /// Gets the current workspace scope, if one has been configured.
    /// </summary>
    public string? CurrentScope
    {
        get
        {
            lock (gate)
            {
                return scope;
            }
        }
    }

    /// <summary>
    /// Creates a new Memori facade.
    /// </summary>
    public Memori(
        IConversationStorage conversationStorage,
        VectorStoreCollection<string, MemoryFactRecord> factCollection,
        MemoriOptions? options = null,
        string? sessionId = null,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        string? scope = null)
    {
        this.conversationStorage = conversationStorage ?? throw new ArgumentNullException(nameof(conversationStorage));
        this.factCollection = factCollection ?? throw new ArgumentNullException(nameof(factCollection));
        this.options = options ?? new MemoriOptions();
        this.options.Validate();
        memorySearchService = new MemorySearchService(factCollection, embeddingGenerator, this.options);
        this.sessionId = sessionId;
        this.scope = scope;
        augmentationService = augmentationClient is null
            ? null
            : new AugmentationService(conversationStorage, factCollection, augmentationClient, embeddingGenerator, this.options);
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
    /// Sets the workspace scope for subsequent recall operations.
    /// When set, only facts matching this scope are returned by recall.
    /// </summary>
    public void SetScope(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Value cannot be empty.", nameof(scope));

        lock (gate)
        {
            this.scope = scope;
        }
    }

    /// <summary>
    /// Clears the workspace scope, allowing recall across all scopes.
    /// </summary>
    public void ClearScope()
    {
        lock (gate)
        {
            scope = null;
        }
    }

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

        var entityId = await conversationStorage.GetOrCreateEntityAsync(currentAttribution.EntityId, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var processId = currentAttribution.ProcessId is null
            ? null
            : await conversationStorage.GetOrCreateProcessAsync(currentAttribution.ProcessId, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        var resolvedSessionId = await conversationStorage.GetOrCreateSessionAsync(
                currentSessionId,
                entityId,
                processId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var conversation = await conversationStorage.GetOrCreateConversationAsync(
                resolvedSessionId,
                options.SessionTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        var capturedMessages = normalizeMessages(messages, options.StripSystemMessagesOnCapture);

        if (capturedMessages.Count == 0)
            return;

        await conversationStorage.AppendMessagesAsync(conversation.Id, capturedMessages, cancellationToken)
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
    /// Recalls relevant facts for the current attribution context, optionally filtered by scope.
    /// </summary>
    public async ValueTask<IReadOnlyList<RecallResult>> RecallAsync(
        string query,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        Attribution? currentAttribution;
        string? currentScope;
        lock (gate)
        {
            currentAttribution = attribution;
            currentScope = scope;
        }

        if (currentAttribution is null)
            return Array.Empty<RecallResult>();

        return await memorySearchService.RecallAsync(
                currentAttribution.EntityId,
                query,
                limit,
                currentScope,
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
    /// Deletes durable memories for the current attribution context, optionally filtered by scope.
    /// </summary>
    public async ValueTask DeleteEntityMemoriesAsync(CancellationToken cancellationToken = default)
    {
        Attribution? currentAttribution;
        string? currentScope;
        lock (gate)
        {
            currentAttribution = attribution;
            currentScope = scope;
        }

        if (currentAttribution is null)
            return;

        var entityId = await conversationStorage.GetOrCreateEntityAsync(currentAttribution.EntityId, currentScope, cancellationToken)
            .ConfigureAwait(false);

        // Search for all facts for this entity and delete them by key
        Expression<Func<MemoryFactRecord, bool>> filter = currentScope is null
            ? r => r.EntityId == entityId
            : r => r.EntityId == entityId && r.Scope == currentScope;

        var searchOptions = new VectorSearchOptions<MemoryFactRecord>
        {
            Filter = filter
        };

        var searchResults = factCollection.SearchAsync(new ReadOnlyMemory<float>(new float[1536]), 1000, searchOptions, cancellationToken);

        var keysToDelete = new List<string>();
        await foreach (var result in searchResults)
        {
            keysToDelete.Add(result.Record.Id);
        }

        if (keysToDelete.Count > 0)
        {
            await factCollection.DeleteAsync(keysToDelete, cancellationToken).ConfigureAwait(false);
        }
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
