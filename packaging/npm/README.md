# @uworx/memori

> Durable memory for AI coding agents — persistent, queryable semantic memory exposed via the Model Context Protocol (MCP).

**Memori** is a local-first durable memory layer for AI agents. It stores, searches, and manages facts across sessions with semantic recall, making your AI coding agents persistent and context-aware.

## Installation

From your terminal run:

```bash
npx -y @uworx/memori --help
```

It should print usage information and exit. You can then proceed to configure your MCP client (VS Code, Cursor, Claude Desktop, etc.) to use the `memori` MCP server as described below.

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

## MCP Tools

| Tool | What it gives you |
|---|---|
| `memori_remember` | Store a new fact about the current entity for future recall |
| `memori_search` | Search stored memories by semantic query, returning ranked results |
| `memori_list` | List all memories for the current entity with optional pagination |
| `memori_get` | Get a specific memory record by its unique identifier |
| `memori_update` | Update the content of an existing memory record |
| `memori_delete` | Soft-delete a memory record by its unique identifier |
| `memori_clear` | Clear all memories for the current entity by soft-deleting each record |

All tools return structured JSON.

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
- The SQLite database (`memori.db`) with all stored memories

You can safely add `.memori/` to your project's `.gitignore`.

## Learn More

Full documentation: [github.com/khurram-uworx/Memori](https://github.com/khurram-uworx/Memori)

## License

MIT
