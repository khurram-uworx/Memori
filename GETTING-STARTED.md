# Getting Started with Memori

Memori is a lightweight library for adding durable memory to AI applications built on `Microsoft.Extensions.AI`.

It gives your AI app a persistent, searchable memory layer: capture conversations, recall relevant facts, and inject context into prompts — without coupling your code to a specific database or LLM provider.

## Why Memori?

- `IConversationStorage` for conversation/session/entity/process data — bring your own backend
- `VectorStoreCollection<string, MemoryFactRecord>` for durable fact storage via any `Microsoft.Extensions.VectorData` provider
- Built-in `InMemoryConversationStorage` and `InMemoryVectorStore` for tests, demos, and local development
- `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` as the embedding surface
- A `Memori` facade for attribution, session tracking, capture, recall, and augmentation in one place
- `IChatClient` middleware that wires recall and capture into any `Microsoft.Extensions.AI`-compatible provider

## Installation

```bash
dotnet add package Memori
```

## Hello World

Store a fact, recall it, and format a prompt context block.

```csharp
using Memori.Models;
using Memori.Search;
using Memori.Storage;

var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

await factCollection.UpsertAsync(new MemoryFactRecord
{
    Id = Guid.NewGuid().ToString("N"),
    EntityId = "user_123",
    Content = "The user's favorite color is blue",
    CreatedAt = DateTimeOffset.UtcNow
});

var search = new MemorySearchService(
    factCollection,
    options: new MemoriOptions { RecallRelevanceThreshold = 0.1 });

var results = await search.RecallAsync("user_123", "What is my favorite color?");
var promptContext = search.FormatPromptContext(results);
Console.WriteLine(promptContext);
```

## The Memori Facade

The `Memori` class combines attribution, session tracking, capture, recall, and augmentation into a single entry point.

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

Example output:

```text
<memori_context>
Only use the relevant context if it is relevant to the user's query.
Relevant context about the user:
- The user's favorite color is blue. Stated at 2026-05-06 01:00:00
</memori_context>
```

### Attribution and Sessions

Attribution identifies who the memory belongs to. Sessions group capture history.

```csharp
memori.Attribution("user_123", "support_agent");
memori.SetSession("session_abc");

// Inspect current state
var currentAttribution = memori.CurrentAttribution;
var currentSessionId = memori.CurrentSessionId;

// Clear or resume
memori.ClearAttribution();
memori.ClearSession();
memori.ResumeSession("session_abc");
```

Recall and delete operations are always scoped to the current attribution entity. Sessions only affect capture grouping and conversation history.

## Microsoft.Extensions.AI Middleware

Memori ships an `IChatClient` middleware that recalls before a model call and captures after it completes. Any provider that exposes `IChatClient` works without additional wiring.

```csharp
using Memori;
using Microsoft.Extensions.AI;

IChatClient innerClient = /* your provider-backed IChatClient */;

IChatClient client = new ChatClientBuilder(innerClient)
    .UseMemori(memori)
    .Build(serviceProvider);
```

By default, recalled memory is injected as a `system` message before the existing chat history. You can change the injected role, placement, or disable injection entirely while keeping capture behavior.

## Dependency Injection

```csharp
using Memori;
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

Bind from configuration:

```csharp
services.AddMemori(configuration.GetSection("Memori"));
```

Supply custom conversation storage, embedding, and augmentation through factories:

```csharp
services.AddMemori(
    sp => new MyConversationStorage(),
    configureOptions: options =>
    {
        options.RecallRelevanceThreshold = 0.2;
    });
```

For full control including fact collection and embedding generator:

```csharp
services.AddMemori(
    conversationStorageFactory: sp => new MyConversationStorage(),
    factCollectionFactory: sp => myVectorStore.GetCollection<string, MemoryFactRecord>("facts"),
    embeddingGeneratorFactory: sp => myEmbeddingGenerator,
    augmentationClientFactory: sp => myAugmentationClient,
    configureOptions: options => { options.RecallRelevanceThreshold = 0.2; });
