using System.Collections.Generic;
using SparseFragments;

[assembly: SparseCompare(
    typeof(SparseFragments.Tests.Comparison.SemanticToken),
    typeof(SparseFragments.Tests.Comparison.IgnoreCaseTokenComparer)
)]

namespace SparseFragments.Tests.Comparison;

public readonly struct SemanticToken
{
    public SemanticToken(string value) => Value = value;

    public string Value { get; }
}

public sealed class IgnoreCaseTokenComparer : IEqualityComparer<SemanticToken>
{
    public bool Equals(SemanticToken left, SemanticToken right) =>
        StringComparer.OrdinalIgnoreCase.Equals(left.Value, right.Value);

    public int GetHashCode(SemanticToken value) =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(value.Value);
}

public sealed class OrdinalTokenComparer : IEqualityComparer<SemanticToken>
{
    public bool Equals(SemanticToken left, SemanticToken right) =>
        StringComparer.Ordinal.Equals(left.Value, right.Value);

    public int GetHashCode(SemanticToken value) => StringComparer.Ordinal.GetHashCode(value.Value);
}

public sealed class AlwaysEqualTokenMerge : FragmentMergeStrategy<SemanticToken>
{
    public override Optional<SemanticToken> Merge(
        Optional<SemanticToken> lowerPriority,
        Optional<SemanticToken> higherPriority
    ) => higherPriority.IsPresent ? higherPriority : lowerPriority;

    public override bool AreEqual(SemanticToken left, SemanticToken right) => true;
}

[SparseFragmentModel]
public partial class AssemblyComparisonModel
{
    public SemanticToken Value { get; set; }
}

[SparseCompare(typeof(SemanticToken), typeof(OrdinalTokenComparer))]
[SparseFragmentModel]
public partial class ModelComparisonModel
{
    public SemanticToken Value { get; set; }
}

[SparseFragmentModel]
public partial class NestedComparisonLeaf
{
    public SemanticToken Value { get; set; }
}

[SparseFragmentModel]
public partial class NestedComparisonRoot
{
    public NestedComparisonLeaf Leaf { get; set; } = new();
}

[SparseCompare(typeof(SemanticToken), typeof(OrdinalTokenComparer))]
[SparseFragmentModel]
public partial class InheritedComparisonRoot
{
    public InheritedComparisonLeaf Leaf { get; set; } = new();
}

[SparseFragmentModel]
public partial class InheritedComparisonLeaf
{
    public SemanticToken Value { get; set; }
}

[SparseCompare(typeof(SemanticToken), typeof(OrdinalTokenComparer))]
[SparseFragmentModel]
public partial class ModelComparisonLeaf
{
    public SemanticToken Value { get; set; }
}

[SparseFragmentModel]
public partial class ModelComparisonRoot
{
    public ModelComparisonLeaf Leaf { get; set; } = new();
}

[SparseCompare(typeof(SemanticToken), typeof(OrdinalTokenComparer))]
[SparseFragmentModel]
public partial class MemberMergeComparisonModel
{
    [SparseMerge(typeof(AlwaysEqualTokenMerge))]
    public SemanticToken Value { get; set; }
}

[SparseFragmentModel]
public partial class SetTransitionModel
{
    public ISet<string> Values { get; set; } = new HashSet<string>();
}

[SparseFragmentModel]
public partial class ReadOnlySetTransitionModel
{
    public IReadOnlySet<string> Values { get; set; } = new HashSet<string>();
}

/// <summary>Pure <see cref="IReadOnlySet{T}"/> with comparer-aware membership (issue #166).</summary>
/// <remarks>Implements only <c>IReadOnlySet&lt;string&gt;</c>, never <c>ISet&lt;string&gt;</c>.</remarks>
public sealed class CaseInsensitiveReadOnlySet : IReadOnlySet<string>
{
    private readonly HashSet<string> _inner = new(StringComparer.OrdinalIgnoreCase);

    public CaseInsensitiveReadOnlySet(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            _inner.Add(value);
        }
    }

    public int Count => _inner.Count;

    public bool Contains(string item) => _inner.Contains(item);

    public bool IsProperSubsetOf(IEnumerable<string> other) => _inner.IsProperSubsetOf(other);

    public bool IsProperSupersetOf(IEnumerable<string> other) => _inner.IsProperSupersetOf(other);

    public bool IsSubsetOf(IEnumerable<string> other) => _inner.IsSubsetOf(other);

    public bool IsSupersetOf(IEnumerable<string> other) => _inner.IsSupersetOf(other);

    public bool Overlaps(IEnumerable<string> other) => _inner.Overlaps(other);

    public bool SetEquals(IEnumerable<string> other) => _inner.SetEquals(other);

    public IEnumerator<string> GetEnumerator() => _inner.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

