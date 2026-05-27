# Storage (Memori.Mcp)

Storage providers bundled in `Memori.Mcp` — `IMemoryStore` implementations backed by SQLite or markdown files.

## SqliteMemoryStore

SQLite-backed `IMemoryStore` with n-gram vector search or FTS5 full-text search.

### Usage

```csharp
using Memori.Mcp.Storage;
using Microsoft.Extensions.DependencyInjection;

services.AddSqliteStorage(options =>
{
    options.DatabasePath = "./data/memori.db";
});
```

This registers `IMemoryStore` backed by SQLite for use with `MemoriMcpServer` and `MemoriTools`.

### Schema

- `conversations` — conversation metadata and summaries
- `messages` — conversation turns
- `entities`, `processes`, `sessions` — attribution and grouping
- `memory_facts` — durable fact records with FTS5 full-text index
- `config` — key-value configuration store

### Features

- n-gram vector search (default) or FTS5 lexical search (`--fulltext`)
- Automatic database file creation
- Thread-safe concurrent access
- Scope and entity isolation

## MarkdownMemoryStore

File-backed `IMemoryStore` — one markdown file per memory type, one line per memory.

### File Format

Each memory is stored as a single line in its category file (e.g., `PREFERENCES.md`):

```markdown
- content <!-- id=uuid entity=default type=preference tags=coding,dotnet ts=2026-05-27T12:00:00Z v=1 -->
```

### Features

- Human-readable, git-friendly files
- One file per memory type (uppercased: `PREFERENCE` → `PREFERENCES.md`)
- Per-file thread-safe writes
- Simple grep/substring search across all `.md` files

## License

MIT
