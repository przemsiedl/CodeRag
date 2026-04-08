using CodeRag.Core.Parsing;
using CodeRag.Core.Query;
using Microsoft.Data.Sqlite;

namespace CodeRag.Core.Storage;

/// <summary>
/// SQLite is single-writer. All operations serialize through _lock.
/// Commands are disposed synchronously (using, not await using) to avoid
/// the NullReferenceException in SqliteConnection.RemoveCommand during async disposal.
/// </summary>
public sealed class SqliteChunkRepository : IChunkRepository
{
    private readonly RagDbContext _db;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SqliteChunkRepository(RagDbContext db) => _db = db;

    public async Task<IReadOnlyDictionary<string, string>> GetContentHashesByPathAsync(
        string relativePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return ReadHashes(relativePath); }
        finally { _lock.Release(); }
    }

    public async Task SyncFileAsync(
        string relativePath,
        IReadOnlyList<CodeChunk> chunks,
        bool deleteStale,
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.Connection.BeginTransaction();
            try
            {
                if (deleteStale)
                    DeleteByPathCore(relativePath, tx);

                foreach (var chunk in chunks)
                    UpsertCore(chunk, tx);

                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }
        finally { _lock.Release(); }
    }

    public async Task DeleteByPathAsync(string relativePath, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var tx = _db.Connection.BeginTransaction();
            try { DeleteByPathCore(relativePath, tx); tx.Commit(); }
            catch { tx.Rollback(); throw; }
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<QueryResult>> SearchAsync(
        float[] queryEmbedding, Query.QueryOptions options, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Build optional WHERE filters on top of vec0 KNN
            var filters = new List<string>();
            if (options.Kinds is { Count: > 0 })
                filters.Add($"c.kind IN ({string.Join(",", options.Kinds.Select((_, i) => $"@kind{i}"))})");
            if (options.ParentClass != null)
                filters.Add("LOWER(c.parent_class) LIKE @parentClass");
            if (options.InFile != null)
                filters.Add("LOWER(c.relative_path) LIKE @inFile");

            var filterSql = filters.Count > 0 ? "AND " + string.Join(" AND ", filters) : "";

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT c.relative_path, c.namespace, c.parent_class, c.symbol_name,
                       c.kind, c.signature, c.source_text, c.context_header_lines, c.start_line, c.end_line, e.distance
                FROM chunk_embeddings e
                JOIN chunks c ON c.id = e.chunk_id
                WHERE e.embedding MATCH @emb
                  AND k = @k
                  {filterSql}
                ORDER BY e.distance
                """;
            cmd.Parameters.AddWithValue("@emb", SerializeEmbedding(queryEmbedding));
            cmd.Parameters.AddWithValue("@k", options.TopK);

            if (options.Kinds is { Count: > 0 })
            {
                int i = 0;
                foreach (var kind in options.Kinds)
                    cmd.Parameters.AddWithValue($"@kind{i++}", kind.ToString());
            }
            if (options.ParentClass != null)
                cmd.Parameters.AddWithValue("@parentClass", $"%{options.ParentClass.ToLower()}%");
            if (options.InFile != null)
                cmd.Parameters.AddWithValue("@inFile", $"%{options.InFile.ToLower()}%");

            var results = new List<QueryResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sourceText = options.OnlySignatures
                    ? reader.GetString(5) // signature column
                    : reader.GetString(6);
                results.Add(new QueryResult(
                    RelativePath: reader.GetString(0),
                    Namespace: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ParentClass: reader.IsDBNull(2) ? null : reader.GetString(2),
                    SymbolName: reader.GetString(3),
                    Kind: Enum.Parse<SymbolKind>(reader.GetString(4)),
                    Signature: reader.GetString(5),
                    SourceText: sourceText,
                    ContextHeaderLines: reader.GetInt32(7),
                    StartLine: reader.GetInt32(8),
                    EndLine: reader.GetInt32(9),
                    Distance: reader.GetDouble(10)
                ));
            }
            return results;
        }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<QueryResult>> GetByFiltersAsync(
        Query.QueryOptions options, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var filters = new List<string>();
            if (options.Kinds is { Count: > 0 })
                filters.Add($"kind IN ({string.Join(",", options.Kinds.Select((_, i) => $"@kind{i}"))})");
            if (options.ParentClass != null)
                filters.Add("LOWER(parent_class) LIKE @parentClass");
            if (options.InFile != null)
                filters.Add("LOWER(relative_path) LIKE @inFile");

            var whereSql = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "";

            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = $"""
                SELECT relative_path, namespace, parent_class, symbol_name,
                       kind, signature, source_text, context_header_lines, start_line, end_line
                FROM chunks
                {whereSql}
                ORDER BY relative_path, start_line
                LIMIT @topK
                """;
            cmd.Parameters.AddWithValue("@topK", options.TopK);

            if (options.Kinds is { Count: > 0 })
            {
                int i = 0;
                foreach (var kind in options.Kinds)
                    cmd.Parameters.AddWithValue($"@kind{i++}", kind.ToString());
            }
            if (options.ParentClass != null)
                cmd.Parameters.AddWithValue("@parentClass", $"%{options.ParentClass.ToLower()}%");
            if (options.InFile != null)
                cmd.Parameters.AddWithValue("@inFile", $"%{options.InFile.ToLower()}%");

            var results = new List<QueryResult>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sourceText = options.OnlySignatures
                    ? reader.GetString(5)
                    : reader.GetString(6);
                results.Add(new QueryResult(
                    RelativePath: reader.GetString(0),
                    Namespace: reader.IsDBNull(1) ? null : reader.GetString(1),
                    ParentClass: reader.IsDBNull(2) ? null : reader.GetString(2),
                    SymbolName: reader.GetString(3),
                    Kind: Enum.Parse<SymbolKind>(reader.GetString(4)),
                    Signature: reader.GetString(5),
                    SourceText: sourceText,
                    ContextHeaderLines: reader.GetInt32(7),
                    StartLine: reader.GetInt32(8),
                    EndLine: reader.GetInt32(9),
                    Distance: 0.0
                ));
            }
            return results;
        }
        finally { _lock.Release(); }
    }

    public async Task<(int chunks, long dbSizeBytes, DateTimeOffset? lastIndexed)> GetStatsAsync(
        CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            using var cmd = _db.Connection.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*), MAX(indexed_at) FROM chunks";
            using var reader = cmd.ExecuteReader();
            reader.Read();
            var count = reader.GetInt32(0);
            DateTimeOffset? lastIndexed = reader.IsDBNull(1)
                ? null : DateTimeOffset.Parse(reader.GetString(1));
            var dbPath = _db.Connection.DataSource;
            var dbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;
            return (count, dbSize, lastIndexed);
        }
        finally { _lock.Release(); }
    }

    // ── private helpers — callers hold the lock, all sync ─────────────────

    private IReadOnlyDictionary<string, string> ReadHashes(string relativePath)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, content_hash FROM chunks WHERE relative_path = @path";
        cmd.Parameters.AddWithValue("@path", relativePath);
        var result = new Dictionary<string, string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private void DeleteByPathCore(string relativePath, SqliteTransaction tx)
    {
        var ids = new List<string>();
        using (var sel = _db.Connection.CreateCommand())
        {
            sel.Transaction = tx;
            sel.CommandText = "SELECT id FROM chunks WHERE relative_path = @path";
            sel.Parameters.AddWithValue("@path", relativePath);
            using var r = sel.ExecuteReader();
            while (r.Read()) ids.Add(r.GetString(0));
        }
        foreach (var id in ids)
        {
            using var del = _db.Connection.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM chunk_embeddings WHERE chunk_id = @id";
            del.Parameters.AddWithValue("@id", id);
            del.ExecuteNonQuery();
        }
        using var delChunks = _db.Connection.CreateCommand();
        delChunks.Transaction = tx;
        delChunks.CommandText = "DELETE FROM chunks WHERE relative_path = @path";
        delChunks.Parameters.AddWithValue("@path", relativePath);
        delChunks.ExecuteNonQuery();
    }

    private void UpsertCore(CodeChunk chunk, SqliteTransaction tx)
    {
        using var metaCmd = _db.Connection.CreateCommand();
        metaCmd.Transaction = tx;
        metaCmd.CommandText = """
            INSERT OR REPLACE INTO chunks
                (id, relative_path, namespace, parent_class, symbol_name, kind, modifiers, signature, source_text, content_hash, context_header_lines, start_line, end_line, indexed_at)
            VALUES
                (@id, @path, @ns, @parent, @name, @kind, @mods, @sig, @src, @hash, @ctxLines, @startLine, @endLine, @at)
            """;
        metaCmd.Parameters.AddWithValue("@id", chunk.Id);
        metaCmd.Parameters.AddWithValue("@path", chunk.RelativePath);
        metaCmd.Parameters.AddWithValue("@ns", (object?)chunk.Namespace ?? DBNull.Value);
        metaCmd.Parameters.AddWithValue("@parent", (object?)chunk.ParentClass ?? DBNull.Value);
        metaCmd.Parameters.AddWithValue("@name", chunk.SymbolName);
        metaCmd.Parameters.AddWithValue("@kind", chunk.Kind.ToString());
        metaCmd.Parameters.AddWithValue("@mods", chunk.Modifiers);
        metaCmd.Parameters.AddWithValue("@sig", chunk.Signature);
        metaCmd.Parameters.AddWithValue("@src", chunk.SourceText);
        metaCmd.Parameters.AddWithValue("@hash", chunk.ContentHash);
        metaCmd.Parameters.AddWithValue("@ctxLines", chunk.ContextHeaderLines);
        metaCmd.Parameters.AddWithValue("@startLine", chunk.StartLine);
        metaCmd.Parameters.AddWithValue("@endLine", chunk.EndLine);
        metaCmd.Parameters.AddWithValue("@at", chunk.IndexedAt.ToString("O"));
        metaCmd.ExecuteNonQuery();

        if (chunk.Embedding.Length > 0)
        {
            // vec0 virtual table does not support INSERT OR REPLACE — must DELETE then INSERT
            using var delVec = _db.Connection.CreateCommand();
            delVec.Transaction = tx;
            delVec.CommandText = "DELETE FROM chunk_embeddings WHERE chunk_id = @id";
            delVec.Parameters.AddWithValue("@id", chunk.Id);
            delVec.ExecuteNonQuery();

            using var vecCmd = _db.Connection.CreateCommand();
            vecCmd.Transaction = tx;
            vecCmd.CommandText = "INSERT INTO chunk_embeddings (chunk_id, embedding) VALUES (@id, @emb)";
            vecCmd.Parameters.AddWithValue("@id", chunk.Id);
            vecCmd.Parameters.AddWithValue("@emb", SerializeEmbedding(chunk.Embedding));
            vecCmd.ExecuteNonQuery();
        }
    }

    private static byte[] SerializeEmbedding(float[] embedding)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
