using System.Text.Json;
using SparseFragments;

namespace SparseFragments.Tests;

// Plain (non-partial, non-fragment) value held directly: atomic replace semantics.
public sealed class MutableAtomicValue
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class AtomicValueHolder
{
    [SparseMerge(MergeMode.Replace)]
    public MutableAtomicValue? Payload { get; set; }
}

/// <summary>
/// Ownership contract for mutable values assigned through a <c>Patch</c>:
/// assignment and <c>Apply</c> share the supplied reference instead of cloning.
/// The patch, the assigned source value, and the resulting fragment alias the
/// same instance, so callers own mutation discipline. This matches
/// <c>Merge</c> (<c>Replace</c>), <c>ApplyChanges</c>, and <c>ToModel</c>;
/// only <c>Fragment.From</c>, <c>DeepClone</c>, whole-contribution
/// <c>Set(model)</c> (which snapshots through <c>From</c>), and Patch/ChangeSet
/// JSON deserialization (freshly deserialized values) produce isolated copies.
/// </summary>
public sealed class PatchOwnershipTests
{
    [Test]
    public void ListValueAssignedThroughPatchIsSharedByReference()
    {
        var tags = new List<string> { "a" };
        var patch = new ScalarSequenceHolder.Patch { Tags = tags };
        var basis = new ScalarSequenceHolder.Fragment { Tags = new List<string> { "x" } };

        var result = basis.Apply(patch);

        ReferenceEquals(result.Tags.Value, tags).ShouldBeTrue();
        ReferenceEquals(patch.Tags.Value, tags).ShouldBeTrue();

        // Mutating the source list after Apply is visible through the patch and the result...
        tags.Add("b");
        result.Tags.Value!.ShouldBe(["a", "b"]);
        patch.Tags.Value!.ShouldBe(["a", "b"]);

        // ...but the basis fragment keeps its own list.
        basis.Tags.Value!.ShouldBe(["x"]);

        // Mutating through the patch value is equally visible in the result.
        patch.Tags.Value!.Add("c");
        result.Tags.Value!.ShouldBe(["a", "b", "c"]);
        tags.ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void SetValueAssignedThroughPatchIsSharedByReference()
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a" };
        var patch = new SetSettings.Patch { Values = values };

        var result = new SetSettings.Fragment().Apply(patch);

        ReferenceEquals(result.Values.Value, values).ShouldBeTrue();
        ReferenceEquals(patch.Values.Value, values).ShouldBeTrue();

        values.Add("b");
        result.Values.Value!.ShouldBe(["a", "b"]);

        patch.Values.Value!.Add("c");
        result.Values.Value!.ShouldBe(["a", "b", "c"]);
        values.ShouldBe(["a", "b", "c"]);
    }

    [Test]
    public void DictionaryWholeSetIsSharedByReference()
    {
        var scores = new Dictionary<string, int> { ["a"] = 1 };
        var patch = new ScalarDictHolder.Patch();
        patch.Scores.Set(scores);
        var basis = ScalarDictHolder.Fragment.From(
            new ScalarDictHolder { Scores = new() { ["x"] = 0 } }
        );

        var result = basis.Apply(patch);

        ReferenceEquals(result.Scores.Value, scores).ShouldBeTrue();

        // Mutating the source dictionary after Apply is visible in the result...
        scores["b"] = 2;
        result.Scores.Value!["b"].ShouldBe(2);

        // ...but the basis fragment keeps its own dictionary.
        basis.Scores.Value!.ContainsKey("b").ShouldBeFalse();
        basis.Scores.Value["x"].ShouldBe(0);
    }

    [Test]
    public void PlainMutableClassValueIsSharedAtomically()
    {
        var payload = new MutableAtomicValue { Name = "a", Count = 1 };
        var patch = new AtomicValueHolder.Patch { Payload = payload };
        var basisPayload = new MutableAtomicValue { Name = "basis" };
        var basis = new AtomicValueHolder.Fragment { Payload = basisPayload };

        var result = basis.Apply(patch);

        // Whole-value replacement: the result aliases the patch instance...
        ReferenceEquals(result.Payload.Value, payload).ShouldBeTrue();
        ReferenceEquals(patch.Payload.Value, payload).ShouldBeTrue();

        // ...so post-Apply mutation from either side is visible in the result...
        payload.Name = "mutated";
        result.Payload.Value!.Name.ShouldBe("mutated");
        patch.Payload.Value!.Count = 42;
        result.Payload.Value.Count.ShouldBe(42);

        // ...while the basis keeps its own instance untouched.
        ReferenceEquals(basis.Payload.Value, basisPayload).ShouldBeTrue();
        basisPayload.Name.ShouldBe("basis");
        basis.Payload.Value!.Name.ShouldBe("basis");
    }

