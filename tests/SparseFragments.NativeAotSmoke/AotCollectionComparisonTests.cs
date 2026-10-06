using SparseFragments.CompilerServices;

namespace SparseFragments.NativeAotSmoke;

/// <summary>
/// NativeAOT regression coverage for collection comparison (#9). Every case
/// runs from the Ahead-of-Time published test binary, proving no runtime
/// generic code generation is attempted for these shapes.
/// </summary>
public sealed class AotCollectionComparisonTests
{
    private static bool IntCollectionsEqual(AotIntCollections.Fragment left, AotIntCollections.Fragment right) =>
        AotIntCollections.Patch
            .Between(
                Optional<AotIntCollections.Fragment?>.Present(left),
                Optional<AotIntCollections.Fragment?>.Present(right)
            )
            .IsEmpty;

    private static bool InterfaceCollectionsEqual(
        AotInterfaceCollections.Fragment left,
        AotInterfaceCollections.Fragment right
    ) =>
        AotInterfaceCollections.Patch
            .Between(
                Optional<AotInterfaceCollections.Fragment?>.Present(left),
                Optional<AotInterfaceCollections.Fragment?>.Present(right)
            )
            .IsEmpty;

    private static bool ReferenceCollectionsEqual(
        AotReferenceCollections.Fragment left,
        AotReferenceCollections.Fragment right
    ) =>
        AotReferenceCollections.Patch
            .Between(
                Optional<AotReferenceCollections.Fragment?>.Present(left),
                Optional<AotReferenceCollections.Fragment?>.Present(right)
            )
            .IsEmpty;

