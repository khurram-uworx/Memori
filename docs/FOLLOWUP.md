# Memori .NET Follow-Up Backlog

## Scope

This document captures useful follow-up work that is not part of the strict `docs/IDEA.md` delta tracked in `docs/GAPS.md`.

Use this file for product ergonomics, implementation hardening, examples, documentation, and release polish that are worth considering after the core idea alignment work is under control.

It intentionally does not redefine the target architecture. `docs/GAPS.md` remains the source of truth for what is missing from the original idea.

## Current Implementation Snapshot

The current C# implementation already has:

- `Memori` facade for attribution, sessions, recall, capture, augmentation waiting, and deletion.
- `IStorage` and `InMemoryStorage`.
- Embedding abstractions and Microsoft.Extensions.AI adapter.
- Recall/search and prompt context formatting primitives.
- Augmentation boundary and background augmentation service.
- `MemoriChatClient` middleware over `IChatClient`.
- Basic `UseMemori(...)` and `AddMemori(...)` helpers.
- Basic tests and README usage.

## Summary

The follow-up items are mostly about making the library easier and safer to adopt:

- More flexible prompt injection placement and formatting.
- Broader persistence filtering and redaction.
- Better provider metadata and streaming edge-case handling.
- Better attribution/session lifecycle ergonomics.
- Better implementer guidance for storage and augmentation.
- More examples, tests, CI, packaging, and release workflow.

These are worth tracking, but they should not block the stricter `docs/IDEA.md` delta unless a task is promoted into `docs/GAPS.md`.

## Follow-Up Matrix

| Area | Current State | Follow-Up Value |
| --- | --- | --- |
| Prompt injection placement | Memory is injected as a system message. | Hosts may need control over where and how memory appears in chat history. |
| Structured prompt formatting | Formatting produces a string block. | Hosts may want structured sections for UI rendering or custom prompt templates. |
| Message filtering before persistence | System-message stripping exists. | Hosts may need role filtering, tool filtering, redaction, or custom transforms. |
| Provider metadata | Basic message metadata is copied. | Provider responses may include useful metadata, continuation tokens, or deferred response state. |
| Session lifecycle ergonomics | Attribution/session can be set, but not inspected or cleared. | Long-running apps need explicit lifecycle control. |
| Storage implementer experience | `IStorage` exists and in-memory storage is available. | External storage authors need guides, examples, and harnesses. |
| Augmentation developer experience | `IAugmentationClient` exists. | App authors need examples, mapping helpers, and idempotency guidance. |
| Documentation/examples | README has basic usage. | Developers need a fuller journey and runnable scenarios. |
| Test hardening | Basic tests exist. | More negative, concurrency, metadata, and formatting tests reduce regressions. |
| Packaging/release | Project has package metadata. | CI, versioning, validation, and publish docs are needed before release. |

## P1 Follow-Ups

### 1. Prompt injection placement and styles

Current state:

- Memory is injected as a system message.
- Formatter alignment is tracked in `docs/GAPS.md`.

Follow-up value:

- Some providers and host applications may prefer developer messages, merged instructions, or later placement in the chat history.

Tasks:

- [ ] Add a placement policy for where recalled memory is inserted relative to existing chat history.
- [ ] Support alternative injection roles or styles for developer messages, instruction messages, or tool-like context.
- [ ] Allow hosts to prepend, append, or merge memory context with an existing host-provided instruction message.
- [ ] Add tests for insertion before all messages, after existing system/developer messages, and disabled injection.

### 2. Structured prompt formatting output

Current state:

- Prompt context formatting currently produces a string block.

Follow-up value:

- Apps may need to render recalled facts and summaries in UI or pass them into custom templates instead of using one final string.

Tasks:

- [ ] Add a structured prompt context model with separate facts, summaries, metadata, and final rendered text.
- [ ] Add formatting options for bullet style, timestamp rendering, section headings, and summary inclusion.
- [ ] Provide helpers for hosts that want to render memory context in UI or custom prompt templates.
- [ ] Add tests for structured output and string rendering consistency.

### 3. Broader message filtering before persistence

Current state:

- Capture has system-message stripping.
- Excluding Memori-injected context is tracked in `docs/GAPS.md`.

Follow-up value:

- Hosts may need privacy filtering, tool-message filtering, empty-message filtering, or redaction before durable storage.

Tasks:

- [ ] Add a configurable message filter for persistence.
- [ ] Allow hosts to drop tool messages, developer messages, empty messages, or provider-specific messages.
- [ ] Allow hosts to redact or transform message content before storage.
- [ ] Add tests for role-based filtering and custom predicate filtering.

### 4. Provider metadata, continuations, and background responses

Current state:

