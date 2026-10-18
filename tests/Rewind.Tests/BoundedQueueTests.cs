using Rewind.Core;
using Xunit;

namespace Rewind.Tests;

public sealed class BoundedQueueTests
{
    [Fact]
    public void OverflowReleasesOldestAndTransfersRemainingOwnership()
    {
        using var queue = new BoundedOwnedQueue<Owned>(2);
        var first = new Owned(); var second = new Owned(); var third = new Owned();
        queue.Enqueue(first); queue.Enqueue(second); queue.Enqueue(third);
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, queue.Dropped);
        Assert.Same(second, queue.TryDequeue());
        queue.Dispose();
        Assert.Equal(0, second.Disposals);
        Assert.Equal(1, third.Disposals);
        second.Dispose();
    }

    [Fact]
    public async Task ConcurrentProductionAndConsumptionReleaseEveryBufferOnce()
    {
        using var queue = new BoundedOwnedQueue<Owned>(8);
        var items = Enumerable.Range(0, 4000).Select(_ => new Owned()).ToArray();
        var producing = 1;
        var consumer = Task.Run(async () =>
        {
            while (Volatile.Read(ref producing) != 0)
            {
                while (queue.TryDequeue() is { } item) item.Dispose();
                await Task.Yield();
            }
            while (queue.TryDequeue() is { } item) item.Dispose();
        });
        var producers = Enumerable.Range(0, 4).Select(partition => Task.Run(() =>
        {
            for (var i = partition * 1000; i < (partition + 1) * 1000; i++) queue.Enqueue(items[i]);
        }));
        await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10));
        Volatile.Write(ref producing, 0);
        await consumer.WaitAsync(TimeSpan.FromSeconds(10));
        queue.Dispose();
        Assert.All(items, item => Assert.Equal(1, item.Disposals));
    }

    [Fact]
    public async Task ShutdownWhileProducersWriteDoesNotLeakOrReleaseTwice()
    {
        var queue = new BoundedOwnedQueue<Owned>(4);
        var items = Enumerable.Range(0, 4000).Select(_ => new Owned()).ToArray();
        using var ready = new ManualResetEventSlim();
        var producers = Enumerable.Range(0, 4).Select(partition => Task.Run(() =>
        {
            ready.Wait();
            for (var i = partition * 1000; i < (partition + 1) * 1000; i++) queue.Enqueue(items[i]);
        })).ToArray();
        ready.Set();
        queue.Dispose();
        await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10));
        queue.Dispose();
        Assert.Null(queue.TryDequeue());
        Assert.All(items, item => Assert.Equal(1, item.Disposals));
    }

    [Fact]
    public void CapacityMustBePositive() => Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedOwnedQueue<Owned>(0));

    private sealed class Owned : IDisposable
    {
        private int disposals;
        public int Disposals => Volatile.Read(ref disposals);
        public void Dispose() => Interlocked.Increment(ref disposals);
    }
}
