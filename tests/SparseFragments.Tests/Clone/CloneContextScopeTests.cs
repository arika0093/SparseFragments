using SparseFragments.CompilerServices;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class CloneContextFailureModel
{
    private int _value;

    [SparseIgnore]
    public bool Fail { get; set; }

    public int Value
    {
        get => Fail ? throw new InvalidOperationException("Clone failed.") : _value;
        set => _value = value;
    }

    public List<int> Items { get; set; } = new();
}

public sealed class CloneContextScopeTests
{
    [Test]
    public void DisposalClearsReferencesAndReusePreservesReferenceIdentity()
    {
        var scope = SparseFragmentRuntime.RentCloneContext();
        var context = scope.Context;
        context.Add(new string('x', 1), new object());
        context.Add(new string('x', 1), new object());
        context.Count.ShouldBe(2);
        scope.Dispose();
        context.ShouldBeEmpty();
        Should.Throw<ObjectDisposedException>(() => scope.Context);

        using var reused = SparseFragmentRuntime.RentCloneContext();
        ReferenceEquals(context, reused.Context).ShouldBeTrue();
        reused.Context.Add(new object(), new object());
        scope.Dispose();
        reused.Context.Count.ShouldBe(1);
    }

    [Test]
    public void NestedScopesOwnDifferentContexts()
    {
        using var outer = SparseFragmentRuntime.RentCloneContext();
        var key = new object();
        outer.Context.Add(key, key);
        using (var inner = SparseFragmentRuntime.RentCloneContext())
        {
            ReferenceEquals(outer.Context, inner.Context).ShouldBeFalse();
            inner.Context.ShouldBeEmpty();
        }
        outer.Context[key].ShouldBeSameAs(key);
    }

    [Test]
    public void ConcurrentScopesDoNotShareContexts()
    {
        Parallel.For(
            0,
            32,
            _ =>
            {
                using var scope = SparseFragmentRuntime.RentCloneContext();
                var key = new object();
                scope.Context.Add(key, key);
                Thread.Yield();
                scope.Context.Count.ShouldBe(1);
                scope.Context[key].ShouldBeSameAs(key);
            }
        );
    }

    [Test]
    public void OversizedContextsAreNotRetained()
    {
        var scope = SparseFragmentRuntime.RentCloneContext();
        var context = scope.Context;
        for (var index = 0; index < 8193; index++)
        {
            var key = new object();
            context.Add(key, key);
        }
        scope.Dispose();
        using var next = SparseFragmentRuntime.RentCloneContext();
        ReferenceEquals(context, next.Context).ShouldBeFalse();
        next.Context.ShouldBeEmpty();
    }

    [Test]
    public void ExceptionDisposesTheOwningScope()
    {
        Dictionary<object, object>? context = null;
        Should.Throw<InvalidOperationException>(() =>
        {
            using var scope = SparseFragmentRuntime.RentCloneContext();
            context = scope.Context;
            var key = new object();
            context.Add(key, key);
            throw new InvalidOperationException("Clone failed.");
        });
        context.ShouldNotBeNull();
        context.ShouldBeEmpty();
    }

    [Test]
    public void GeneratedOperationsReturnTheirContextsAfterExceptions()
    {
        var scope = SparseFragmentRuntime.RentCloneContext();
        var context = scope.Context;
        scope.Dispose();
        var model = new CloneContextFailureModel { Fail = true };
        Should.Throw<InvalidOperationException>(() => model.DeepClone());
        using (var afterClone = SparseFragmentRuntime.RentCloneContext())
        {
            afterClone.Context.ShouldBeSameAs(context);
            afterClone.Context.ShouldBeEmpty();
        }
        Should.Throw<InvalidOperationException>(() =>
            CloneContextFailureModel.Fragment.From(model)
        );
        using var afterFrom = SparseFragmentRuntime.RentCloneContext();
        afterFrom.Context.ShouldBeSameAs(context);
        afterFrom.Context.ShouldBeEmpty();
    }
}
