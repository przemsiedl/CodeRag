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
            var config = RagConfiguration.Load(path);
            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

            var bootstrapper = new ModelBootstrapper(config, logFactory.CreateLogger<ModelBootstrapper>());
            await bootstrapper.EnsureReadyAsync();

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            DbInitializer.Initialize(db.Connection);

            using var model = new MiniLmEmbeddingModel(RagConfiguration.ModelPath, RagConfiguration.VocabPath, config.UseGpu);
            var repo = new SqliteChunkRepository(db);

            var nonCsExtensions = config.IndexedExtensions.Where(e => !e.Equals(".cs", StringComparison.OrdinalIgnoreCase));
            var extractors = new IFileExtractor[]
            {
                new CSharpSyntaxExtractor(),
                new PlainTextExtractor(nonCsExtensions)
            };

            var pipeline = new IndexingPipeline(
                extractors, model, repo,
                config.ProjectRoot,
                config.IndexedExtensions,
                config.IgnoredDirectories,
                config.IgnorePatterns,
                logFactory.CreateLogger<IndexingPipeline>());

            await pipeline.IndexDirectoryAsync(config.ProjectRoot, config.IndexingParallelism);

            var (chunks, dbSize, _) = await repo.GetStatsAsync();
            Console.WriteLine($"Index complete: {chunks} chunks, {dbSize / 1024.0:F1} KB");
        }, pathArg);

        return cmd;
    }
}
