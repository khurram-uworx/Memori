# Memori for .NET

> Durable memory for AI applications.
> Built on `Microsoft.Extensions.AI` with a pluggable storage model and zero provider lock-in.

Memori adds a persistent, searchable memory layer to your AI app: capture conversations, extract structured facts in the background, recall relevant context at query time, and inject it into your prompt pipeline automatically.

## What It Does

- **Attribution & sessions** — identify who memory belongs to and group capture history.
- **Capture** — persist conversation turns to durable storage.
- **Recall** — retrieve relevant facts using vector or lexical search.
- **Augmentation** — extract facts, semantic triples, process attributes, and summaries in the background.
- **Injection** — format recalled context and inject it into an `IChatClient` pipeline automatically.
- **Scope isolation** — isolate memory by workspace or team.
- **Versioning & conflict resolution** — detect and resolve concurrent memory updates.
- **Thread summarization** — generate conversation summaries via `IChatClient`.
- **Memory management** — list, search, edit, soft-delete, and restore stored memories.

All through a composable middleware pipeline, with no provider lock-in.

## Quick Taste

```csharp
// Wrap any IChatClient with memory
IChatClient client = new ChatClientBuilder(yourProvider)
    .UseMemori(memori)
    .Build();

// Turn 1 — user shares a preference
await client.CompleteAsync([new ChatMessage(ChatRole.User, "My favorite color is blue.")]);
await memori.WaitForAugmentationAsync();

// Turn 2 — Memori recalls and injects context automatically
var response = await client.CompleteAsync([new ChatMessage(ChatRole.User, "What's my favorite color?")]);
// → "Your favorite color is blue."
```

```csharp
// Facade usage — capture and recall without middleware
memori.Attribution("user_123");
memori.SetSession("session_abc");

await memori.CaptureAsync([
    new ConversationMessage(ConversationRoles.User, "I prefer dark mode."),
    new ConversationMessage(ConversationRoles.Assistant, "Noted.")
]);

var recalled = await memori.RecallAsync("What are the user's UI preferences?");
var context = memori.BuildPromptContext(recalled);
Console.WriteLine(context.RenderedText);
```

```csharp
// Dependency injection — zero config
services.AddMemori(options =>
{
    options.SessionTimeout = TimeSpan.FromMinutes(30);
});
```

## Documentation

- [GETTING-STARTED.md](GETTING-STARTED.md): installation, Hello World, facade usage, middleware, DI, capture policy, storage, embeddings, augmentation, and all Phase 2 features.
- [ARCHITECTURE.md](ARCHITECTURE.md): design principles, storage contract, augmentation pipeline, recall/search model, middleware semantics, and extension points.

## Packages