public sealed class ChangeSemanticsTests
{
    [Test]
    public void AssemblyAndModelRulesDriveDiffRebaseAndSessionHasChanges()
    {
        var baseline = new AssemblyComparisonModel { Value = new("alpha") };
        var equivalent = new AssemblyComparisonModel { Value = new("ALPHA") };
        AssemblyComparisonModel.Fragment.Diff(baseline, equivalent).IsEmpty.ShouldBeTrue();
        baseline.CreateChangeSet(equivalent).IsEmpty.ShouldBeTrue();

        var session = baseline.CreateEditSession();
        baseline.Value = new("aLpHa");
        session.HasChanges.ShouldBeFalse();
        baseline.Value = new("different");
        session.HasChanges.ShouldBeTrue();

        var desired = new AssemblyComparisonModel { Value = new("beta") };
        var current = new AssemblyComparisonModel { Value = new("BETA") };
        var beforeState = Optional<AssemblyComparisonModel.Fragment?>.Present(
            AssemblyComparisonModel.Fragment.From(baseline)
        );
        var desiredState = Optional<AssemblyComparisonModel.Fragment?>.Present(
            AssemblyComparisonModel.Fragment.From(desired)
        );
        var currentState = Optional<AssemblyComparisonModel.Fragment?>.Present(
            AssemblyComparisonModel.Fragment.From(current)
        );
        var patchRebased = AssemblyComparisonModel.Patch.Rebase(
            beforeState,
            AssemblyComparisonModel.Patch.Between(beforeState, desiredState),
            currentState
        );
        patchRebased.HasConflicts.ShouldBeFalse();
        patchRebased.Rebased.IsEmpty.ShouldBeTrue();

        var rebased = baseline.CreateChangeSet(desired).RebaseOnto(currentState);
        rebased.HasConflicts.ShouldBeFalse();
        rebased.Rebased.IsEmpty.ShouldBeTrue();

        var modelOverride = new ModelComparisonModel { Value = new("alpha") };
        var modelDifferent = new ModelComparisonModel { Value = new("ALPHA") };
        ModelComparisonModel.Fragment.Diff(modelOverride, modelDifferent).IsEmpty.ShouldBeFalse();
        modelOverride.CreateChangeSet(modelDifferent).IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void AssemblyComparisonRuleAppliesInsideNestedGeneratedModels()
    {
        var before = new NestedComparisonRoot
        {
            Leaf = new NestedComparisonLeaf { Value = new("alpha") },
        };
        var after = new NestedComparisonRoot
        {
            Leaf = new NestedComparisonLeaf { Value = new("ALPHA") },
        };

        NestedComparisonRoot.Fragment.Diff(before, after).IsEmpty.ShouldBeTrue();
        var changes = before.CreateChangeSet(after);
        changes.IsEmpty.ShouldBeTrue();
        changes.Leaf.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RootModelComparisonRulePropagatesToNestedGeneratedModel()
    {
        var before = new InheritedComparisonRoot
        {
            Leaf = new InheritedComparisonLeaf { Value = new("base") },
        };
        var after = new InheritedComparisonRoot
        {
            Leaf = new InheritedComparisonLeaf { Value = new("local") },
        };
        var concurrent = new InheritedComparisonRoot
        {
            Leaf = new InheritedComparisonLeaf { Value = new("LOCAL") },
        };

        InheritedComparisonRoot.Fragment.Diff(before, after).IsEmpty.ShouldBeFalse();
        before.CreateChangeSet(after).IsEmpty.ShouldBeFalse();
        before.CreateChangeSet(after).RebaseOnto(concurrent).HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void NestedModelComparisonRuleOverridesAssemblyRule()
    {
        var before = new ModelComparisonRoot
        {
            Leaf = new ModelComparisonLeaf { Value = new("base") },
        };
        var after = new ModelComparisonRoot
        {
            Leaf = new ModelComparisonLeaf { Value = new("local") },
        };
        var concurrent = new ModelComparisonRoot
        {
            Leaf = new ModelComparisonLeaf { Value = new("LOCAL") },
        };

        ModelComparisonRoot.Fragment.Diff(before, after).IsEmpty.ShouldBeFalse();
        before.CreateChangeSet(after).IsEmpty.ShouldBeFalse();
        var rebase = before.CreateChangeSet(after).RebaseOnto(concurrent);
        rebase.HasConflicts.ShouldBeTrue();
    }

    [Test]
    public void MemberSparseMergeComparisonOverridesTypeLevelRule()
    {
        var before = new MemberMergeComparisonModel { Value = new("before") };
        var after = new MemberMergeComparisonModel { Value = new("after") };

        MemberMergeComparisonModel.Fragment.Diff(before, after).IsEmpty.ShouldBeTrue();
        before.CreateChangeSet(after).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SetTransitionExposesAddedAndRemovedValues()
    {
        var before = new SetTransitionModel
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "keep", "remove" },
        };
        var after = new SetTransitionModel
        {
            Values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "KEEP", "add" },
        };

        var transition = SetTransitionModel.ChangeSet.Between(before, after).Values;

        transition.IsEmpty.ShouldBeFalse();
        transition.IsChanged.ShouldBeTrue();
        transition.Before.IsPresent.ShouldBeTrue();
        transition.After.IsPresent.ShouldBeTrue();
        transition.Added.ShouldBe(["add"]);
        transition.Removed.ShouldBe(["remove"]);
    }

    [Test]
    public void PureReadOnlySetHonorsCustomMembership()
    {
        // Same letters under a case-insensitive set: no semantic change.
        var before = new ReadOnlySetTransitionModel
        {
            Values = new CaseInsensitiveReadOnlySet(["ABC"]),
        };
        var after = new ReadOnlySetTransitionModel
        {
            Values = new CaseInsensitiveReadOnlySet(["abc"]),
        };
        var same = ReadOnlySetTransitionModel.ChangeSet.Between(before, after).Values;
        same.Added.ShouldBeEmpty();
        same.Removed.ShouldBeEmpty();

        // A genuine addition is still reported.
        var added = ReadOnlySetTransitionModel
            .ChangeSet.Between(
                before,
                new ReadOnlySetTransitionModel
                {
                    Values = new CaseInsensitiveReadOnlySet(["ABC", "new"]),
                }
            )
            .Values;
        added.Added.ShouldBe(["new"]);
        added.Removed.ShouldBeEmpty();

        // Null and empty sets stay consistent.
        var empty = ReadOnlySetTransitionModel.ChangeSet.Between(
            new ReadOnlySetTransitionModel { Values = new HashSet<string>() },
            new ReadOnlySetTransitionModel { Values = new HashSet<string>() }
        );
        empty.IsEmpty.ShouldBeTrue();
    }
}
