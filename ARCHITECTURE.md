# Memori Architecture

This document covers the design intent, behavioral semantics, and implementation notes for Memori.

## Core Model

Memori is a durable memory layer for AI applications. Its primary concerns are:

- **Capture**: persist conversation messages to a durable store.
- **Recall**: retrieve relevant facts for a given query using semantic and/or lexical search.
- **Augmentation**: extract structured memory (facts, triples, summaries) from captured conversations in the background.
- **Injection**: format recalled memory as a prompt context block and inject it into an `IChatClient` pipeline.

The library is built around three extension points:

- `IConversationStorage`: the durable boundary for conversation/session/entity/process data.
- `VectorStoreCollection<string, MemoryFactRecord>`: the durable boundary for fact storage, using `Microsoft.Extensions.VectorData`.
- `IEmbeddingGenerator<string, Embedding<float>>`: the embedding surface, taken directly from `Microsoft.Extensions.AI`.
- `IAugmentationClient`: the extraction boundary for turning conversations into structured memory.

## Design Principles

- **Storage-provider agnostic**: no SQL commands, migrations, connection handles, or provider dialects leak through `IConversationStorage` or the `VectorStore` abstraction.
- **Microsoft.Extensions.AI native**: `IChatClient` and `IEmbeddingGenerator` are the primary integration surfaces. No provider-specific wrappers.
- **Microsoft.Extensions.VectorData native**: any `VectorStore` provider works directly for fact storage — no Memori-specific adapter needed.
- **Facade ergonomics**: the `Memori` class provides attribution, session tracking, capture, recall, and augmentation in one place without forcing callers to wire each service manually.
- **Explicit ownership**: session timeout, prompt injection placement, capture filtering, and augmentation are all opt-in and configurable.
- **No first-party database integrations**: PostgreSQL, SQL Server, Cosmos DB, MongoDB, SQLite, Redis, and similar backends are intentionally out of scope for this package.

## Storage Contract

Memori splits persistent storage into two concerns:

### `IConversationStorage`

The durable boundary for relational/ordered operations. Storage providers should keep provider details behind the interface and expose domain behavior only.

**Core Expectations:**
- Implementations must be safe for concurrent use by multiple requests.
- All methods must observe the supplied `CancellationToken`.
- Public identifiers returned by storage must be stable and non-empty.
- Get-or-create methods must be idempotent for the same logical identifier.
- Each operation should be internally atomic from the caller's point of view. The interface intentionally does not expose transactions.
- Conversation history is durable history. Fact deletion does not affect captured conversation messages.
- All entity, process, and session operations accept an optional `scope` parameter for workspace or multi-tenant isolation. When set, scope acts as a partition key that restricts subsequent recall and management operations.

**Domain Objects:**

- entities
- processes
- sessions
- conversations
- conversation messages
- conversation summaries

**Methods:**

`GetOrCreateEntityAsync(externalId)` creates or returns the durable entity row for an external entity such as a user id. The same `externalId` must always return the same storage id.

`GetOrCreateProcessAsync(externalId)` creates or returns a durable process/workflow row. The same `externalId` must always return the same storage id.

`GetOrCreateSessionAsync(sessionId, entityId, processId)` creates or returns a capture/history grouping. If later calls provide missing entity or process ids for an existing session, implementations should associate them without replacing existing associations.

`GetOrCreateConversationAsync(sessionId, timeout)` returns the latest conversation for a session when its `UpdatedAt` is within `timeout`; otherwise it creates a new conversation. `timeout` must be greater than zero.

`AppendMessagesAsync(conversationId, messages)` appends messages in the order supplied and updates the conversation's `UpdatedAt` timestamp. It must not reorder or de-duplicate messages.

`GetConversationMessagesAsync(conversationId)` returns all messages for a conversation in insertion order. Returns an empty list for a new conversation.

`UpdateConversationSummaryAsync(conversationId, summary)` stores the latest rolling summary and updates the conversation timestamp.

### `VectorStoreCollection<string, MemoryFactRecord>`

The durable boundary for fact storage. This is a standard `Microsoft.Extensions.VectorData` collection — any `VectorStore` provider works directly.

**Domain Objects:**

