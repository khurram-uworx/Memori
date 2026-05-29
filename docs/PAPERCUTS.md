# Task Breakdown — Integration Friction Improvements

## Purpose

This document breaks the suggestions from `SUGGESTIONS.md` into concrete, assignable tasks for coding agents. Task numbers match the suggestion numbers in the action plan for consistent cross-referencing.

## How To Use

- Each task is small enough that one coding agent can own it end-to-end.
- Decision-gate tasks are clearly marked.
- Tasks within the same phase can run in parallel unless noted.
- Acceptance criteria are included so agent handoffs are testable and reviewable.

## Suggested Execution Order

### ✅ Phase 1 — Quick Wins (parallel-safe)

1. ✅ Task 1: Rename `Memori` class → `MemoriEngine`
2. ✅ Task 2: Log warning on silent no-op capture
3. ✅ Task 3: Change `RecallRelevanceThreshold` default + add `RelaxedMode`
4. ✅ Task 4: Change `PromptInjectionPlacement` default to `AfterSystemMessages`

### Phase 2 — DI Onboarding (depends on Task 1)

5. Task 5: Add `AddMemoriWithDefaults()` DI extension
6. Task 6: Add middleware-derived default attribution
7. Task 7: Add `AddMemoriChatClient()` DI shortcut

### Phase 3 — Polished "Won't Forget" (parallel-safe, depends on Task 6)

8. Task 8: Per-request attribution override via ChatOptions
9. Task 9: Validate attribution presence in middleware path

### Phase 4 — Future (post-MVP)

10. Task 10: Storage-optional facade mode
11. Task 11: `dotnet new memori-chat` scaffold template
12. Task 12: Update docs after API stabilizes

## Coordination Notes

- **Decision gate**: Task 1 (rename) changes the public API. All subsequent tasks should target the new name. Do not start Phase 2 tasks until Task 1 is merged.
- **Parallel-safe within phase**: Tasks 2, 3, 4 can run in parallel after Task 1. Tasks 8 and 9 can run in parallel after Task 6.
- **Tasks 10–12** are intentionally deferred; no implementation until explicit go-ahead.
- **Shared files**: `Memori.cs`, `MemoriOptions.cs`, `ServiceCollectionExtensions.cs`, and `MemoriChatClient.cs` are touched by multiple tasks. Sequence carefully or stack changes in a single branch.

## Task 1: Rename `Memori` class to `MemoriEngine`

### Priority

High

### Goal

Eliminate the `Memori.Memori` naming collision so callers can write `new MemoriEngine(...)` instead of `new Memori.Memori(...)`.

### Why this exists

Namespace `Memori` and class `Memori` clash, forcing awkward qualification everywhere. This is the first thing a new user notices.

### Decision required

Pick the replacement name: `MemoriEngine`, `MemoriFacade`, `MemoryEngine`, or `MemoriCore`. `MemoriEngine` is recommended for consistency with the "engine" metaphor.

### Scope

- Rename the class in `Memori.cs`
- Update all references in `src/Memori/` (extensions, middleware, DI registration)
- Update all references in `tests/Memori.Tests/`
- Update `Sample/` if it references the class
- Update `GETTING-STARTED.md` usage snippets

### Constraints

- Preserve the existing namespace (`Memori`); only the class name changes.
- Keep `MemoriChatClient` and `MemoriOptions` names unchanged — they do not collide.
- Do not change public method signatures on the renamed class.

### Acceptance criteria

- `dotnet build --configuration Release` passes with zero warnings
- `dotnet test --configuration Release` passes (all 211+ tests)
- No occurrence of `Memori.Memori` remains in the codebase
- `new MemoriEngine(...)` compiles without qualification tricks

### Files likely involved

- `src/Memori/Memori.cs`
- `src/Memori/Extensions/ChatClientBuilderExtensions.cs`
- `src/Memori/Extensions/ServiceCollectionExtensions.cs`
- `src/Memori/Extensions/MemoriChatClient.cs`
- `tests/Memori.Tests/` (multiple files)
- `Sample/` (if present)
- `GETTING-STARTED.md`

## Task 2: Log warning when `CaptureAsync` is a no-op due to missing attribution

### Priority

High

### Goal

Emit a `ILogger.Warning` when `CaptureAsync` is called without attribution set, so developers are immediately alerted to the silent data loss.

### Why this exists

Currently `CaptureAsync` returns silently when `currentAttribution is null` (line ~221 in `Memori.cs`). Users who forget `Attribution()` get no feedback — their data simply disappears.

### Scope

