namespace CodeRag.Core.Storage;

/// <summary>
/// File-based cross-process lock for the index. Writers acquire it; readers wait for it to be released.
/// </summary>
public static class IndexLock
{
    /// <summary>
    /// Creates the lock file and returns a handle that deletes it on Dispose.
    /// </summary>
    public static IDisposable Acquire(string lockFilePath)
    {
        File.WriteAllText(lockFilePath, Environment.ProcessId.ToString());
        return new LockHandle(lockFilePath);
    }

    /// <summary>
    /// Waits until the lock file disappears, then returns. Polls every 100 ms.
    /// </summary>
    public static async Task WaitForFreeAsync(string lockFilePath, CancellationToken ct = default)
    {
        while (File.Exists(lockFilePath))
        {
            Console.Error.WriteLine("Waiting for index operation to finish...");
            await Task.Delay(100, ct);
        }
    }

    private sealed class LockHandle : IDisposable
    {
        private readonly string _path;
        public LockHandle(string path) => _path = path;
        public void Dispose() { try { File.Delete(_path); } catch { } }
    }
}
