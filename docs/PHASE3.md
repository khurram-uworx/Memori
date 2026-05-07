# Memori .NET Phase 3 Roadmap

## Prerequisite

Phase 2 is complete — all 13 foundation/production/enterprise tier items are implemented, all 8 gap tasks closed, 211 tests passing.

Phase 3 targets items explicitly marked as non-goals or future backlog in `docs/IDEA.md` that naturally extend the existing infrastructure.

---

## 1. Graph Reasoning Over Semantic Triples

### Background

Phase 1/2 stores semantic triples (`subject-predicate-object`) as `MemoryFactRecord` entries with `MemoryType = "semantic_triple"`, but they are stored as opaque text. There is no query mechanism that exploits their structure for knowledge graph construction or relationship inference.

### Scope

- Define a `SemanticTripleStore` or extend `MemorySearchService` to query triples by subject/predicate/object.
- Support basic graph operations: "find all triples where entity X is subject", "what relationships exist between X and Y".
- Optionally support multi-hop inference ("X works at Y, Y is located in Z → X is related to Z").

### Acceptance criteria

- [ ] Semantic triples are queryable by subject, predicate, and object.
- [ ] Graph query returns structured results (not opaque `MemoryFactRecord` content).
- [ ] Existing semantic triple storage remains unchanged.
- [ ] Build and 211+ tests pass.

---

## 2. Episodic / Cross-Conversation Memory Synthesis

### Background

Thread summarization per conversation exists (`IThreadSummarizer`). But the library has no mechanism to synthesize higher-level memories *across* multiple conversations — e.g., noticing that "user always asks about dark mode" across 5 separate chats and promoting it to a durable preference.

### Scope

- Define an `IEpisodicMemoryClient` interface (or extend `IAugmentationClient`) for cross-conversation synthesis.
- Implement a default client that queries recent facts/summaries for a given entity and asks an `IChatClient` to extract cross-cutting patterns.
- Store synthesized patterns as `MemoryFactRecord` entries with `MemoryType = "episodic"` and elevated confidence.

### Design constraints

- Must be optional — existing behavior unchanged when not configured.
- Should run as a background job (separate from per-turn augmentation), not inline in the chat pipeline.
- Synthesis frequency should be configurable (every N conversations, on demand, etc.).

### Acceptance criteria

- [ ] Cross-conversation synthesis produces consolidated memory entries.
- [ ] Only non-deleted, non-synthesized facts are used as input.
- [ ] Existing augmentation pipeline is unaffected.
- [ ] Build and 230+ tests pass.

---

## 3. Enterprise Access Control Filters

### Background

Scope isolation exists (workspace/tenant filtering via `SetScope`). But there is no mechanism for attribute-based access control (ABAC) or role-based filters on recall — e.g., "only return facts tagged with `security_level=confidential` if the caller has clearance".

### Scope

- Add an optional `IMemoryAccessFilter` abstraction that can inspect and filter facts before/after recall.
- Define a filter delegate on `MemoriOptions` (e.g., `Func<MemoryFactRecord, bool>? RecallFilter`).
- Ensure filters compose with scope isolation.

### Constraints

- Must be optional — existing behavior unchanged when not configured.
- Must not leak credential/access logic into the core library — only provide the extension point.

### Acceptance criteria

- [ ] Recall accepts an optional access filter.
- [ ] Filtered facts are excluded from results (not just hidden).
- [ ] Existing recall behavior unchanged when filter is null.
- [ ] Build and 240+ tests pass.

---

## 4. Memory Audit Trail

### Background

The versioning system tracks record versions and `PreviousVersionId`, but there is no structured audit log of who changed what and when.

### Scope

- Optionally record audit events (create, update, soft-delete, restore, hard-delete) to an `IAuditLog` sink.
- Events should include: timestamp, actor (entity/process identity), operation, fact ID, previous version, new version.
- Ship a null/no-op implementation by default.

### Constraints

- Must be optional — no performance impact when audit logging is not configured.
- Audit sink is an abstraction — consuming apps plug in their own storage.

### Acceptance criteria

- [ ] Audit events emitted for all memory mutation operations.
- [ ] Default implementation is a no-op.
- [ ] Existing tests pass without configuring audit logging.
- [ ] Build and all tests pass.

---

## 5. Async Memory Export / Import

### Background

There is no bulk export/import mechanism for entity memory. Users who want to migrate or backup have no supported path.

### Scope

- Add `ExportEntityMemoriesAsync(entityId, cancellationToken)` returning a serializable DTO.
- Add `ImportEntityMemoriesAsync(entityId, exportDto, mergeStrategy, cancellationToken)` with conflict handling.
- The format should be storage-provider agnostic (plain DTOs, not storage-specific snapshots).

### Constraints

- Must work with any `VectorStoreCollection` / `IConversationStorage` implementation.
- Import should support merge/overwrite/skip strategies.
- Export must include both facts and conversation history.

### Acceptance criteria

- [ ] Export produces a complete snapshot of an entity's memories and conversations.
- [ ] Import restores with configurable merge strategy.
- [ ] Large exports do not OOM (streaming support).
- [ ] Build and all tests pass.

---

## Non-Goals (Phase 3)

- First-party vector database integrations (Azure AI Search, Qdrant, etc.) — users supply their own `VectorStore` provider.
- Provider-specific LLM wrappers — all provider integration via `IChatClient`.
- Distributed / sharded ranking across regions.
- Real-time memory replication / CDC.

---

## Summary

| # | Feature | Builds On | Effort |
|---|---------|-----------|--------|
| 1 | Graph reasoning over semantic triples | Existing triples storage | Medium |
| 2 | Episodic / cross-conversation synthesis | Augmentation pipeline + summarizer | Medium |
| 3 | Access control filters | Scope isolation | Small |
| 4 | Memory audit trail | Versioning system | Small |
| 5 | Bulk export/import | Storage abstractions | Medium |

All items preserve the pattern of optional, backward-compatible extension points with no mandatory changes to existing behavior.