- Inject `ILogger<Memori>` into the `Memori` (now `MemoriEngine`) constructor
- Add a `ILogger` field and a `using Microsoft.Extensions.Logging` import
- Emit `LogWarning` at the silent-return guard in `CaptureAsync`
- Keep the guard behavior unchanged (still returns, still a no-op)

### Constraints

- `ILogger` must be optional — the class is still constructable without it (existing constructor overloads without logger should remain)
- Do not introduce a required dependency on `ILogger<Memori>` in DI registration; use `ILoggerFactory` or `ILogger<T>` retrieved via `GetService` (nullable)
- No change to `MemoriChatClient` or other types

### Acceptance criteria

- When attribution is null: a warning is logged, `CaptureAsync` still returns without persisting
- When attribution is set: no warning, behavior unchanged
- Existing constructor usage without logger compiles and works (null logger = no crash, no log output)
- All existing tests pass

### Files likely involved

- `src/Memori/Memori.cs`
- `src/Memori/Memori.csproj` (verify `Microsoft.Extensions.Logging.Abstractions` is a dependency)

## Task 3: Change `RecallRelevanceThreshold` default + add `RelaxedMode`

### Priority

High

### Goal

Change the default `RecallRelevanceThreshold` from `0` to `0.15` and add a `bool RelaxedMode` flag that overrides it to `0` for debugging.

### Why this exists

A threshold of `0` bypasses relevance filtering entirely, making recall appear to work during prototyping while silently returning junk in production. A small positive default aligns demo and production behavior.

### Scope

- Change default value of `RecallRelevanceThreshold` in `MemoriOptions` from `0` to `0.15`
- Add `bool RelaxedMode` property to `MemoriOptions` (default `false`)
- In `MemorySearchService.RecallAsync` (or wherever threshold is read): use `0` when `RelaxedMode` is true, else use `RecallRelevanceThreshold`

### Constraints

- `RelaxedMode` must be settable via `MemoriOptions` configuration delegate and config binding
- Existing tests that rely on `0` threshold must be updated or explicitly set `RelaxedMode = true`

### Acceptance criteria

- Default `RecallRelevanceThreshold` is `0.15`
- When `RelaxedMode = false` (default): recall filters at the configured threshold
- When `RelaxedMode = true`: threshold effectively becomes `0`
- Config binding works: `"MemoriOptions:RelaxedMode": true`
- All tests pass

### Files likely involved

- `src/Memori/Models/MemoriOptions.cs`
- `src/Memori/Search/MemorySearchService.cs` (or wherever threshold is applied)
- `tests/Memori.Tests/` (threshold-dependent tests)

## Task 4: Change default `PromptInjectionPlacement` to `AfterSystemMessages`

### Priority

Medium

### Goal

Switch the default placement of injected prompt context from `BeforeAllMessages` to `AfterSystemMessages` so recalled context feels more natural to the LLM.

### Why this exists

`BeforeAllMessages` places context before the system prompt, which can interfere with system instruction delivery. `AfterSystemMessages` is less disruptive and more intuitive.

### Scope

- Change the default value of `PromptInjectionPlacement` in `MemoriOptions` from `BeforeAllMessages` to `AfterSystemMessages`
- Update any tests that assert the old default
- No behavioral logic changes — only the default constant

### Constraints

- The `PromptInjectionPlacement` enum and all placement-switch logic remain unchanged
- Users can still override via `MemoriOptions`

### Acceptance criteria

- `new MemoriOptions().PromptInjectionPlacement` returns `AfterSystemMessages`
- All tests pass
- No behavioral regression in middleware injection logic

### Files likely involved

- `src/Memori/Models/MemoriOptions.cs`
- `tests/Memori.Tests/` (default-asserting tests)

## Task 5: Add `AddMemoriWithDefaults()` DI extension

### Priority

High

### Goal

Add a one-call `builder.Services.AddMemoriWithDefaults("khurram")` that registers in-memory storage, facade, options, and pre-sets attribution so new users never touch `IConversationStorage`, `InMemoryVectorStore`, or `MemoryFactRecord`.

### Why this exists

Even the "quick start" path requires choosing and instantiating storage explicitly. An opinionated default turns 10 lines of setup into 1.

### Scope

- Add `AddMemoriWithDefaults(this IServiceCollection, string entityId, string? processId, Action<MemoriOptions>?)` overload to `ServiceCollectionExtensions`
- Internally calls `addCoreServices` + registers a singleton `Attribution` instance
- Pre-sets attribution on the facade after construction (or passes entityId into the constructor)
- Optionally registers an `IEmbeddingGenerator<string, Embedding<float>>` — try to resolve from DI or skip

### Constraints

