using Memori.Mcp.Models;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace Memori.Mcp.Storage;

public sealed class MarkdownMemoryStore : IMemoryStore
{
    readonly string basePath;
    readonly ConcurrentDictionary<string, SemaphoreSlim> fileLocks = new(StringComparer.OrdinalIgnoreCase);
    static readonly Regex LineRegex = new(@"^\- (.+?) <!-- (.+) -->$", RegexOptions.Compiled);
    static readonly Regex MetaRegex = new(@"(\w+)=([^\s]+)", RegexOptions.Compiled);

    public MarkdownMemoryStore(string? basePath)
    {
        this.basePath = basePath is not null
            ? Path.GetFullPath(basePath)
            : Path.Combine(Environment.CurrentDirectory, ".memori", "memories");

        Directory.CreateDirectory(this.basePath);
    }

    static string GetFileName(string? memoryType)
    {
        var name = string.IsNullOrWhiteSpace(memoryType) ? "MEMORIES" : memoryType.ToUpperInvariant();
        return name + ".md";
    }

    string GetFilePath(string? memoryType) => Path.Combine(basePath, GetFileName(memoryType));

    SemaphoreSlim GetLock(string filePath) =>
        fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

    static McpFactRecord? ParseLine(string line, string? memoryType)
    {
        var match = LineRegex.Match(line);
        if (!match.Success)
            return null;

        var record = new McpFactRecord
        {
            Content = match.Groups[1].Value.Trim()
        };

        var meta = match.Groups[2].Value;
        foreach (Match m in MetaRegex.Matches(meta))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Value;

            switch (key)
            {
                case "id": record.Id = value; break;
                case "entity": record.EntityId = value; break;
                case "type": record.MemoryType = value; break;
                case "ts": record.CreatedAt = DateTimeOffset.Parse(value, null); break;
                case "v": record.Version = int.Parse(value); break;
                case "tags": record.Tags = value.Replace(',', ' ').Trim(); break;
            }
        }

        record.MemoryType ??= memoryType;
        return record;
    }

    static string FormatLine(McpFactRecord record)
    {
        var meta = $"id={record.Id} entity={record.EntityId} type={record.MemoryType} ts={record.CreatedAt:O} v={record.Version}";

        if (!string.IsNullOrWhiteSpace(record.Tags))
            meta += $" tags={record.Tags.Replace(' ', ',')}";

        return $"- {record.Content} <!-- {meta} -->";
    }

    async Task<List<(string Line, McpFactRecord Record)>> ReadAllRecordsAsync(string? memoryType, CancellationToken ct = default)
    {
        var results = new List<(string Line, McpFactRecord Record)>();

        if (!string.IsNullOrWhiteSpace(memoryType))
        {
            var filePath = GetFilePath(memoryType);
            if (!File.Exists(filePath))
                return results;

            var lines = await File.ReadAllLinesAsync(filePath, ct).ConfigureAwait(false);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("- "))
                    continue;

                var record = ParseLine(trimmed, memoryType);
                if (record is not null)
                    results.Add((trimmed, record));
            }

            return results;
        }

        foreach (var mdFile in Directory.EnumerateFiles(basePath, "*.md"))
        {
            var fileName = Path.GetFileNameWithoutExtension(mdFile);
            var lines = await File.ReadAllLinesAsync(mdFile, ct).ConfigureAwait(false);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("- "))
                    continue;

                var record = ParseLine(trimmed, fileName);
                if (record is not null)
                    results.Add((trimmed, record));
            }
        }

        return results;
    }

    async Task AppendLineAsync(string filePath, string line, CancellationToken ct = default)
    {
        var fileLock = GetLock(filePath);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var header = !File.Exists(filePath) || new FileInfo(filePath).Length == 0
                ? "# Memories" + Environment.NewLine + Environment.NewLine
                : "";

            await File.AppendAllTextAsync(filePath, header + line + Environment.NewLine, ct).ConfigureAwait(false);
        }
        finally
        {
            fileLock.Release();
        }
    }

    async Task RewriteFileAsync(string filePath, List<string> lines, CancellationToken ct = default)
    {
        var fileLock = GetLock(filePath);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (lines.Count == 0)
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
                return;
            }

            var content = "# Memories" + Environment.NewLine + Environment.NewLine;
            content += string.Join(Environment.NewLine, lines) + Environment.NewLine;
            await File.WriteAllTextAsync(filePath, content, ct).ConfigureAwait(false);
        }
        finally
        {
            fileLock.Release();
        }
    }

    public async Task InsertAsync(McpFactRecord record)
    {
        var filePath = GetFilePath(record.MemoryType);
        var line = FormatLine(record);
        await AppendLineAsync(filePath, line).ConfigureAwait(false);
    }

    public async Task<McpFactRecord?> GetAsync(string id)
    {
        var all = await ReadAllRecordsAsync(null).ConfigureAwait(false);
        return all.FirstOrDefault(r => r.Record.Id == id).Record;
    }

    public async IAsyncEnumerable<McpFactRecord> ListAsync(string entityId, int skip, int limit, bool includeDeleted)
    {
        var all = await ReadAllRecordsAsync(null).ConfigureAwait(false);
        var filtered = all
            .Select(r => r.Record)
            .Where(r => r.EntityId == entityId)
            .OrderByDescending(r => r.CreatedAt)
            .Skip(skip)
            .Take(limit);

        foreach (var record in filtered)
            yield return record;
    }

    public async IAsyncEnumerable<(McpFactRecord Record, double Score)> SearchAsync(string query, string entityId, int limit)
    {
        var all = await ReadAllRecordsAsync(null).ConfigureAwait(false);
        var q = query.ToUpperInvariant();
        var filtered = all
            .Select(r => r.Record)
            .Where(r => r.EntityId == entityId && r.Content.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit);

        foreach (var record in filtered)
            yield return (record, 1.0);
    }

    public async Task ReplaceAsync(McpFactRecord record)
    {
        var all = await ReadAllRecordsAsync(record.MemoryType).ConfigureAwait(false);
        var filePath = GetFilePath(record.MemoryType);
        var index = all.FindIndex(r => r.Record.Id == record.Id);

        if (index < 0)
            return;

        all[index] = (FormatLine(record), record);
        await RewriteFileAsync(filePath, all.Select(r => r.Line).ToList()).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, string entityId)
    {
        var all = await ReadAllRecordsAsync(null).ConfigureAwait(false);
        var entry = all.FirstOrDefault(r => r.Record.Id == id && r.Record.EntityId == entityId);

        if (entry.Record is null)
            return false;

        var filePath = GetFilePath(entry.Record.MemoryType);
        var fileLines = all
            .Where(r => r.Record.Id != id || r.Record.EntityId != entityId)
            .ToList();

        var grouped = fileLines.GroupBy(r => GetFilePath(r.Record.MemoryType));
        foreach (var group in grouped)
            await RewriteFileAsync(group.Key, group.Select(r => r.Line).ToList()).ConfigureAwait(false);

        return true;
    }

    public async Task<int> ClearAsync(string entityId)
    {
        var all = await ReadAllRecordsAsync(null).ConfigureAwait(false);
        var remaining = all
            .Where(r => r.Record.EntityId != entityId)
            .ToList();

        var removed = all.Count - remaining.Count;
        if (removed == 0)
            return 0;

        var grouped = remaining.GroupBy(r => GetFilePath(r.Record.MemoryType));
        foreach (var group in grouped)
            await RewriteFileAsync(group.Key, group.Select(r => r.Line).ToList()).ConfigureAwait(false);

        return removed;
    }
}