    [Test]
    public async Task ListOfIntEqualityIsOrderSensitive()
    {
        var left = new AotIntCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present([1, 2, 3]),
        };
        var same = new AotIntCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present([1, 2, 3]),
        };
        var reordered = new AotIntCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present([3, 2, 1]),
        };
        var changed = new AotIntCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present([1, 2, 4]),
        };

        await Assert.That(IntCollectionsEqual(left, same)).IsTrue();
        await Assert.That(IntCollectionsEqual(left, reordered)).IsFalse();
        await Assert.That(IntCollectionsEqual(left, changed)).IsFalse();
    }

    [Test]
    public async Task ListOfIntDiffReplays()
    {
        var before = new AotIntCollections { Numbers = [1, 2], Unique = [7], Scores = new() { ["a"] = 1 } };
        var after = new AotIntCollections
        {
            Numbers = [1, 2, 3],
            Unique = [7],
            Scores = new() { ["a"] = 1 },
        };

        var replayed = AotIntCollections.Fragment.From(before).ApplyChanges(AotIntCollections.Fragment.Diff(before, after));

        await Assert.That(IntCollectionsEqual(replayed, AotIntCollections.Fragment.From(after))).IsTrue();
    }

    [Test]
    public async Task HashSetOfIntEqualityIsOrderIndependent()
    {
        var left = new AotIntCollections.Fragment
        {
            Unique = Optional<HashSet<int>>.Present([1, 2, 3]),
        };
        var reordered = new AotIntCollections.Fragment
        {
            Unique = Optional<HashSet<int>>.Present([3, 2, 1]),
        };
        var changed = new AotIntCollections.Fragment
        {
            Unique = Optional<HashSet<int>>.Present([1, 2, 4]),
        };

        await Assert.That(IntCollectionsEqual(left, reordered)).IsTrue();
        await Assert.That(IntCollectionsEqual(left, changed)).IsFalse();
    }

    [Test]
    public async Task HashSetOfIntDiffReplays()
    {
        var before = new AotIntCollections { Numbers = [1], Unique = [1, 2], Scores = new() { ["a"] = 1 } };
        var after = new AotIntCollections { Numbers = [1], Unique = [2, 3], Scores = new() { ["a"] = 1 } };

        var replayed = AotIntCollections.Fragment.From(before).ApplyChanges(AotIntCollections.Fragment.Diff(before, after));

        await Assert.That(IntCollectionsEqual(replayed, AotIntCollections.Fragment.From(after))).IsTrue();
    }

    [Test]
    public async Task DictionaryEqualityIsOrderIndependent()
    {
        var left = new AotIntCollections.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["first"] = 1, ["second"] = 2 }
            ),
        };
        var reordered = new AotIntCollections.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["second"] = 2, ["first"] = 1 }
            ),
        };
        var changed = new AotIntCollections.Fragment
        {
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["first"] = 1, ["second"] = 3 }
            ),
        };

        await Assert.That(IntCollectionsEqual(left, reordered)).IsTrue();
        await Assert.That(IntCollectionsEqual(left, changed)).IsFalse();
    }

    [Test]
    public async Task DictionaryDiffReplays()
    {
        var before = new AotIntCollections { Numbers = [1], Unique = [1], Scores = new() { ["a"] = 1 } };
        var after = new AotIntCollections
        {
            Numbers = [1],
            Unique = [1],
            Scores = new() { ["a"] = 2, ["b"] = 3 },
        };

        var replayed = AotIntCollections.Fragment.From(before).ApplyChanges(AotIntCollections.Fragment.Diff(before, after));

        await Assert.That(IntCollectionsEqual(replayed, AotIntCollections.Fragment.From(after))).IsTrue();
    }

    [Test]
    public async Task InterfaceTypedCollectionsCompare()
    {
        var left = new AotInterfaceCollections.Fragment
        {
            Numbers = Optional<IReadOnlyList<int>>.Present(new List<int> { 1, 2 }),
            Unique = Optional<ISet<int>>.Present(new HashSet<int> { 1, 2 }),
            Scores = Optional<IDictionary<string, int>>.Present(new Dictionary<string, int> { ["a"] = 1 }),
        };
        var same = new AotInterfaceCollections.Fragment
        {
            Numbers = Optional<IReadOnlyList<int>>.Present(new List<int> { 1, 2 }),
            Unique = Optional<ISet<int>>.Present(new HashSet<int> { 2, 1 }),
            Scores = Optional<IDictionary<string, int>>.Present(new Dictionary<string, int> { ["a"] = 1 }),
        };
        var changedSequence = new AotInterfaceCollections.Fragment
        {
            Numbers = Optional<IReadOnlyList<int>>.Present(new List<int> { 2, 1 }),
            Unique = Optional<ISet<int>>.Present(new HashSet<int> { 1, 2 }),
            Scores = Optional<IDictionary<string, int>>.Present(new Dictionary<string, int> { ["a"] = 1 }),
        };
        var changedSet = new AotInterfaceCollections.Fragment
        {
            Numbers = Optional<IReadOnlyList<int>>.Present(new List<int> { 1, 2 }),
            Unique = Optional<ISet<int>>.Present(new HashSet<int> { 1, 3 }),
            Scores = Optional<IDictionary<string, int>>.Present(new Dictionary<string, int> { ["a"] = 1 }),
        };
        var changedDictionary = new AotInterfaceCollections.Fragment
        {
            Numbers = Optional<IReadOnlyList<int>>.Present(new List<int> { 1, 2 }),
            Unique = Optional<ISet<int>>.Present(new HashSet<int> { 1, 2 }),
            Scores = Optional<IDictionary<string, int>>.Present(new Dictionary<string, int> { ["a"] = 2 }),
        };

        await Assert.That(InterfaceCollectionsEqual(left, same)).IsTrue();
        await Assert.That(InterfaceCollectionsEqual(left, changedSequence)).IsFalse();
        await Assert.That(InterfaceCollectionsEqual(left, changedSet)).IsFalse();
        await Assert.That(InterfaceCollectionsEqual(left, changedDictionary)).IsFalse();
    }

    [Test]
    public async Task ReferenceElementCollectionsCompare()
    {
        var left = new AotReferenceCollections.Fragment
        {
            Names = Optional<List<string?>>.Present(["a", null, "b"]),
            Tags = Optional<HashSet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" }
            ),
        };
        var same = new AotReferenceCollections.Fragment
        {
            Names = Optional<List<string?>>.Present(["a", null, "b"]),
            Tags = Optional<HashSet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "BETA", "ALPHA" }
            ),
        };
        var changedSequence = new AotReferenceCollections.Fragment
        {
            Names = Optional<List<string?>>.Present(["a", "b", null]),
            Tags = Optional<HashSet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "beta" }
            ),
        };
        var changedSet = new AotReferenceCollections.Fragment
        {
            Names = Optional<List<string?>>.Present(["a", null, "b"]),
            Tags = Optional<HashSet<string>>.Present(
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "alpha", "gamma" }
            ),
        };

        await Assert.That(ReferenceCollectionsEqual(left, same)).IsTrue();
        await Assert.That(ReferenceCollectionsEqual(left, changedSequence)).IsFalse();
        await Assert.That(ReferenceCollectionsEqual(left, changedSet)).IsFalse();
    }

    [Test]
    public async Task RuntimeComparesValueTypeCollectionsDirectly()
    {
        var listLeft = new List<int> { 1, 2, 3 };
        var listSame = new List<int> { 1, 2, 3 };
        var listReordered = new List<int> { 3, 2, 1 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)listLeft, (object)listSame)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)listLeft, (object)listReordered)).IsFalse();

        var setLeft = new HashSet<int> { 1, 2, 3 };
        var setReordered = new HashSet<int> { 3, 2, 1 };
        var setChanged = new HashSet<int> { 1, 2, 4 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)setLeft, (object)setReordered)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)setLeft, (object)setChanged)).IsFalse();

        var dictionaryLeft = new Dictionary<string, int> { ["first"] = 1, ["second"] = 2 };
        var dictionaryReordered = new Dictionary<string, int> { ["second"] = 2, ["first"] = 1 };
        var dictionaryChanged = new Dictionary<string, int> { ["first"] = 1, ["second"] = 3 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)dictionaryLeft, (object)dictionaryReordered)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)dictionaryLeft, (object)dictionaryChanged)).IsFalse();
    }

    [Test]
    public async Task RuntimeComparesInterfaceTypedCollectionsDirectly()
    {
        IReadOnlyList<int> sequenceLeft = new List<int> { 1, 2 };
        IReadOnlyList<int> sequenceSame = new List<int> { 1, 2 };
        IReadOnlyList<int> sequenceReordered = new List<int> { 2, 1 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)sequenceLeft, (object)sequenceSame)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)sequenceLeft, (object)sequenceReordered)).IsFalse();

        ISet<int> setLeft = new HashSet<int> { 1, 2 };
        ISet<int> setReordered = new HashSet<int> { 2, 1 };
        ISet<int> setChanged = new HashSet<int> { 1, 3 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)setLeft, (object)setReordered)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)setLeft, (object)setChanged)).IsFalse();

        IDictionary<string, int> dictionaryLeft = new Dictionary<string, int> { ["a"] = 1 };
        IDictionary<string, int> dictionarySame = new Dictionary<string, int> { ["a"] = 1 };
        IDictionary<string, int> dictionaryChanged = new Dictionary<string, int> { ["a"] = 2 };

        await Assert.That(SparseFragmentRuntime.AreEqual((object)dictionaryLeft, (object)dictionarySame)).IsTrue();
        await Assert.That(SparseFragmentRuntime.AreEqual((object)dictionaryLeft, (object)dictionaryChanged)).IsFalse();
    }
}
