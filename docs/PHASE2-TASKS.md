# Memori .NET Phase 2 Implementation Tasks

## Purpose

This document breaks down the Phase 2 roadmap defined in `docs/PHASE2.md` into concrete, assignable tasks for coding agents.

Each task is sized for end-to-end ownership by a single coding agent and includes clear acceptance criteria, scope, and file references.

---

## General Instructions for Agents

### Before Starting Any Task

1. **Read the context documents** in this order:
   - `docs/IDEA.md` — Phase 1 design and Phase 2 overview
   - `docs/PHASE2.md` — Phase 2 architectural direction and rationale
   - `docs/FOLLOWUP.md` — Implementation patterns and lessons learned from Phase 1

2. **Understand the existing codebase:**
   - Review `src/Memori` for Phase 1 abstractions and patterns
   - Study `src/Memori.Tests` for testing conventions
   - Read `src/Memori/Abstractions/IStorage.cs` — this is what Tier 1 replaces
   - Read `src/Memori/Storage/InMemoryStorage.cs` — reference for what splits into two implementations

3. **Follow established patterns:**
   - Use dependency injection consistently with existing code
   - Write NUnit tests alongside implementation
   - Document public APIs with XML comments
   - Keep abstractions composable and provider-agnostic
   - Use `async`/`await` with `ConfigureAwait(false)` throughout
   - Follow `.editorconfig`: 4 spaces, CRLF, nullable enabled, no underscore prefix on private fields

4. **API shape decisions:**
   - Do not add features beyond the task scope
   - Document any design decisions via comments or a brief note in the PR
   - If uncertain, call out assumptions before implementing

---

## Execution Order

### Tier 1: VectorStore Foundation (do these first, in order)

1. **PHASE2-TASK-1** — Define `MemoryFactRecord` and `IConversationStorage`
2. **PHASE2-TASK-2** — Implement `InMemoryConversationStorage`
3. **PHASE2-TASK-3** — Wire `Memori` facade, `AugmentationService`, `MemorySearchService` to the new split
4. **PHASE2-TASK-4** — Update DI registration
5. **PHASE2-TASK-5** — Update tests
6. **PHASE2-TASK-6** — Update public documentation

### Tier 2: Production Scale (start after Tier 1)

7. **PHASE2-TASK-7** — Distributed ranker abstraction
8. **PHASE2-TASK-8** — Composite VectorStore querying

### Tier 3: Enterprise and Advanced (start after Tier 2)

9. **PHASE2-TASK-9** — Workspace/scope support
10. **PHASE2-TASK-10** — Conflict resolution and versioning
11. **PHASE2-TASK-11** — Thread summarization
12. **PHASE2-TASK-12** — User memory management APIs

---

## Tier 1: VectorStore Foundation

---

## PHASE2-TASK-1: Define `MemoryFactRecord` and `IConversationStorage`

### Priority

Critical — blocks all other Tier 1 tasks.

### Goal

Define the two new abstractions that replace `IStorage`:

1. **`MemoryFactRecord`** — a `Microsoft.Extensions.VectorData`-attributed record type that represents a durable memory fact. This is the VectorStore `TRecord` type Memori uses directly.
2. **`IConversationStorage`** — a slim interface covering only the relational/ordered operations that VectorStore cannot handle: entities, processes, sessions, conversations, and messages.

### Rationale

`IStorage` bundles two fundamentally different concerns. Vector memory (facts, triples, attributes) belongs in a `VectorStoreCollection`. Conversation management (sessions, messages, ordering) is relational and does not benefit from vector search. Splitting them lets users configure any `VectorStore` provider for the memory half without writing Memori-specific adapters.

See `docs/PHASE2.md` for the full architectural rationale.

### Scope

