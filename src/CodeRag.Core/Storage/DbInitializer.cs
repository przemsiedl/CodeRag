using Microsoft.Data.Sqlite;

namespace CodeRag.Core.Storage;

public static class DbInitializer
{
    public static void Initialize(SqliteConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS chunks (
                id            TEXT PRIMARY KEY,
                relative_path TEXT NOT NULL,
                namespace     TEXT,
                parent_class  TEXT,
                symbol_name   TEXT NOT NULL,
                kind          TEXT NOT NULL,
                modifiers     TEXT,
                signature     TEXT,
                usings        TEXT NOT NULL DEFAULT '',
                source_text          TEXT NOT NULL,
                content_hash         TEXT NOT NULL,
                context_header_lines INTEGER NOT NULL DEFAULT 0,
                start_line           INTEGER NOT NULL DEFAULT 0,
                end_line             INTEGER NOT NULL DEFAULT 0,
                indexed_at    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_chunks_path ON chunks(relative_path);

            CREATE VIRTUAL TABLE IF NOT EXISTS chunk_embeddings USING vec0(
                chunk_id TEXT PRIMARY KEY,
                embedding FLOAT[384]
            );
            """;
        cmd.ExecuteNonQuery();

        // Migration: add context_header_lines column to existing databases
        try
        {
            using var migCmd = connection.CreateCommand();
            migCmd.CommandText = "ALTER TABLE chunks ADD COLUMN context_header_lines INTEGER NOT NULL DEFAULT 0";
            migCmd.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Column already exists — ignore
        }
    }
}
