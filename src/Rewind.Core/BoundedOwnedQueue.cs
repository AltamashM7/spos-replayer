namespace Rewind.Core;

/// <summary>A bounded producer-consumer queue. Enqueue transfers ownership, including on drop.</summary>
public sealed class BoundedOwnedQueue<T> : IDisposable where T : class, IDisposable
{
    private readonly object gate = new();
    private readonly Queue<T> queue = new();
    private readonly int capacity;
    private bool disposed;
    private long dropped;

    public BoundedOwnedQueue(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.capacity = capacity;
    }

    public long Dropped { get { lock (gate) return dropped; } }

    public void Enqueue(T item)
    {
        T? discarded = null;
        lock (gate)
        {
            if (disposed) discarded = item;
            else
            {
                if (queue.Count == capacity) { discarded = queue.Dequeue(); dropped++; }
                queue.Enqueue(item);
            }
        }
        discarded?.Dispose();
    }

    public T? TryDequeue()
    {
        lock (gate) return queue.TryDequeue(out var item) ? item : null;
    }

    public void Dispose()
    {
        T[] remaining;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            remaining = queue.ToArray();
            queue.Clear();
        }
        foreach (var item in remaining) item.Dispose();
    }
}
