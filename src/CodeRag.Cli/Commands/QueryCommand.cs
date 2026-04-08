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
            "--results",
            () => 5,
            "How many matching symbols to return");

        var kindOpt = new Option<string?>(
            "--symbol-type",
            "Return only symbols of given type(s), comma-separated.\n" +
            "Allowed values: Class, Record, Interface, Enum, Method, Constructor, Property, Field, File\n" +
            "Example: --symbol-type Method,Constructor");

        var classOpt = new Option<string?>(
            "--in-class",
            "Return only symbols that belong to the given class (partial name match).\n" +
            "Example: --in-class OrderService");

        var fileOpt = new Option<string?>(
            "--in-file",
            "Return only symbols from files matching the given name/path (partial match).\n" +
            "Example: --in-file RagQueryService");

        var fileNameOpt = new Option<string?>(
            "--file-name",
            "Find files by name (partial match). Returns File-level chunks only.\n" +
            "Example: --file-name .sln  or  --file-name MyProject.csproj");

        var fullOpt = new Option<bool>(
            "--full",
            "Include full source text in output (default: signatures only).");

        var cmd = new Command("query", "Search the indexed codebase for symbols matching a query")
            { pathArg, queryOpt, topKOpt, kindOpt, classOpt, fileOpt, fileNameOpt, fullOpt };

        cmd.SetHandler(async (InvocationContext ctx) =>
        {
            var path       = ctx.ParseResult.GetValueForArgument(pathArg);
            var query      = ctx.ParseResult.GetValueForOption(queryOpt);
            var results    = ctx.ParseResult.GetValueForOption(topKOpt);
            var symbolType = ctx.ParseResult.GetValueForOption(kindOpt);
            var inClass    = ctx.ParseResult.GetValueForOption(classOpt);
            var inFile     = ctx.ParseResult.GetValueForOption(fileOpt);
            var fileName   = ctx.ParseResult.GetValueForOption(fileNameOpt);
            var full       = ctx.ParseResult.GetValueForOption(fullOpt);
            var ct         = ctx.GetCancellationToken();

            if (string.IsNullOrWhiteSpace(query) && inClass == null && inFile == null && fileName == null)
            {
                Console.Error.WriteLine("Error: at least one of -q/--query, --in-class, --in-file, --file-name must be provided.");
                ctx.ExitCode = 1;
                return;
            }

            var onlySignatures = !full;

            var config = new RagConfiguration { ProjectRoot = Path.GetFullPath(path) };
            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

            IReadOnlySet<SymbolKind>? kinds = null;
            if (!string.IsNullOrWhiteSpace(symbolType))
            {
                kinds = symbolType
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(k => Enum.Parse<SymbolKind>(k, ignoreCase: true))
                    .ToHashSet();
            }

            var options = new QueryOptions
            {
                TopK = results,
                Kinds = kinds,
                ParentClass = inClass,
                InFile = inFile,
                FileName = fileName,
                OnlySignatures = onlySignatures
            };

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            using var model = new MiniLmEmbeddingModel(RagConfiguration.ModelPath, RagConfiguration.VocabPath);
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

            int i = 1;
            foreach (var g in grouped)
            {
                foreach (var r in g.Results)
                {
                    Console.WriteLine($"[{i++}] {r.Kind,-12} {r.SymbolName}");
                    Console.WriteLine($"     namespace : {r.Namespace}");
                    Console.WriteLine($"     class     : {r.ParentClass ?? "-"}");
                    Console.WriteLine($"     file      : {r.RelativePath}:{r.StartLine}-{r.EndLine}");
                    Console.WriteLine($"     signature : {r.Signature}");
                    if (!onlySignatures)
                    {
                        Console.WriteLine($"     source    :");
                        var lines = r.SourceText.Split('\n');
                        for (int li = 0; li < lines.Length; li++)
                        {
                            if (li < r.ContextHeaderLines)
                            {
                                Console.WriteLine($"       {lines[li]}");
                            }
                            else
                            {
                                var lineNo = r.StartLine + (li - r.ContextHeaderLines);
                                Console.WriteLine($"  {lineNo,5} | {lines[li]}");
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
