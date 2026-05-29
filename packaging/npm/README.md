# @uworx/memori

> Durable memory for AI coding agents — persistent, queryable semantic memory exposed via the Model Context Protocol (MCP).

**Memori** is a local-first durable memory layer for AI agents. It stores, searches, and manages facts across sessions with semantic recall, making your AI coding agents persistent and context-aware.

## Quick Start

Run this to see available options:

```bash
npx -y @uworx/memori --help
```

Then configure your MCP client as shown below.

## Configuration

Configure the MCP server in your client (VS Code, Cursor, Claude Desktop, etc.) by adding the following to your MCP settings:

```json
{
  "mcpServers": {
    "memori": {
      "command": "npx",
      "args": ["-y", "@uworx/memori"]
    }
  }
}
```

To use markdown mode (human-readable, git-friendly files) instead of the default SQLite mode:

```json
{
  "mcpServers": {
    "memori": {
      "command": "npx",
      "args": ["-y", "@uworx/memori", "--markdown"]
    }
  }
}
```

To store data in a custom location, pass `--path`:

```json
{
  "mcpServers": {
    "memori": {
      "command": "npx",
      "args": ["-y", "@uworx/memori", "--path", "/path/to/memories"]
    }
  }
}
```

## How It Works

The `@uworx/memori` package is a lightweight CLI wrapper. On `npm install`, it downloads the platform-specific native binary from GitHub Releases. The binary is a self-contained .NET single-file publish with no runtime dependencies.

To start the MCP server, run without arguments:

```bash
npx -y @uworx/memori
```

To enable debug logging:

```bash
npx -y @uworx/memori --debug
```

## MCP Tools

Tool names are namespaced by the MCP server key (`memori`), so you invoke them as `memori_ping`, `memori_remember`, etc.

| Tool | What it gives you |
|---|---|
| `ping` | Health check — returns status, mode, path, version |
| `remember` | Store a new fact about the current entity for future recall |
| `search` | Search stored memories by semantic query, returning ranked results |
| `list` | List all memories for the current entity with optional pagination |
| `get` | Get a specific memory record by its unique identifier |
| `update` | Update the content of an existing memory record |
| `delete` | Soft-delete a memory record by its unique identifier |
| `clear` | Clear all memories for the current entity by soft-deleting each record |

All tools return structured JSON.

## MCP Resources

| Resource | What it gives you |
|---|---|
| `memori://facts` | All stored facts as JSON |
| `memori://stats` | Memory count, storage mode, path, version |

## MCP Prompts

| Prompt | What it does |
|---|---|
| `remember-context` | Walk through storing the current session context as durable memories |
| `recall-session` | Prompt to recall and restore context from a past session |
| `review-memories` | Review and optionally prune stored memories |

## Requirements

- Node.js 18+ (for `npx` / `npm install`)
- No .NET runtime required — the binary is self-contained

## Supported Platforms

- **Windows**: x64
- **Linux**: x64
- *macOS: coming soon*

## `.memori` Folder

Memori creates a `.memori/` folder in the working directory that stores:

- Diagnostic logs (`Log.*.txt`) — include these when reporting issues
- The SQLite database (`memori.db`) with all stored memories (sqlite mode)

In **markdown mode**, memories are stored as markdown files directly in the configured path (e.g., `PREFERENCES.md`, `FACTS.md`) — one line per memory, one file per type. These files are human-readable and can be checked into version control.

## Learn More

Full documentation: [github.com/khurram-uworx/Memori](https://github.com/khurram-uworx/Memori)

## License

MIT
