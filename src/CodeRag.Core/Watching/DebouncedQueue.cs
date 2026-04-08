using System.Collections.Concurrent;

namespace CodeRag.Core.Watching;

public sealed class DebouncedQueue<T> : IDisposable
{
    private readonly ConcurrentDictionary<string, (T item, Timer timer)> _pending = new();
    private readonly Func<string, T, CancellationToken, Task> _handler;
    private readonly int _debounceMs;

    public DebouncedQueue(Func<string, T, CancellationToken, Task> handler, int debounceMs = 500)
    {
        _handler = handler;
        _debounceMs = debounceMs;
    }

    public void Enqueue(string key, T item, CancellationToken ct = default)
    {
        if (_pending.TryRemove(key, out var existing))
            existing.timer.Dispose();

        var timer = new Timer(_ =>
        {
            if (_pending.TryRemove(key, out var entry))
            {
                entry.timer.Dispose();
                _ = _handler(key, entry.item, ct);
            }
        }, null, _debounceMs, Timeout.Infinite);

        _pending[key] = (item, timer);
    }

    public void Dispose()
    {
        foreach (var (_, (_, timer)) in _pending)
            timer.Dispose();
        _pending.Clear();
    }
}
