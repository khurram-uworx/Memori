namespace Memori.Mcp;

/// <summary>
/// Parses command-line arguments and returns a tuple containing the selected MemoriMode, an optional storage path, a
/// debug flag, and a full-text flag.
/// </summary>
/// <remarks>Recognizes --mode|-m with values 'sqlite' (default) or 'markdown', --markdown as a shorthand for
/// --mode markdown, --path|-p for the storage path, --fulltext, --debug, and --verbose|-v as a hidden alias for
/// --debug. Defaults: Mode = Sqlite, StoragePath = null, Debug = false, FullText = false.</remarks>
public static class CliParser
{
    public static (MemoriMode Mode, string? StoragePath, bool Debug, bool FullText) Parse(string[] args)
    {
        var mode = MemoriMode.Sqlite;
        string? storagePath = null;
        var debug = false;
        var fullText = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" or "-m" when i + 1 < args.Length:
                    mode = args[++i] switch
                    {
                        "sqlite" => MemoriMode.Sqlite,
                        "markdown" => MemoriMode.Markdown,
                        var v => throw new ArgumentException($"Unknown mode: {v}")
                    };
                    break;
                case "--markdown":
                    mode = MemoriMode.Markdown;
                    break;
                case "--path" or "-p" when i + 1 < args.Length:
                    storagePath = args[++i];
                    break;
                case "--fulltext":
                    fullText = true;
                    break;
                case "--debug" or "--verbose" or "-v":
                    debug = true;
                    break;
            }
        }

        return (mode, storagePath, debug, fullText);
    }
}
