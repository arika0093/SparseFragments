using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class CycleSettings
{
    public int Value { get; set; }
    public CycleSettings? Next { get; set; }
}

[SparseFragmentModel]
public partial class CycleParent
{
    public CycleChild? Child { get; set; }
}

[SparseFragmentModel]
public partial class CycleChild
{
    public int Value { get; set; }
    public CycleParent? Parent { get; set; }
}

[SparseFragmentModel]
public partial class DagRoot
{
    public DagChild? Left { get; set; }
    public DagChild? Right { get; set; }
}

[SparseFragmentModel]
public partial class DagChild
{
    public int Value { get; set; }
}

public sealed class DiffCycleTests
{
    [Test]
    public void Diff_SameReference_ReturnsEmpty()
    {
        var model = new CycleSettings
        {
            Value = 1,
            Next = new CycleSettings { Value = 2 },
        };

        var diff = CycleSettings.Fragment.Diff(model, model);

        diff.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void Diff_Acyclic_ReturnsMinimalDelta()
    {
        var before = new CycleSettings
        {
            Value = 1,
            Next = new CycleSettings { Value = 1 },
        };
        var after = new CycleSettings
        {
            Value = 1,
            Next = new CycleSettings { Value = 2 },
        };

        var diff = CycleSettings.Fragment.Diff(before, after);

        diff.IsEmpty.ShouldBeFalse();
        diff.Value.IsPresent.ShouldBeFalse();
        diff.Next.IsPresent.ShouldBeTrue();
        diff.Next.Value!.Value.IsPresent.ShouldBeTrue();
        diff.Next.Value!.Value.Value.ShouldBe(2);
    }

    [Test]
    public void Diff_SharedDag_DoesNotThrow()
    {
        var sharedBefore = new DagChild { Value = 1 };
        var before = new DagRoot { Left = sharedBefore, Right = sharedBefore };
        var sharedAfter = new DagChild { Value = 1 };
        var after = new DagRoot { Left = sharedAfter, Right = sharedAfter };

        var empty = DagRoot.Fragment.Diff(before, after);

        empty.IsEmpty.ShouldBeTrue();

        var changedAfter = new DagRoot
        {
            Left = new DagChild { Value = 2 },
            Right = new DagChild { Value = 2 },
        };

        var diff = DagRoot.Fragment.Diff(before, changedAfter);

        diff.IsEmpty.ShouldBeFalse();
        diff.Left.IsPresent.ShouldBeTrue();
        diff.Right.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void From_SharedDag_Succeeds()
    {
        var shared = new DagChild { Value = 3 };
        var model = new DagRoot { Left = shared, Right = shared };

        var fragment = DagRoot.Fragment.From(model);

        fragment.Left.IsPresent.ShouldBeTrue();
        fragment.Right.IsPresent.ShouldBeTrue();
        fragment.Left.Value!.Value.Value.ShouldBe(3);
        fragment.Right.Value!.Value.Value.ShouldBe(3);
    }

    [Test]
    public void Diff_SelfCycle_ThrowsNotSupported()
    {
        var original = new CycleSettings { Value = 1 };
        original.Next = original;
        var clone = original.DeepClone();

        ReferenceEquals(clone.Next, clone).ShouldBeTrue();

        var exception = Should.Throw<NotSupportedException>(() =>
            CycleSettings.Fragment.Diff(original, clone)
        );
        exception.Message.ShouldContain("Diff");
        exception.Message.ShouldContain("Next");
    }

    [Test]
    public void From_SelfCycle_ThrowsNotSupported()
    {
        var model = new CycleSettings { Value = 1 };
        model.Next = model;

        var exception = Should.Throw<NotSupportedException>(() =>
            CycleSettings.Fragment.From(model)
        );
        exception.Message.ShouldContain("From");
        exception.Message.ShouldContain("Next");
    }

    [Test]
    public void Diff_TwoNodeCycle_ThrowsNotSupported()
    {
        var a = new CycleSettings { Value = 1 };
        var b = new CycleSettings { Value = 2 };
        a.Next = b;
        b.Next = a;

        var clone = a.DeepClone();

        ReferenceEquals(clone.Next!.Next, clone).ShouldBeTrue();

        var exception = Should.Throw<NotSupportedException>(() =>
            CycleSettings.Fragment.Diff(a, clone)
        );
        exception.Message.ShouldContain("Diff");
        exception.Message.ShouldContain("Next");
    }

    [Test]
    public void From_TwoNodeCycle_ThrowsNotSupported()
    {
        var a = new CycleSettings { Value = 1 };
        var b = new CycleSettings { Value = 2 };
        a.Next = b;
        b.Next = a;

        var exception = Should.Throw<NotSupportedException>(() => CycleSettings.Fragment.From(a));
        exception.Message.ShouldContain("From");
    }

    [Test]
    public void Diff_CrossTypeMutualRefs_ThrowsNotSupported()
    {
        var parent = new CycleParent();
        var child = new CycleChild { Value = 1, Parent = parent };
        parent.Child = child;

        var clone = parent.DeepClone();

        ReferenceEquals(clone.Child!.Parent, clone).ShouldBeTrue();

        var diffException = Should.Throw<NotSupportedException>(() =>
            CycleParent.Fragment.Diff(parent, clone)
        );
        diffException.Message.ShouldContain("Diff");

        var fromException = Should.Throw<NotSupportedException>(() =>
            CycleParent.Fragment.From(parent)
        );
        fromException.Message.ShouldContain("From");
    }

    [Test]
    public void DeepClone_PreservesSelfCycleAndSharedDag()
    {
        var cyclic = new CycleSettings { Value = 5 };
        cyclic.Next = cyclic;
        var cyclicClone = cyclic.DeepClone();

        ReferenceEquals(cyclicClone, cyclic).ShouldBeFalse();
        ReferenceEquals(cyclicClone.Next, cyclicClone).ShouldBeTrue();

        var shared = new DagChild { Value = 7 };
        var root = new DagRoot { Left = shared, Right = shared };
        var rootClone = root.DeepClone();

        ReferenceEquals(rootClone.Left, rootClone.Right).ShouldBeTrue();
        ReferenceEquals(rootClone.Left, shared).ShouldBeFalse();
    }

    private const int DeepChainDepth = 1500;

    private static CycleSettings BuildChain(int depth, int startValue = 0)
    {
        var head = new CycleSettings { Value = startValue };
        var current = head;
        for (var index = 1; index < depth; index++)
        {
            var next = new CycleSettings { Value = startValue + index };
            current.Next = next;
            current = next;
        }

        return head;
    }

    private static void AssertFragmentChain(
        CycleSettings.Fragment fragment,
        int depth,
        int startValue = 0
    )
    {
        var current = fragment;
        for (var index = 0; index < depth; index++)
        {
            current.Value.Value.ShouldBe(startValue + index);
            if (index == depth - 1)
            {
                // From marks every member present, so the terminal null reads Present(null).
                current.Next.IsPresent.ShouldBeTrue();
                current.Next.Value.ShouldBeNull();
                return;
            }

            current.Next.IsPresent.ShouldBeTrue();
            current.Next.Value.ShouldNotBeNull();
            current = current.Next.Value!;
        }
    }

    [Test]
    public void From_LongAcyclicChain_Succeeds()
    {
        var model = BuildChain(DeepChainDepth);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var fragment = CycleSettings.Fragment.From(model);

        stopwatch.Stop();
        AssertFragmentChain(fragment, DeepChainDepth);
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test]
    public void Diff_LongAcyclicChain_DetectsLeafChange()
    {
        var before = BuildChain(DeepChainDepth);
        var after = BuildChain(DeepChainDepth);
        var leaf = after;
        while (leaf.Next is not null)
        {
            leaf = leaf.Next;
        }

        leaf.Value = -1;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var diff = CycleSettings.Fragment.Diff(before, after);

        stopwatch.Stop();
        diff.IsEmpty.ShouldBeFalse();
        var current = diff;
        for (var index = 0; index < DeepChainDepth - 1; index++)
        {
            current.Value.IsPresent.ShouldBeFalse();
            current.Next.IsPresent.ShouldBeTrue();
            current = current.Next.Value!;
        }

        current.Value.IsPresent.ShouldBeTrue();
        current.Value.Value.ShouldBe(-1);
        current.Next.IsPresent.ShouldBeFalse();
        stopwatch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(15));
    }

    [Test]
    public void From_DeepCycle_ThrowsNotSupported()
    {
        var model = BuildChain(500);
        var tail = model;
        while (tail.Next is not null)
        {
            tail = tail.Next;
        }

        tail.Next = model;

        var exception = Should.Throw<NotSupportedException>(() =>
            CycleSettings.Fragment.From(model)
        );
        exception.Message.ShouldContain("From");
        exception.Message.ShouldContain("Next");
    }

    [Test]
    public void Diff_DeepCycle_ThrowsNotSupported()
    {
        var original = BuildChain(500);
        var tail = original;
        while (tail.Next is not null)
        {
            tail = tail.Next;
        }

        tail.Next = original;
        var clone = original.DeepClone();

        var exception = Should.Throw<NotSupportedException>(() =>
            CycleSettings.Fragment.Diff(original, clone)
        );
        exception.Message.ShouldContain("Diff");
        exception.Message.ShouldContain("Next");
    }

    [Test]
    public void From_DeepSharedTail_Succeeds()
    {
        // Shared tail values continue the left chain so the merged fragment reads 0..599.
        var sharedTail = BuildChain(500, startValue: 100);
        var left = BuildChain(100);
        var leftTail = left;
        while (leftTail.Next is not null)
        {
            leftTail = leftTail.Next;
        }

        leftTail.Next = sharedTail;
        var right = BuildChain(50, startValue: 5000);
        var rightTail = right;
        while (rightTail.Next is not null)
        {
            rightTail = rightTail.Next;
        }

        rightTail.Next = sharedTail;

        var leftFragment = CycleSettings.Fragment.From(left);
        AssertFragmentChain(leftFragment, 600);

        var rightFragment = CycleSettings.Fragment.From(right);
        var current = rightFragment;
        for (var index = 0; index < 50; index++)
        {
            current.Value.Value.ShouldBe(5000 + index);
            current.Next.IsPresent.ShouldBeTrue();
            current = current.Next.Value!;
        }

        current.Value.Value.ShouldBe(100);

        ReferenceEquals(leftTail.Next, sharedTail).ShouldBeTrue();
        ReferenceEquals(rightTail.Next, sharedTail).ShouldBeTrue();
    }

    [Test]
    public void Diff_SharedNodesAcrossGraphs_DoesNotThrow()
    {
        var shared = new DagChild { Value = 9 };
        var before = new DagRoot
        {
            Left = shared,
            Right = new DagChild { Value = 1 },
        };
        var after = new DagRoot
        {
            Left = new DagChild { Value = 1 },
            Right = shared,
        };

        var diff = DagRoot.Fragment.Diff(before, after);

        diff.IsEmpty.ShouldBeFalse();
        diff.Left.IsPresent.ShouldBeTrue();
        diff.Right.IsPresent.ShouldBeTrue();
    }
}