- memory facts (with embeddings, confidence, memory type, summaries)
- semantic triples (stored as `MemoryFactRecord` with `MemoryType = "semantic_triple"`)
- process attributes (stored as `MemoryFactRecord` with `MemoryType = "process_attribute"`)

**Operations:**

- `UpsertAsync` — insert or update a fact record.
- `SearchAsync` — search by vector (embedding) or text query with optional filter (e.g., by `EntityId`).
- `DeleteAsync` — delete by record key.

### Storage Contract Tests

The test project includes `ConversationStorageContractTests`, an abstract NUnit fixture that specifies the expected `IConversationStorage` behavior. To test a custom implementation inside this repository, derive from it and return a fresh storage instance:

```csharp
using Memori.Abstractions;
using NUnit.Framework;

namespace Memori.Tests;

public sealed class MyConversationStorageTests : ConversationStorageContractTests
{
    protected override IConversationStorage CreateConversationStorage()
        => new MyConversationStorage(/* connection or test fixture dependencies */);
}
```

Each test expects an isolated storage instance. For database-backed providers, create a unique database/schema/container per test or clean all state in `CreateConversationStorage`.

### Minimal Custom Storage Registration

```csharp
using Memori;
using Memori.Abstractions;
using Microsoft.Extensions.DependencyInjection;

services.AddSingleton<IConversationStorage, MyConversationStorage>();
services.AddMemori();
```

For production vector stores, register a `VectorStore` provider before calling `AddMemori()`:

```csharp
services.AddSingleton<VectorStore>(sp => new MyProductionVectorStore(endpoint, credential));
services.AddMemori();
```

`InMemoryConversationStorage` and `InMemoryVectorStore` are the reference behaviors for semantics, not production database drivers.

## Augmentation Pipeline

`IAugmentationClient` turns captured conversation messages into durable memory updates. Memori supplies the conversation context and writes the returned output through `IConversationStorage` (for summaries) and `VectorStoreCollection<string, MemoryFactRecord>` (for facts, triples, and attributes).

### Contract

An augmentation client receives an `AugmentationInput` with:

- `EntityId`: storage id for the current attribution entity.
- `ProcessId`: optional storage id for the current process/workflow.
- `ConversationId`: storage id for the conversation being augmented.
- `Messages`: converted conversation messages that were captured.
- `ConversationSummary`: optional previous summary when available.

It returns an `AugmentationResult` with any combination of:

- `Facts`: entity-scoped facts for recall.
- `SemanticTriples`: entity-scoped triples.
- `ProcessAttributes`: process/workflow attributes. Written only when `ProcessId` is available.
- `ConversationSummary`: replacement rolling summary for the conversation.

Return `null` when there is nothing useful to write.

### Mapping Helpers

`AugmentationMapper` provides small helpers for common extraction output:

```csharp
using Memori.Abstractions;
using Memori.Augmentation;

public sealed class MyAugmentationClient : IAugmentationClient
{
    public ValueTask<AugmentationResult?> AugmentAsync(
        AugmentationInput context,
        CancellationToken cancellationToken = default)
    {
        var summary = AugmentationMapper.ToSummary("User prefers coffee.");
        var fact = AugmentationMapper.ToFact(
            "User prefers coffee.",
            memoryType: "preference",
            summaries: summary is null ? null : [summary]);
        var triple = AugmentationMapper.ToSemanticTriple(
            "user", "person", "prefers", "coffee", "drink");
        var attributes = AugmentationMapper.ToProcessAttributes(["support", "triage"]);

        return ValueTask.FromResult<AugmentationResult?>(
            new AugmentationResult(
                Facts: fact is null ? null : [fact],
                SemanticTriples: triple is null ? null : [triple],
                ProcessAttributes: attributes,
                ConversationSummary: "User prefers coffee."));
    }
}
```

The helpers trim values, return `null` for incomplete facts/triples/summaries, and de-duplicate process attributes.

### Idempotency and Deduplication

Memori calls augmentation after capture. If a host retries a request, replays history, or uses background workers, the same logical memory may be generated more than once.

Current expectations:

- `IAugmentationClient` implementations should prefer deterministic extraction.
- Storage providers may de-duplicate facts or triples if their backend supports stable keys.
- App-specific augmentation clients should avoid generating low-value repeated facts.
- Process attributes should be safe to repeat; the in-memory reference stores them as a set.
- Conversation summaries are replacement values, not append-only values.

