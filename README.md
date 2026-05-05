# Memori for .NET

Memori for .NET is a .NET 10 library for adding durable memory to AI applications.

This repository is currently migrating the core Memori library surface to C# with a narrow scope:

- Microsoft.Extensions.AI integration as the primary LLM surface.
- A pluggable `IStorage` abstraction for durable memory.
- A built-in in-memory storage implementation for tests, demos, and local development.
- No first-party database integrations in this package.

If you want PostgreSQL, SQL Server, Cosmos DB, MongoDB, SQLite, Redis, or another backend, implement `IStorage` in your own package or application and pass it to Memori.

## Status

This .NET port is in active migration. The core library skeleton and lower-level memory primitives are in place; the high-level `Memori` facade and `IChatClient` middleware are still upcoming.

Completed:

- .NET 10 class library project under `src/Memori`.
- Core models and options.
- Domain-oriented `IStorage` contract.
- Thread-safe `InMemoryStorage`.
- Embedding abstraction with Microsoft.Extensions.AI adapter.
- Recall/search service with cosine, lexical, relevance filtering, and prompt formatting.

Upcoming:

- Conversation capture service.
- Augmentation boundary.
- `Memori` facade.
- Microsoft.Extensions.AI `IChatClient` integration.
- Tests and usage documentation.

See [src/Plan.md](src/Plan.md) for the migration plan.

## Requirements

- .NET 10 SDK or newer.

The project currently targets:

```xml
<TargetFramework>net10.0</TargetFramework>
```

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

Example prompt context:

```text
<memori_context>
Only use the relevant context if it is relevant to the user's query.
Relevant context about the user:
- The user's favorite color is blue. Stated at 2026-05-06 01:00:00
</memori_context>
```

## Intended Microsoft.Extensions.AI Shape

The planned high-level API is an `IChatClient` wrapper that recalls before a model call and captures after it completes.

Planned usage shape:

```csharp
using Microsoft.Extensions.AI;

IChatClient innerClient = /* your provider-backed IChatClient */;

IChatClient client = new ChatClientBuilder(innerClient)
    .UseMemori(options =>
    {
        options.EntityId = "user_123";
        options.ProcessId = "support_agent";
    })
    .Build();
```

Provider-specific integrations are intentionally out of scope. Any provider that exposes or can be adapted to `IChatClient` should work through the same Memori middleware.

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
  Plan.md
  Memori/
    Abstractions/
    Embeddings/
    Models/
    Search/
    Storage/
    Memori.csproj
```

## License

Apache-2.0