    [Test]
    public void PatchBetweenSharesAfterValues()
    {
        var before = Optional<ScalarSequenceHolder.Fragment?>.Present(
            ScalarSequenceHolder.Fragment.From(new ScalarSequenceHolder { Tags = ["x"] })
        );
        var after = ScalarSequenceHolder.Fragment.From(
            new ScalarSequenceHolder { Tags = ["a", "b"] }
        );

        var patch = ScalarSequenceHolder.Patch.Between(
            before,
            Optional<ScalarSequenceHolder.Fragment?>.Present(after)
        );

        var applied = patch.Apply(before);
        ReferenceEquals(applied.Value!.Tags.Value, after.Tags.Value).ShouldBeTrue();
    }

    [Test]
    public void WholeSetModelSnapshotsThroughFrom()
    {
        var model = new ScalarSequenceHolder { Tags = ["a"], Numbers = [1] };
        var patch = new ScalarSequenceHolder.Patch();
        patch.Set(model);

        // Whole-contribution Set clones at Set time, so later source mutation is isolated.
        model.Tags.Add("mutated");

        var applied = patch.Apply(Optional<ScalarSequenceHolder.Fragment?>.Missing);
        applied.Value!.Tags.Value!.ShouldBe(["a"]);
        ReferenceEquals(applied.Value.Tags.Value, model.Tags).ShouldBeFalse();
    }

    private static string ChangeSetJson(ScalarSequenceHolder.ChangeSet changes) =>
        JsonSerializer.Serialize(changes.ToPayload());

    /// <summary>
    /// ChangeSet ownership (issue #170): unlike <c>Patch</c> above, a
    /// <c>ChangeSet</c> snapshots its inputs. Whole-root fragments are
    /// deep-cloned and member collection containers are copied at capture
    /// (comparers preserved), so caller-side mutation after <c>Between</c> or
    /// through typed <c>Before</c>/<c>After</c> endpoints cannot alter history.
    /// Element values stay shared by reference; exotic container shapes fall
    /// back to the borrowed reference.
    /// </summary>
    [Test]
    public void ChangeSetSnapshotsWholeRootFragments()
    {
        var tags = new List<string> { "a" };
        var frag = new ScalarSequenceHolder.Fragment
        {
            Tags = Optional<List<string>>.Present(tags),
        };
        var changes = ScalarSequenceHolder.ChangeSet.Between(
            Optional<ScalarSequenceHolder.Fragment?>.Missing,
            Optional<ScalarSequenceHolder.Fragment?>.Present(frag)
        );
        changes.IsEmpty.ShouldBeFalse();
        var payload = ChangeSetJson(changes);

        tags.Add("mutated");
        frag.Tags.Value!.Add("mutated");

        changes.IsEmpty.ShouldBeFalse();
        ChangeSetJson(changes).ShouldBe(payload);
        var applied = changes.ToPatch().Apply(Optional<ScalarSequenceHolder.Fragment?>.Missing);
        applied.Value!.Tags.Value!.ShouldBe(["a"]);
    }

    [Test]
    public void ChangeSetSnapshotsMemberwiseCollections()
    {
        var beforeTags = new List<string> { "a" };
        var afterTags = new List<string> { "b" };
        var before = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(beforeTags) }
        );
        var after = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(afterTags) }
        );
        var changes = ScalarSequenceHolder.ChangeSet.Between(before, after);
        var payload = ChangeSetJson(changes);

        beforeTags.Add("mutated");
        afterTags.Add("mutated");

        changes.IsEmpty.ShouldBeFalse();
        ChangeSetJson(changes).ShouldBe(payload);
        changes.Tags.After.Value!.ShouldBe(["b"]);
    }

    [Test]
    public void ChangeSetTypedTransitionsReturnDefensiveSnapshots()
    {
        var before = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(["a"]) }
        );
        var after = Optional<ScalarSequenceHolder.Fragment?>.Present(
            new ScalarSequenceHolder.Fragment { Tags = Optional<List<string>>.Present(["b"]) }
        );
        var changes = ScalarSequenceHolder.ChangeSet.Between(before, after);
        var payload = ChangeSetJson(changes);

        changes.Tags.Before.Value!.Add("mutated");
        changes.Tags.After.Value!.Add("mutated");

        ChangeSetJson(changes).ShouldBe(payload);
        changes.Tags.Before.Value!.ShouldBe(["a"]);
        changes.Tags.After.Value!.ShouldBe(["b"]);
    }

    [Test]
    public void ChangeSetSnapshotsDictionaryMembers()
    {
        var scores = new Dictionary<string, int> { ["a"] = 1 };
        var before = Optional<ScalarDictHolder.Fragment?>.Present(
            new ScalarDictHolder.Fragment { Scores = Optional<Dictionary<string, int>>.Missing }
        );
        var after = Optional<ScalarDictHolder.Fragment?>.Present(
            new ScalarDictHolder.Fragment
            {
                Scores = Optional<Dictionary<string, int>>.Present(scores),
            }
        );
        var changes = ScalarDictHolder.ChangeSet.Between(before, after);
        var payload = JsonSerializer.Serialize(changes.ToPayload());

        scores["mutated"] = 2;

        changes.IsEmpty.ShouldBeFalse();
        JsonSerializer.Serialize(changes.ToPayload()).ShouldBe(payload);
        changes.Scores.After.Value!.ContainsKey("mutated").ShouldBeFalse();
    }
}
