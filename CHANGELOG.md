# Changelog

All notable changes to Memori are documented here.

## 0.2.0

### Added

- **Scope isolation** (`SetScope` / `ClearScope`): isolate memory by workspace, team, or tenant. Recall and entity deletion respect the current scope.
- **Versioning and conflict resolution**: `VersioningService` with three strategies — `LastWriteWins`, `Merge`, `Manual`. Each `MemoryFactRecord` carries a `Version` integer, `PreviousVersionId` for audit trail, and conflict detection.
- **Thread summarization**: `IThreadSummarizer` / `ChatClientThreadSummarizer` generates rolling conversation summaries via any `IChatClient`. Summaries are stored as `MemoryFactRecord` entries (`MemoryType = "summary"`).
- **Memory management APIs**: `IMemoryManagementService` with `ListMemoriesAsync`, `SearchMemoriesAsync`, `GetMemoryAsync`, `UpdateMemoryAsync`, `SoftDeleteMemoryAsync`, `RestoreMemoryAsync`, `HardDeleteMemoryAsync`, `GetMemoryCountAsync`. Exposed as convenience methods on the `Memori` facade.
- **Distributed ranker**: `IDistributedRanker` / `DefaultDistributedRanker` with three strategies — `MergeSortByScore`, `WeightedScore`, `RoundRobin` — for merging results from multiple backends.
- **Composite memory collection**: `CompositeMemoryCollection` wraps multiple `VectorStoreCollection<string, MemoryFactRecord>` backends and dispatches search/upsert/delete across all of them.
- **DI registration for Tier 3 services**: `VersioningService`, `IMemoryManagementService`, and `IThreadSummarizer` are auto-registered by `AddMemori()`. New overloads accept custom factories for each.
- **Augmentation pipeline integration**: `AugmentationService` now optionally accepts `VersioningService` (for conflict-aware fact upserts) and `IThreadSummarizer` (for automatic summary generation).
- **Capture policy options**: `ExcludedCaptureRoles`, `CaptureMessageFilter`, `CaptureMessageTransform`, `DropEmptyMessagesOnCapture` — all configurable through `MemoriOptions`.
- **Prompt injection placement**: `BeforeAllMessages`, `AfterSystemMessages`, `AfterSystemAndDeveloperMessages`, `Append` — configurable via `PromptInjectionPlacement`.
- **Prompt injection merge**: `AppendToLastMatchingRole`, `PrependToFirstMatchingRole` — merge context into existing instructions instead of inserting a new message.
- **Prompt injection role**: configurable via `PromptInjectionRole` (defaults to `system`, can use `developer`).
- **Provider metadata propagation**: response metadata (`ResponseId`, `ConversationId`, `ModelId`, `CreatedAt`, `FinishReason`, `Usage`, `AdditionalProperties`) is captured on conversation messages with `memori.provider.*` keys.
- **Streaming capture**: assistant responses from streaming are reconstructed and captured by assembling `ChatResponseUpdate` items.

### Changed

- Storage split into `IConversationStorage` (conversation lifecycle) and `VectorStoreCollection<string, MemoryFactRecord>` (fact storage). `IStorage` abstraction removed.
- `Memori` constructor now takes `IConversationStorage` and `VectorStoreCollection<string, MemoryFactRecord>` separately.
- `MemorySearchService` now depends on `VectorStoreCollection<string, MemoryFactRecord>` instead of `IStorage`.
- Options validation is more strict: `PromptFactBullet`, `PromptSummaryBullet`, `PromptContextTagName` must be non-empty. `RecallFactsLimit` must be positive.

### Deprecated

- (none)

### Removed

- `IStorage` abstraction (replaced by `IConversationStorage` + `VectorStoreCollection`).

### Fixed

- Injected memory context is no longer persisted as conversation history (the system message carrying recalled facts is stripped from capture).
- Streaming responses now correctly capture the reconstructed assistant message.
- Cancelled streaming responses do not leave partial captures.

### Security

- (none)
