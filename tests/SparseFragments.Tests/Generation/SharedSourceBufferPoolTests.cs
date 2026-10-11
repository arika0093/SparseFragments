using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

public sealed class SharedSourceBufferPoolTests
{
    [Test]
    public void ReturnedBufferIsEmptyAndHasOnlyOneOwner()
    {
        DrainPool();
        var buffer = SharedSourceBufferPool.Rent();
        buffer.Append("previous source");
        SharedSourceBufferPool.Return(buffer);
        var reused = SharedSourceBufferPool.Rent();
        reused.ShouldBeSameAs(buffer);
        reused.Length.ShouldBe(0);
        SharedSourceBufferPool.Rent().ShouldNotBeSameAs(reused);
    }

    [Test]
    public void OversizedBuffersAreNotRetained()
    {
        DrainPool();
        var buffer = SharedSourceBufferPool.Rent();
        buffer.EnsureCapacity(512 * 1024 + 1);
        SharedSourceBufferPool.Return(buffer);
        SharedSourceBufferPool.Rent().ShouldNotBeSameAs(buffer);
    }

    [Test]
    public void ASharedBudgetCanRetainOneLargeAndOneOrdinaryBuffer()
    {
        DrainPool();
        var large = SharedSourceBufferPool.Rent(384 * 1024);
        var ordinary = SharedSourceBufferPool.Rent(128 * 1024);
        SharedSourceBufferPool.Return(large);
        SharedSourceBufferPool.Return(ordinary);
        SharedSourceBufferPool.Rent(350 * 1024).ShouldBeSameAs(large);
        SharedSourceBufferPool.Rent(64 * 1024).ShouldBeSameAs(ordinary);
    }

    [Test]
    public void EmptySlotsCannotExceedTheTotalCharacterBudget()
    {
        DrainPool();
        var large = SharedSourceBufferPool.Rent(400 * 1024);
        var ordinary = SharedSourceBufferPool.Rent(128 * 1024);
        SharedSourceBufferPool.Return(large);
        SharedSourceBufferPool.Return(ordinary);
        SharedSourceBufferPool.Rent().ShouldBeSameAs(large);
        SharedSourceBufferPool.Rent().ShouldNotBeSameAs(ordinary);
    }

    [Test]
    public void AFullOrdinaryCacheIsPreservedWhenALargeReplacementWouldExceedTheBudget()
    {
        DrainPool();
        var ordinary = Enumerable
            .Range(0, 4)
            .Select(_ => SharedSourceBufferPool.Rent(128 * 1024))
            .ToArray();
        foreach (var buffer in ordinary)
            SharedSourceBufferPool.Return(buffer);
        var large = SharedSourceBufferPool.Rent(256 * 1024);
        SharedSourceBufferPool.Return(large);
        for (var index = 0; index < 4; index++)
            SharedSourceBufferPool.Rent().ShouldBeSameAs(ordinary[index]);
    }

    [Test]
    public void CapacityRequestsChooseTheSmallestCompatibleBuffer()
    {
        DrainPool();
        var large = SharedSourceBufferPool.Rent(256 * 1024);
        var ordinary = SharedSourceBufferPool.Rent(64 * 1024);
        SharedSourceBufferPool.Return(large);
        SharedSourceBufferPool.Return(ordinary);
        SharedSourceBufferPool.Rent(32 * 1024).ShouldBeSameAs(ordinary);
        SharedSourceBufferPool.Rent(200 * 1024).ShouldBeSameAs(large);
    }

    [Test]
    public void RetentionHasFourSlots()
    {
        DrainPool();
        var buffers = Enumerable.Range(0, 5).Select(_ => SharedSourceBufferPool.Rent()).ToArray();
        foreach (var buffer in buffers)
            SharedSourceBufferPool.Return(buffer);
        for (var index = 0; index < 4; index++)
            SharedSourceBufferPool.Rent().ShouldBeSameAs(buffers[index]);
        SharedSourceBufferPool.Rent().ShouldNotBeSameAs(buffers[4]);
    }

    [Test]
    public void LargeBuffersReplaceSmallBuffersWhenTheCacheIsFull()
    {
        DrainPool();
        var small = Enumerable.Range(0, 4).Select(_ => SharedSourceBufferPool.Rent()).ToArray();
        var large = SharedSourceBufferPool.Rent();
        large.EnsureCapacity(4096);
        foreach (var buffer in small)
            SharedSourceBufferPool.Return(buffer);
        SharedSourceBufferPool.Return(large);
        SharedSourceBufferPool.Rent().ShouldBeSameAs(large);
    }

    [Test]
    public void DisposedBuilderCannotMutateARentedBufferOrReturnItTwice()
    {
        DrainPool();
        var first = new SharedIndentedBuilder(CancellationToken.None);
        first.Append("first");
        first.Dispose();
        using var second = new SharedIndentedBuilder(CancellationToken.None) { IndentOffset = 1 };
        second.Append("second");
        first.Dispose();
        using var nested = new SharedIndentedBuilder(CancellationToken.None);
        nested.Append("nested");
        Should.Throw<ObjectDisposedException>(() => first.Append("stale"));
        Should.Throw<ObjectDisposedException>(() => first.Append('x'));
        Should.Throw<ObjectDisposedException>(() => first.AppendIndent(0));
        Should.Throw<ObjectDisposedException>(() => first.ToString());
        second.ToString().ShouldBe("    second");
        nested.ToString().ShouldBe("nested");
    }

    [Test]
    public void ConcurrentThreadsNeverRentEachOthersActiveBuffer()
    {
        DrainPool();
        var first = SharedSourceBufferPool.Rent();
        first.Append("main");
        System.Text.StringBuilder? workerBuffer = null;
        string? workerText = null;
        var worker = new Thread(() =>
        {
            workerBuffer = SharedSourceBufferPool.Rent();
            workerBuffer.Append("worker");
            workerText = workerBuffer.ToString();
            SharedSourceBufferPool.Return(workerBuffer);
        });
        worker.Start();
        worker.Join();
        workerBuffer.ShouldNotBeSameAs(first);
        workerText.ShouldBe("worker");
        first.ToString().ShouldBe("main");
    }

    [Test]
    public void CancellationReturnsTheBufferAndDoesNotLeakLineState()
    {
        using var cancellation = new CancellationTokenSource();
        using (var first = new SharedIndentedBuilder(cancellation.Token) { IndentOffset = 2 })
        {
            first.Append("partial");
            cancellation.Cancel();
            Should.Throw<OperationCanceledException>(() => first.Append("\nmore"));
        }
        using var next = new SharedIndentedBuilder(CancellationToken.None);
        next.AppendLine("fresh");
        next.ToString().ShouldBe("fresh\n");
    }

    private static void DrainPool()
    {
        for (var index = 0; index < 4; index++)
            SharedSourceBufferPool.Rent();
    }
}