- Basic metadata is copied from `ChatMessage.AdditionalProperties`.
- Streaming capture reconstructs final messages after streaming completes.

Follow-up value:

- Provider metadata is useful for traceability, debugging, and future integrations.

Tasks:

- [ ] Preserve useful provider metadata from `ChatResponse` and `ChatResponseUpdate` where available.
- [ ] Add explicit handling for continuation tokens if exposed by Microsoft.Extensions.AI APIs.
- [ ] Add explicit handling for background or deferred responses if supported by provider clients.
- [ ] Document which metadata is stored, ignored, or intentionally not normalized.
- [ ] Add fake provider tests for continuation/background response scenarios when APIs support them.

### 5. Entity and session lifecycle ergonomics

Current state:

- `Memori` can set attribution, create a new session, and set a session.
- Current attribution and session state are not directly inspectable or clearable.

Follow-up value:

- Long-running applications and hosted services need explicit state lifecycle controls.

Tasks:

- [ ] Add read-only accessors for current attribution and session id if they are safe for the chosen lifetime model.
- [ ] Add `ClearAttribution()` and `ClearSession()` helpers.
- [ ] Add a session resume helper for hosts that manage conversation lifecycle externally.
- [ ] Add tests for session reuse, timeout rollover, clear, and resume behavior.

## P2 Follow-Ups

### 6. Storage implementer experience

Current state:

- `IStorage` exists.
- `InMemoryStorage` is the reference implementation.
- Storage contract testing is tracked in `docs/GAPS.md`.

Follow-up value:

- External storage providers need clear semantics and reusable examples.

Tasks:

- [ ] Add a storage implementer guide with expected semantics for every `IStorage` method.
- [ ] Add examples for custom storage implementations.
- [ ] Add an in-process test harness that external storage providers can run against their own backend.
- [ ] Document concurrency, idempotency, and atomicity expectations in more detail.

### 7. Augmentation developer experience

Current state:

- `IAugmentationClient` exists.
- Built-in extraction support is tracked in `docs/GAPS.md`.

Follow-up value:

- Developers need practical guidance for producing facts, triples, attributes, and summaries safely.

Tasks:

- [ ] Add a custom augmentation client guide.
- [ ] Add helpers for mapping raw extraction output into `NewMemoryFact`, `SemanticTriple`, attributes, and summaries.
- [ ] Add guidance for deduplication and idempotency expectations around generated memories.
- [ ] Add tests that verify each augmentation output type is written correctly.

### 8. Documentation and examples

Current state:

- README contains basic .NET usage.
- Tests include a hero scenario.

Follow-up value:

- Users need a clear path from installation to a full memory lifecycle.

Tasks:

- [ ] Add a compact getting-started guide for the .NET library.
- [ ] Add a complete chat integration example using `ChatClientBuilder`.
- [ ] Add a dependency injection example using `IServiceCollection`.
- [ ] Add a custom storage implementation guide.
- [ ] Add a custom augmentation/extraction guide.
- [ ] Add a runnable hero scenario example that shows attribution, capture, augmentation, recall, and injection across turns.

### 9. Test hardening beyond IDEA delta

Current state:

- Tests cover the basic path.
- Direct idea-alignment tests are tracked in `docs/GAPS.md`.

Follow-up value:

- Broader negative, concurrency, and metadata tests reduce regressions once the public surface grows.

Tasks:

- [ ] Add stress tests for concurrent capture and recall.
- [ ] Add negative tests for invalid options.
- [ ] Add negative tests for invalid attribution/session input.
- [ ] Add more prompt-formatting tests for timestamps, summaries, and formatting options.
- [ ] Add provider metadata propagation tests once metadata behavior is defined.

### 10. Packaging, CI, and release polish

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
- [ ] Decide supported SDK/runtime versions before publishing.

## Suggested Order

1. Add prompt injection placement and structured formatting after the formatter gap is fixed.
2. Add persistence filtering before broader production use.
3. Add lifecycle helpers once the intended `Memori` lifetime model is clear.
4. Add provider metadata and continuation handling when the relevant Microsoft.Extensions.AI APIs are confirmed.
5. Add storage and augmentation implementer guides.
6. Add examples and test hardening.
7. Add CI, packaging, and release workflow.

## Open Decisions From Follow-Up Work

- Should prompt injection support non-system roles by default, or only through explicit host configuration?
- Should structured prompt context become a public model or remain an internal helper?
- Should persistence filtering happen before or after conversion from `ChatMessage` to `ConversationMessage`?
- Should attribution/session lifecycle helpers exist on `Memori`, or should a separate session/context object own that state?
- Should external storage provider tests ship in the main test project or a separate reusable test package?
- Should release target remain `net10.0`, or should the package target be broadened before publishing?
