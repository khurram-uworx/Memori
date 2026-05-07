# Memori .NET Follow-Up Backlog

## Coding-Agent Guidance

- Treat this document as an implementation backlog with resolved design constraints, not as a brainstorming document.
- Keep changes minimal and cohesive. Prefer extending the existing `Memori`, `MemoriOptions`, `MemoriRequestOptions`, `MemoriChatClient`, formatting, and test patterns.
- Preserve storage-provider agnosticism. Do not introduce first-party database integrations in this backlog.
- Preserve the default host experience: memory injection and capture should work with sensible defaults, with flexibility exposed through options for applications that need it.
- When adding public API, include NUnit coverage in `src/Memori.Tests` and update `README.md` if usage changes.
- If an implementation exposes a need for provider-native pre-conversion filtering or a separate storage test package, document the evidence before changing direction.

---

### 1. Test hardening beyond Phase 1

Current state:

- 66 tests cover core paths and edge cases.
- Direct Phase 1 alignment tests are complete.

Follow-up value:

- Broader negative, concurrency, and metadata tests reduce regressions once the public surface grows.

Status: ✅ Completed in Phase 2. 46 tests added (165→211), covering concurrent capture/recall stress, invalid options, invalid attribution/session, prompt-formatting edge cases, and provider metadata propagation.

Tasks:

- [x] Add stress tests for concurrent capture and recall.
- [x] Add negative tests for invalid options.
- [x] Add negative tests for invalid attribution/session input.
- [x] Add more prompt-formatting tests for timestamps, summaries, and formatting options.
- [x] Add provider metadata propagation tests once metadata behavior is defined.

### 2. Packaging, CI, and release polish

Current state:

- The package project has metadata and a version.
- Release workflow is not yet established.

Follow-up value:

- The library needs repeatable release mechanics before public package publishing.

Status: ✅ Completed in Phase 2. CI workflow builds/tests/packs, package metadata + SourceLink/icon/license/tags complete, versioning policy documented in `RELEASING.md`, changelog created, NuGet publish instructions documented, `net10.0` confirmed as target.

Tasks:

- [x] Add CI that restores, builds, tests, and packs the solution.
- [x] Add package metadata validation.
- [x] Add a versioning policy.
- [x] Add release notes or changelog entries for the .NET package surface.
- [x] Add NuGet publish instructions.
- [x] Document `net10.0` as the supported target for the initial package and explain that this follows the library's use of current .NET and tensor APIs.
