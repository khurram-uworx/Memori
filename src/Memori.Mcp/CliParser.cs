namespace Memori.Mcp;

static class CliParser
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
