using CodeRag.Core.Embedding;
using CodeRag.Core.Parsing;
using CodeRag.Core.Storage;
using Microsoft.Extensions.Logging;

namespace CodeRag.Core.Watching;

public sealed class IndexingPipeline
{
    private readonly IReadOnlyList<IFileExtractor> _extractors;
    private readonly IOnnxEmbeddingModel _embeddingModel;
    private readonly IChunkRepository _repository;
    private readonly string _projectRoot;
    private readonly IReadOnlyList<string> _indexedPatterns;
    private readonly IReadOnlyList<string> _ignorePatterns;
    private readonly ILogger<IndexingPipeline> _logger;

    public IndexingPipeline(
        IReadOnlyList<IFileExtractor> extractors,
        IOnnxEmbeddingModel embeddingModel,
        IChunkRepository repository,
        string projectRoot,
        IReadOnlyList<string> indexedPatterns,
        IReadOnlyList<string> ignorePatterns,
        ILogger<IndexingPipeline> logger)
    {
        _extractors = extractors;
        _embeddingModel = embeddingModel;
        _repository = repository;
        _projectRoot = projectRoot;
        _indexedPatterns = indexedPatterns;
        _ignorePatterns = ignorePatterns;
        _logger = logger;
    }

    private IFileExtractor? ResolveExtractor(string path)
    {
        var ext = Path.GetExtension(path);
        return _extractors.FirstOrDefault(e => e.CanHandle(ext));
    }

    public bool IsIndexable(string absolutePath)
    {
        if (IsIgnored(absolutePath)) return false;
        return MatchesAnyInclude(absolutePath);
    }

    private bool MatchesAnyInclude(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath).Replace('\\', '/');
        var ext = Path.GetExtension(absolutePath);

        foreach (var pattern in _indexedPatterns)
        {
            if (GlobMatcher.IsGlob(pattern))
            {
                if (GlobMatcher.Matches(relativePath, pattern)) return true;
            }
            else
            {
                if (ext.Equals(pattern, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private bool IsIgnored(string absolutePath)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath).Replace('\\', '/');

        foreach (var pattern in _ignorePatterns)
        {
            if (GlobMatcher.Matches(relativePath, pattern)) return true;
        }

        return false;
    }

    public async Task IndexFileAsync(string absolutePath, CancellationToken ct = default)
    {
        var relativePath = Path.GetRelativePath(_projectRoot, absolutePath)
            .Replace('\\', '/');
        var extractor = ResolveExtractor(absolutePath);
        if (extractor is null)
        {
            _logger.LogDebug("No extractor for {Path}, skipping", relativePath);
            return;
        }

        try
        {
            var sourceText = await File.ReadAllTextAsync(absolutePath, ct);
            var newChunks = extractor.Extract(sourceText, relativePath);

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
        var simpleExtensions = _indexedPatterns.Where(e => !GlobMatcher.IsGlob(e)).ToList();
        bool hasGlobIncludes = simpleExtensions.Count < _indexedPatterns.Count;

        IEnumerable<string> candidates;
        if (hasGlobIncludes)
        {
            // Glob patterns require full directory scan; IsIndexable applies all filters
            candidates = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        else
        {
            candidates = simpleExtensions
                .SelectMany(ext => Directory.GetFiles(directory, $"*{ext}", SearchOption.AllDirectories));
        }

        var files = candidates
            .Where(IsIndexable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Indexing {Count} files ({Patterns})...",
            files.Count, string.Join(", ", _indexedPatterns));

        // Embedding (ONNX) runs in parallel; DB writes serialize through the repo's internal semaphore
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        }, async (file, token) => await IndexFileAsync(file, token));

        _logger.LogInformation("Done. Indexed {Count} files.", files.Count);
    }
}
