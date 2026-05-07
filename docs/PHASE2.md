# Memori .NET Phase 2 Roadmap

This document captures Phase 2 work for the Memori .NET library, as defined in `docs/IDEA.md`.

## Phase 2 Scope (from `docs/IDEA.md`)

Phase 2 focuses on **advanced features and integrations** beyond the Phase 1 primitives:

* Vector database integration via Microsoft Vector Data Extensions
* Distributed retrieval/ranking
* Workspace/team/shared memory scopes
* Conflict resolution/versioning
* Summarized thread memory
* User memory management APIs/UI

---

## Architectural Direction

### The Core Decision

Phase 1 shipped with `IStorage` as a single abstraction covering two fundamentally different concerns:

1. **Vector memory** — facts, semantic triples, process attributes. These are semantically searched, ranked by similarity, and benefit from vector database backends.
2. **Conversation storage** — entities, processes, sessions, conversations, messages. These are relational and ordered; they do not benefit from vector search.

Bundling both into `IStorage` meant any production vector database integration required an adapter layer that translated Memori's domain operations into VectorStore operations — unnecessary complexity that would have to be repeated for every provider.

**Phase 2 splits `IStorage` into two focused abstractions:**

- **`IConversationStorage`** — the relational/ordered half. Manages entities, processes, sessions, conversations, and messages. Small surface, easy to implement, ships with an `InMemoryConversationStorage` default.
- **`VectorStoreCollection<string, MemoryFactRecord>`** — the vector half. Provided directly by `Microsoft.Extensions.VectorData`, configured by the user in their DI container. Memori consumes it directly, the same way it already consumes `IEmbeddingGenerator`.

### Why This Is Better Than an Adapter Layer

The original Phase 2 plan proposed building a `VectorStoreStorageAdapter` that bridged `IStorage` to `VectorStore`. That approach had two problems:

1. It required Memori to maintain adapter code for every provider, even though `Microsoft.Extensions.VectorData` already provides a standard interface that providers implement.
2. It forced users to learn a Memori-specific storage abstraction when the ecosystem already has a well-supported one.

The new approach mirrors how `IEmbeddingGenerator` works: Memori declares a dependency on a standard MEAI-family abstraction, and users configure whatever provider they want in their DI container. Every current and future `VectorStore` provider works automatically.

### Memory Record as a First-Class VectorStore Type

`MemoryFactRecord` is defined with `Microsoft.Extensions.VectorData` attributes directly. No mapping, no conversion, no adapter record type. The record is the VectorStore record.

```csharp
public sealed class MemoryFactRecord
{
    [VectorStoreKey]
    public string Id { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public string EntityId { get; set; }

    [VectorStoreData(IsFullTextSearchable = true)]
    public string Content { get; set; }

    [VectorStoreVector(Dimensions = 1536)]
    public ReadOnlyMemory<float> Embedding { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public string MemoryType { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public double Confidence { get; set; }

    [VectorStoreData(IsFilterable = true)]
    public DateTimeOffset CreatedAt { get; set; }

    [VectorStoreData]
    public string? ConversationId { get; set; }
}
```

### DI Registration Pattern

```csharp
// Dev / test — zero config, everything in-memory
services.AddMemori();

// Production with Azure AI Search
services.AddAzureAISearch(endpoint, credential);
services.AddMemori(options => { ... });

// Production with Qdrant
services.AddQdrantVectorStore("localhost");
services.AddMemori();
```

Memori resolves `VectorStoreCollection<string, MemoryFactRecord>` from DI. The user configures the provider. No Memori-specific adapter packages needed.

---

## Phase 2 Implementation Tiers

### Tier 1: Foundation (VectorStore Integration)

Replaces `IStorage` with the split abstraction. This is the prerequisite for everything else.

1. Define `MemoryFactRecord` as a VectorStore record type
2. Define `IConversationStorage` (relational/ordered operations only)
3. Ship `InMemoryConversationStorage` as the default implementation
4. Update `Memori` facade, `AugmentationService`, and `MemorySearchService` to use the new split
5. Update DI registration to resolve `VectorStoreCollection<string, MemoryFactRecord>` from the container
6. Update all tests to wire up the in-memory VectorStore provider
7. Update public documentation

### Tier 2: Production Scale

Builds on Tier 1. Enables multi-backend and distributed scenarios.

8. Distributed retrieval/ranking across multiple VectorStore backends
9. Composite storage for multi-backend querying with result merging

### Tier 3: Enterprise and Advanced Features

Builds on Tier 2. Enables organizational and long-running memory scenarios.

10. Workspace/team/shared memory scopes
11. Conflict resolution and versioning for memory records
12. Summarized thread memory
13. User memory management APIs/UI

---

## What This Is Not

- **Not replacing VectorStore** — Memori composes with it, not over it.
- **Not shipping database drivers** — providers like Azure AI Search, Qdrant, and Postgres pgvector ship their own `VectorStore` implementations. Memori does not wrap them.
- **Not an embedding library** — `IEmbeddingGenerator` from MEAI handles embeddings, same as Phase 1.
- **Not adding enterprise features in Tier 1** — distributed retrieval, conflict resolution, and multi-tenancy are Tier 2 and Tier 3 items.

---

## Related Documents

- `docs/IDEA.md` — Original Phase 1 and Phase 2 design proposal.
