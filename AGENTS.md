# AGENTS.md

## Purpose

This repository hosts the .NET Memori library: durable memory primitives and middleware for AI apps using `Microsoft.Extensions.AI`.

Primary goals for changes:

- Keep the core package storage-provider agnostic.
- Preserve the `Memori` facade ergonomics for attribution, capture, recall, and augmentation.
- Keep `IChatClient` middleware behavior correct for both non-streaming and streaming flows.
- Maintain strong tests for memory correctness and regressions.

## Repository Map

- `src/Memori`: main library (`net10.0`)
  - `Abstractions`: public extension points (`IStorage`, embeddings, augmentation contracts).
  - `Models`: domain models and options.
  - `Storage`: `InMemoryStorage` reference implementation.
  - `Search`: recall orchestration and formatting.
  - `Augmentation`: background augmentation pipeline.
  - `MicrosoftExtensionsAI`: `IChatClient` middleware and DI extensions.
  - `Memori.cs`: facade entry point.
- `src/Memori.Tests`: NUnit test suite for facade, storage, augmentation, and chat middleware behavior.
- `docs`: design notes, gaps, and follow-up plans.
- `.github/workflows/ci.yml`: authoritative CI build/test steps.

## Local Workflow

From repo root:

- Restore: `dotnet restore`
- Build (release): `dotnet build --configuration Release`
- Test (release): `dotnet test --configuration Release --verbosity normal`
- Targeted library build: `dotnet build src/Memori/Memori.csproj`

Match CI defaults when possible (`Release` configuration, full test run).

## Coding Conventions

- Respect `.editorconfig`:
  - C# uses 4 spaces and CRLF line endings.
  - Nullable is enabled; avoid introducing nullability warnings.
  - Private fields use `camelCase` without underscore prefix.
- Keep public API names and docs clear; this package is intended for external consumption.
- Prefer domain-oriented APIs; do not leak provider-specific storage details into `Memori`.
- Keep async APIs cancellation-aware and use `ConfigureAwait(false)` in library code.
- Avoid unnecessary dependencies or framework-specific coupling in the core package.

## Architectural Guardrails

- `IStorage` is the durable boundary. New persistent behavior should flow through it.
- `InMemoryStorage` is reference behavior, not a production database driver.
- `MemoriChatClient` must:
  - recall before model call,
  - avoid persisting injected memory context as conversation content,
  - capture input + assistant output correctly for both standard and streaming responses.
- Prompt-context formatting should remain driven by configurable options and shared formatting paths.

## Testing Expectations

- Add or update NUnit tests in `src/Memori.Tests` for any non-trivial behavior change.
- Prefer behavior-focused tests around:
  - attribution/session lifecycle,
  - capture and recall correctness,
  - augmentation side effects,
  - middleware injection/capture behavior,
  - streaming edge cases.
- Keep test doubles simple and deterministic.

## Scope and Non-Goals

- Do not add first-party database integrations to this package.
- Do not add provider-specific LLM wrappers when `IChatClient` middleware can cover the use case.
- Keep cloud/service-specific helpers out of the core library unless explicitly requested.

## Change Hygiene

- Keep edits minimal and cohesive.
- Update `README.md` when public behavior or usage changes.
- Prefer extending existing patterns over introducing parallel abstractions.
- If a change impacts architecture direction, capture rationale in `docs/`.
