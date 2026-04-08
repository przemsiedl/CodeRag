using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace CodeRag.Core.Storage;

public sealed class RagDbContext : IDisposable
{
    public SqliteConnection Connection { get; }

    public RagDbContext(string databasePath, string vec0ExtensionPath)
    {
        Batteries.Init();
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        Connection = new SqliteConnection(connStr);
        Connection.Open();

        // Enable extension loading
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "SELECT load_extension(@ext)";
        cmd.Parameters.AddWithValue("@ext", vec0ExtensionPath);
        try
        {
            // Need to enable load extension via raw handle first
            raw.sqlite3_enable_load_extension(Connection.Handle, 1);
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to load sqlite-vec extension from '{vec0ExtensionPath}'. " +
                $"Ensure the extension file exists (run 'rag index' first to bootstrap). Inner: {ex.Message}", ex);
        }
    }

    public void Dispose() => Connection.Dispose();
}
