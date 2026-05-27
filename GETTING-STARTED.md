# Getting Started with Memori

Memori is a lightweight library for adding durable memory to AI applications built on `Microsoft.Extensions.AI`.

It gives your AI app a persistent, searchable memory layer: capture conversations, recall relevant facts, and inject context into prompts — without coupling your code to a specific database or LLM provider.

## Is Memori Right for You?

Memori is a good fit when:
- Your AI app needs persistent, searchable memory across sessions
- You're building chatbots or assistants that should remember user preferences and history
- You want recall-augmented generation in a `Microsoft.Extensions.AI`-based pipeline
- You need structured memory operations (list, search, edit, soft-delete, restore)

Memori is **not** a good fit for:
- Single-turn completions with no memory requirement
- Apps where the LLM provider already manages conversation history and no cross-session recall is needed
- Scenarios requiring first-party database integrations (bring your own storage provider)

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

var search = new MemorySearchService(factCollection);

var results = await search.RecallAsync("user_123", "What is my favorite color?");
var promptContext = search.FormatPromptContext(results);
Console.WriteLine(promptContext);
```

## The Memori Facade

The `MemoriEngine` class combines attribution, session tracking, capture, recall, and augmentation into a single entry point.

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

By default, recalled memory is injected as a `system` message before the existing chat history. You can configure the placement and role:

```csharp
options.PromptInjectionPlacement = PromptInjectionPlacement.AfterSystemAndDeveloperMessages;
options.PromptInjectionRole = "developer";
```

Placement options:
- `BeforeHistory` (default): insert as first message
- `AfterSystemAndDeveloperMessages`: insert after existing system/developer instructions
- `AppendToRequest`: append to the end of the request
- `MergeIntoFirstSystemMessage`: merge into an existing instruction message
- `Disabled`: disable prompt injection while keeping capture behavior

The middleware supports streaming responses with correct cancellation semantics.

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

## Full Example: Memory Lifecycle

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

var memori = new MemoriEngine(
    conversationStorage,
    factCollection,
    new MemoriOptions
    {
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

## Advanced Features

### Capture Policy

Capture policy is applied after messages are converted to Memori's durable `ConversationMessage` model. You can drop roles, omit empty messages, filter by predicate, or transform messages before storage.

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

### Prompt Context Formatting

`BuildPromptContext(...)` returns structured facts, summaries, rendering metadata, and the final rendered text. `FormatPromptContext(...)` is available when you only need the rendered string.

Formatting is driven by options:

- `PromptFactBullet` / `PromptSummaryBullet`
- `PromptTimestampFormat`
- `PromptFactsHeading` / `PromptSummariesHeading`
- `IncludeSummariesInPrompt`

### Storage

Memori splits persistent storage into two concerns:

**`IConversationStorage`** covers conversations, sessions, entities, processes, and messages. The built-in `InMemoryConversationStorage` is suitable for tests and local development. For production, implement `IConversationStorage` in your own package:

```csharp
services.AddSingleton<IConversationStorage, MyConversationStorage>();
services.AddMemori();
```

**`VectorStoreCollection<string, MemoryFactRecord>`** covers durable fact storage with vector and lexical search. Any `VectorStore` provider works directly. For production, register your provider:

```csharp
services.AddSingleton<VectorStore>(sp => new MyProductionVectorStore(endpoint, credential));
services.AddMemori();
```

See [ARCHITECTURE.md](ARCHITECTURE.md) for the full storage contract and semantics.

### Embeddings

Memori uses `IEmbeddingGenerator<string, Embedding<float>>` from `Microsoft.Extensions.AI` directly.

- `DeterministicEmbeddingGenerator`: dependency-free vectors for tests and local demos
- `NgramEmbeddingGenerator`: Character n-gram embedding generator for demos and prototyping
- For lexical-only recall, omit embedding generator registration
- Production embedding providers are supplied by the consuming application

### Augmentation

Augmentation turns captured conversation messages into durable memory updates (facts, semantic triples, process attributes, and conversation summaries) in the background.

Included clients:
- `NullAugmentationClient`: no-op, for hosts that only want capture/recall
- `PromptAugmentationClient`: uses an `IChatClient` to extract structured JSON output

Implement `IAugmentationClient` to use custom extraction logic. See [ARCHITECTURE.md](ARCHITECTURE.md) for the augmentation contract and mapping helpers.

### Versioning and Conflict Resolution

Memori tracks record versions for concurrent memory updates:

```csharp
var versioning = new VersioningService(ConflictResolutionStrategy.LastWriteWins);

