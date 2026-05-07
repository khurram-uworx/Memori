# Memori .NET Follow-Up Backlog

## Scope

This document captures useful follow-up work that is **not part of Phase 1 or Phase 2** as defined in `docs/IDEA.md`.

These are ergonomics, implementation hardening, examples, documentation, and release polish items that emerged during Phase 1 implementation and are worth considering for future releases.

It intentionally does not redefine the target architecture. `docs/IDEA.md` Phase 1 Scope section is the source of truth for Phase 1 requirements, and `docs/PHASE2.md` contains the Phase 2 roadmap from `docs/IDEA.md`.

## Phase 1 Completion Status

**Phase 1 is complete.** All items from `docs/IDEA.md` Phase 1 Scope section are implemented:

✅ Attribution, sessions, and conversation lifecycle management.
✅ Capture and recall primitives.
✅ Storage abstraction and in-memory reference implementation.
✅ Embedding abstraction with Microsoft.Extensions.AI adapter.
✅ Augmentation boundary and built-in extraction support.
✅ `IChatClient` middleware for recall and capture.
✅ Request-scoped control over recall and capture behavior.
✅ Streaming support with proper cancellation semantics.

## Current Implementation Snapshot

The current C# implementation includes:

- `Memori` facade for attribution, sessions, recall, capture, augmentation waiting, and deletion.
- `IStorage` and `InMemoryStorage` with comprehensive contract tests.
- `IMemoryRanker` abstraction for ranking memory candidates.
- Embedding abstractions and Microsoft.Extensions.AI adapter.
- `DeterministicEmbeddingGenerator` for tests and local development.
- Recall/search service with cosine, lexical, and hybrid ranking.
- Prompt context formatting with configurable options.
- Augmentation boundary with background augmentation service.
- `PromptAugmentationClient` for built-in extraction support.
- `MemoriChatClient` middleware over `IChatClient` with proper recall/capture separation.
- Request-scoped options for fine-grained control (`MemoriRequestOptions`).
- Streaming support with cancellation semantics and edge case handling.
- Full DI integration with `UseMemori(...)` and `AddMemori(...)` helpers.
- 66 comprehensive NUnit tests covering all major paths.
- README with current state and usage examples.

## Summary

The follow-up items are ergonomics, implementation hardening, examples, documentation, and release polish discovered during Phase 1 implementation:

- More flexible prompt injection placement and formatting.
- Broader persistence filtering and redaction.
- Better provider metadata and streaming edge-case handling.
- Better attribution/session lifecycle ergonomics.
- Better implementer guidance for storage and augmentation.
- More examples, tests, CI, packaging, and release workflow.

These are worth tracking for future releases beyond Phase 1 and Phase 2.

## Resolved Implementation Decisions

Use these decisions as constraints for follow-up implementation work. Do not re-open them unless new usage evidence or API limitations make the current direction impractical.

- Prompt injection should keep the current good-citizen default of inserting recalled memory as a `system` message, while exposing options for hosts that need another role, placement, or merge strategy.
- Structured prompt context should be public enough for host applications to inspect, render, and override formatting. Keep the rendered string path as the default convenience API.
- Persistence filtering should happen after conversion to `ConversationMessage`. This keeps the durable conversation model as the policy boundary, minimizes immediate changes, and leaves provider-native pre-conversion filtering as a later extension if real provider scenarios require it.
- Durable memory attribution should remain entity-centered. Sessions and conversations provide capture/history grouping, but should not become a separate durable memory scope in this backlog.
- Storage implementer tests and examples should stay inside the existing `src/Memori.Tests` project for now. The repository is still small enough that a separate reusable test package would add process overhead before it adds value.
- The package should continue to target `net10.0` for now. The library is intentionally aligned with current .NET and newer tensor APIs.

## Follow-Up Matrix

| Area | Current State | Follow-Up Value |
| --- | --- | --- |
| Prompt injection placement | Memory is injected as a system message via configured formatter. | Hosts may need control over where and how memory appears in chat history. |
| Structured prompt formatting | Formatting produces a string block with configurable sections. | Hosts may want structured sections for UI rendering or custom prompt templates. |
| Message filtering before persistence | System-message stripping and Memori-context exclusion implemented. | Hosts may need role filtering, tool filtering, redaction, or custom transforms. |
| Provider metadata | Basic message metadata is copied. | Provider responses may include useful metadata, continuation tokens, or deferred response state. |
| Session lifecycle ergonomics | Attribution/session can be set, created, and managed. | Long-running apps may need explicit inspection, clearing, and resumption helpers. |
| Storage implementer experience | `IStorage` exists with comprehensive contract tests and `InMemoryStorage` reference. | External storage authors need guides, examples, and reusable test harnesses. |
| Augmentation developer experience | `IAugmentationClient` and `PromptAugmentationClient` implemented. | App authors need examples, mapping helpers, and idempotency guidance. |
| Documentation/examples | README has current state and basic usage. | Developers need a fuller journey and runnable scenarios. |
| Test hardening | 66 tests covering core paths and edge cases. | More negative, concurrency, metadata, and formatting tests reduce regressions. |
| Packaging/release | Project has package metadata and version. | CI, versioning, validation, and publish docs are needed before release. |

