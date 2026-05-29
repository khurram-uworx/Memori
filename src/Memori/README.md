# Memori

Durable memory for AI applications built on `Microsoft.Extensions.AI`.

Memori gives your AI app a persistent, searchable memory layer: capture conversations, recall relevant facts, and inject context into prompts — without coupling your code to a specific database or LLM provider.

## Installation

```bash
dotnet add package Memori
```

## What You Get

- `IConversationStorage` for conversation/session/entity/process data — bring your own backend
- `VectorStoreCollection<string, MemoryFactRecord>` for durable fact storage via any `Microsoft.Extensions.VectorData` provider
- Built-in `InMemoryConversationStorage` and `InMemoryVectorStore` for tests, demos, and local development
- `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` as the embedding surface
- A `Memori` facade for attribution, session tracking, capture, recall, and augmentation in one place
- `IChatClient` middleware that wires recall and capture into any `Microsoft.Extensions.AI`-compatible provider
- Scope isolation for multi-tenant memory
- Versioning and conflict resolution (last-write-wins, merge, manual)
- Thread summarization via `ChatClientThreadSummarizer`
- Memory management APIs for user-facing inspection, search, edit, soft-delete, and restore

## Basic Example

```csharp
using Memori.Models;
using Memori.Search;
using Memori.Storage;

var conversationStorage = new InMemoryConversationStorage();
var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

var entityId = await conversationStorage.GetOrCreateEntityAsync("user_123");

await factCollection.UpsertAsync(new MemoryFactRecord
{
    Id = Guid.NewGuid().ToString("N"),
    EntityId = entityId,
    Content = "The user's favorite color is blue",
    CreatedAt = DateTimeOffset.UtcNow
});

var search = new MemorySearchService(factCollection);

var results = await search.RecallAsync(entityId, "What is my favorite color?");
Console.WriteLine(search.FormatPromptContext(results));
```

## Facade Usage

```csharp
using Memori;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;

var conversationStorage = new InMemoryConversationStorage();
var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

var memori = new MemoriEngine(
    conversationStorage,
    factCollection,
    new MemoriOptions { StripSystemMessagesOnCapture = true },
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

## Scope Isolation

```csharp
memori.Attribution("user_123");
memori.SetScope("workspace-a");
var results = await memori.RecallAsync("coffee");
memori.ClearScope();
```

## Microsoft.Extensions.AI Middleware

```csharp
using Memori;
using Microsoft.Extensions.AI;

IChatClient innerClient = /* your provider-backed IChatClient */;

IChatClient client = new ChatClientBuilder(innerClient)
    .UseMemori(memori)
    .Build(serviceProvider);
```

Recalled memory is injected as a `system` message before the existing chat history by default. Injection placement and role are configurable.

## Dependency Injection

```csharp
services.AddMemori(options =>
{
    options.SessionTimeout = TimeSpan.FromMinutes(30);
    options.PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemAndDeveloperMessages;
});

var memori = serviceProvider.CreateMemori();
```

Bind from configuration:

```csharp
services.AddMemori(configuration.GetSection("Memori"));
```

Supply custom conversation storage, embedding, and augmentation through factories:

```csharp
services.AddMemori(
    sp => new MyConversationStorage(),
    configureOptions: options => { options.RecallRelevanceThreshold = 0.2; });
```

## Augmentation

Augmentation extracts structured memory (facts, semantic triples, process attributes, summaries) from captured conversations in the background.

- `NullAugmentationClient`: no-op, for hosts that only want capture/recall.
- `PromptAugmentationClient`: uses an `IChatClient` to extract structured JSON output.
- Implement `IAugmentationClient` for custom extraction logic.

## Versioning

```csharp
var versioning = new VersioningService(ConflictResolutionStrategy.LastWriteWins);
var resolution = versioning.ResolveConflict(incoming, existing, expectedVersion: 1);
```

Supports last-write-wins, merge, and manual resolution strategies with audit trail.

## Thread Summarization

```csharp
var summarizer = new ChatClientThreadSummarizer(chatClient);
var summary = await summarizer.SummarizeAsync(messages);
var updated = await summarizer.SummarizeAsync(newMessages, previousSummary);
```

## Memory Management

```csharp
var management = serviceProvider.GetRequiredService<IMemoryManagementService>();
var memories = await management.ListMemoriesAsync("entity-1");
await management.SoftDeleteMemoryAsync("fact-id");
await management.RestoreMemoryAsync("fact-id");
await management.HardDeleteMemoryAsync("fact-id");
```

## Learn More

- Full feature overview: [README.md](README.md)
- Developer guide: [GETTING-STARTED.md](GETTING-STARTED.md)
- Architecture and design notes: [ARCHITECTURE.md](ARCHITECTURE.md)

## Status

Core primitives, distributed ranking, composite collection, scope isolation, versioning, thread summarization, and memory management are all implemented. 211 NUnit tests. No first-party database integrations — implement `IConversationStorage` and supply a `VectorStore` provider in your own package.

## License

MIT