For durable production stores, consider deriving a stable key from entity id, normalized content, memory type, and source conversation id when writing facts or triples.

### Built-In Augmentation Clients

- `NullAugmentationClient`: no-op, for hosts that only want capture/recall plumbing.
- `PromptAugmentationClient`: asks an `IChatClient` for JSON containing facts, semantic triples, process attributes, and a conversation summary. It ignores malformed JSON and filler values. For production extraction, tune the prompt, model, and output validation for your domain.

## Recall and Search

`MemorySearchService` orchestrates recall using `VectorStoreCollection<string, MemoryFactRecord>.SearchAsync` and an optional `IEmbeddingGenerator`.

- Vector search via `SearchAsync(ReadOnlyMemory<float>, ...)` when embeddings are available.
- Lexical search via `SearchAsync(string, ...)` fallback when no embedding generator is registered.
- Results are filtered by `RecallRelevanceThreshold` and returned in descending relevance order.
- `FormatPromptContext(...)` renders results as a `<memori_context>` block for prompt injection.
- `BuildPromptContext(...)` returns structured facts, summaries, rendering metadata, and the final rendered text.

### IMemoryRanker

`IMemoryRanker` is the public extension point for ranking memory candidates before they are returned to the caller. The interface defines a single method `double Rank(RecallResult result, DateTimeOffset now)`.

The default implementation `DefaultMemoryRanker` combines three signals into a final score:
- Base similarity or rank score from the vector or lexical search.
- Confidence boost (0–1, weighted at 20%).
- Recency boost that decays logarithmically with age (`0.1 / (1 + ageInDays)`).

Hosts can supply a custom ranker through DI to apply domain-specific ranking logic (e.g., boosting recent interactions, filtering by content category) without replacing the full search service.

### CompositeMemoryCollection

`CompositeMemoryCollection` is a `VectorStoreCollection<string, MemoryFactRecord>` that transparently wraps multiple backend collections. It enables multi-provider setups such as hybrid in-memory + production vector database.

- **Reads**: queries are fanned out to all backends in parallel. Results are deduplicated by record key and merged via `IDistributedRanker` for a unified ranked list.
- **Writes**: configurable via `CompositeWriteStrategy` — either all backends (`All`) or only the primary (`PrimaryOnly`).
- **Configuration**: `CompositeMemoryCollectionOptions` controls max concurrency, write strategy, ranking strategy, per-backend source weights, and the composite collection name.
- **Graceful degradation**: per-backend failures are isolated; remaining backends still return results.

Register a `CompositeMemoryCollection` through DI as the single `VectorStoreCollection<string, MemoryFactRecord>` that downstream services (search, management) consume.

## IChatClient Middleware

`MemoriChatClient` wraps any `IChatClient` and adds recall-before and capture-after behavior.

- Recall runs before the model call and injects a prompt context block.
- Capture runs after the model call completes (including streaming reconstruction).
- Injected memory context is not persisted as conversation content.
- Provider response metadata is copied to assistant messages with `memori.provider.*` metadata keys when exposed by `Microsoft.Extensions.AI`.

Prompt injection placement is configurable:

- Before existing history (default)
- After system and developer messages
- Appended to the end of the request
- Merged into an existing instruction message
- Disabled (capture only)

### Request-Scoped Control

`MemoriRequestOptions` allows per-request overrides of recall and capture behavior without changing the shared `MemoriOptions`. Pass it through `ChatOptions.Extensions` when you need to suppress recall, suppress capture, or override injection placement for a single call.

## Attribution and Session Model

- **Attribution** identifies the entity (e.g. user id) and optional process/workflow that memory belongs to.
- **Sessions** group capture history. `GetOrCreateConversationAsync` uses `SessionTimeout` to decide whether to continue an existing conversation or start a new one.
- Recall and delete operations are always scoped to the current attribution entity.
- Sessions only affect capture grouping and conversation history, not recall scope.

## Tier 3 Features

Tier 3 adds enterprise-oriented features on top of the core capture/recall/augmentation pipeline. These are opt-in components that build on the existing storage and search abstractions.

### Scope Isolation

