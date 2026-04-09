using System.CommandLine;
using CodeRag.Core;
using CodeRag.Core.Embedding;
using CodeRag.Core.Parsing;
using CodeRag.Core.Storage;
using CodeRag.Core.Watching;
using Microsoft.Extensions.Logging;

namespace CodeRag.Cli.Commands;

public static class WatchCommand
{
    public static Command Build()
    {
        var pathArg = new Argument<string>("path", "Project root to watch");
        var cmd = new Command("watch", "Watch for .cs file changes and update index") { pathArg };

        cmd.SetHandler(async (string path) =>
        {
            var config = RagConfiguration.Load(path);
            using var logFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Debug));

            var bootstrapper = new ModelBootstrapper(config, logFactory.CreateLogger<ModelBootstrapper>());
            await bootstrapper.EnsureReadyAsync();

            using var db = new RagDbContext(config.DatabasePath, RagConfiguration.Vec0ExtensionPath);
            DbInitializer.Initialize(db.Connection);

            using var model = new MiniLmEmbeddingModel(RagConfiguration.ModelPath, RagConfiguration.VocabPath, config.UseGpu);
            var repo = new SqliteChunkRepository(db);

            // For PlainTextExtractor, derive handled extensions from patterns (globs → their extension)
            var nonCsPatterns = config.IndexedPatterns
                .Where(e => !e.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Contains('*') || p.Contains('/') ? Path.GetExtension(p) : p)
                .Where(e => !string.IsNullOrEmpty(e))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var extractors = new IFileExtractor[]
            {
                new CSharpSyntaxExtractor(),
                new PlainTextExtractor(nonCsPatterns)
            };

            var pipeline = new IndexingPipeline(
                extractors, model, repo,
                config.ProjectRoot,
                config.IndexedPatterns,
                config.IgnorePatterns,
                logFactory.CreateLogger<IndexingPipeline>(),
                config.LockFilePath);

            using var watcher = new FileWatcherService(
                pipeline, config.ProjectRoot, config.WatchDebounceMs,
                logFactory.CreateLogger<FileWatcherService>());

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            Console.WriteLine("Watching... Press Ctrl+C to stop.");

            await watcher.StartAsync(cts.Token);
            await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { });
            await watcher.StopAsync(CancellationToken.None);
        }, pathArg);

        return cmd;
    }
}
