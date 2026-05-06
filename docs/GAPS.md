# Memori .NET Gaps From `docs/IDEA.md`

## Scope

This document is the delta between:

- Target design: `docs/IDEA.md`
- Current implementation: `src/Memori` and `src/Memori.Tests`

It intentionally ignores other planning/backlog documents. It does not attempt to migrate every feature from the original Python/TypeScript/cloud codebase. The only question here is: how far is the current C# implementation from the Phase 1 primitive-first memory architecture described in `docs/IDEA.md`?

## Current Implementation Snapshot

The current C# implementation includes these major pieces:

- `Memori` facade for attribution, session management, recall, capture, augmentation waiting, and memory deletion.
- `IStorage` storage abstraction.
- `InMemoryStorage` reference storage implementation.
- Core models for attribution, options, conversations, messages, facts, triples, summaries, and recall results.
- `IMemoriEmbeddingGenerator` abstraction with null, deterministic, and Microsoft.Extensions.AI adapter implementations.
- `MemorySearchService` for recall orchestration, relevance filtering, and prompt context formatting.
- `Similarity` helpers for cosine and lexical scoring.
- `IAugmentationClient`, `NullAugmentationClient`, and `AugmentationService`.
- `MemoriChatClient` middleware over `Microsoft.Extensions.AI.IChatClient`.
- `UseMemori(...)` and `AddMemori(...)` integration helpers.
- NUnit tests covering storage, facade behavior, chat wrapping, and a hero scenario.

## Summary

The current implementation covers the broad Phase 1 skeleton from `docs/IDEA.md`, but it is not yet aligned with several important design details.

Main deltas:

- The idea names `IMemoryRanker`; current code has no public ranker abstraction.
- The idea describes configurable middleware primitives; current chat integration is a single wrapper with hard-coded prompt injection behavior.
- The idea says embeddings are required in Phase 1; current code supports lexical-only operation when no embedding generator is configured.
- The idea includes built-in extraction prompt support; current code only provides an augmentation boundary and null/test implementations.
- The idea's memory record schema includes a memory `type`; current fact models do not expose a first-class type/category.
- The idea calls out confidence/recency ranking boosts; current ranking behavior needs clearer implementation boundaries and tests.

## Direct Coverage Matrix

| `docs/IDEA.md` Area | Current State | Delta |
| --- | --- | --- |
| `IChatClient` remains the inference abstraction | Implemented. `MemoriChatClient` wraps `IChatClient`. | Needs stronger middleware configurability and request-scoped controls. |
| Memory as composable middleware | Partially implemented. One wrapper performs recall, injection, provider call, capture, and augmentation scheduling. | Retrieval and capture are not independently composable middleware primitives. |
| Storage-provider agnostic design | Implemented. Core package has no database-specific provider. | Contract validation and provider guidance are not yet strong enough for external implementers. |
| In-memory storage implementation | Implemented. | Needs stronger contract/concurrency tests before serving as the reference behavior. |
| Built-in extraction prompt support | Missing. | No default prompt-based extractor exists. |
| Custom extractor override | Partially possible through `IAugmentationClient`. | The abstraction is not named or scoped as an extractor, and no helper exists for common prompt/JSON extraction. |
| `IMemoryRanker` abstraction | Missing. | Ranking is split between `IStorage.SearchFactsAsync`, `InMemoryStorage`, `Similarity`, and `MemorySearchService`. |
| Cosine similarity | Implemented. | Need tests that lock expected vector ranking behavior. |
| Confidence/recency boosts | Partially implemented. | Confidence exists on facts, and timestamps affect ordering, but there is no explicit ranker/policy matching the idea. |
| `IEmbeddingGenerator` from Microsoft.Extensions.AI | Partially implemented. | Current public abstraction is `IMemoriEmbeddingGenerator`; Microsoft.Extensions.AI is adapted in, not used directly as the primary public embedding contract. |
| Embeddings required in Phase 1 | Partially implemented. | Current library can operate without embeddings via lexical ranking. Decide whether this is intentional divergence. |
| Store raw embedding vectors in memory records | Implemented for facts. | Ensure all fact creation paths consistently generate or preserve embeddings when an embedding generator exists. |
| Memory record schema with `userId`, `type`, `content`, `embedding`, `confidence`, timestamps | Partially implemented. | User/entity id, content, embedding, confidence, and timestamps exist; first-class memory type/category is missing. |
| Retrieval: fetch candidates, embed query, rank, boost, inject top N | Mostly implemented. | Ranking/boosting responsibility is not explicit, and injection does not use the shared formatter. |
| Prompt injection of top memories | Implemented. | `MemoriChatClient` uses a hard-coded formatter instead of `MemorySearchService.FormatPromptContext` and `MemoriOptions`. |
| Optional built-in extraction prompt | Missing. | Needs a default extractor or an explicit decision to keep extraction host-provided only. |
| DI registration model with `.UseMemory(...)` style | Partially implemented as `.UseMemori(...)` and `AddMemori(...)`. | Registration is narrow and lacks storage/embedding/augmentation factory overloads. |
| Phase 1 non-goals | Mostly respected. | No vector DBs, cloud SDK, distributed ranking, summarization engine, or enterprise integrations were added. |