Scope is a first-class concept for workspace or multi-tenant isolation. Every `MemoryFactRecord` carries an optional `Scope` string. When scope is active:

- Recall filters results to only return facts matching the current scope.
- Memory management operations (list, search, delete) respect scope boundaries.
- Conversation storage operations (entity, process, session) accept an optional scope parameter.

On the `Memori` facade, scope is controlled via `SetScope(string)` and `ClearScope()`. The current scope is accessible through `CurrentScope`. When scope is null, recall and management operate across all scopes (tenant-unaware mode).

Scope applies only to memory facts and conversation metadata — it does not control access to the underlying `IChatClient` or `IEmbeddingGenerator`.

### Versioning and Conflict Resolution

`VersioningService` provides optimistic concurrency control for `MemoryFactRecord` writes. Each record carries:

- `Version` — monotonic version number incremented on each update.
- `PreviousVersionId` — optional link to the prior version for audit trails.
- `IsDeleted` — soft-delete flag.

Conflict detection compares the caller's expected version against the current stored version. When a mismatch is detected, resolution follows a configurable strategy:

- `LastWriteWins` (default) — the most recent write succeeds unconditionally. High throughput, accepts occasional data loss.
- `Merge` — when content differs, conflicting text is combined with a semicolon delimiter.
- `Manual` — the conflict is flagged for human or external resolution; the new version is stored with a conflict marker.

The service is available for production database drivers where concurrent writes are a real concern. Wire it through `AugmentationService` when versioned writes are required.

### Thread Summarization

`IThreadSummarizer` is the extension point for generating conversation thread summaries. It defines two operations:

- Initial summarization from a message list.
- Rolling summarization that builds on a previous summary for continuity.

`ChatClientThreadSummarizer` is the default implementation. It leverages `IChatClient` to generate summaries by sending conversation messages with a system prompt. Configuration is handled through `ThreadSummarizationOptions`:

- `MaxMessagesPerSummary` (default: 50) — caps messages sent per summarization request.
- `IncludeTimestamps` — controls whether timestamps are prepended to messages.
- `SummaryMemoryType` (default: `"summary"`) — the `MemoryType` value for stored summary records.

Summaries can be stored as `MemoryFactRecord` entries and participate in recall. Integration into the augmentation pipeline is opt-in.

### Memory Management

`IMemoryManagementService` provides a user-facing API for inspecting, searching, editing, and deleting durable memories. It is designed to back settings pages, privacy dashboards, admin panels, or any interface where users need visibility into what the AI "remembers".

| Method | Description |
|---|---|
| `ListMemoriesAsync` | Paginated listing of all memories for an entity. |
| `SearchMemoriesAsync` | Full-text or vector search with optional type, scope, and soft-delete filters. |
| `GetMemoryAsync` | Single record lookup by memory ID. |
| `UpdateMemoryAsync` | Edit memory content. |
| `SoftDeleteMemoryAsync` | Logical delete (sets `IsDeleted = true`). |
| `HardDeleteMemoryAsync` | Physical delete from the backing store. |
| `RestoreMemoryAsync` | Undo a soft delete. |
| `GetMemoryCountAsync` | Total memory count for an entity. |

`MemoryManagementService` is the default implementation backed by a `VectorStoreCollection<string, MemoryFactRecord>`. The `Memori` facade exposes convenience methods (`ListMemoriesAsync`, `SearchMemoriesAsync`, etc.) that auto-scope to the current attribution entity when the management service is registered.

## Extension Package Boundaries

- `Memori` (core) has no first-party database dependencies.
- `IConversationStorage` backends and `VectorStore` providers are implemented by consuming applications or separate packages.
- `Microsoft.Extensions.AI` and `Microsoft.Extensions.VectorData.Abstractions` are the only framework dependencies in the core package.

## Implementation Notes

- `InMemoryConversationStorage` uses concurrent collections and is safe for concurrent use.
- `InMemoryVectorStore` is thread-safe and implements the standard `VectorStore` contract.
- Nullable reference types are enabled throughout; avoid introducing nullability warnings.
- All async APIs are cancellation-aware and use `ConfigureAwait(false)` in library code.
- Prefer domain-oriented APIs; do not leak provider-specific storage details into `Memori`.