**`MemoryFactRecord`** (new file: `src/Memori/Models/MemoryFactRecord.cs`):
- Annotate with `[VectorStoreKey]`, `[VectorStoreVector]`, and `[VectorStoreData]` attributes from `Microsoft.Extensions.VectorData`
- Fields: `Id`, `EntityId`, `Content`, `Embedding`, `MemoryType`, `Confidence`, `CreatedAt`, `ConversationId`
- Use `ReadOnlyMemory<float>` for the vector field
- Make it a `class` (not `record`) — VectorStore providers typically require mutable types with parameterless constructors
- Include XML documentation on all public members
- The `Dimensions` value on `[VectorStoreVector]` should be configurable or left at a sensible default (e.g., 1536); document this

**`IConversationStorage`** (new file: `src/Memori/Abstractions/IConversationStorage.cs`):
- Extract the conversation/session/entity/process operations verbatim from `IStorage`:
  - `GetOrCreateEntityAsync`
  - `GetOrCreateProcessAsync`
  - `GetOrCreateSessionAsync`
  - `GetOrCreateConversationAsync`
  - `AppendMessagesAsync`
  - `GetConversationMessagesAsync`
  - `UpdateConversationSummaryAsync`
- Preserve all existing XML documentation
- Do not add new methods in this task

### Constraints

- Do not delete `IStorage` yet — that happens in PHASE2-TASK-3 once all consumers are migrated
- Do not modify `InMemoryStorage` yet
- `MemoryFactRecord` must be usable as `TRecord` in `VectorStoreCollection<string, MemoryFactRecord>` without additional configuration by the caller
- `Microsoft.Extensions.VectorData.Abstractions` is already a dependency in `Memori.csproj` — no new package references needed

### Acceptance Criteria

- [ ] `MemoryFactRecord` compiles with VectorStore attributes and is usable as `TRecord`
- [ ] `IConversationStorage` covers all conversation/session/entity/process operations from `IStorage`
- [ ] Both types have complete XML documentation
- [ ] No existing code is broken (old `IStorage` still exists)
- [ ] Build passes: `dotnet build --configuration Release`

### Files

- New: `src/Memori/Models/MemoryFactRecord.cs`
- New: `src/Memori/Abstractions/IConversationStorage.cs`
- Reference: `src/Memori/Abstractions/IStorage.cs`
- Reference: `src/Memori/Models/MemoryFact.cs`

---

## PHASE2-TASK-2: Implement `InMemoryConversationStorage`

### Priority

Critical — blocks PHASE2-TASK-3.

### Goal

Implement `IConversationStorage` using in-process dictionaries. This replaces the conversation/session/entity/process half of `InMemoryStorage` and serves as the default implementation for development, testing, and local use.

### Scope

- Create `src/Memori/Storage/InMemoryConversationStorage.cs`
- Extract the entity, process, session, and conversation state management from `InMemoryStorage` verbatim
- The implementation should be thread-safe (same `lock (gate)` pattern as `InMemoryStorage`)
- Implement all methods from `IConversationStorage`
- Include XML documentation

The vector/facts half of `InMemoryStorage` is not part of this task — that is handled by the in-memory `VectorStore` provider from `Microsoft.Extensions.VectorData`.

### Constraints

- Do not delete `InMemoryStorage` yet — that happens in PHASE2-TASK-3
- Keep the implementation simple and deterministic; this is a reference/test implementation, not a production database
- Match the existing `InMemoryStorage` behavior exactly for the operations being extracted

### Acceptance Criteria

- [ ] `InMemoryConversationStorage` implements all `IConversationStorage` methods
- [ ] Thread-safe (uses lock or equivalent)
- [ ] Behavior matches the corresponding methods in `InMemoryStorage`
- [ ] XML documentation is complete
- [ ] Build passes: `dotnet build --configuration Release`

### Files

- New: `src/Memori/Storage/InMemoryConversationStorage.cs`
- Reference: `src/Memori/Storage/InMemoryStorage.cs` (extract from here)
- Reference: `src/Memori/Abstractions/IConversationStorage.cs` (from PHASE2-TASK-1)

---

## PHASE2-TASK-3: Wire Facade, AugmentationService, and MemorySearchService to the New Split

### Priority

