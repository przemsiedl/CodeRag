using System.CommandLine;
using System.CommandLine.Invocation;
using CodeRag.Core;
using CodeRag.Core.Embedding;
using CodeRag.Core.Parsing;
using CodeRag.Core.Query;
using CodeRag.Core.Storage;
using Microsoft.Extensions.Logging;

namespace CodeRag.Cli.Commands;

public static class QueryCommand
{
    public static Command Build()
    {
        var pathArg = new Argument<string>("path", "Project root with .rag index");

        var queryOpt = new Option<string?>(
            new[] { "-q", "--query" },
            "Natural language query");

        var topKOpt = new Option<int>(
            ["--results", "-r"],
            () => 5,
            "How many matching symbols to return");

        var kindOpt = new Option<string?>(
            ["--symbol-type", "-s"],
            "Filter by code symbol kind, comma-separated.\n" +
            "Allowed values: Class, Record, Interface, Enum, Method, Constructor, Property, Field\n" +
            "Example: --symbol-type Method,Constructor");

        var chunkKindOpt = new Option<string?>(
            ["--chunk-type", "-ct"],
            "Filter by chunk kind (what the chunk represents in the index), comma-separated.\n" +
            "Allowed values: Symbol, FileDocument, SymbolUsage\n" +
            "Example: --chunk-type Symbol,FileDocument");

        var classOpt = new Option<string?>(
            ["--in-class", "-ic"],
            "Return only symbols that belong to the given class (partial name match).\n" +
            "Example: --in-class OrderService");

        var fileOpt = new Option<string?>(
            ["--in-file", "-if"],
            "Return only symbols from files matching the given name/path (partial match).\n" +
            "Example: --in-file RagQueryService");

        var namespaceOpt = new Option<string?>(
            ["--in-namespace", "-in"],
            "Return only symbols from namespaces matching the given name (partial match).\n" +
            "Example: --in-namespace CodeRag.Core.Query");

        var fileNameOpt = new Option<string?>(
            ["--file-name", "-fn"],
            "Find files by name (partial match). Returns File-level chunks only.\n" +
            "Example: --file-name .sln  or  --file-name MyProject.csproj");

        var fullOpt = new Option<bool>(
            ["--full", "-f"],
            "Include full source text in output (default: signatures only).");

        var contextOpt = new Option<int>(
            ["--context", "-c"],
            () => 0,
            "Show N lines of context around the symbol (reads from source file). Overrides --full.");

        var grepOpt = new Option<string?>(
            ["--grep", "-g"],
            "Filter displayed source lines to those matching the given pattern (regex). Only affects -f/--full and -c/--context output.");

        var linesOpt = new Option<string?>(
            ["--lines", "-lr"],
            "Show only lines in the given range (e.g. 5-10). Line numbers are absolute (same as in the file). Only affects -f/--full and -c/--context output.");

        var cmd = new Command("query", "Search the indexed codebase for symbols matching a query")
            { pathArg, queryOpt, topKOpt, kindOpt, chunkKindOpt, classOpt, fileOpt, namespaceOpt, fileNameOpt, fullOpt, contextOpt, grepOpt, linesOpt };

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var path       = ctx.ParseResult.GetValueForArgument(pathArg);
            var query      = ctx.ParseResult.GetValueForOption(queryOpt);
            var results    = ctx.ParseResult.GetValueForOption(topKOpt);
            var symbolType = ctx.ParseResult.GetValueForOption(kindOpt);
            var chunkType  = ctx.ParseResult.GetValueForOption(chunkKindOpt);
            var inClass    = ctx.ParseResult.GetValueForOption(classOpt);
            var inFile     = ctx.ParseResult.GetValueForOption(fileOpt);
            var inNamespace = ctx.ParseResult.GetValueForOption(namespaceOpt);
            var fileName   = ctx.ParseResult.GetValueForOption(fileNameOpt);
            var full       = ctx.ParseResult.GetValueForOption(fullOpt);
            var contextN   = ctx.ParseResult.GetValueForOption(contextOpt);
            var grep       = ctx.ParseResult.GetValueForOption(grepOpt);
            var linesRange = ctx.ParseResult.GetValueForOption(linesOpt);
            var ct         = ctx.GetCancellationToken();

            System.Text.RegularExpressions.Regex? grepRegex = null;
            if (!string.IsNullOrEmpty(grep))
            {
                try { grepRegex = new System.Text.RegularExpressions.Regex(grep, System.Text.RegularExpressions.RegexOptions.IgnoreCase); }
                catch (ArgumentException ex)
                {
                    Console.Error.WriteLine($"Error: invalid --grep pattern: {ex.Message}");
                    ctx.ExitCode = 1;
                    return;
                }
            }

            int linesFrom = 0, linesTo = int.MaxValue;
            if (!string.IsNullOrEmpty(linesRange))
            {
                var parts = linesRange.Split('-');
                if (parts.Length != 2 || !int.TryParse(parts[0], out linesFrom) || !int.TryParse(parts[1], out linesTo) || linesFrom > linesTo)
                {
                    Console.Error.WriteLine("Error: --lines must be in format FROM-TO (e.g. 5-10).");
                    ctx.ExitCode = 1;
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(query) && inClass == null && inFile == null && inNamespace == null && fileName == null)
            {
                Console.Error.WriteLine("Error: at least one of -q/--query, --in-class, --in-file, --in-namespace, --file-name must be provided.");
                ctx.ExitCode = 1;
                return;
            }

            var onlySignatures = !full;

            var config = new RagConfiguration { ProjectRoot = Path.GetFullPath(path) };

            await CodeRag.Core.Storage.IndexLock.WaitForFreeAsync(config.LockFilePath, ct);

            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

            IReadOnlySet<SymbolKind>? symbolKinds = null;
            if (!string.IsNullOrWhiteSpace(symbolType))
            {
                symbolKinds = symbolType
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(k => Enum.Parse<SymbolKind>(k, ignoreCase: true))
                    .ToHashSet();
            }

            IReadOnlySet<ChunkKind>? chunkKinds = null;
            if (!string.IsNullOrWhiteSpace(chunkType))
            {
                chunkKinds = chunkType
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(k => Enum.Parse<ChunkKind>(k, ignoreCase: true))
                    .ToHashSet();
            }

            var options = new QueryOptions
            {
                TopK = results,
                ChunkKinds = chunkKinds,
                SymbolKinds = symbolKinds,
                ParentClass = inClass,
                InFile = inFile,
                InNamespace = inNamespace,
                FileName = fileName,
                OnlySignatures = onlySignatures,
                ContextLines = contextN
            };

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            using var model = new MiniLmEmbeddingModel(RagConfiguration.ModelPath, RagConfiguration.VocabPath, config.UseGpu);
            var repo = new SqliteChunkRepository(db);
            var svc = new RagQueryService(model, repo);

            var queryResults = await svc.QueryAsync(query, options, ct);

            // Group by file, sort within each group by StartLine
            var grouped = queryResults
                .GroupBy(r => r.RelativePath)
                .Select(g => (
                    File: g.Key,
                    Results: g.OrderBy(r => r.StartLine).ToList()
                ))
                .ToList();

            // Resolve project root for --context file reads
            var projectRoot = Path.GetFullPath(path);

            int i = 1;
            foreach (var g in grouped)
            {
                foreach (var r in g.Results)
                {
                    var kindLabel = r.Kind == Core.Parsing.ChunkKind.Symbol && r.SymbolKind.HasValue
                        ? r.SymbolKind.Value.ToString()
                        : r.Kind.ToString();
                    Console.WriteLine($"[{i++}] {kindLabel,-12} {r.SymbolName}");
                    switch (r.Kind)
                    {
                        case Core.Parsing.ChunkKind.Symbol:
                            Console.WriteLine($"     namespace : {r.Namespace ?? "-"}");
                            Console.WriteLine($"     class     : {r.ParentClass ?? "-"}");
                            Console.WriteLine($"     file      : {r.RelativePath}:{r.StartLine}-{r.EndLine}");
                            Console.WriteLine($"     signature : {r.Signature}");
                            break;
                        case Core.Parsing.ChunkKind.FileDocument:
                            Console.WriteLine($"     file      : {r.RelativePath}");
                            break;
                        case Core.Parsing.ChunkKind.SymbolUsage:
                            Console.WriteLine($"     found in  : {r.RelativePath}");
                            Console.WriteLine($"     signature : {r.Signature}");
                            break;
                    }

                    if (contextN > 0)
                    {
                        // --context N: read source file and show N lines around the symbol
                        var filePath = Path.Combine(projectRoot, r.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(filePath))
                        {
                            var fileLines = File.ReadAllLines(filePath);
                            int from = Math.Max(0, r.StartLine - 1 - contextN);
                            int to = Math.Min(fileLines.Length - 1, r.EndLine - 1 + contextN);
                            Console.WriteLine($"     context   :");
                            for (int li = from; li <= to; li++)
                            {
                                int lineNo = li + 1;
                                if (lineNo < linesFrom || lineNo > linesTo) continue;
                                if (grepRegex != null && !grepRegex.IsMatch(fileLines[li])) continue;
                                var marker = (li >= r.StartLine - 1 && li <= r.EndLine - 1) ? ">" : " ";
                                Console.WriteLine($"  {marker}{lineNo,5} | {fileLines[li]}");
                            }
                        }
                    }
                    else if (!onlySignatures)
                    {
                        Console.WriteLine($"     source    :");
                        var lines = r.SourceText.Split('\n');
                        if (r.Kind == Core.Parsing.ChunkKind.SymbolUsage)
                        {
                            // SymbolUsage chunks: source is pre-formatted "file\n  line: content" — print as-is
                            foreach (var line in lines)
                            {
                                if (grepRegex != null && !grepRegex.IsMatch(line)) continue;
                                Console.WriteLine($"     {line}");
                            }
                        }
                        else
                        {
                            for (int li = 0; li < lines.Length; li++)
                            {
                                if (li < r.ContextHeaderLines)
                                {
                                    if (grepRegex == null || grepRegex.IsMatch(lines[li]))
                                        Console.WriteLine($"       {lines[li]}");
                                }
                                else
                                {
                                    var lineNo = r.StartLine + (li - r.ContextHeaderLines);
                                    if (lineNo < linesFrom || lineNo > linesTo) continue;
                                    if (grepRegex != null && !grepRegex.IsMatch(lines[li])) continue;
                                    Console.WriteLine($"  {lineNo,5} | {lines[li]}");
                                }
                            }
                        }
                    }
                    Console.WriteLine();
                }
            }
        });

        return cmd;
    }
}
