# Integration Friction Observations

This doc captures what a first-time user must touch to add Memori to the
[AI Chat Template](https://learn.microsoft.com/dotnet/ai/quickstarts/ai-templates),
along with suggestions for lowering that bar.

## Baseline: changes required (3 files, ~25 net lines)

| File | Change |
|---|---|
| `Sample.csproj` | Add `<ProjectReference>` to Memori |
| `Program.cs` | Import 4-5 namespaces, instantiate storage/vector/facade, wrap chat client |
| `Chat.razor` | Inject memori, call `Attribution()` before each user turn |

## Observations

### 1. Too many concepts exposed on day one

A user wanting "chat with memory" must learn about:

- `IConversationStorage` + `InMemoryConversationStorage`
- `InMemoryVectorStore` + `VectorStoreCollection<string, MemoryFactRecord>`
- `MemoryFactRecord` + `MemoriOptions`
- `Memori` facade constructor signature
- `MemoriChatClient` middleware wrapper
- `Attribution()` / entity model

That's ~6 new abstractions before the first message is sent. For a getting-started
flow, most of these could be hidden behind a default.

### 2. Manual attribution is easy to forget

```razor
private async Task AddUserMessageAsync(ChatMessage userMessage)
{
    CancelAnyCurrentResponse();
    memori.Attribution("khurram", "sample");  // required before each turn
    ...
}
```

If the user skips this, `CaptureAsync()` becomes a silent no-op. No warning,
no error — just lost data. Middleware-level attribution (e.g. from `HttpContext`
user identity) would eliminate this foot-gun.

### 3. `Memori.Memori` naming collision

The namespace `Memori` and the class `Memori` clash, forcing callers to write
`new Memori.Memori(...)`. This is awkward and looks like a typo.

### 4. Storage choice forced upfront

Even the "quick start" path requires choosing a storage backend and instantiating
it explicitly. An opinionated default (InMemory for prototyping, swappable later)
would let the user write `builder.AddMemori()` and move on.

### 5. No single "add to chat app" extension

There is no one-call extension that says "wrap my chat client with memory". The
user must manually wire storage, the facade, the middleware, and DI. Compare:

```csharp
// Current:
var store = new InMemoryConversationStorage();
var vec = new InMemoryVectorStore();
var col = vec.GetCollection<string, MemoryFactRecord>("f");
var opts = new MemoriOptions { ... };
var m = new Memori.Memori(store, col, opts);
chatClient = new MemoriChatClient(chatClient, m);

// Could be:
builder.AddMemoriChatClient(chatClient, options => { ... });
```

### 6. `RecallRelevanceThreshold = 0` is a demo trap

Setting this to 0 bypasses relevance filtering, which makes recall seem to work
during development but silently returns junk in production. A better default
(0.1-0.3) and a friendlier "relaxed mode" flag would prevent this.

## Suggestions

### Short-term (quick wins)

1. **Rename `Memori.Memori`** — change the class name (e.g. `MemoryEngine`,
   `MemoriEngine`, `MemoriFacade`) or move it to a sub-namespace to resolve the
   collision. Then `new Memori(...)` works naturally.

2. **Add `AddMemoriChatClient()` extension** — a single call on
   `ChatClientBuilder` that wires InMemory storage, default options, and the
   facade in one step:
   ```csharp
   builder.AddChatClient(chatClient)
       .UseFunctionInvocation()
       .UseLogging()
       .UseMemori();
   ```
   (This exists on `ChatClientBuilderExtensions` but still requires the user to
   have registered a `Memori` instance first. A self-contained variant would
   skip that prerequisite.)

3. **Default entity via middleware** — allow the middleware to derive
   attribution from `HttpContext.User` or a configurable callback, so the
   explicit `Attribution()` call in every page handler is optional.

### Medium-term

4. **`AddMemoriWithDefaults()`** — a DI extension that registers everything
   (InMemory storage, facade, embedding generator) so the user only writes:
   ```csharp
   builder.Services.AddMemoriWithDefaults(entityId: "user");
   ```

5. **Improve prompt injection defaults** — use `AfterSystemMessages` placement
   by default (currently `BeforeAllMessages`) so recalled context feels more
   natural to the LLM.

6. **Warn on missing attribution** — log a warning (not silence) when
   `CaptureAsync` is called without attribution set.

### Longer-term

7. **Storage-optional facade** — consider a mode where the facade works with
   just a vector store and no `IConversationStorage` for simple "memory-only"
   use cases.

8. **Scaffold a sample** — ship a `dotnet new memori-chat` template that
   starts from the AI Chat Template with Memori pre-integrated. Users see a
   working app immediately and can peel back layers as they learn.
