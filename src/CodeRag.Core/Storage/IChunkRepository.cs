using CodeRag.Core.Parsing;
using CodeRag.Core.Query;

namespace CodeRag.Core.Storage;

public interface IChunkRepository
{
    /// <summary>
    /// Reads existing hashes for a path, then atomically deletes stale chunks and upserts new ones.
    /// All DB work is done inside a single lock+transaction — safe to call concurrently across files.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetContentHashesByPathAsync(string relativePath, CancellationToken ct = default);
    Task SyncFileAsync(string relativePath, IReadOnlyList<CodeChunk> chunks, bool deleteStale, CancellationToken ct = default);
    Task DeleteByPathAsync(string relativePath, CancellationToken ct = default);
    Task<IReadOnlyList<QueryResult>> SearchAsync(float[] queryEmbedding, Query.QueryOptions options, CancellationToken ct = default);
    Task<IReadOnlyList<QueryResult>> GetByFiltersAsync(Query.QueryOptions options, CancellationToken ct = default);
    Task<(int chunks, long dbSizeBytes, DateTimeOffset? lastIndexed)> GetStatsAsync(CancellationToken ct = default);
}
