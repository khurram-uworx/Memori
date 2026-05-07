# Memori for .NET

> Durable memory for AI applications.
> Built on `Microsoft.Extensions.AI` with a pluggable storage model and zero provider lock-in.

Memori adds a persistent, searchable memory layer to your AI app: capture conversations, extract structured facts in the background, recall relevant context at query time, and inject it into your prompt pipeline automatically.

## Why It Exists

`Microsoft.Extensions.AI` gives you a clean `IChatClient` abstraction, but it leaves a gap between a single-turn chat call and a memory-aware assistant that remembers users across sessions.

Memori fills that gap with:

- A `Memori` facade for attribution, session tracking, capture, recall, and augmentation
- `IChatClient` middleware that wires recall and capture into any provider automatically
- `IConversationStorage` for conversation/session/entity/process data — bring your own backend
- `VectorStoreCollection<string, MemoryFactRecord>` for durable fact storage via any `Microsoft.Extensions.VectorData` provider
- Built-in `InMemoryConversationStorage` and `InMemoryVectorStore` for tests, demos, and local development
- No first-party database integrations — implement `IConversationStorage` and supply a `VectorStore` provider

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
// Dependency injection
services.AddMemori(options =>
{
    options.SessionTimeout = TimeSpan.FromMinutes(30);
    options.PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemAndDeveloperMessages;
});

// Custom conversation storage, embeddings, and augmentation
services.AddMemori(
    sp => new MyConversationStorage(),
    configureOptions: options => { options.RecallRelevanceThreshold = 0.2; });
```

```csharp
// Capture policy — filter and redact before storage
services.AddMemori(options =>
{
    options.ExcludedCaptureRoles.Add(ConversationRoles.Tool);
    options.DropEmptyMessagesOnCapture = true;
    options.CaptureMessageTransform = message => new ConversationMessage(
        message.Role,
        message.Content.Replace("secret-token", "[redacted]", StringComparison.OrdinalIgnoreCase),
        message.Type, message.CreatedAt, message.Metadata);
});
```

## Documentation

- [GETTING-STARTED.md](GETTING-STARTED.md): installation, Hello World, facade usage, middleware, DI, capture policy, storage, embeddings, and augmentation
- [ARCHITECTURE.md](ARCHITECTURE.md): design principles, storage contract (split into `IConversationStorage` + `VectorStoreCollection`), augmentation pipeline, recall/search model, and middleware semantics

## Packages

- [`Memori`](https://www.nuget.org/packages/Memori): core memory primitives, facade, `IChatClient` middleware, and DI integration

## Status

Memori is in active early development. Phase 1 (core primitives, `IChatClient` middleware, and augmentation) and Phase 2 Tier 1 (VectorStore foundation) are complete:

- Core memory primitives, the `Memori` facade, and `IChatClient` middleware are implemented.
- Storage is now split into `IConversationStorage` (conversations, sessions, entities, processes) and `VectorStoreCollection<string, MemoryFactRecord>` (durable facts with vector search).
- Built-in `InMemoryConversationStorage` and `InMemoryVectorStore` implementations for development and testing.
- Embedding abstraction with `Microsoft.Extensions.AI` adapter.
- Recall/search with cosine, lexical, and hybrid ranking.
- Augmentation boundary with `PromptAugmentationClient` and background augmentation service.
- Full DI integration with `AddMemori(...)` and `UseMemori(...)`.
- 71 NUnit tests covering all major paths.

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

Sessions group capture/history. Recall and delete operations remain scoped to the current attribution entity.

Example prompt context:

```text
<memori_context>
Only use the relevant context if it is relevant to the user's query.
Relevant context about the user:
- The user's favorite color is blue. Stated at 2026-05-06 01:00:00
</memori_context>
```

`BuildPromptContext(...)` returns structured facts, summaries, rendering metadata, and the final rendered text. `FormatPromptContext(...)` remains available when you only need the rendered string. Formatting can be customized with options such as `PromptFactBullet`, `PromptSummaryBullet`, `PromptTimestampFormat`, `PromptFactsHeading`, `PromptSummariesHeading`, and `IncludeSummariesInPrompt`.

Capture policy is applied after provider messages are converted to Memori's durable `ConversationMessage` model. Hosts can drop roles, omit empty messages, provide a custom predicate, or redact/transform messages before storage:

```csharp
services.AddMemori(options =>
{
    options.ExcludedCaptureRoles.Add(ConversationRoles.Tool);
    options.DropEmptyMessagesOnCapture = true;
    options.CaptureMessageFilter = message => message.Role != "developer";
    options.CaptureMessageTransform = message => new ConversationMessage(
        message.Role,
        message.Content.Replace("secret-token", "[redacted]", StringComparison.OrdinalIgnoreCase),
        message.Type,
        message.CreatedAt,
        message.Metadata);
});
```

Provider-native filtering before conversion is intentionally deferred until concrete provider scenarios require it.

Provider response metadata is copied to assistant messages with `memori.provider.*` metadata keys when exposed by Microsoft.Extensions.AI. Memori stores response ids, provider conversation ids, model ids, response timestamps, finish reasons, usage objects, continuation tokens, and response additional properties. Streaming responses are reconstructed after completion and also preserve observed update response ids, message ids, and continuation tokens. Raw provider objects are not normalized or stored by default because they are provider-specific and may not be durable.

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

- memory facts (with embeddings, confidence, memory type, summaries)
- semantic triples (stored as `MemoryFactRecord` with `MemoryType = "semantic_triple"`)
- process attributes (stored as `MemoryFactRecord` with `MemoryType = "process_attribute"`)

This is a standard `Microsoft.Extensions.VectorData` collection. Any `VectorStore` provider (Azure AI Search, Qdrant, etc.) works directly — no Memori-specific adapter needed. `InMemoryVectorStore` ships as the in-memory default.

Storage implementers should start with [ARCHITECTURE.md](ARCHITECTURE.md). The test project also exposes `ConversationStorageContractTests`, an abstract NUnit fixture for `IConversationStorage` implementations.

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

## Repository Layout

```text
src/
  Memori/
    Abstractions/
    Embeddings/
    Augmentation/
    Models/
    MicrosoftExtensionsAI/
    Search/
    Storage/
    Memori.csproj
  Memori.Tests/
    Memori.Tests.csproj
```

## Contributing

- Keep the core package storage-provider agnostic
- Preserve `Memori` facade ergonomics for attribution, capture, recall, and augmentation
- Keep `IChatClient` middleware behavior correct for both standard and streaming flows
- See `AGENTS.md` for project-specific implementation and review guardrails

## License

Apache-2.0