```

## Capture Policy

Capture policy is applied after provider messages are converted to Memori's durable `ConversationMessage` model. You can drop roles, omit empty messages, filter by predicate, or transform messages before storage.

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

## Prompt Context Formatting

`BuildPromptContext(...)` returns structured facts, summaries, rendering metadata, and the final rendered text. `FormatPromptContext(...)` is available when you only need the rendered string.

Formatting is driven by options:

- `PromptFactBullet` / `PromptSummaryBullet`
- `PromptTimestampFormat`
- `PromptFactsHeading` / `PromptSummariesHeading`
- `IncludeSummariesInPrompt`

## Storage

Memori splits persistent storage into two concerns:

### `IConversationStorage`

Covers conversations, sessions, entities, processes, and messages. The built-in `InMemoryConversationStorage` is suitable for tests and local development. For production, implement `IConversationStorage` in your own package and register it through DI.

```csharp
services.AddSingleton<IConversationStorage, MyConversationStorage>();
services.AddMemori();
```

### `VectorStoreCollection<string, MemoryFactRecord>`

Covers durable fact storage with vector and lexical search. Any `VectorStore` provider works directly. The built-in `InMemoryVectorStore` is the default.

For production, register your `VectorStore` provider (e.g., Azure AI Search):

```csharp
services.AddSingleton<VectorStore>(sp => new MyProductionVectorStore(endpoint, credential));
services.AddMemori();
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full storage contract and semantics.

## Embeddings

Memori uses `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` directly.

- `DeterministicEmbeddingGenerator`: dependency-free vectors for tests and local demos.
- For lexical-only recall, omit embedding generator registration.
- Production embedding providers are supplied by the consuming application.

## Augmentation

Augmentation turns captured conversation messages into durable memory updates (facts, semantic triples, process attributes, and conversation summaries) in the background.

Included clients:

- `NullAugmentationClient`: no-op, for hosts that only want capture/recall.
- `PromptAugmentationClient`: uses an `IChatClient` to extract structured JSON output.

Implement `IAugmentationClient` to use custom extraction logic. See [ARCHITECTURE.md](ARCHITECTURE.md) for the augmentation contract and mapping helpers.

## Hero Scenario: Full Memory Lifecycle

This example shows attribution, capture, augmentation, recall, and prompt injection across two conversation turns using the `IChatClient` middleware.

```csharp
using Memori;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;

// --- Setup ---
var conversationStorage = new InMemoryConversationStorage();
var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");

var memori = new Memori.Memori(
    conversationStorage,
    factCollection,
    new MemoriOptions
    {
        RecallRelevanceThreshold = 0.1,
        SessionTimeout = TimeSpan.FromMinutes(30)
    },
    augmentationClient: new PromptAugmentationClient(yourChatClient));

// Identify the user and start a session
memori.Attribution("user_123");
memori.SetSession("session_abc");

// --- Turn 1: user tells us something ---
// Wrap your provider client with Memori middleware
IChatClient client = new ChatClientBuilder(yourChatClient)
    .UseMemori(memori)
    .Build();

var turn1 = await client.CompleteAsync(new[]
{
    new ChatMessage(ChatRole.User, "My favorite color is blue.")
});

// Wait for background augmentation to extract and store the fact
await memori.WaitForAugmentationAsync();

// --- Turn 2: user asks a question ---
// Memori recalls the stored fact and injects it as context before the model call
var turn2 = await client.CompleteAsync(new[]
{
    new ChatMessage(ChatRole.User, "What is my favorite color?")
});

Console.WriteLine(turn2.Message.Text);
// The model sees the injected <memori_context> block and answers: "Your favorite color is blue."
```

The middleware handles recall, injection, and capture automatically on every turn. You only need to manage attribution and sessions.

## When to Use

- AI apps that need persistent, searchable memory across sessions
- Chatbots and assistants that should remember user preferences and history
- Any `Microsoft.Extensions.AI`-based pipeline where you want recall-augmented generation

## When Not to Use

- Simple single-turn completions with no memory requirement
- Apps where the LLM provider already manages conversation history natively and no cross-session recall is needed

## Learn More

- [README.md](README.md): full feature surface, DI setup, and package overview
- [ARCHITECTURE.md](ARCHITECTURE.md): design principles, storage contract, augmentation pipeline, and implementation notes