## Ergonomics Follow-Ups

### 1. First-class dependency injection and composition

Current state:

- `AddMemori(...)` and `UseMemori(...)` helpers exist.
- DI registration works but requires understanding the internal object graph.

Follow-up value:

- A single entry point that registers all required services would simplify adoption.
- Configuration-bound options would enable standard .NET configuration patterns.

Tasks:

- [x] Add a comprehensive registration API that covers the common path with defaults.
- [x] Add factory-based overloads for host-provided `IStorage`, `IEmbeddingGenerator<string, Embedding<float>>`, and `IAugmentationClient`.
- [x] Add a configuration-bound options path so `MemoriOptions` can be created from standard .NET configuration.
- [x] Add a clean way to create a `Memori` instance from `IServiceProvider` without explicit plumbing.
- [x] Add tests that verify the DI graph resolves correctly and that custom factories are honored.

### 2. Prompt injection placement and styles

Current state:

- Memory is injected as a system message.
- Formatter is aligned with `MemoriOptions` configuration.

Follow-up value:

- Some providers and host applications may prefer developer messages, merged instructions, or later placement in the chat history.

Tasks:

- [x] Add a placement policy for where recalled memory is inserted relative to existing chat history.
- [x] Keep system-message injection as the default and support alternative injection roles or styles for developer messages, instruction messages, or tool-like context through explicit host configuration.
- [x] Allow hosts to prepend, append, or merge memory context with an existing host-provided instruction message.
- [x] Add tests for insertion before all messages, after existing system/developer messages, and disabled injection.

### 3. Structured prompt formatting output

Current state:

- Prompt context formatting currently produces a string block.

Follow-up value:

- Apps may need to render recalled facts and summaries in UI or pass them into custom templates instead of using one final string.

Tasks:

- [x] Add a public structured prompt context model with separate facts, summaries, metadata, and final rendered text.
- [x] Add formatting options for bullet style, timestamp rendering, section headings, and summary inclusion.
- [x] Provide helpers for hosts that want to render memory context in UI or custom prompt templates.
- [x] Add tests for structured output and string rendering consistency.

### 4. Broader message filtering before persistence

Current state:

- Capture has system-message stripping.
- Excluding Memori-injected context is implemented.
- `MemoriChatClient` converts `ChatMessage` values to `ConversationMessage` before calling `Memori.CaptureAsync`.

Follow-up value:

- Hosts may need privacy filtering, tool-message filtering, empty-message filtering, or redaction before durable storage.
- Filtering after conversion keeps the durable conversation model as the policy boundary and avoids provider-specific middleware hooks until usage proves they are needed.

Tasks:

- [x] Add a configurable `ConversationMessage` filter for persistence after conversion from provider messages.
- [x] Allow hosts to drop tool messages, developer messages, empty messages, or provider-specific messages.
- [x] Allow hosts to redact or transform message content before storage.
- [x] Add tests for role-based filtering and custom predicate filtering.
- [x] Document that provider-native pre-conversion filtering is intentionally deferred until concrete provider scenarios justify it.

### 5. Provider metadata, continuations, and background responses

Current state:

- Basic metadata is copied from `ChatMessage.AdditionalProperties`.
- Streaming capture reconstructs final messages after streaming completes.

Follow-up value:

- Provider metadata is useful for traceability, debugging, and future integrations.

Tasks:

- [x] Preserve useful provider metadata from `ChatResponse` and `ChatResponseUpdate` where available.
- [x] Add explicit handling for continuation tokens if exposed by Microsoft.Extensions.AI APIs.
- [x] Add explicit handling for background or deferred responses if supported by provider clients.
- [x] Document which metadata is stored, ignored, or intentionally not normalized.
- [x] Add fake provider tests for continuation/background response scenarios when APIs support them.

### 6. Entity and session lifecycle ergonomics

Current state:

- `Memori` can set attribution, create a new session, and set a session.
- Current attribution and session state are not directly inspectable or clearable.
- Durable memory scope is entity-centered; sessions and conversations provide lifecycle and history grouping.

Follow-up value:

