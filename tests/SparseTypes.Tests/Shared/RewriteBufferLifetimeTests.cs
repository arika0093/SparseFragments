using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Shared;

public sealed class RewriteBufferLifetimeTests
{
    [Test]
    public void CapacityRequestsReuseSmallBuffersAndLeaveTheCacheAvailableForOversizedSources()
    {
        DrainPool();
        var cached = SharedSourceBufferPool.Rent();
        SharedSourceBufferPool.Return(cached);
        var large = SharedSourceBufferPool.Rent(128 * 1024 + 1);
        large.Capacity.ShouldBeGreaterThan(128 * 1024);
        var resized = SharedSourceBufferPool.Rent(4096);
        resized.ShouldBeSameAs(cached);
        resized.Capacity.ShouldBeGreaterThanOrEqualTo(4096);
        resized.Length.ShouldBe(0);
    }

    [Test]
    public void NegativeCapacityIsRejectedBeforeTakingOwnership()
    {
        DrainPool();
        var cached = SharedSourceBufferPool.Rent();
        SharedSourceBufferPool.Return(cached);
        Should.Throw<ArgumentOutOfRangeException>(() => SharedSourceBufferPool.Rent(-1));
        SharedSourceBufferPool.Rent().ShouldBeSameAs(cached);
    }

    [Test]
    public void UnchangedResultsDoNotRentABuffer()
    {
        DrainPool();
        var cached = SharedSourceBufferPool.Rent();
        SharedSourceBufferPool.Return(cached);
        const string source = "\"a\"";
        using var code = new SparseRewriteBuffer(source, "a", "b", 1);
        code.BuildResult().ShouldBeSameAs(source);
        SharedSourceBufferPool.Rent().ShouldBeSameAs(cached);
    }

    [Test]
    public void ExceptionsReturnTheBufferEmpty()
    {
        DrainPool();
        var cached = SharedSourceBufferPool.Rent();
        SharedSourceBufferPool.Return(cached);
        Should.Throw<InvalidOperationException>(() =>
        {
            using var code = Create("a tail");
            code.MarkCode(0);
            throw new InvalidOperationException("Stop before building the result.");
        });
        SharedSourceBufferPool.Rent().ShouldBeSameAs(cached);
        cached.Length.ShouldBe(0);
    }

    [Test]
    public void DisposedOwnersCannotReturnAnActiveBufferTwice()
    {
        DrainPool();
        var first = Create("a first");
        first.MarkCode(0);
        first.Dispose();
        using var second = Create("a second");
        second.MarkCode(0);
        first.Dispose();
        using var nested = Create("a nested");
        nested.MarkCode(0);
        Should.Throw<ObjectDisposedException>(() => first.MarkCode(0));
        Should.Throw<ObjectDisposedException>(() => first.BuildResult());
        second.BuildResult().ShouldBe("b second");
        nested.BuildResult().ShouldBe("b nested");
    }

    private static SparseRewriteBuffer Create(string source) => new(source, "a", "b", 0);

    private static void DrainPool()
    {
        for (var index = 0; index < 4; index++)
            SharedSourceBufferPool.Rent();
    }
}