Critical — the main migration task.

### Goal

Update `Memori` (facade), `AugmentationService`, and `MemorySearchService` to use `IConversationStorage` and `VectorStoreCollection<string, MemoryFactRecord>` instead of `IStorage`. Remove `IStorage` once all consumers are migrated.

### Scope

**`Memori` facade (`src/Memori/Memori.cs`):**
- Replace `IStorage storage` constructor parameter with `IConversationStorage conversationStorage` and `VectorStoreCollection<string, MemoryFactRecord> factCollection`
- Update `CaptureAsync` to use `conversationStorage` for session/conversation operations
- Update `RecallAsync` to delegate to `MemorySearchService` (which now uses `factCollection`)
- Update `DeleteEntityMemoriesAsync` to delete from `factCollection` (search by `EntityId` filter, then delete by key)
- Keep the facade's public API surface unchanged — callers should not need to change

**`AugmentationService` (`src/Memori/Augmentation/AugmentationService.cs`):**
- Replace `IStorage storage` with `IConversationStorage conversationStorage` and `VectorStoreCollection<string, MemoryFactRecord> factCollection`
- `UpdateConversationSummaryAsync` → `conversationStorage`
- `AddFactsAsync` → upsert `MemoryFactRecord` instances into `factCollection`
- `AddSemanticTriplesAsync` → store as `MemoryFactRecord` with `MemoryType = "semantic_triple"`, content = formatted triple string
- `AddProcessAttributesAsync` → store as `MemoryFactRecord` with `MemoryType = "process_attribute"`
- Embedding generation before upsert stays the same — generate via `IEmbeddingGenerator`, set on the record before calling `UpsertAsync`

**`MemorySearchService` (`src/Memori/Search/MemorySearchService.cs`):**
- Replace `IStorage storage` with `VectorStoreCollection<string, MemoryFactRecord> factCollection`
- `RecallAsync` → call `factCollection.SearchAsync(queryEmbedding, new VectorSearchOptions { Top = candidateLimit, Filter = ... })` filtered by `EntityId`
- Map `VectorSearchResult<MemoryFactRecord>` → `RecallResult` (similarity score comes from the search result)
- When no embedding generator is present, fall back to `GetAsync` + lexical scoring (same behavior as today)
- `IMemoryRanker` post-processing stays unchanged

**Remove `IStorage`:**
- Once all consumers above are migrated, delete `src/Memori/Abstractions/IStorage.cs`
- Delete `src/Memori/Storage/InMemoryStorage.cs`
- Remove any remaining references

### Constraints

- The public `Memori` facade API must not change — `Attribution()`, `CaptureAsync()`, `RecallAsync()`, `DeleteEntityMemoriesAsync()`, `FormatPromptContext()`, etc. all stay the same
- `MemoriChatClient` should require no changes (it only calls the `Memori` facade)
- Lexical fallback in `MemorySearchService` must be preserved for the no-embedding-generator case
- `IMemoryRanker` and `DefaultMemoryRanker` are unchanged

### Acceptance Criteria

- [ ] `Memori` facade compiles and uses `IConversationStorage` + `VectorStoreCollection<string, MemoryFactRecord>`
- [ ] `AugmentationService` upserts `MemoryFactRecord` instances correctly
- [ ] `MemorySearchService` searches via `VectorStoreCollection` and maps results to `RecallResult`
- [ ] `IStorage` and `InMemoryStorage` are deleted
- [ ] `MemoriChatClient` requires no changes
- [ ] Build passes: `dotnet build --configuration Release`

### Files

- Modify: `src/Memori/Memori.cs`
- Modify: `src/Memori/Augmentation/AugmentationService.cs`
- Modify: `src/Memori/Search/MemorySearchService.cs`
- Delete: `src/Memori/Abstractions/IStorage.cs`
- Delete: `src/Memori/Storage/InMemoryStorage.cs`
- Reference: `src/Memori/Abstractions/IConversationStorage.cs` (PHASE2-TASK-1)
- Reference: `src/Memori/Models/MemoryFactRecord.cs` (PHASE2-TASK-1)
- Reference: `src/Memori/Storage/InMemoryConversationStorage.cs` (PHASE2-TASK-2)