- Long-running applications and hosted services need explicit state lifecycle controls.
- Hosts should be able to clear state on reused `Memori` instances without treating sessions as a separate durable memory scope.

Tasks:

- [x] Add read-only accessors for current attribution and session id if they are safe for the chosen lifetime model.
- [x] Add `ClearAttribution()` and `ClearSession()` helpers.
- [x] Add a session resume helper for hosts that manage conversation lifecycle externally, while keeping recall/delete memory operations entity-scoped.
- [x] Add tests for session reuse, timeout rollover, clear, and resume behavior.

### 7. Storage implementer experience

Current state:

- `IStorage` exists.
- `InMemoryStorage` is the reference implementation with comprehensive contract tests.
- Storage contract testing is complete.

Follow-up value:

- External storage providers need clear semantics and reusable examples.

Tasks:

- [x] Add a storage implementer guide with expected semantics for every `IStorage` method.
- [x] Add examples for custom storage implementations.
- [x] Add in-process storage contract tests in `src/Memori.Tests` that can be reused internally without creating a separate test package.
- [x] Document concurrency, idempotency, and atomicity expectations in more detail.

### 8. Augmentation developer experience

Current state:

- `IAugmentationClient` exists.
- `PromptAugmentationClient` provides built-in extraction support.

Follow-up value:

- Developers need practical guidance for producing facts, triples, attributes, and summaries safely.

Tasks:

- [x] Add a custom augmentation client guide.
- [x] Add helpers for mapping raw extraction output into `NewMemoryFact`, `SemanticTriple`, attributes, and summaries.
- [x] Add guidance for deduplication and idempotency expectations around generated memories.
- [x] Add tests that verify each augmentation output type is written correctly.

### 9. Documentation and examples

Current state:

- README contains current state and basic usage.
- Tests include a hero scenario.

Follow-up value:

- Users need a clear path from installation to a full memory lifecycle.

Tasks:

- [x] Add a compact getting-started guide for the .NET library.
- [x] Add a complete chat integration example using `ChatClientBuilder`.
- [x] Add a dependency injection example using `IServiceCollection`.
- [x] Add a custom storage implementation guide.
- [x] Add a custom augmentation/extraction guide.
- [x] Add a runnable hero scenario example that shows attribution, capture, augmentation, recall, and injection across turns.

### 10. Test hardening beyond Phase 1

Current state:

- 66 tests cover core paths and edge cases.
- Direct Phase 1 alignment tests are complete.

Follow-up value:

- Broader negative, concurrency, and metadata tests reduce regressions once the public surface grows.

Tasks:

- [ ] Add stress tests for concurrent capture and recall.
- [ ] Add negative tests for invalid options.
- [ ] Add negative tests for invalid attribution/session input.
- [ ] Add more prompt-formatting tests for timestamps, summaries, and formatting options.
- [ ] Add provider metadata propagation tests once metadata behavior is defined.

### 11. Packaging, CI, and release polish

Current state:

- The package project has metadata and a version.
- Release workflow is not yet established.

Follow-up value:

- The library needs repeatable release mechanics before public package publishing.

Tasks:

- [ ] Add CI that restores, builds, tests, and packs the solution.
- [ ] Add package metadata validation.
- [ ] Add a versioning policy.
- [ ] Add release notes or changelog entries for the .NET package surface.
- [ ] Add NuGet publish instructions.
- [ ] Document `net10.0` as the supported target for the initial package and explain that this follows the library's use of current .NET and tensor APIs.

## Suggested Order

1. Add first-class DI and composition for better adoption ergonomics.
2. Add prompt injection placement and structured formatting.
3. Add persistence filtering before broader production use.
4. Add lifecycle helpers once the intended `Memori` lifetime model is clear.
5. Add provider metadata and continuation handling when the relevant Microsoft.Extensions.AI APIs are confirmed.
6. Add storage and augmentation implementer guides.
7. Add examples and test hardening.
8. Add CI, packaging, and release workflow.

## Coding-Agent Guidance

- Treat this document as an implementation backlog with resolved design constraints, not as a brainstorming document.
- Keep changes minimal and cohesive. Prefer extending the existing `Memori`, `MemoriOptions`, `MemoriRequestOptions`, `MemoriChatClient`, formatting, and test patterns.
- Preserve storage-provider agnosticism. Do not introduce first-party database integrations in this backlog.
- Preserve the default host experience: memory injection and capture should work with sensible defaults, with flexibility exposed through options for applications that need it.
- When adding public API, include NUnit coverage in `src/Memori.Tests` and update `README.md` if usage changes.
- If an implementation exposes a need for provider-native pre-conversion filtering or a separate storage test package, document the evidence before changing direction.