## P0 Gaps

### 1. Align automatic prompt injection with the configured prompt formatter

Current state:

- `MemorySearchService.FormatPromptContext(...)` supports configured tag names, instructions, headings, timestamps, and summaries.
- `MemoriChatClient` bypasses that and builds a hard-coded `<memori_context>` block.

Delta from idea:

- The idea expects middleware-controlled prompt injection as a configurable primitive.
- The current automatic middleware path ignores part of the configured prompt behavior.

Tasks:

- [ ] Route automatic chat injection through `MemorySearchService.FormatPromptContext(...)` or an equivalent shared public formatter.
- [ ] Remove the hard-coded formatter from `MemoriChatClient`.
- [ ] Add tests proving `MemoriOptions.PromptContextTagName`, `PromptContextInstruction`, `PromptFactsHeading`, and timestamp settings affect injected chat messages.
- [ ] Add tests proving summaries are included when recalled results contain summaries.

### 2. Prevent injected memory context from being captured as raw conversation

Current state:

- `MemoriChatClient` prepares messages by inserting a memory system message before calling the inner provider.
- Capture receives the prepared messages.
- Default stripping of system messages usually prevents injected memory from being stored.

Delta from idea:

- The idea separates retrieval/injection from capture. Injected recall context is not raw conversation and should never be captured as a new conversation message.

Tasks:

- [ ] Keep original input messages separate from prepared/injected messages.
- [ ] Capture original input messages plus assistant response, not the injected message list.
- [ ] Add a test where `StripSystemMessagesOnCapture = false` and injected memory still is not persisted.
- [ ] Preserve host-provided system messages according to capture options while excluding Memori-generated context.

### 3. Not needed / Done

## P1 Gaps

### 4. Not needed / Done

### 5. Implement built-in extraction prompt support

Current state:

- `NullAugmentationClient` is the only built-in production-safe augmentation implementation.
- Tests define local fake augmentation clients.

Delta from idea:

- The idea explicitly includes built-in optional extraction prompt support.

Tasks:

- [ ] Add a reference prompt-based extraction implementation.
- [ ] Let hosts provide the `IChatClient` or extraction model client used by that implementation.
- [ ] Define the expected JSON output shape for extracted memories.
- [ ] Parse extraction output into facts, triples, process attributes, and summaries.
- [ ] Add malformed JSON, empty output, and filler-message tests.

### 6. Make ranking policy explicit

Current state:

- Storage search returns ranked `RecallResult` values.
- In-memory storage uses similarity helpers.
- Search service filters and orders results.
- There is no `IMemoryRanker`.

Delta from idea:

- The idea names `IMemoryRanker` as the component responsible for ranking memory candidates with cosine similarity and confidence/recency boosts.

Tasks:

- [ ] Decide whether ranking is storage-owned or library-owned.
- [ ] If storage-owned, document that `IStorage.SearchFactsAsync` is the .NET replacement for `IMemoryRanker`.
- [ ] If library-owned, add `IMemoryRanker` and route in-memory ranking through it.
- [ ] Add tests for lexical score, dense score, confidence boost, recency boost, and threshold filtering.

### 7. Resolve embedding contract divergence

Current state:

- Current code exposes `IMemoriEmbeddingGenerator`.
- `MicrosoftEmbeddingGeneratorAdapter` adapts Microsoft.Extensions.AI embedding generators.
- `NullEmbeddingGenerator` allows lexical-only behavior.

Delta from idea:

- The idea says to use `IEmbeddingGenerator` from Microsoft.Extensions.AI for query and memory embeddings and says embeddings are required in Phase 1.

Tasks:

- [ ] Decide whether `IMemoriEmbeddingGenerator` remains the public abstraction.
- [ ] Decide whether lexical-only mode is officially supported or only a test/development fallback.
- [ ] If Microsoft.Extensions.AI should be primary, add overloads/DI registration that accept Microsoft's `IEmbeddingGenerator<string, Embedding<float>>` directly.
- [ ] Add docs and tests for behavior with embeddings configured and with embeddings absent.

### 8. Add first-class memory type/category

Current state:

- Facts have content, embedding, summaries, confidence, source conversation id, and timestamps.
- No first-class type/category is visible in the fact model.

Delta from idea:

- The idea's memory record schema includes `type`, with examples like `preference`, `profile`, `goal`, and `constraint`.

Tasks:

- [ ] Decide whether memory type is required for v1.
- [ ] Add a string or enum-backed type field to new facts, stored facts, and recall results.
- [ ] Thread the field through augmentation output and storage.
- [ ] Add tests for storing and recalling typed memories.

### 9. Expand DI registration to match the idea's composition model

Current state:

- `AddMemori(...)` registers default singleton in-memory storage, null augmentation, `Memori`, and search service.
- `UseMemori(...)` wraps a `ChatClientBuilder`.

Delta from idea:

- The idea shows an idiomatic registration path where memory behavior can be configured during middleware registration.
- Current registration does not cleanly expose storage, embedding, extraction/augmentation, and middleware policy replacement.

Tasks:

- [ ] Add `AddMemori(...)` overloads for custom `IStorage`.
- [ ] Add `AddMemori(...)` overloads for custom embedding generation.
- [ ] Add `AddMemori(...)` overloads for custom extraction/augmentation.
- [ ] Add `UseMemori(...)` overloads that configure memory behavior at middleware registration time.
- [ ] Add DI tests for default and custom registrations.

## P2 Gaps

### 10. Make retrieval and capture independently controllable

Current state:

- `MemoriChatClient` always attempts recall before the provider call and capture after the provider call when attribution/query state allows it.

Delta from idea:

- The idea describes middleware components and a pipeline. Current behavior is monolithic and has no per-request policy knobs.

Tasks:

- [ ] Add request-scoped options to skip recall.
- [ ] Add request-scoped options to skip capture.
- [ ] Add request-scoped options to override top K.
- [ ] Add tests for recall-only, capture-only, disabled recall, and disabled capture flows.

### 11. Strengthen in-memory storage as reference behavior

Current state:

- `InMemoryStorage` exists and is used by tests.

Delta from idea:

- The idea says the library ships an in-memory implementation. For that to be useful as the reference implementation, its behavior must be well specified and tested.

Tasks:

- [ ] Add contract-style tests for `IStorage`.
- [ ] Cover idempotent get-or-create behavior.
- [ ] Cover message ordering.
- [ ] Cover conversation timeout behavior.
- [ ] Cover fact search with lexical-only, vector-only, and hybrid paths.
- [ ] Cover delete behavior preserving conversation history while deleting durable memories.

### 12. Harden streaming middleware behavior

Current state:

- Streaming support collects updates, yields them, reconstructs final messages, and captures after stream completion.

Delta from idea:

- The idea requires middleware around `IChatClient`; streaming is part of a credible chat middleware implementation, but edge cases are not yet deeply specified.

Tasks:

- [ ] Add tests for multi-update assistant responses.
- [ ] Add tests for responses with multiple assistant messages.
- [ ] Add tests for tool-call-like or non-text updates if supported by the current Microsoft.Extensions.AI version.
- [ ] Ensure cancellation during streaming does not persist partial assistant output unless explicitly intended.

### 13. Clarify Phase 1 versus Phase 2 boundaries in the C# docs

Current state:

- Code mostly respects the idea's Phase 1 non-goals.
- Some implemented behavior, such as summaries and semantic triples, reaches toward richer augmentation concepts.

Delta from idea:

- `docs/IDEA.md` says Phase 1 is primitives and infrastructure only, while advanced summarization and conflict resolution are deferred.

Tasks:

- [ ] Document that summaries/triples are stored primitives, not advanced summarization or graph reasoning engines.
- [ ] Document that external vector DBs and provider-specific storage packages are out of scope for the core package.
- [ ] Document that conflict resolution/versioning is not implemented in Phase 1.

## Suggested Order

1. Fix prompt injection to use configured formatting.
2. Ensure injected memory is never captured as raw conversation.
3. Not needed / Done.
4. Not needed / Done.
5. Add built-in prompt-based extraction support.
6. Decide ranking ownership and either add `IMemoryRanker` or document storage-owned ranking.
7. Resolve embedding contract and lexical-only behavior.
8. Add memory type/category if it remains part of the Phase 1 schema.
9. Expand DI/middleware registration.
10. Add request-scoped controls, storage contract tests, streaming hardening, and Phase 1 docs.

## Open Decisions From The Delta

- Add `IMemoryRanker`, or make `IStorage.SearchFactsAsync` the ranking boundary?
- Require embeddings, or officially support lexical-only mode?
- Add memory type/category to v1 fact records?
- Keep `IMemoriEmbeddingGenerator`, or expose Microsoft.Extensions.AI `IEmbeddingGenerator` as the primary public contract?

## Verification Note

This document is based on static inspection of the current C# implementation. Test execution was attempted but blocked by the current sandbox before useful compile/test results were produced.
