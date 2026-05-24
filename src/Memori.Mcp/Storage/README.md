# SQLite Storage (Memori.Mcp)

SQLite storage providers bundled in `Memori.Mcp` — durable `IConversationStorage` and `VectorStoreCollection` backed by SQLite with FTS5 full-text search.

## Usage

```csharp
using Memori.Mcp.Storage;
using Microsoft.Extensions.DependencyInjection;

services.AddSqliteStorage(options =>
{
    options.DatabasePath = "./data/memori.db";
});
```

This registers both `IConversationStorage` and `VectorStoreCollection<string, MemoryFactRecord>` for use with `MemoriEngine`.

## Schema

- `conversations` — conversation metadata and summaries
- `messages` — conversation turns
- `entities`, `processes`, `sessions` — attribution and grouping
- `memory_facts` — durable fact records with FTS5 full-text index
- `config` — key-value configuration store

## Features

- FTS5 lexical search with ranked results
- Automatic database file creation
- Thread-safe concurrent access
- Scope and entity isolation
- No external dependencies beyond `Microsoft.Data.Sqlite`

## License

MIT
