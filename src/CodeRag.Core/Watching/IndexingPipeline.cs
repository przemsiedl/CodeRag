using CodeRag.Core.Embedding;
using CodeRag.Core.Parsing;
using CodeRag.Core.Storage;
using Microsoft.Extensions.Logging;

namespace CodeRag.Core.Watching;

public sealed class IndexingPipeline
{
    private readonly CSharpSyntaxExtractor _extractor;
    private readonly IOnnxEmbeddingModel _embeddingModel;
    private readonly IChunkRepository _repository;
    private readonly string _projectRoot;
    private readonly ILogger<IndexingPipeline> _logger;

    public IndexingPipeline(
        CSharpSyntaxExtractor extractor,
        IOnnxEmbeddingModel embeddingModel,
        IChunkRepository repository,
        string projectRoot,
        ILogger<IndexingPipeline> logger)
    {
        _extractor = extractor;
        _embeddingModel = embeddingModel;
        _repository = repository;
        _projectRoot = projectRoot;
        _logger = logger;
    }

    public async Task IndexFileAsync(string absolutePath, CancellationToken ct = default)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath)
            .Replace('\\', '/');
        try
        {
            var sourceText = await File.ReadAllTextAsync(absolutePath, ct);
            var newChunks = _extractor.Extract(sourceText, relativePath);

            // Read existing hashes (one lock acquisition)
            var existingHashes = await _repository.GetContentHashesByPathAsync(relativePath, ct);
            var newChunkIds = new HashSet<string>(newChunks.Select(c => c.Id));
            bool hasDeletedSymbols = existingHashes.Keys.Any(id => !newChunkIds.Contains(id));

            // Embed only changed/new chunks — CPU-bound work outside the DB lock
            var toSync = new List<CodeChunk>();
            foreach (var chunk in newChunks)
            {
                bool unchanged = existingHashes.TryGetValue(chunk.Id, out var existingHash)
                                 && existingHash == chunk.ContentHash;
                if (!unchanged)
                {
                    chunk.Embedding = _embeddingModel.Embed($"{chunk.Signature}\n{chunk.SourceText}");
                    chunk.IndexedAt = DateTimeOffset.UtcNow;
                    toSync.Add(chunk);
                }
            }

            if (hasDeletedSymbols)
            {
                // Delete all for this path, re-insert everything (including unchanged chunks need embeddings)
                // Re-embed the unchanged ones we skipped above
                var allChunks = newChunks.ToList();
                foreach (var chunk in allChunks.Where(c => c.Embedding.Length == 0))
                {
                    chunk.Embedding = _embeddingModel.Embed($"{chunk.Signature}\n{chunk.SourceText}");
                    chunk.IndexedAt = DateTimeOffset.UtcNow;
                }
                await _repository.SyncFileAsync(relativePath, allChunks, deleteStale: true, ct);
                _logger.LogInformation("Re-synced {Count} chunks for {Path} (symbols removed)", allChunks.Count, relativePath);
            }
            else if (toSync.Count > 0)
            {
                await _repository.SyncFileAsync(relativePath, toSync, deleteStale: false, ct);
                _logger.LogInformation("Updated {Upserted}/{Total} chunks in {Path}", toSync.Count, newChunks.Count, relativePath);
            }
            else
            {
                _logger.LogDebug("No changes in {Path}", relativePath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to index {Path}", absolutePath);
        }
    }

    public async Task DeleteFileAsync(string absolutePath, CancellationToken ct = default)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath)
            .Replace('\\', '/');
        await _repository.DeleteByPathAsync(relativePath, ct);
        _logger.LogInformation("Removed index entries for {Path}", relativePath);
    }

    public async Task IndexDirectoryAsync(string directory, int parallelism, CancellationToken ct = default)
    {
        var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".rag" + Path.DirectorySeparatorChar))
            .ToList();

        _logger.LogInformation("Indexing {Count} .cs files...", files.Count);

        // Embedding (ONNX) runs in parallel; DB writes serialize through the repo's internal semaphore
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        }, async (file, token) => await IndexFileAsync(file, token));

        _logger.LogInformation("Done indexing {Count} files.", files.Count);
    }
}
