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
# Ephemeral (in-memory)
npx -y @uworx/memori

# Durable (SQLite)
npx -y @uworx/memori --long

# Custom path
npx -y @uworx/memori --long --path ./my-memories --verbose
```

## Global dotnet tool (alternative)

```bash
dotnet tool install -g Memori.Mcp --prerelease
memori-mcp
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `memori_remember` | Store a fact |
| `memori_search` | Search stored memories |
| `memori_list` | List memories with pagination |
| `memori_get` | Get a memory by ID |
| `memori_update` | Update memory content |
| `memori_delete` | Soft-delete a memory |
| `memori_clear` | Clear all memories for current entity |

## Modes

- **Ephemeral** (default): in-memory, lost on restart
- **Durable**: SQLite with FTS5 lexical search

## MCP Client Registration

**Cursor / VS Code (Cline) / Claude Desktop:**

```json
{
  "mcpServers": {
    "memori": {
      "command": "npx",
      "args": ["-y", "@uworx/memori", "--long"]
    }
  }
}
```

## License

MIT