- Must not break existing `AddMemori()` overloads
- Must work without any prior service registrations (self-contained)
- `Attribution` must be accessible to middleware for auto-attribution (Task 6)

### Acceptance criteria

- `services.AddMemoriWithDefaults("khurram")` produces a fully functional `MemoriEngine` with in-memory storage
- `services.AddMemoriWithDefaults("khurram", o => o.RecallRelevanceThreshold = 0.3)` applies custom options
- Existing `AddMemori()` callers are unaffected
- All tests pass

### Files likely involved

- `src/Memori/Extensions/ServiceCollectionExtensions.cs`

## Task 6: Add middleware-derived default attribution

### Priority

High

### Goal

Allow `MemoriChatClient` to derive attribution from a configurable delegate or `HttpContext`, making explicit `Attribution()` calls in page handlers optional.

### Why this exists

Users must call `memori.Attribution(...)` before each turn. This is easy to forget and creates a foot-gun. Middleware-level attribution (e.g. from `HttpContext.User.Identity.Name`) eliminates the pattern.

### Scope

- Add a `Func<IServiceProvider, Attribution>?` optional factory parameter to `MemoriChatClient`
- In `GetResponseAsync` / `GetStreamingResponseAsync`: if no current attribution is set on the facade and the factory is configured, invoke the factory and call `memori.Attribution(...)` automatically
- Add a `UseMemori(Action<MemoriAttributionOptions>)` overload on `ChatClientBuilderExtensions` that configures the factory
- Add a `MemoriAttributionOptions` class with an `AttributionFactory` delegate property

### Constraints

- Must not break existing `UseMemori()` overloads
- Must be opt-in: existing callers see no change
- Should work in non-DI scenarios (factory can be set directly)

### Acceptance criteria

- When factory is configured and no attribution is set: middleware auto-sets attribution before processing messages
- When attribution is already set: factory is not invoked
- When factory is null: current behavior unchanged
- All tests pass

### Files likely involved

- `src/Memori/Extensions/MemoriChatClient.cs`
- `src/Memori/Extensions/ChatClientBuilderExtensions.cs`
- `src/Memori/Models/MemoriAttributionOptions.cs` (new file)

## Task 7: Add `AddMemoriChatClient()` DI shortcut

### Priority

Medium

### Goal

Add `builder.Services.AddMemoriChatClient(innerClient, entityId: "khurram")` that registers everything and wraps the chat client in one call.

### Why this exists

Currently users must call `AddMemori()` then separately wire `UseMemori()` on the `ChatClientBuilder`. A single extension method lowers the onboarding ceremony to one line.

### Scope

- Add static class `MemoriChatClientServiceCollectionExtensions` (or extend existing `ServiceCollectionExtensions`)
- Method signature: `AddMemoriChatClient(this IServiceCollection, IChatClient innerClient, string entityId, Action<MemoriOptions>?)`
- Internally calls `AddMemoriWithDefaults` then `AddChatClient(innerClient).UseMemori()`
- Options delegate configures both `MemoriOptions` and attribution

### Constraints

- Must work with the standard `AddChatClient()` pattern from `Microsoft.Extensions.AI`
- Must not introduce a new required NuGet dependency
- Must preserve the ability to override storage via existing `AddMemori()` overloads

### Acceptance criteria

- `services.AddMemoriChatClient(openAiClient, "khurram")` produces a working `IChatClient` pipeline with memory
- Memory capture and recall work without any additional setup
- Existing `AddMemori()` + `UseMemori()` callers are unaffected
- All tests pass

### Files likely involved

- `src/Memori/Extensions/ServiceCollectionExtensions.cs` (or new file)

## Task 8: Per-request attribution override via `ChatOptions`

### Priority

Medium

### Goal

Allow per-turn attribution override via `ChatOptions.AdditionalProperties`, enabling multi-entity apps to switch attribution without touching facade state.

### Why this exists

Today the `Attribution()` call is global facade state. An app serving multiple users needs per-request attribution without locking/contention.

### Scope

- Add an `Attribution` property to `MemoriRequestOptions`
- In `MemoriChatClient.prepareMessagesAsync`: if `requestOptions.Attribution` is set, temporarily set it on the facade (or pass it through the recall path)
- In `MemoriChatClient.captureAsync`: use the request-scoped attribution for capture
- Ensure thread safety — per-request override should not leak between concurrent calls

### Constraints

- Must not break existing `MemoriRequestOptions` behavior
- `MemoriRequestOptions` is already carried via `ChatOptions.AdditionalProperties[MemoriRequestOptionsKey]`

### Acceptance criteria

- Passing `MemoriRequestOptions` with `Attribution` set overrides facade-level attribution for that single turn
- Next turn without the override falls back to facade-level attribution
- Thread safety: concurrent calls with different overrides do not interfere
- All tests pass