---

## PHASE2-TASK-4: Update DI Registration

### Priority

High — needed before tests can run.

### Goal

Update `ServiceCollectionExtensions` to register `IConversationStorage` and resolve `VectorStoreCollection<string, MemoryFactRecord>` from the DI container. The default registration should use `InMemoryConversationStorage` and the in-memory `VectorStore` provider so that `services.AddMemori()` still works with zero configuration.

### Scope

**`ServiceCollectionExtensions` (`src/Memori/MicrosoftExtensionsAI/ServiceCollectionExtensions.cs`):**
- Default registration: `TryAddSingleton<IConversationStorage, InMemoryConversationStorage>()`
- Default vector store: register the in-memory `VectorStore` provider and resolve `VectorStoreCollection<string, MemoryFactRecord>` from it
- Remove all `IStorage`-based overloads
- Add overloads that accept a `IConversationStorage` factory and/or a `VectorStoreCollection<string, MemoryFactRecord>` factory for users who want to supply their own
- Keep the `AddMemori(IConfiguration)` overload working
- Keep the `CreateMemori(IServiceProvider)` extension working

**Target DI experience:**

```csharp
// Zero config — in-memory everything
services.AddMemori();

// Custom conversation storage
services.AddMemori(sp => new MyConversationStorage(...));

// Production vector store — user registers provider, Memori resolves it
services.AddAzureAISearch(endpoint, credential);
services.AddMemori();

// Full explicit control
services.AddMemori(
    conversationStorageFactory: sp => new MyConversationStorage(...),
    configureOptions: options => { options.RecallFactsLimit = 10; });
```

### Constraints

- `services.AddMemori()` with no arguments must still work and produce a fully functional `Memori` instance
- Do not require users to know about `VectorStoreCollection` unless they want to customize it
- If no `VectorStore` provider is registered, fall back to the in-memory provider automatically
- Keep the `MemoriOptions` configuration path unchanged

### Acceptance Criteria

- [ ] `services.AddMemori()` resolves a working `Memori` instance with in-memory defaults
- [ ] Custom `IConversationStorage` can be supplied via factory overload
- [ ] `VectorStoreCollection<string, MemoryFactRecord>` is resolved from DI when a provider is registered
- [ ] `AddMemori(IConfiguration)` still works
- [ ] `CreateMemori(IServiceProvider)` still works
- [ ] Build passes: `dotnet build --configuration Release`

### Files

- Modify: `src/Memori/MicrosoftExtensionsAI/ServiceCollectionExtensions.cs`
- Reference: `src/Memori/Storage/InMemoryConversationStorage.cs` (PHASE2-TASK-2)
- Reference: `src/Memori/Models/MemoryFactRecord.cs` (PHASE2-TASK-1)

---

## PHASE2-TASK-5: Update Tests

### Priority

High — validates the Tier 1 migration.

### Goal

Update all existing tests to use the new `IConversationStorage` + `VectorStoreCollection<string, MemoryFactRecord>` split. Ensure the full test suite passes. Add new tests for the VectorStore integration path.

### Scope

**Existing tests to update:**

- `MemoriFacadeTests.cs` — replace `InMemoryStorage` with `InMemoryConversationStorage` + in-memory VectorStore collection
- `HeroScenarioTests.cs` — same replacement
- `AugmentationServiceTests.cs` — update to use `VectorStoreCollection` directly
- `ChatClientTests.cs` — update wiring
- Any storage contract tests — update or replace with `IConversationStorage` contract tests

**New tests to add:**

- `ConversationStorageTests.cs` — contract tests for `IConversationStorage` covering all methods (mirrors the old `IStorage` conversation tests)
- `MemoryFactRecordTests.cs` — verify `MemoryFactRecord` can be upserted and searched via the in-memory VectorStore provider
- At least one test that registers a VectorStore provider via DI and verifies end-to-end recall works

