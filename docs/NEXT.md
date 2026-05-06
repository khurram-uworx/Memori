# What's Next with Memori .NET

This document captures the most useful next steps for the .NET library after the current core surface. It is written to be turned into implementation tasks later, so each section is intentionally concrete and scoped for coding work.

The priorities below assume:

- The core `Memori` facade already exists.
- Microsoft.Extensions.AI is the primary integration surface.
- Storage provider implementations remain out of scope for the core package.

## 1. First-class dependency injection and composition

The current DI story works, but it is still manual enough that hosts must understand the internal object graph.

What to add:
- A single entry point that registers all required services for `Memori`, recall, augmentation, and chat integration.
- Overloads that let a host provide custom `IStorage`, `IMemoriEmbeddingGenerator`, and `IAugmentationClient` implementations.
- A configuration-bound options path so `MemoriOptions` can be created from standard .NET configuration.
- A clean way to create a `Memori` instance from `IServiceProvider` without explicit plumbing in user code.

Task breakdown:
- Add a registration API that covers the common path with defaults.
- Add factory-based overloads for host-provided services.
- Add tests that verify the DI graph resolves correctly and that custom factories are honored.

## 2. Prompt injection policy

The current chat wrapper injects a system message with recalled context. That is a reasonable default, but hosts may need more control.

What to add:
- A formatter abstraction for building the memory context block.
- A placement policy for where injected memory should appear in the request.
- A request-scoped option to disable recall injection for a single turn.
- Support for alternative injection roles or instruction strategies where a host needs a non-system approach.

Task breakdown:
- Define an injection formatter interface and a default implementation.
- Add a request options type for recall and capture flags.
- Add tests for injection placement and opt-out behavior.

## 3. More complete Microsoft.Extensions.AI middleware

The wrapper is useful now, but there are still edge cases around richer responses and streaming behavior.

What to add:
- Better handling for provider metadata on both chat responses and response updates.
- Safer streaming capture when the underlying client returns tool calls, multi-message outputs, or background continuation tokens.
- Explicit support for chat options that need to flow through unchanged.
- Better behavior when the host uses conversation continuation semantics.

Task breakdown:
- Expand the wrapper tests to cover multi-message and tool-heavy responses.
- Capture provider metadata into the durable message model where it is useful.
- Add streaming continuation tests against a fake `IChatClient`.

## 4. Augmentation workflow improvements

The augmentation boundary exists, but it is still intentionally minimal. The next step is to make it easier for applications to plug in a useful structured-memory producer.

What to add:
- A reference augmentation client for demos and tests.
- A stronger contract for generated facts, triples, process attributes, and conversation summaries.
- Helper utilities for turning raw augmentation output into storage-ready models.
- Better guidance and tests around deduplication/idempotency expectations.

Task breakdown:
- Add a reference augmentation client implementation.
- Add a few augmentation fixture tests that validate each output type.
- Add helper methods for common conversion patterns.

## 5. Prompt formatting and recall ergonomics

Recall works, but the developer-facing formatting surface can still be made easier to use.

What to add:
- A richer formatting API that can return sections instead of only a single formatted block.
- Options for headings, bullets, and timestamp rendering.
- A separate formatter for summaries and a way to reuse those sections outside prompt injection.
- A helper that returns both raw recall results and the final prompt block.

Task breakdown:
- Introduce formatting options for prompt context generation.
- Add structured formatting helpers for facts and summaries.
- Add tests for timestamp formatting and summary de-duplication.

## 6. Session and attribution lifecycle helpers

The facade manages attribution and session state, but it could be more explicit and ergonomic for long-running apps.

What to add:
- Helpers to inspect, clear, and replace the current attribution context.
- A clearer session continuation API.
- Utilities for hosts that manage their own conversation lifecycle but still want Memori to bind to it.

Task breakdown:
- Add small lifecycle helpers that do not complicate the core facade.
- Add tests for session reuse, rollover, and explicit reset behavior.

## 7. Storage contract validation

The storage abstraction is intentionally kept outside the core package implementation work, but the contract itself should be easier to validate for third-party implementers.

What to add:
- A reusable contract test suite that custom storage implementations can run.
- Better examples showing the expected behavior of each storage method.
- Guidance on concurrency, idempotency, and atomicity expectations.

Task breakdown:
- Create a storage contract test base that can be reused by external providers.
- Add a short implementer guide with expected semantics.
- Add concurrency-focused tests for the in-memory reference implementation.

## 8. Documentation and examples

The repository benefits from a few strong examples that show how the pieces fit together in the .NET style.

What to add:
- A compact getting-started guide for the core library.
- A full end-to-end “hero” scenario that shows attribution, capture, recall, augmentation, and chat wrapping.
- A DI example that shows a standard .NET application wiring the library in.
- A custom augmentation client example.
- A custom storage implementation guide, while keeping the actual storage implementations out of the core package.

Task breakdown:
- Add one or two focused .NET examples.
- Add a more narrative README section for the main integration pattern.
- Add a short guide for implementing custom `IStorage`.

## 9. Test hardening

The current tests cover the most important flows, but there are still a few areas that would benefit from stronger coverage.

What to add:
- Additional concurrency coverage for capture and recall.
- Better validation tests for invalid identifiers and invalid option values.
- Streaming tests with more realistic multi-update responses.
- Tests that verify provider metadata propagation where it matters.

Task breakdown:
- Add negative tests for options and identifiers.
- Add a stress test for concurrent capture and recall against in-memory storage.
- Add one more streaming integration test that exercises a multi-part response.

## 10. Packaging and release quality

The code is usable now, but a few release-oriented tasks would make it easier to ship and maintain.

What to add:
- A clear versioning policy for the .NET package.
- Changelog entries for public releases.
- CI coverage that builds the library and test project together.
- Package validation before publishing.

Task breakdown:
- Add a build-and-test pipeline for the solution.
- Add release notes and package metadata checks.
- Add a NuGet publish checklist for maintainers.
