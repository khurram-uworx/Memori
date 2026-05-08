# Memori for .NET

> Durable memory for AI applications.
> Built on `Microsoft.Extensions.AI` with a pluggable storage model and zero provider lock-in.

Memori adds a persistent, searchable memory layer to your AI app: capture conversations, extract structured facts in the background, recall relevant context at query time, and inject it into your prompt pipeline automatically.

## When to Use

- AI apps that need persistent, searchable memory across sessions
- Chatbots and assistants that should remember user preferences and history
- Any `Microsoft.Extensions.AI`-based pipeline where you want recall-augmented generation

**Not a fit for:** single-turn completions with no memory requirement, or apps where the LLM provider already manages conversation history and no cross-session recall is needed.

## Quick Taste

```csharp
// Wrap any IChatClient with memory
IChatClient client = new ChatClientBuilder(yourProvider)
    .UseMemori(memori)
    .Build();

// Turn 1 — user shares a preference; capture happens after the model responds
await client.CompleteAsync([new ChatMessage(ChatRole.User, "My favorite color is blue.")]);

// Turn 2 — Memori recalls the stored fact and injects it before the model call
var response = await client.CompleteAsync([new ChatMessage(ChatRole.User, "What's my favorite color?")]);
// → "Your favorite color is blue."
```

## How It Works

Memori is built around three usage modes that build on each other:

| Mode | What it does | Get started |
|---|---|---|
| **Facade** (`Memori` class) | Programmatic attribution, capture, recall, and augmentation in one place | [GETTING-STARTED.md → The Memori Facade](GETTING-STARTED.md#the-memori-facade) |
| **Middleware** (`UseMemori`) | Automatic recall, prompt injection, and capture through any `IChatClient` pipeline | [GETTING-STARTED.md → Middleware](GETTING-STARTED.md#microsoft-extensionsai-middleware) |
| **Dependency injection** (`AddMemori`) | Wire everything from config, with factory overrides for storage, embeddings, and augmentation | [GETTING-STARTED.md → Dependency Injection](GETTING-STARTED.md#dependency-injection) |

## Features

| Area | Capabilities |
|---|---|
| **Capture & recall** | Conversation persistence, vector/lexical/hybrid search, composite collections, distributed ranking |
| **Augmentation** | Background extraction of facts, semantic triples, process attributes, and summaries via `IAugmentationClient` |
| **Middleware** | `IChatClient` pipeline that recalls, injects, and captures — streaming and non-streaming |
| **Storage** | `IConversationStorage` for conversations; `VectorStoreCollection<string, MemoryFactRecord>` for facts via any `Microsoft.Extensions.VectorData` provider |
| **Embeddings** | `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` — bring your own or use `DeterministicEmbeddingGenerator` for tests |
| **Scope isolation** | Workspace/team-level partitioning of memory |
| **Versioning** | Record versioning with last-write-wins, merge, and manual conflict resolution |
| **Summarization** | `IThreadSummarizer` for rolling conversation summaries via any `IChatClient` |
| **Memory management** | List, search, edit, soft-delete, and restore stored memories |
| **DI integration** | `AddMemori(...)` with configuration binding, factory overrides, and full middleware setup |

No first-party database integrations are included. Implement `IConversationStorage` and supply a `VectorStore` provider in your own package.

## Packages

| Package | NuGet |
|---|---|
| [Memori](https://www.nuget.org/packages/Memori) | [![NuGet](https://img.shields.io/nuget/v/Memori)](https://www.nuget.org/packages/Memori) |

## Requirements

- .NET 10 SDK or newer

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release --verbosity normal
```

## Learn More

- [GETTING-STARTED.md](GETTING-STARTED.md) — installation, facade, middleware, DI, capture policy, storage, embeddings, augmentation, versioning, summarization, memory management
- [ARCHITECTURE.md](ARCHITECTURE.md) — design principles, storage contracts, augmentation pipeline, recall/search model, middleware semantics, extension points

## Contributing

- Keep the core package storage-provider agnostic
- Preserve `Memori` facade ergonomics for attribution, capture, recall, and augmentation
- Keep `IChatClient` middleware behavior correct for both standard and streaming flows
- See `AGENTS.md` for project-specific implementation and review guardrails

## License

Apache-2.0