var existing = await factCollection.GetAsync("fact-id");
var resolution = versioning.ResolveConflict(incoming, existing, expectedVersion: 1);
```

Three strategies are available:
- **LastWriteWins** (default): the latest write overwrites
- **Merge**: conflicting content is combined
- **Manual**: conflicts are flagged for external review

Each record carries a `Version` integer, a `PreviousVersionId` for audit trail, and an `IsDeleted` flag for soft-delete.

### Thread Summarization

Generate conversation summaries using any `IChatClient`:

```csharp
var summarizer = new ChatClientThreadSummarizer(chatClient);

// Initial summary
var summary = await summarizer.SummarizeAsync(messages);

// Rolling summary with previous context
var updated = await summarizer.SummarizeAsync(newMessages, previousSummary);
```

Summaries are stored as `MemoryFactRecord` entries with `MemoryType = "summary"`.

### Memory Management

Inspect, search, edit, soft-delete, and restore stored memories:

```csharp
var management = serviceProvider.GetRequiredService<IMemoryManagementService>();

var memories = await management.ListMemoriesAsync("entity-1");
var results = await management.SearchMemoriesAsync("entity-1", "coffee");
await management.SoftDeleteMemoryAsync("fact-id");
await management.RestoreMemoryAsync("fact-id");
await management.HardDeleteMemoryAsync("fact-id");
```

## Running as MCP Server

Memori ships a [Model Context Protocol](https://modelcontextprotocol.io) server that exposes durable memory as MCP tools for AI coding agents.

### Quick Start

```bash
# Install the dotnet tool
dotnet tool install -g Memori.Mcp --prerelease

# Run the MCP server (SQLite-backed, persists across sessions)
memori-mcp

# Markdown mode — human-readable, git-friendly files
memori-mcp --markdown

# Custom storage path
memori-mcp --path ./my-memories

# Enable debug logging
memori-mcp --verbose

# All flags:
#   --mode, -m <sqlite|markdown>     Operation mode (default: sqlite)
#   --markdown                       Shorthand for --mode markdown
#   --path, -p <path>                Storage path (default per mode)
#   --fulltext                       Enable FTS5 (sqlite mode only)
#   --verbose, -v                    Enable debug logging
```

Or via NPM:

```bash
npx -y @uworx/memori
```

### Available Tools

Once connected, the MCP server exposes these tools:

| Tool | Description |
|---|---|
| `memori_remember` | Store a fact about the current entity |
| `memori_search` | Semantic + lexical search across stored memories |
| `memori_list` | List all memories for the current entity (with pagination) |
| `memori_get` | Get a single memory record by ID |
| `memori_update` | Update an existing memory's content |
| `memori_delete` | Soft-delete a memory record |
| `memori_clear` | Clear all memories for the current entity |

### Storage

Memori supports two storage modes:

- **Sqlite** (default) — SQLite-backed with n-gram vector search (or FTS5 via `--fulltext`). The database file is created at the configured path (defaults to `.memori/memori.db`).
- **Markdown** — file-per-category, line-per-entry markdown files. One file per memory type (e.g., `PREFERENCES.md`) with one memory per line. Human-readable and git-friendly.

## Next Steps

- [README.md](README.md) — full feature surface, package overview, and quick taste
- [ARCHITECTURE.md](ARCHITECTURE.md) — design principles, storage contracts, augmentation pipeline, middleware semantics, and extension points
