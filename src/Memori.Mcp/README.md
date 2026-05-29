# Memori.Mcp

MCP server adapter for [Memori](https://github.com/khurram-uworx/Memori) — exposes durable memory as Model Context Protocol tools over STDIO transport for AI coding agents.

## Installation (recommended)

Via NPM (no .NET runtime required):

```bash
npx -y @uworx/memori --help
```

The NPM package is a lightweight downloader. On first use it fetches a self-contained .NET binary for your platform.

## Usage

```bash
# Default — SQLite-backed, persists across sessions
npx -y @uworx/memori

# Markdown mode — human-readable, git-friendly files
npx -y @uworx/memori --markdown

# Markdown mode with custom path
npx -y @uworx/memori --mode markdown --path ./project-memories

# SQLite with full-text search (FTS5) instead of n-gram vectors
npx -y @uworx/memori --fulltext

# Debug mode (verbose logging)
npx -y @uworx/memori --debug

# All flags:
#   --mode, -m <sqlite|markdown>     Operation mode (default: sqlite)
#   --markdown                       Shorthand for --mode markdown
#   --path, -p <path>                Storage path (default per mode)
#   --fulltext                       Enable FTS5 (sqlite mode only)
#   --debug                          Enable debug logging
#   --version                        Show version
```

## Global dotnet tool (alternative)

```bash
dotnet tool install -g Memori.Mcp --prerelease
memori-mcp
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `ping` | Health check — returns status, mode, path, version |
| `remember` | Store a fact |
| `search` | Search stored memories |
| `list` | List memories with pagination |
| `get` | Get a memory by ID |
| `update` | Update memory content |
| `delete` | Soft-delete a memory |
| `clear` | Clear all memories for current entity |

## MCP Resources

| Resource | Description |
|----------|-------------|
| `memori://facts` | All stored facts as JSON |
| `memori://stats` | Memory count, storage mode, path |

## MCP Prompts

| Prompt | Description |
|--------|-------------|
| `remember-context` | Walk through storing current session context |
| `recall-session` | Recall context from a past session |
| `review-memories` | Review and prune stored memories |

## MCP Client Registration

**Cursor / VS Code (Cline) / Claude Desktop:**

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

## License

MIT