### Constraints

- All 66 existing tests (or their direct replacements) must pass
- Tests must not require external services — use the in-memory VectorStore provider
- Keep test doubles simple and deterministic
- Use `async Task` for all async tests

### Acceptance Criteria

- [ ] All tests pass: `dotnet test --configuration Release`
- [ ] No tests reference `IStorage` or `InMemoryStorage`
- [ ] `ConversationStorageTests` covers all `IConversationStorage` methods
- [ ] At least one end-to-end test exercises the VectorStore search path
- [ ] Test count is equal to or greater than the pre-migration count

### Files

- Modify: `src/Memori.Tests/MemoriFacadeTests.cs`
- Modify: `src/Memori.Tests/HeroScenarioTests.cs`
- Modify: `src/Memori.Tests/AugmentationServiceTests.cs`
- Modify: `src/Memori.Tests/ChatClientTests.cs`
- New: `src/Memori.Tests/ConversationStorageTests.cs`
- New: `src/Memori.Tests/MemoryFactRecordTests.cs`

---

## PHASE2-TASK-6: Update Public Documentation

### Priority

High — required before any public release of Phase 2.

### Goal

Update `README.md`, `GETTING-STARTED.md`, `ARCHITECTURE.md`, and `src/Memori/README.md` to reflect the new architecture. Remove all references to `IStorage` as a user-facing abstraction. Document the new `IConversationStorage` and `VectorStoreCollection` integration pattern.

### Scope

**`README.md`:**
- Update the storage section to describe `IConversationStorage` and `VectorStoreCollection`
- Add a section showing how to configure a production VectorStore provider
- Update the DI registration examples
- Remove `IStorage` implementation guidance

**`GETTING-STARTED.md`:**
- Update the getting-started flow to use the new DI pattern
- Add a "using a production vector store" section with a concrete example (e.g., Azure AI Search or Qdrant)
- Keep the zero-config in-memory example as the default starting point

**`ARCHITECTURE.md`:**
- Update the storage layer description to reflect the split
- Add a diagram or description of how `IConversationStorage` and `VectorStoreCollection` relate
- Document the embedding generation flow (unchanged from Phase 1, but now flows into VectorStore upsert)

**`src/Memori/README.md`:**
- Update to match the new public API surface

**`docs/IDEA.md`:**
- Update the `IStorage` section to note that Phase 2 replaced it with `IConversationStorage` + `VectorStoreCollection`
- Do not rewrite the Phase 1 design intent — just add a note at the relevant section

### Constraints

- Do not remove Phase 1 design rationale from `IDEA.md`
- Keep documentation accurate to the implemented behavior, not aspirational
- Code examples in docs must compile against the new API

### Acceptance Criteria

- [ ] `README.md` accurately describes the new storage model
- [ ] `GETTING-STARTED.md` shows a working zero-config example and a production VectorStore example
- [ ] `ARCHITECTURE.md` reflects the split abstraction
- [ ] `src/Memori/README.md` is up to date
- [ ] No documentation references `IStorage` as a user-facing extension point
- [ ] All code examples in docs are consistent with the new API

### Files

- Modify: `README.md`
- Modify: `GETTING-STARTED.md`
- Modify: `ARCHITECTURE.md`
- Modify: `src/Memori/README.md`
- Modify: `docs/IDEA.md` (addendum only)

---

## Tier 2: Production Scale

---

## PHASE2-TASK-7: Distributed Ranker Abstraction

### Priority

Medium

### Goal

Create an abstraction for ranking memory candidates across multiple `VectorStoreCollection` backends and combining results consistently. Enables scenarios where facts are spread across multiple vector stores (e.g., a local cache and a remote Azure AI Search index).

### Scope

- Define `IDistributedRanker` — extends or complements `IMemoryRanker` for multi-source result combining
- Define a result-combining strategy abstraction
- Implement reference strategies: merge-sort by score, weighted, round-robin
- Add configuration for strategy selection
- Write 15+ tests covering combining logic and edge cases

