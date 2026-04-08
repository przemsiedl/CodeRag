using System.CommandLine;
using CodeRag.Core;
using CodeRag.Core.Storage;
using Microsoft.Extensions.Logging;

namespace CodeRag.Cli.Commands;

public static class StatusCommand
{
    public static Command Build()
    {
        var pathArg = new Argument<string>("path", "Project root with .rag index");
        var cmd = new Command("status", "Show index statistics") { pathArg };

        cmd.SetHandler(async (string path) =>
        {
            var config = new RagConfiguration { ProjectRoot = Path.GetFullPath(path) };
            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

            if (!File.Exists(config.DatabasePath))
            {
                Console.WriteLine("No index found. Run 'rag index <path>' first.");
                return;
            }

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            var repo = new SqliteChunkRepository(db);
            var (chunks, dbSize, lastIndexed) = await repo.GetStatsAsync();

            Console.WriteLine($"Chunks:       {chunks}");
            Console.WriteLine($"DB size:      {dbSize / 1024.0:F1} KB");
            Console.WriteLine($"Last indexed: {lastIndexed?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "never"}");
            Console.WriteLine($"DB path:      {config.DatabasePath}");
            Console.WriteLine($"Models:       {RagConfiguration.ModelsDirectory}");
        }, pathArg);

        return cmd;
    }
}
