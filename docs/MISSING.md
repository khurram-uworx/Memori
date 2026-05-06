# Memori .NET Missing Features

This document is a forward-looking inventory of product gaps and follow-up work for the .NET library. It excludes storage provider implementations themselves, since those are intentionally outside the core package scope.

## 1. Public configuration and composition surface

The current core library can be used directly, but the composition story is still narrow.

Missing work:
- Add a first-class options model for application wiring beyond the current constructor-based setup.
- Expose a consistent registration pattern for `IStorage`, embeddings, augmentation, and chat middleware.
- Allow hosts to supply custom implementations through DI without needing to understand the internal class graph.
- Provide a single entry point for “hosted” setup so applications can register Memori in one call.

Suggested follow-up tasks:
- Add a configuration binder for `MemoriOptions`.
- Add overloads of `AddMemori(...)` that accept factory delegates for storage, embeddings, and augmentation.
- Add a small helper or factory for building a fully configured `Memori` instance from services.

## 2. Custom prompt injection controls

The current chat wrapper injects recalled memory as a system message block, which is a good default, but it is not yet customizable enough for all hosts.

Missing work:
- Add a strategy abstraction for memory injection formatting.
- Allow callers to choose where recalled context is placed relative to the chat history.
- Support alternative injection styles for hosts that prefer developer messages, instructions, or tool-like context instead of a system message.
- Make the memory context formatting reusable outside the chat wrapper.

Suggested follow-up tasks:
- Introduce a prompt injection formatter interface.
- Add a default formatter that emits the current `<memori_context>` block.
- Allow hosts to plug in a custom placement policy for the injected message.

## 3. Augmentation client guidance and scaffolding

The augmentation boundary exists, but the library still leaves the real augmentation story open-ended.

Missing work:
- Add a concrete reference augmentation client implementation for local demos and tests.
- Add a documented shape for generated facts, semantic triples, process attributes, and conversation summaries.
- Add a helper for converting augmentation output into storage writes in a more reusable way.
- Add guidance for deduplication and idempotency expectations around generated memories.

Suggested follow-up tasks:
- Create a reference augmentation client that maps a simple conversation summary into structured memory updates.
- Add tests that verify the augmentation service writes each result type correctly.
- Add a small helper for mapping raw text output into `NewMemoryFact` / `SemanticTriple` instances.

## 4. Chat wrapper completeness

The Microsoft.Extensions.AI middleware is functional, but there are still edge cases and quality-of-life items to close out.

Missing work:
- Preserve and propagate more provider metadata when capturing chat results.
- Add explicit handling for background responses and continuation tokens.
- Make streaming capture more robust when responses include tool messages or multi-part updates.
- Add support for message filtering before persistence beyond the current system-message stripping flag.
- Expose a way to opt out of injection for a single request.

Suggested follow-up tasks:
- Extend the chat wrapper to copy richer response metadata into captured conversation messages.
- Add request-scoped options to skip recall or capture when needed.
- Add tests for conversation continuation, tool messages, and mixed-content responses.

## 5. Recall and prompt formatting refinements

The recall pipeline works, but the developer-facing ergonomics can still improve.

Missing work:
- Provide richer formatting options for recalled results.
- Add a dedicated way to format summaries independently from facts.
- Expose a higher-level “build prompt context” helper that supports multiple output shapes.
- Add more reusable formatting primitives for hosts that want to render memory context in their own UI or prompt template.

Suggested follow-up tasks:
- Add `FormatPromptContext` options for heading names, bullet styles, and timestamp rendering.
- Add a formatter that returns structured sections instead of a single string.
- Add a helper that returns raw recalled facts plus the final formatted block.

## 6. Entity/session lifecycle helpers

The facade currently handles the main flow, but application ergonomics for lifecycle management can still be improved.

Missing work:
- Add a way to inspect or reset the active attribution and session state.
- Add explicit session continuation helpers for hosts that manage a conversation lifecycle externally.
- Add a helper for resuming the current session without replacing the attribution context.
- Add more lifecycle tests around session reuse and conversation rollover.

Suggested follow-up tasks:
- Add `ClearAttribution()` and `ClearSession()` helpers if the host needs them.
- Add a `ResumeSession(...)` flow that returns the active conversation.
- Add tests for session timeout transitions and conversation reuse.

## 7. Storage contract ergonomics

The storage abstraction is intentionally small, but the developer experience around it can still be improved.

Missing work:
- Add a contract test suite for third-party storage implementations.
- Add clearer examples for implementing `IStorage`.
- Add a small in-process harness that storage implementers can run against their own backend.
- Add a more explicit concurrency guidance section for implementers.

Suggested follow-up tasks:
- Create a storage contract test base class.
- Add example custom storage documentation in the repository.
- Add tests that verify custom storage implementations support the expected idempotent operations.

## 8. Documentation and examples

The current README is useful, but the public docs still need a fuller developer journey.

Missing work:
- Add a short getting-started guide for the core library.
- Add a complete chat integration example using `ChatClientBuilder`.
- Add a DI example using `IServiceCollection`.
- Add a custom storage implementation guide.
- Add a custom augmentation client guide.
- Add a “hero scenario” example that shows the full memory lifecycle.

Suggested follow-up tasks:
- Create a compact end-to-end usage guide.
- Add one or two more examples under `examples/` focused on .NET usage.
- Document the expected role of `Memori`, `IStorage`, augmentation, and the chat wrapper.

## 9. Test coverage gaps

The current tests cover the basic path, but a few important cases are still missing.

Missing work:
- More concurrency coverage for `InMemoryStorage`.
- More prompt-formatting assertions for timestamp and summary rendering.
- Negative tests for invalid options and invalid attribution/session input.
- Tests for request-scoped behavior in the chat wrapper.
- Tests for provider metadata propagation through capture.

Suggested follow-up tasks:
- Add stress tests for concurrent capture and recall.
- Add validation tests for option bounds and empty identifiers.
- Add a dedicated streaming integration test with tool-call-like responses.

## 10. Packaging and release polish

The package is functional, but the release story can still be hardened.

Missing work:
- Add a changelog entry for the .NET release surface.
- Add package metadata verification and versioning policy.
- Add a CI matrix entry for the test project.
- Add sample publish instructions for the NuGet package.

Suggested follow-up tasks:
- Add CI build and test coverage for both library and test projects.
- Add release notes for the public .NET package surface.
- Add a package validation step before publishing.
