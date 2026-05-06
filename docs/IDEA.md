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

* Microsoft Vector Data Extensions adapters
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

## Summary

Treat memory as middleware around `IChatClient`, not as part of the model provider abstraction.

This preserves the clean design of `Microsoft.Extensions.AI` while enabling advanced persistent-memory behaviors in higher application layers.