### Files likely involved

- `src/Memori/Models/MemoriRequestOptions.cs`
- `src/Memori/Extensions/MemoriChatClient.cs`

## Task 9: Validate attribution presence in middleware path

### Priority

Medium

### Goal

Log a warning (not silence) in the middleware's `prepareMessagesAsync` when attribution is missing, so users integrating via `UseMemori()` get immediate feedback.

### Why this exists

Currently if a user forgets `Attribution()` when using the middleware, recall silently returns empty results (line ~286-291 in `Memori.cs`). The middleware path has no visible feedback.

### Scope

- In `MemoriChatClient` constructor: accept an optional `ILogger<MemoriChatClient>`
- In `prepareMessagesAsync`: after calling `memori.RecallAsync`, check if result count is 0 — if so, check if attribution was missing and log a warning
- Pass the logger through to `Memori.RecallAsync` or check `memori.CurrentAttribution` after recall

### Constraints

- Logger must be optional (existing constructor overloads without logger continue to work)
- Do not change recall behavior, only add observability

### Acceptance criteria

- When attribution is missing: recall returns empty, a warning is logged
- When attribution is set: no warning, behavior unchanged
- All tests pass

### Files likely involved

- `src/Memori/Extensions/MemoriChatClient.cs`

## Task 10: Storage-optional facade mode

### Priority

Low

### Goal

Allow `MemoriEngine` to work with just a vector store and no `IConversationStorage` for simple "memory-only" use cases.

### Why this exists

Some users only want fact recall (memory) without full conversation capture and replay. Currently both are required.

### Scope

- This is intentionally deferred. No implementation until explicit go-ahead.
- Investigation: identify which facade methods depend on `IConversationStorage` and whether they can be no-op'd or gated.

### Acceptance criteria

- Design document produced (no code change)

### Files likely involved

- `docs/` (design note)

## Task 11: `dotnet new memori-chat` scaffold template

### Priority

Low

### Goal

Ship a `dotnet new` template that starts from the AI Chat Template with Memori pre-integrated, so users see a working app immediately.

### Why this exists

The `Microsoft.Extensions.AI.Templates` package already provides `dotnet new aichatweb`. A Memori variant would lower the bar to zero files changed.

### Scope

- This is intentionally deferred. No implementation until explicit go-ahead.
- Investigation: evaluate whether the template should be in this repo or a separate package.

### Acceptance criteria

- Design document produced (no code change)

### Files likely involved

- `docs/` (design note)

## Task 12: Update docs after API stabilizes

### Priority

Low

### Goal

Update `README.md`, `GETTING-STARTED.md`, and `ARCHITECTURE.md` to reflect the simplified onboarding paths introduced in Tasks 1, 5, 6, and 7.

### Why this exists

Docs will be out of date after the rename and new DI extensions. Keeping them synchronized avoids confusing new users.

### Scope

- This is intentionally deferred until after Phase 2 is merged.
- The action plan/suggestions in `SUGGESTIONS.md` remain as-is and are not modified.

### Acceptance criteria

- `README.md` quick-start shows `AddMemoriWithDefaults()` or `AddMemoriChatClient()` as primary path
- `GETTING-STARTED.md` mentions the rename (`MemoriEngine`)
- `ARCHITECTURE.md` class diagram reflects the new name if applicable

### Files likely involved

- `README.md`
- `GETTING-STARTED.md`
- `ARCHITECTURE.md`
- `src/Memori/README.md`

## Suggested Agent Handout Batches

### Batch A: Phase 1 decision + rename

- Task 1 (rename — prerequisite for everything)

### Batch B: Phase 1 parallel

- Task 2 (warning log)
- Task 3 (threshold default)
- Task 4 (placement default)

### Batch C: Phase 2 depends-on-rename

- Task 5 (AddMemoriWithDefaults)
- Task 6 (middleware attribution)

### Batch D: Phase 2 follow-on

- Task 7 (AddMemoriChatClient)

### Batch E: Phase 3 parallel

- Task 8 (per-request override)
- Task 9 (middleware warning)

### Batch F: Future

- Task 10 (storage-optional — design only)
- Task 11 (template — design only)
- Task 12 (docs — after stabilisation)

## Final Checklist

- [x] every task has a clear owner-sized scope
- [x] every task has acceptance criteria
- [x] decision-gate tasks are clearly marked (Task 1)
- [x] likely files are listed to reduce agent search time
- [x] execution order reflects real dependencies
- [x] task numbers are stable and cross-referenceable with the action plan in `SUGGESTIONS.md`
