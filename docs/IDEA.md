# Memory Middleware for `IChatClient`

## Overview

This document proposes a **Phase 1 primitive-first memory architecture** for GenAI applications built on **C#**, **Microsoft.Extensions.AI**, and the **`IChatClient` abstraction**.

The goal is to provide ChatGPT-style persistent memory by implementing memory as middleware/services around `IChatClient`, while keeping the library storage-provider-agnostic and developer-configurable.

---

## Phase 1 Design Philosophy

Phase 1 focuses on **primitives and infrastructure only**.

Included:

* Middleware components
* Core abstractions/interfaces
* Ranking/retrieval pipeline
* Built-in optional extraction prompt support
* In-memory storage implementation for development/testing

Deferred to Phase 2:

* Advanced summarization
* Enterprise/vector DB integrations
* Conflict resolution engines
* Large-scale distributed retrieval optimizations

---

## Core Principle

`IChatClient` remains the inference abstraction.

Memory is implemented as composable middleware around the chat pipeline.

---

## Proposed Middleware Pipeline

```text
User Request
   ↓
MemoryRetrievalChatClient
   ↓
Telemetry / Cache / Other Middleware
   ↓
Underlying IChatClient Provider
   ↓
MemoryCaptureChatClient
   ↓
Response Returned
```

---

## Core Abstractions

### `IStorage`

Storage abstraction owned by application/provider.

Responsibilities:

* Persist memory records
* Return memory candidates for a user/scope
* Store raw embedding vectors

Library ships only:

* In-memory implementation

Planned future ecosystem packages may provide:

* Microsoft Vector Data Extensions adapters (IStorage implementations using VectorStore backends for production vector databases)
* Cosmos/SQL providers
* Other persistence providers

---

### `IAugmentationClient`

Optional abstraction for extracting durable memory candidates from conversation turns.

Library supports:

* Built-in extraction prompt strategy
* Custom extractor implementation override

Configured during middleware registration.

---

### `IMemoryRanker`

Responsible for ranking memory candidates.

Default implementation includes:

* Cosine similarity using `System.Numerics.Tensors`
* Confidence/recency boosts

Custom rankers may override.

---

## Embedding Strategy

Embeddings are required in Phase 1.

Use `IEmbeddingGenerator` from Microsoft.Extensions.AI for:

* Query embeddings
* Memory embeddings

Embedding vectors are stored directly in memory records.

---

## Memory Record Schema

```json
{
  "userId": "...",
  "type": "preference|profile|goal|constraint",
  "content": "User prefers concise architectural explanations",
  "embedding": [...],
  "confidence": 0.93,
  "createdAt": "...",
  "lastUsedAt": "..."
}
```

---

## Retrieval Strategy

Phase 1 retrieval is optimized for small/mid-scale applications.

1. Fetch candidate memories from `IStorage`
2. Generate embedding for current request/context
3. Rank via cosine similarity (`System.Numerics.Tensors`)
4. Apply recency/confidence boosts
5. Inject top N memories into prompt

---

## Capture Strategy

Optional built-in extraction prompt:

```text
Extract durable user memories from this conversation.
Only include information likely useful in future conversations.
Return JSON array of structured memory records.
```

Or allow custom extractor implementation.

---

## DI / Registration Model

Designed for idiomatic .NET middleware composition.

Example target API:

```csharp
services.AddChatClient(...)
    .UseMemory(options =>
    {
        options.UseBuiltInExtractor = true;
        options.TopK = 5;
    });
```

---

## Phase 1 Scope

This library is Phase 1 of Memori for .NET, focused on **primitives and infrastructure** for durable memory in AI applications.

### Included in Phase 1

- Attribution, sessions, and conversation lifecycle management.
- Capture and recall primitives.
- Storage abstraction and in-memory reference implementation.
- Embedding abstraction with Microsoft.Extensions.AI adapter.
- Augmentation boundary and built-in extraction support.
- `IChatClient` middleware for recall and capture.
- Request-scoped control over recall and capture behavior.
- Streaming support with proper cancellation semantics.

### Explicitly Not Included in Phase 1

- **Advanced summarization**: The library does not automatically generate conversation or fact summaries. Summaries are stored primitives that can be extracted by augmentation clients.
- **Graph reasoning**: Semantic triples are stored primitives, not used for knowledge graph construction or reasoning.
- **Conflict resolution**: Handling contradictory or versioned memories is deferred.
- **Vector database integrations**: No first-party integrations for Pinecone, Weaviate, or other vector DBs. Implement `IStorage` for custom backends.
- **Provider-specific LLM wrappers**: All provider integration happens through `Microsoft.Extensions.AI.IChatClient`.
- **Enterprise features**: Multi-tenancy, access control, audit logging, and encryption are the responsibility of consuming applications and storage implementations.


## Non-Goals (Phase 1)

* Large-scale distributed vector search
* Enterprise memory federation
* Multi-tenant/global ranking strategies
* Advanced memory conflict resolution
* Automatic summarization/episodic memory layers

---

## Phase 2 / Future Backlog

* Microsoft Vector Data Extensions integration
* External vector database providers
* Distributed retrieval/ranking
* Workspace/team/shared memory scopes
* Conflict resolution/versioning
* Summarized thread memory
* User memory management APIs/UI

---

### Phase 2 Update: `IStorage` replaced by `IConversationStorage` + `VectorStoreCollection`

Phase 2 Tier 1 replaced the monolithic `IStorage` interface with a split:

- **`IConversationStorage`** — covers conversations, sessions, entities, processes, and messages (the relational half).
- **`VectorStoreCollection<string, MemoryFactRecord>`** — covers durable fact storage with vector and lexical search, using `Microsoft.Extensions.VectorData` (the fact half).

This lets users configure any `VectorStore` provider (Azure AI Search, Qdrant, etc.) for facts without writing Memori-specific adapters. The `IStorage` interface and `InMemoryStorage` class have been removed. See `docs/PHASE2.md` for the full architectural rationale.

---

## Summary

Treat memory as middleware around `IChatClient`, not as part of the model provider abstraction.

This preserves the clean design of `Microsoft.Extensions.AI` while enabling advanced persistent-memory behaviors in higher application layers.
