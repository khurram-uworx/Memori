namespace Memori.Mcp;

/// <summary>
/// Parses command-line arguments and returns a tuple containing the selected MemoriMode, an optional storage path, a
/// verbose flag, and a full-text flag.
/// </summary>
/// <remarks>Recognizes --mode|-m with values 'ephemeral' (default) or 'durable', --long as an alias for durable,
/// --path|-p for the storage path, --fulltext, and --verbose|-v. Defaults: Mode = Ephemeral, StoragePath = null,
/// Verbose = false, FullText = false. An unknown value supplied to --mode results in an ArgumentException.</remarks>
public static class CliParser
{
    public static (MemoriMode Mode, string? StoragePath, bool Verbose, bool FullText) Parse(string[] args)
    {
        var mode = MemoriMode.Ephemeral;
        string? storagePath = null;
        var verbose = false;
        var fullText = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--mode" or "-m" when i + 1 < args.Length:
                    mode = args[++i] switch
                    {
                        "ephemeral" => MemoriMode.Ephemeral,
                        "durable" => MemoriMode.Durable,
                        var v => throw new ArgumentException($"Unknown mode: {v}")
                    };
                    break;
                case "--long":
                    mode = MemoriMode.Durable;
                    break;
                case "--path" or "-p" when i + 1 < args.Length:
                    storagePath = args[++i];
                    break;
                case "--fulltext":
                    fullText = true;
                    break;
                case "--verbose" or "-v":
                    verbose = true;
                    break;
            }
        }

        return (mode, storagePath, verbose, fullText);
    }
}