### Constraints

- Do not change `IMemoryRanker` (backward compatible)
- Keep provider-agnostic — no VectorStore-specific logic in the abstraction
- Ranking must be deterministic and reproducible

### Acceptance Criteria

- [ ] `IDistributedRanker` is defined and documented
- [ ] At least 3 combining strategies are implemented with clear semantics
- [ ] 15+ tests cover combining, ordering, and edge cases
- [ ] Strategy selection is configurable via DI
- [ ] Code follows Memori patterns

### Files

- New: `src/Memori/Search/IDistributedRanker.cs`
- New: `src/Memori/Search/DistributedRankingStrategies.cs` (or per-strategy files)
- New: `src/Memori.Tests/DistributedRankerTests.cs`

---

## PHASE2-TASK-8: Composite VectorStore Querying

### Priority

Medium

### Goal

Build a composite wrapper that queries multiple `VectorStoreCollection<string, MemoryFactRecord>` backends in parallel and merges results using the distributed ranker from PHASE2-TASK-7.

### Scope

- Create `CompositeMemoryCollection` — wraps multiple `VectorStoreCollection<string, MemoryFactRecord>` instances
- Implements parallel querying with configurable concurrency limits
- Implements result merging via `IDistributedRanker`
- Write strategy (all backends vs. primary-only) is configurable
- Write 15+ tests for parallel, fallback, and partial-failure scenarios

### Constraints

- Failures in one backend must not crash others
- Transparent to `MemorySearchService` — acts like a single collection
- All backends must use `MemoryFactRecord` as the record type

### Acceptance Criteria

- [ ] `CompositeMemoryCollection` queries multiple backends in parallel
- [ ] Result merging uses `IDistributedRanker`
- [ ] Partial backend failure is handled gracefully
- [ ] Write and query strategies are configurable
- [ ] 15+ tests cover success, partial failure, and full failure scenarios

### Files

- New: `src/Memori/Search/CompositeMemoryCollection.cs`
- New: `src/Memori/Search/CompositeMemoryCollectionOptions.cs`
- New: `src/Memori.Tests/CompositeMemoryCollectionTests.cs`
- Reference: `src/Memori/Search/IDistributedRanker.cs` (PHASE2-TASK-7)

---

## Tier 3: Enterprise and Advanced Features

---

## PHASE2-TASK-9: Workspace/Scope Support

### Priority

Medium

### Goal

Extend `IConversationStorage` and `MemoryFactRecord` to support workspace/scope identifiers, enabling shared memory across teams or organizations while maintaining isolation boundaries.

### Decision Required

- How should scopes be represented — string scope ID, composite key, or hierarchy?
- Should scope be part of `MemoryFactRecord` as a filterable field, or a query-time parameter?
- How does scope interact with `EntityId` isolation?

### Scope

- Add scope field to `MemoryFactRecord` (filterable)
- Extend `IConversationStorage` with scope-aware methods or overloads (backward compatible)
- Implement scope-aware search filtering in `MemorySearchService`
- Write tests for scope isolation and cross-scope retrieval
- Document scope semantics

### Acceptance Criteria

- [ ] Scope field is defined on `MemoryFactRecord`
- [ ] `IConversationStorage` extension is backward compatible
- [ ] Scope isolation tests pass
- [ ] Cross-scope retrieval works when explicitly requested
- [ ] Documentation explains scope semantics

### Files

- Modify: `src/Memori/Models/MemoryFactRecord.cs`
- Modify: `src/Memori/Abstractions/IConversationStorage.cs`
- Modify: `src/Memori/Search/MemorySearchService.cs`
- New: `src/Memori.Tests/ScopeIsolationTests.cs`

---

## PHASE2-TASK-10: Conflict Resolution and Versioning

### Priority

Medium

### Goal

Add versioning fields to `MemoryFactRecord` and implement conflict detection and resolution strategies for concurrent memory updates.

### Decision Required

