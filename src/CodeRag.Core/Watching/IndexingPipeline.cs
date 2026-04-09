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
    private readonly IReadOnlyList<string> _indexedExtensions;
    private readonly IReadOnlyList<string> _ignoredDirectories;
    private readonly IReadOnlyList<string> _ignorePatterns;
    private readonly ILogger<IndexingPipeline> _logger;

    public IndexingPipeline(
        IReadOnlyList<IFileExtractor> extractors,
        IOnnxEmbeddingModel embeddingModel,
        IChunkRepository repository,
        string projectRoot,
        IReadOnlyList<string> indexedExtensions,
        IReadOnlyList<string> ignoredDirectories,
        IReadOnlyList<string> ignorePatterns,
        ILogger<IndexingPipeline> logger)
    {
        _extractors = extractors;
        _embeddingModel = embeddingModel;
        _repository = repository;
        _projectRoot = projectRoot;
        _indexedExtensions = indexedExtensions;
        _ignoredDirectories = ignoredDirectories;
        _ignorePatterns = ignorePatterns;
        _logger = logger;
    }

    private IFileExtractor? ResolveExtractor(string path)
    {
        var ext = Path.GetExtension(path);
        return _extractors.FirstOrDefault(e => e.CanHandle(ext));
    }

    public bool IsIndexable(string path)
    {
        var ext = Path.GetExtension(path);
        if (!_indexedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (IsIgnored(path))
            return false;

        return true;
    }

    private bool IsIgnored(string path)
    {
        // Check directory segments
        var segments = path.Replace('\\', '/').Split('/');
        foreach (var segment in segments[..^1]) // skip filename
        {
            if (_ignoredDirectories.Any(d => d.Equals(segment, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        // Check filename patterns
        var fileName = Path.GetFileName(path);
        foreach (var pattern in _ignorePatterns)
        {
            if (MatchesGlob(fileName, pattern))
                return true;
        }

        return false;
    }

    private static bool MatchesGlob(string fileName, string pattern)
    {
        // Simple glob: only '*' wildcard supported, matched case-insensitively
        var parts = pattern.Split('*');
        if (parts.Length == 1)
            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase);

        int pos = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;

            var idx = fileName.IndexOf(part, pos, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            // First segment must be at start; last segment must be at end
            if (i == 0 && idx != 0) return false;
            if (i == parts.Length - 1 && idx + part.Length != fileName.Length) return false;

            pos = idx + part.Length;
        }
        return true;
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
        var files = _indexedExtensions
            .SelectMany(ext => Directory.GetFiles(directory, $"*{ext}", SearchOption.AllDirectories))
            .Where(IsIndexable)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Indexing {Count} files ({Exts})...",
            files.Count, string.Join(", ", _indexedExtensions));

        // Embedding (ONNX) runs in parallel; DB writes serialize through the repo's internal semaphore
        await Parallel.ForEachAsync(files, new ParallelOptions
        {
            MaxDegreeOfParallelism = parallelism,
            CancellationToken = ct
        }, async (file, token) => await IndexFileAsync(file, token));

        _logger.LogInformation("Done. Indexed {Count} files.", files.Count);
    }
}
