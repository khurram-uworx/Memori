# Memori .NET Phase 2 Roadmap

This document captures Phase 2 work for the Memori .NET library, as defined in `docs/IDEA.md`.

## Phase 2 Scope (from `docs/IDEA.md`)

Phase 2 focuses on **advanced features and integrations** beyond the Phase 1 primitives:

* Microsoft Vector Data Extensions integration
* External vector database providers
* Distributed retrieval/ranking
* Workspace/team/shared memory scopes
* Conflict resolution/versioning
* Summarized thread memory
* User memory management APIs/UI

---

## Phase 2 Implementation Tasks

### 1. Microsoft Vector Data Extensions integration

**Phase 2 item from IDEA.md:** Microsoft Vector Data Extensions integration

What to add:
- Adapter for Microsoft Vector Data Extensions as an alternative to direct `IEmbeddingGenerator` usage.
- Integration helpers for hosts using Microsoft's vector data abstractions.
- Examples showing how to use Microsoft Vector Data Extensions with Memori.

Task breakdown:
- Add a new package or module for Microsoft Vector Data Extensions support.
- Create adapter implementations for common vector data operations.
- Add tests that verify the adapter works with the core `IStorage` contract.

### 2. External vector database providers

**Phase 2 item from IDEA.md:** External vector database providers

What to add:
- Reference implementations for popular vector databases (Pinecone, Weaviate, Milvus, etc.).
- These should be in separate packages, not in the core Memori library.
- Clear examples and documentation for implementing custom `IStorage` for other backends.

Task breakdown:
- Create separate NuGet packages for each vector database provider.
- Each package implements `IStorage` with provider-specific optimizations.
- Add integration tests for each provider.
- Document the expected behavior and performance characteristics.

### 3. Distributed retrieval/ranking

**Phase 2 item from IDEA.md:** Distributed retrieval/ranking

What to add:
- Support for ranking memory candidates across multiple storage backends.
- Distributed ranking strategies that combine results from multiple sources.
- Load balancing and failover for distributed memory retrieval.

Task breakdown:
- Add a distributed ranker abstraction that extends `IMemoryRanker`.
- Implement a composite storage adapter that queries multiple backends.
- Add tests for distributed ranking with multiple storage sources.
- Document consistency and ordering guarantees.

### 4. Workspace/team/shared memory scopes

**Phase 2 item from IDEA.md:** Workspace/team/shared memory scopes

What to add:
- Support for shared memory contexts across multiple users or teams.
- Scope management for workspace-level vs. user-level memories.
- Access control patterns for shared memory.

Task breakdown:
- Extend `IStorage` with scope/workspace identifiers.
- Add scope-aware recall and capture methods.
- Add tests for scope isolation and shared memory retrieval.
- Document scope semantics and access patterns.

### 5. Conflict resolution/versioning

**Phase 2 item from IDEA.md:** Conflict resolution/versioning

What to add:
- Support for versioning memory records when conflicts arise.
- Conflict detection and resolution strategies.
- Audit trails for memory changes.

Task breakdown:
- Add versioning fields to memory record models.
- Implement conflict detection in storage implementations.
- Add resolution strategies (last-write-wins, merge, manual review).
- Add tests for conflict scenarios and resolution.

### 6. Summarized thread memory

**Phase 2 item from IDEA.md:** Summarized thread memory

What to add:
- Automatic or semi-automatic summarization of conversation threads.
- Storage and retrieval of thread summaries.
- Integration with augmentation pipeline for summary generation.

Task breakdown:
- Add a thread summarization abstraction.
- Implement a reference summarizer using `IChatClient`.
- Add storage support for thread summaries.
- Add tests for summary generation and retrieval.

### 7. User memory management APIs/UI

**Phase 2 item from IDEA.md:** User memory management APIs/UI

What to add:
- APIs for users to inspect, edit, and delete their stored memories.
- Optional UI components for memory management.
- Privacy and consent controls.

Task breakdown:
- Add memory inspection and management APIs to `Memori`.
- Add filtering and search capabilities for user memories.
- Add deletion and editing endpoints.
- Add tests for memory management operations.
- Document privacy and consent patterns.

---

## Suggested Implementation Order

1. **Microsoft Vector Data Extensions integration** - Foundation for Phase 2 vector work.
2. **External vector database providers** - Enables production deployments.
3. **Distributed retrieval/ranking** - Supports larger-scale applications.
4. **Workspace/team/shared memory scopes** - Enables multi-user scenarios.
5. **Conflict resolution/versioning** - Ensures data consistency.
6. **Summarized thread memory** - Improves memory quality over time.
7. **User memory management APIs/UI** - Completes the user-facing experience.

---

## Related Documents

- `docs/IDEA.md` - Original Phase 1 and Phase 2 design proposal.
- `docs/FOLLOWUP.md` - Phase 1 ergonomics and polish items discovered during implementation.
- `docs/NEXT.md` - Additional follow-up work and future considerations.