- [`Memori`](https://www.nuget.org/packages/Memori): core memory primitives, facade, `IChatClient` middleware, and DI integration.

## Status

All Phase 1 and Phase 2 features are implemented:

**Phase 1 — Core primitives:**
- Attribution, sessions, and conversation lifecycle management.
- Capture and recall primitives.
- `IConversationStorage` with `InMemoryConversationStorage` reference implementation.
- `VectorStoreCollection<string, MemoryFactRecord>` for fact storage via any `Microsoft.Extensions.VectorData` provider.
- `InMemoryVectorStore` reference implementation.
- `IEmbeddingGenerator` adapter and `DeterministicEmbeddingGenerator`.
- Recall/search with cosine, lexical, and hybrid ranking.
- `IMemoryRanker` abstraction with `DefaultMemoryRanker`.
- `IAugmentationClient` with `PromptAugmentationClient` and `NullAugmentationClient`.
- Background augmentation service.
- `IChatClient` middleware for recall, injection, and capture.
- Streaming support with cancellation semantics.
- Full DI integration with `AddMemori(...)` and `UseMemori(...)`.

**Phase 2 — Enterprise and scale:**
- Distributed ranker (`IDistributedRanker`, `DefaultDistributedRanker`) for merging results from multiple backends.
- Composite memory collection (`CompositeMemoryCollection`) for querying multiple vector stores in parallel.
- Workspace/scope isolation for multi-tenant memory.
- Versioning and conflict resolution (last-write-wins, merge, manual) with audit trail.
- `IThreadSummarizer` and `ChatClientThreadSummarizer` for conversation summarization.
- `IMemoryManagementService` for user-facing memory inspection, search, edit, soft-delete, and restore.

No first-party database integrations are included. Implement `IConversationStorage` and supply a `VectorStore` provider in your own package.

## Requirements

- .NET 10 SDK or newer.

## Build and Test

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

## Facade Usage

Use `Memori` when you want attribution, session tracking, capture, recall, and optional augmentation in one place.

```csharp
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;

var conversationStorage = new InMemoryConversationStorage();
var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

var memori = new Memori.Memori(
    conversationStorage,
    factCollection,
    new MemoriOptions
    {
        StripSystemMessagesOnCapture = true
    },
    augmentationClient: new NullAugmentationClient());

memori.Attribution("user_123", "support_agent");
memori.SetSession("session_abc");

await memori.CaptureAsync(new[]
{
    new ConversationMessage(ConversationRoles.User, "My favorite color is blue."),
    new ConversationMessage(ConversationRoles.Assistant, "Noted.")
});

var recalled = await memori.RecallAsync("What is my favorite color?");
await memori.WaitForAugmentationAsync();

var promptContext = memori.BuildPromptContext(recalled);
Console.WriteLine(promptContext.RenderedText);
```

Reusable `Memori` instances expose their current lifecycle state and can be cleared or resumed explicitly:

```csharp
var currentAttribution = memori.CurrentAttribution;
var currentSessionId = memori.CurrentSessionId;

memori.ClearAttribution();
memori.ClearSession();
memori.ResumeSession("session_abc");
```

Example prompt context:

```text
<memori_context>
Only use the relevant context if it is relevant to the user's query.
Relevant context about the user:
- The user's favorite color is blue. Stated at 2026-05-06 01:00:00
</memori_context>
```

`BuildPromptContext(...)` returns structured facts, summaries, rendering metadata, and the final rendered text. `FormatPromptContext(...)` remains available when you only need the rendered string. Formatting can be customized with options such as `PromptFactBullet`, `PromptSummaryBullet`, `PromptTimestampFormat`, `PromptFactsHeading`, `PromptSummariesHeading`, and `IncludeSummariesInPrompt`.

### Scope Isolation

Isolate memory by workspace or team:

```csharp
memori.Attribution("user_123");
memori.SetScope("workspace-a");

// Only facts in "workspace-a" are returned
var recalled = await memori.RecallAsync("coffee");

memori.ClearScope();
// All scopes are searched when no scope is set
```

### Memory Management

Inspect, search, edit, soft-delete, and restore stored memories:

```csharp
var management = serviceProvider.GetRequiredService<IMemoryManagementService>();

// List all memories for an entity
var memories = await management.ListMemoriesAsync("entity-1");

// Search by content
var results = await management.SearchMemoriesAsync("entity-1", "coffee");

// Soft-delete a memory
await management.SoftDeleteMemoryAsync("fact-id");

// Restore a soft-deleted memory
await management.RestoreMemoryAsync("fact-id");

// Permanently delete
await management.HardDeleteMemoryAsync("fact-id");
```

## Microsoft.Extensions.AI Integration

Memori ships a chat pipeline wrapper that recalls before a model call and captures after it completes.

```csharp
using Memori;
using Microsoft.Extensions.AI;

IChatClient innerClient = /* your provider-backed IChatClient */;

IChatClient client = new ChatClientBuilder(innerClient)
    .UseMemori(memori)
    .Build(serviceProvider);
```

Provider-specific integrations are intentionally out of scope. Any provider that exposes or can be adapted to `IChatClient` should work through the same Memori middleware.

By default, recalled memory is injected as a `system` message before the existing chat history. Hosts can change the injected role, insert it after existing system/developer instructions, append it to the end of the request, merge it into an existing instruction message, or disable prompt injection while leaving capture behavior available.

For dependency injection:

```csharp
using Memori;
using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;

services.AddMemori(options =>
{
    options.SessionTimeout = TimeSpan.FromMinutes(30);
    options.PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemAndDeveloperMessages;
    options.PromptInjectionRole = "developer";
});

var memori = serviceProvider.CreateMemori();
```

You can also bind options from standard .NET configuration:

```csharp
services.AddMemori(configuration.GetSection("Memori"));
```

Custom conversation storage, embedding, and augmentation implementations can be supplied through factories:

```csharp
services.AddMemori(
    sp => new MyConversationStorage(),
    configureOptions: options =>
    {
        options.RecallRelevanceThreshold = 0.2;
    });
```

For full control including a custom fact collection and embedding generator:

```csharp
services.AddMemori(
    conversationStorageFactory: sp => new MyConversationStorage(),
    factCollectionFactory: sp => myVectorStore.GetCollection<string, MemoryFactRecord>("facts"),
    embeddingGeneratorFactory: sp => myEmbeddingGenerator,
    augmentationClientFactory: sp => myAugmentationClient,
    configureOptions: options => { options.RecallRelevanceThreshold = 0.2; });
```

## Storage Model

Memori splits persistent storage into two concerns:

### `IConversationStorage`

Covers the relational/ordered operations that do not benefit from vector search:

- entities
- processes
- sessions
- conversations
- conversation messages
- conversation summaries

`InMemoryConversationStorage` is the reference implementation for tests, demos, and local development. Implement `IConversationStorage` in your own package for production backends.

### `VectorStoreCollection<string, MemoryFactRecord>`

Covers durable fact storage with vector and lexical search:

- memory facts (with embeddings, confidence, memory type, summaries, scope, versioning)
- semantic triples (stored as `MemoryFactRecord` with `MemoryType = "semantic_triple"`)
- process attributes (stored as `MemoryFactRecord` with `MemoryType = "process_attribute"`)

This is a standard `Microsoft.Extensions.VectorData` collection. Any `VectorStore` provider (Azure AI Search, Qdrant, etc.) works directly — no Memori-specific adapter needed. `InMemoryVectorStore` ships as the in-memory default.

Storage implementers should start with [ARCHITECTURE.md](ARCHITECTURE.md).

## Embeddings

Memori relies directly on `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`.

Included implementation:

- `DeterministicEmbeddingGenerator`: dependency-free vectors for tests and local demos.

To run lexical-only recall, omit embedding generator registration.

Production embedding providers should be supplied by the consuming application.

## Augmentation

Memori includes:

- `NullAugmentationClient`: no-op augmentation for hosts that only want capture/recall plumbing.
- `PromptAugmentationClient`: built-in prompt-based extraction client that uses an `IChatClient` and expects JSON output for facts, semantic triples, process attributes, and optional conversation summaries.

Hosts can also implement `IAugmentationClient` to use custom extraction logic.

See [ARCHITECTURE.md](ARCHITECTURE.md) for the custom augmentation contract, mapping helpers, and idempotency guidance.

## Versioning and Conflict Resolution

Memori tracks record versions and provides three conflict resolution strategies for concurrent memory updates:

```csharp
var versioning = new VersioningService(ConflictResolutionStrategy.LastWriteWins);

var existing = await factCollection.GetAsync("fact-id");
var resolution = versioning.ResolveConflict(incoming, existing, expectedVersion: 1);

// Resolution strategies:
// - LastWriteWins: the latest write overwrites (default)
// - Merge: conflicting content is combined
// - Manual: conflicts are flagged for external review
```

Each record carries a `Version` integer, a `PreviousVersionId` for audit trail traversal, and an `IsDeleted` flag for soft-delete support.

## Thread Summarization

Generate conversation summaries using any `IChatClient`:

```csharp
var summarizer = new ChatClientThreadSummarizer(chatClient);

// Initial summary
var summary = await summarizer.SummarizeAsync(messages);

// Rolling summary (incorporates previous summary for continuity)
var updated = await summarizer.SummarizeAsync(newMessages, previousSummary);
```

Summaries are stored as `MemoryFactRecord` entries with `MemoryType = "summary"`.

## Contributing

- Keep the core package storage-provider agnostic
- Preserve `Memori` facade ergonomics for attribution, capture, recall, and augmentation
- Keep `IChatClient` middleware behavior correct for both standard and streaming flows
- See `AGENTS.md` for project-specific implementation and review guardrails

## License

Apache-2.0
