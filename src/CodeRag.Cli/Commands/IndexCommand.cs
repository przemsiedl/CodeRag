using System.CommandLine;
using CodeRag.Core;
using CodeRag.Core.Embedding;
using CodeRag.Core.Parsing;
using CodeRag.Core.Storage;
using CodeRag.Core.Watching;
using Microsoft.Extensions.Logging;

namespace CodeRag.Cli.Commands;

public static class IndexCommand
{
    public static Command Build()
    {
        var pathArg = new Argument<string>("path", "Project root to index");
        var cmd = new Command("index", "Index all .cs files in a project") { pathArg };

        cmd.SetHandler(async (string path) =>
        {
            var config = new RagConfiguration { ProjectRoot = Path.GetFullPath(path) };
            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

            var bootstrapper = new ModelBootstrapper(config, logFactory.CreateLogger<ModelBootstrapper>());
            await bootstrapper.EnsureReadyAsync();

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            DbInitializer.Initialize(db.Connection);

            using var model = new MiniLmEmbeddingModel(RagConfiguration.ModelPath, RagConfiguration.VocabPath);
            var repo = new SqliteChunkRepository(db);
            var extractor = new CSharpSyntaxExtractor();

            var pipeline = new IndexingPipeline(
                extractor, model, repo,
                config.ProjectRoot,
                logFactory.CreateLogger<IndexingPipeline>());

            await pipeline.IndexDirectoryAsync(config.ProjectRoot, config.IndexingParallelism);

            var (chunks, dbSize, _) = await repo.GetStatsAsync();
            Console.WriteLine($"Index complete: {chunks} chunks, {dbSize / 1024.0:F1} KB");
        }, pathArg);

        return cmd;
    }
}
