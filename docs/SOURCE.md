# Original/Source Memori Notes

This document summarizes what is currently understood from the codebase about how Memori works, with emphasis on memory capture, augmentation, and recall.

## High-Level Model

Memori acts as middleware around an LLM client.

It does two main jobs:

1. Injects relevant recalled memory into the next prompt before the LLM call.
2. Captures the conversation turn after the LLM call and asynchronously processes it into memory.

The important distinction is:

- Raw conversation is stored.
- Long-term memory is selectively extracted from that conversation.

Memori does not appear to treat every message as a reusable memory fact.

## Request Flow

The runtime flow is:

1. A wrapped LLM client receives a request.
2. Memori injects recalled facts into the request context.
3. Memori injects prior conversation messages when configured to do so.
4. The request is forwarded to the LLM provider.
5. The response is returned.
6. Memori persists the conversation turn.
7. Memori triggers augmentation in the background.
8. Augmentation extracts facts, triples, attributes, and summaries.
9. Extracted memory is written to the database or sent to the cloud service.

```text
User/App
  |
  v
Memori LLM wrapper
  |-- inject recalled facts
  |-- inject prior conversation
  v
LLM provider
  |
  v
Response returned
  |
  v
Memori persists raw turn
  |
  v
Augmentation service
  |-- extract facts / triples / attributes / summary
  v
Storage / DB writes
  |
  v
Future recall injects only relevant memories
```

Relevant code paths:

- `memori/llm/invoke/invoke.py`
- `memori/llm/pipelines/recall_injection.py`
- `memori/llm/pipelines/conversation_injection.py`
- `memori/llm/pipelines/post_invoke.py`
- `memori/memory/_manager.py`
- `memori/memory/augmentation/_handler.py`
- `memori/memory/augmentation/augmentations/memori/_augmentation.py`

## What Gets Stored

### Raw Conversation

The conversation turn is persisted as conversation history.

In local/BYODB mode, this is written to the connected database.
In cloud mode, the payload is sent to the Memori API, and the local code may also persist a mirrored copy when a storage backend is available.

### Extracted Memories

The augmentation step can produce:

- facts
- semantic triples
- process attributes
- conversation summaries

These are the items that become long-term memory and are used later for recall.

## What Gets Recalled

Recall is separate from storage.

Memori searches previously stored facts using embedding similarity plus a lexical ranking path, then filters results by recall threshold.

Relevant defaults in current code:

- `recall_facts_limit = 5`
- `recall_embeddings_limit = 1000`
- `recall_relevance_threshold = 0.1`

This means:

- even if a fact is stored, it may not be injected later if it is not relevant enough
- recall is query-driven, not a blind replay of all memory

## Memory Qualification

The key question was whether Memori stores everything or decides what is worth remembering.

Current understanding:

- It stores the raw conversation.
- A separate augmentation step decides what qualifies as memory.
- That qualification is not a simple rule like "store every Nth message".
- It is driven by the augmentation service / extractor.

The extractor is expected to identify useful content such as:

- user identity facts
- preferences
- skills
- rules
- events
- triples / relationships

## Cloud vs BYODB

### Cloud Mode

In cloud mode, Memori sends augmentation payloads to a hosted Memori endpoint.

Observed behavior:

- the augmentation service is remote
- the service receives conversation content plus metadata
- the service returns structured memory output

### BYODB Mode

In BYODB mode, Memori uses the local database and local orchestration/runtime for persistence and recall.

However, based on the current code:

- the augmentation pipeline still calls `sdk/augmentation`
- the payload includes model/provider metadata
- the local code does not appear to run the actual memory extraction model itself

So BYODB changes where the data is stored and how orchestration runs, but it does not currently look like a fully local, model-pluggable augmentation implementation.

| Aspect | Cloud mode | BYODB mode |
| --- | --- | --- |
| Raw conversation storage | Sent to Memori service, with local mirroring when storage exists | Stored in your database |
| Augmentation/extraction | Hosted Memori augmentation endpoint | Local orchestration, but still calls `sdk/augmentation` in current code |
| Recall | Retrieved from Memori service | Retrieved from local DB / local core |
| Data ownership | Service-assisted | Your database |
| Current evidence of local extractor | No | No clear local extractor in this repo |
| Main benefit | Least setup | Self-hosted storage and recall |

## Current Best Interpretation

The best mental model is:

- Your LLM produces the chat response.
- Memori wraps that call as middleware.
- Memori stores the conversation.
- Memori sends the conversation to an augmentation service.
- The augmentation service decides which content becomes durable memory.
- Memori later recalls only the relevant stored memory.

## Open Questions

Things worth confirming later if needed:

1. Whether the hosted augmentation backend is the only implementation of the extractor.
2. Whether there is a supported configuration for swapping in a custom local extraction model.
3. Whether cloud mode and BYODB mode differ in extraction quality or only in storage/hosting location.
4. Whether the current open-source repo contains the full extraction logic or only the client/orchestrator side of it.

## Practical Summary

If a user says:

- "I live in Karachi"
- "My favorite color is green"
- plus several chatty filler messages

then Memori will likely:

- store the full conversation turn
- extract the identity/preference facts
- write those facts into long-term memory
- ignore most filler for recall purposes

That is the selective memory behavior the system is designed around.
