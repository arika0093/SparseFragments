using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

public sealed class KeyValidationScopeTests
{
    [Test]
    public void DisposalClearsKeysAndCannotDisposeTheNextOwner()
    {
        var scope = Rent<string>(16);
        var keys = scope.Keys;
        keys.Add(new string('x', 1));
        keys.Contains(new string('x', 1)).ShouldBeTrue();
        scope.Dispose();
        keys.ShouldBeEmpty();
        Should.Throw<ObjectDisposedException>(() => scope.Keys);
        using var next = Rent<string>(16);
        next.Keys.ShouldBeSameAs(keys);
        next.Keys.Add("next");
        scope.Dispose();
        next.Keys.Count.ShouldBe(1);
    }

    [Test]
    public void NestedScopesHaveExclusiveStorage()
    {
        using var outer = Rent<int>(16);
        outer.Keys.Add(1);
        using (var inner = Rent<int>(16))
        {
            ReferenceEquals(outer.Keys, inner.Keys).ShouldBeFalse();
            inner.Keys.ShouldBeEmpty();
        }
        outer.Keys.Count.ShouldBe(1);
    }

    [Test]
    public void OversizedRequestIsNotRetainedEvenAfterEarlyFailure()
    {
        var scope = Rent<Guid>(16384);
        var keys = scope.Keys;
        keys.Add(Guid.Empty);
        scope.Dispose();
        using var next = Rent<Guid>(1);
        ReferenceEquals(keys, next.Keys).ShouldBeFalse();
        next.Keys.ShouldBeEmpty();
    }

    [Test]
    public void ExceptionReturnsEmptyStorage()
    {
        HashSet<long>? keys = null;
        Should.Throw<InvalidOperationException>(() =>
        {
            using var scope = Rent<long>(16);
            keys = scope.Keys;
            keys.Add(1);
            throw new InvalidOperationException();
        });
        using var next = Rent<long>(16);
        next.Keys.ShouldBeSameAs(keys);
        next.Keys.ShouldBeEmpty();
    }

    [Test]
    public void LargerRequestGetsEnoughStorageAndCanBeReusedForSmallerRequests()
    {
        using (var small = Rent<decimal>(16))
            small.Keys.Add(1);
        var large = Rent<decimal>(2048);
        var keys = large.Keys;
        keys.ShouldBeEmpty();
        for (var i = 0; i < 2048; i++)
            keys.Add(i);
        large.Dispose();
        using var next = Rent<decimal>(16);
        next.Keys.ShouldBeSameAs(keys);
        next.Keys.ShouldBeEmpty();
    }

    [Test]
    public void ConcurrentChecksOwnDifferentStorage()
    {
        Parallel.For(
            0,
            32,
            index =>
            {
                using var scope = Rent<int>(16);
                scope.Keys.Add(index);
                Thread.Yield();
                scope.Keys.Count.ShouldBe(1);
                scope.Keys.Contains(index).ShouldBeTrue();
            }
        );
    }

    private static SparseKeyValidationScope<TKey> Rent<TKey>(int capacity)
        where TKey : notnull =>
        SparseFragmentRuntime.RentKeyValidation<TKey>(
            capacity,
            static count => new HashSet<TKey>(count)
        );

    [Test]
    public void ColdRentPassesCapacityAndWarmRentSkipsConstruction()
    {
        var calls = 0;
        HashSet<DateTime> Create(int capacity)
        {
            capacity.ShouldBe(2048);
            calls++;
            return new HashSet<DateTime>(capacity);
        }
        using (var first = SparseFragmentRuntime.RentKeyValidation<DateTime>(2048, Create))
            first.Keys.Add(DateTime.MinValue);
        using var next = SparseFragmentRuntime.RentKeyValidation<DateTime>(2048, Create);
        next.Keys.ShouldBeEmpty();
        calls.ShouldBe(1);
    }

    [Test]
    public void NullConstructorIsRejected() =>
        Should
            .Throw<ArgumentNullException>(() =>
                SparseFragmentRuntime.RentKeyValidation<int>(16, null!)
            )
            .ParamName.ShouldBe("create");

    [Test]
    public void NegativeCapacityIsRejected() =>
        Should
            .Throw<ArgumentOutOfRangeException>(() => Rent<int>(-1))
            .ParamName.ShouldBe("capacity");
}
