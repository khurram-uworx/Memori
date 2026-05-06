# Memori for .NET

Memori for .NET is a library for adding durable memory to AI applications.

This repository is currently migrating the core Memori library surface to C# with a narrow scope:

- Microsoft.Extensions.AI integration as the primary LLM surface.
- A pluggable `IStorage` abstraction for durable memory.
- A built-in in-memory storage implementation for tests, demos, and local development.
- No first-party database integrations in this package.

If you want PostgreSQL, SQL Server, Cosmos DB, MongoDB, SQLite, Redis, or another backend, implement `IStorage` in your own package or application and pass it to Memori.

## Status

This .NET port now includes the core memory primitives, the `Memori` facade, NUnit tests, and Microsoft.Extensions.AI middleware.

Completed:

- .NET 10 class library project under `src/Memori`.
- Core models and options.
- Domain-oriented `IStorage` contract.
- Thread-safe `InMemoryStorage`.
- Embedding abstraction with Microsoft.Extensions.AI adapter.
- Recall/search service with cosine, lexical, relevance filtering, and prompt formatting.
- Conversation capture facade.
- Augmentation boundary.
- Microsoft.Extensions.AI `IChatClient` middleware.
- NUnit test project under `src/Memori.Tests`.

## Requirements

- .NET 10 SDK or newer.

## Build

```bash
dotnet build src/Memori/Memori.csproj
```

## Current Low-Level Usage

The current implemented surface can store facts in memory, generate deterministic local embeddings, recall relevant facts, and format a prompt context block.

```csharp
using Memori.Embeddings;
using Memori.Models;
using Memori.Search;
using Memori.Storage;

var storage = new InMemoryStorage();
var embeddings = new DeterministicEmbeddingGenerator();

var entityId = await storage.GetOrCreateEntityAsync("user_123");
var factEmbedding = await embeddings.GenerateEmbeddingAsync(
    "The user's favorite color is blue");

await storage.AddFactsAsync(
    entityId,
    new[]
    {
        new NewMemoryFact(
            "The user's favorite color is blue",
            factEmbedding)
    });

var search = new MemorySearchService(
    storage,
    embeddings,
    new MemoriOptions
    {
        RecallRelevanceThreshold = 0.1
    });

var results = await search.RecallAsync(
    entityId,
    "What is my favorite color?");

var promptContext = search.FormatPromptContext(results);
Console.WriteLine(promptContext);
```

## Facade Usage

Use `Memori` when you want attribution, session tracking, capture, recall, and optional augmentation in one place.

```csharp
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;

var memori = new Memori.Memori(
    new InMemoryStorage(),
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
```

Example prompt context:

```text
<memori_context>
Only use the relevant context if it is relevant to the user's query.
Relevant context about the user:
- The user's favorite color is blue. Stated at 2026-05-06 01:00:00
</memori_context>
```

## Microsoft.Extensions.AI Integration

Memori ships a chat pipeline wrapper that recalls before a model call and captures after it completes.

```csharp
using Memori.MicrosoftExtensionsAI;
using Microsoft.Extensions.AI;

IChatClient innerClient = /* your provider-backed IChatClient */;

IChatClient client = new ChatClientBuilder(innerClient)
    .UseMemori(memori)
    .Build(serviceProvider);
```

Provider-specific integrations are intentionally out of scope. Any provider that exposes or can be adapted to `IChatClient` should work through the same Memori middleware.

For dependency injection:

```csharp
using Memori.MicrosoftExtensionsAI;

services.AddMemori(options =>
{
    options.SessionTimeout = TimeSpan.FromMinutes(30);
});
```

## Storage Model

`IStorage` is the extension point for durable memory.

The contract is domain-oriented and async. It stores:

- entities
- processes
- sessions
- conversations
- conversation messages
- memory facts
- semantic triples
- process attributes

It also owns `SearchFactsAsync`, so each storage provider can use the best native ranking strategy available to that backend.

The library does not expose SQL commands, migrations, connections, transaction handles, or provider dialects.

## Embeddings

Memori exposes `IMemoriEmbeddingGenerator`, a small abstraction that returns `float` vectors or `null`.

Included implementations:

- `DeterministicEmbeddingGenerator`: dependency-free vectors for tests and local demos.
- `NullEmbeddingGenerator`: lexical-only mode.
- `MicrosoftEmbeddingGeneratorAdapter`: adapts `Microsoft.Extensions.AI.IEmbeddingGenerator<string, Embedding<float>>`.

Production embedding providers should be supplied by the consuming application.

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

## License

Apache-2.0