- How should versions be represented — timestamps, sequence numbers, or UUIDs?
- Which resolution strategy is the default — last-write-wins, merge, or manual review?

### Scope

- Add version metadata to `MemoryFactRecord`
- Define conflict detection logic
- Implement 2–3 resolution strategies (last-write-wins, merge, manual)
- Write 15+ tests for conflict scenarios and resolution

### Acceptance Criteria

- [ ] Version fields are defined on `MemoryFactRecord`
- [ ] Conflict detection logic is clear and tested
- [ ] At least 3 resolution strategies are implemented
- [ ] Audit trail is queryable
- [ ] 15+ tests cover conflict scenarios

### Files

- Modify: `src/Memori/Models/MemoryFactRecord.cs`
- New: `src/Memori/Versioning/ConflictResolutionStrategy.cs`
- New: `src/Memori/Versioning/VersioningService.cs`
- New: `src/Memori.Tests/ConflictResolutionTests.cs`

---

## PHASE2-TASK-11: Thread Summarization

### Priority

Low

### Goal

Define `IThreadSummarizer` and a reference implementation using `IChatClient`. Integrate with the augmentation pipeline so summaries can be stored as `MemoryFactRecord` entries with `MemoryType = "summary"`.

### Scope

- Define `IThreadSummarizer` interface
- Implement `ChatClientThreadSummarizer` using `IChatClient`
- Integrate with `AugmentationService` (optional, deferred)
- Write 10+ tests for summary generation and storage

### Acceptance Criteria

- [ ] `IThreadSummarizer` is defined
- [ ] Reference implementation uses `IChatClient`
- [ ] Summaries stored as `MemoryFactRecord` with appropriate `MemoryType`
- [ ] 10+ tests cover happy path and error handling

### Files

- New: `src/Memori/Summarization/IThreadSummarizer.cs`
- New: `src/Memori/Summarization/ChatClientThreadSummarizer.cs`
- New: `src/Memori/Summarization/ThreadSummarizationOptions.cs`
- New: `src/Memori.Tests/ThreadSummarizerTests.cs`

---

## PHASE2-TASK-12: User Memory Management APIs

### Priority

Low

### Goal

Add public APIs for users to inspect, search, filter, edit, and delete their stored memories. Enables transparency and user control over durable memory.

### Scope

- Define `IMemoryManagementService` with list, search, edit, and delete methods
- Implement against `VectorStoreCollection<string, MemoryFactRecord>`
- Add soft-delete option
- Write 15+ tests for all operations

### Acceptance Criteria

- [ ] List, search, filter, edit, and delete operations are implemented
- [ ] Soft delete is supported
- [ ] 15+ tests cover all operations
- [ ] Access control patterns are documented (app responsibility, not Memori)

### Files

- New: `src/Memori/Management/IMemoryManagementService.cs`
- New: `src/Memori/Management/MemoryManagementService.cs`
- New: `src/Memori.Tests/MemoryManagementTests.cs`

---

## Coordination Notes

### Critical Path

```
TASK-1 → TASK-2 → TASK-3 → TASK-4 → TASK-5 → TASK-6
                                                  ↓
                                          TASK-7 → TASK-8
                                                  ↓
                                    TASK-9, TASK-10, TASK-11, TASK-12 (parallel)
```

### Shared Files — Coordinate Carefully

- `src/Memori/Models/MemoryFactRecord.cs` — modified by TASK-1, TASK-9, TASK-10
- `src/Memori/Abstractions/IConversationStorage.cs` — modified by TASK-1, TASK-9
- `src/Memori/Search/MemorySearchService.cs` — modified by TASK-3, TASK-9

### Parallelization

- TASK-1 and TASK-2 can run in parallel (no dependency between them)
- TASK-9 through TASK-12 can run in parallel once TASK-8 is complete

---

## Related Documents

- `docs/IDEA.md` — Phase 1 design and Phase 2 overview
- `docs/PHASE2.md` — Phase 2 architectural direction and rationale
