using BenchmarkDotNet.Attributes;
using SparseFragments;

[SparseFragmentModel]
public partial class BenchInspectionItem
{
    [SparseKey]
    public string Id { get; set; } = "";
    public int Score { get; set; }
}

[SparseFragmentModel]
public partial class BenchInspectionRecord
{
    public int Counter { get; set; }
    public string? Label { get; set; }
    public BenchInspectionItem? Child { get; set; }
    public List<BenchInspectionItem> Items { get; set; } = [];
    public Dictionary<string, int> Scores { get; set; } = [];
    public Dictionary<string, BenchInspectionItem> Details { get; set; } = [];
}

[MemoryDiagnoser]
public class PatchInspectionBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    private BenchInspectionRecord.Patch _empty = null!;
    private BenchInspectionRecord.Patch _scalar = null!;
    private BenchInspectionRecord.Patch _nested = null!;
    private BenchInspectionRecord.Patch _keyed = null!;
    private BenchInspectionRecord.Patch _dictionary = null!;
    private BenchInspectionRecord.Patch _nestedDictionary = null!;
    private BenchInspectionRecord.Patch _keyedMixed = null!;
    private BenchInspectionRecord.Patch _dictionaryMixed = null!;

    [GlobalSetup]
    public void Setup()
    {
        _empty = new();
        _scalar = Between(new() { Label = "before" }, new() { Counter = 1, Label = null });
        _nested = Between(new() { Child = Item("child", 0) }, new() { Child = Item("child", 1) });
        var keys = Enumerable.Range(0, Size).Select(index => "key-" + index).ToArray();
        _keyed = Between(
            new() { Items = keys.Select(key => Item(key, 0)).ToList() },
            new() { Items = keys.Select(key => Item(key, 1)).ToList() }
        );
        _dictionary = Between(
            new() { Scores = keys.ToDictionary(key => key, _ => 0) },
            new() { Scores = keys.ToDictionary(key => key, _ => 1) }
        );
        _nestedDictionary = Between(
            new() { Details = keys.ToDictionary(key => key, key => Item(key, 0)) },
            new() { Details = keys.ToDictionary(key => key, key => Item(key, 1)) }
        );
        var retained = keys.Where((_, index) => index % 2 != 0).ToArray();
        var removed = keys.Where((_, index) => index % 2 == 0).ToArray();
        var added = Enumerable.Range(Size, Size / 2).Select(index => "key-" + index).ToArray();
        var desiredOrder = retained.Concat(added).Reverse().ToArray();
        _keyedMixed = Between(
            new() { Items = keys.Select(key => Item(key, 0)).ToList() },
            new() { Items = desiredOrder.Select(key => Item(key, 1)).ToList() }
        );
        _dictionaryMixed = Between(
            new() { Scores = keys.ToDictionary(key => key, _ => 0) },
            new() { Scores = desiredOrder.ToDictionary(key => key, _ => 1) }
        );
        var keyedMixed = KeyedMixed().Single().Keyed!;
        var dictionaryMixed = DictionaryMixed().Single().Dictionary!;
        if (
            !keyedMixed.HasOrder
            || !keyedMixed.KeyOrder.SequenceEqual(desiredOrder.Cast<object?>())
            || !new HashSet<object?>(keyedMixed.RemovedKeys).SetEquals(removed)
            || keyedMixed.Added.Count != added.Length
            || !new HashSet<string>(
                keyedMixed.Added.Select(value => ((BenchInspectionItem)value!).Id)
            ).SetEquals(added)
            || keyedMixed.Edited.Count != retained.Length
            || !new HashSet<object?>(keyedMixed.Edited.Select(edit => edit.Key)).SetEquals(retained)
            || keyedMixed.Edited.Any(edit => !ValidScore(edit.NestedChanges))
            || !new HashSet<object?>(dictionaryMixed.RemovedKeys).SetEquals(removed)
            || dictionaryMixed.SetEntries.Count != added.Length
            || !new HashSet<object?>(
                dictionaryMixed.SetEntries.Select(entry => entry.Key)
            ).SetEquals(added)
            || dictionaryMixed.SetEntries.Any(entry => !Equals(entry.Value, 1))
            || dictionaryMixed.Edited.Count != retained.Length
            || !new HashSet<object?>(dictionaryMixed.Edited.Select(edit => edit.Key)).SetEquals(
                retained
            )
            || dictionaryMixed.Edited.Any(edit => edit.HasNestedChanges || !Equals(edit.Value, 1))
        )
        {
            throw new InvalidOperationException(
                "Mixed inspection must preserve additions, removals, edits and key order."
            );
        }
        var scalar = Scalar();
        var nested = Nested();
        var keyed = KeyedEdits().Single().Keyed!;
        var dictionary = DictionaryEdits().Single().Dictionary!;
        var nestedDictionary = NestedDictionaryEdits().Single().Dictionary!;
        if (
            Empty().Count != 0
            || !scalar.Select(change => change.Property.Name).SequenceEqual(["Counter", "Label"])
            || scalar.Any(change =>
                change.Kind != SparseChangeKind.Set || change.NestedChanges.Count != 0
            )
            || !Equals(scalar[0].Value, 1)
            || scalar[1].Value is not null
            || nested.Count != 1
            || nested[0].Kind != SparseChangeKind.Nested
            || !ValidScore(nested[0].NestedChanges)
            || keyed.Added.Count != 0
            || keyed.RemovedKeys.Count != 0
            || keyed.Edited.Count != Size
            || keyed.HasOrder
            || keyed.Edited.Any(edit => !ValidScore(edit.NestedChanges))
            || !new HashSet<object?>(keyed.Edited.Select(edit => edit.Key)).SetEquals(keys)
            || dictionary.SetEntries.Count != 0
            || dictionary.RemovedKeys.Count != 0
            || dictionary.Edited.Count != Size
            || dictionary.Edited.Any(edit =>
                edit.HasNestedChanges || !Equals(edit.Value, 1) || edit.NestedChanges.Count != 0
            )
            || nestedDictionary.Edited.Count != Size
            || nestedDictionary.Edited.Any(edit =>
                !edit.HasNestedChanges || !ValidScore(edit.NestedChanges)
            )
            || !new HashSet<object?>(nestedDictionary.Edited.Select(edit => edit.Key)).SetEquals(
                keys
            )
            || scalar.Any(change =>
                !ReferenceEquals(
                    change.Property,
                    BenchInspectionRecord.Sparse.Properties.Single(property =>
                        property.Name == change.Property.Name
                    )
                )
            )
        )
        {
            throw new InvalidOperationException(
                "Inspection must preserve property identity, null values, scalar edits and nested collection edits."
            );
        }
        CheckSnapshots(scalar[0]);
        var baseline = Optional<BenchInspectionRecord.Fragment?>.Present(
            BenchInspectionRecord.Fragment.From(
                new BenchInspectionRecord { Items = keys.Select(key => Item(key, 0)).ToList() }
            )
        );
        var original = _keyed.ToJsonPatch(baseline);
        _ = KeyedEdits();
        if (!_keyed.ToJsonPatch(baseline).Span.SequenceEqual(original.Span))
        {
            throw new InvalidOperationException("Inspection must not mutate the patch.");
        }
    }

    private static bool ValidScore(IReadOnlyList<SparsePatchChange> changes) =>
        changes.Count == 1
        && changes[0].Property.Name == "Score"
        && changes[0].Kind == SparseChangeKind.Set
        && Equals(changes[0].Value, 1);

    private static void CheckSnapshots(SparsePatchChange change)
    {
        var source = new List<SparsePatchChange> { change };
        var edit = new SparseKeyedEdit("key", source);
        var dictionaryEdit = new SparseDictionaryEdit("key", null, source, true);
        var nested = new SparsePatchChange(
            change.Property,
            SparseChangeKind.Nested,
            null,
            source,
            null,
            null
        );
        source.Clear();
        if (
            edit.NestedChanges.Count != 1
            || dictionaryEdit.NestedChanges.Count != 1
            || nested.NestedChanges.Count != 1
            || ((IList<SparsePatchChange>)nested.NestedChanges).IsReadOnly != true
        )
        {
            throw new InvalidOperationException(
                "Inspection constructors must retain independent read-only snapshots."
            );
        }
        var empty = new SparsePatchChange(
            change.Property,
            SparseChangeKind.Unset,
            null,
            null,
            null,
            null
        );
        if (
            empty.NestedChanges.Count != 0
            || !((IList<SparsePatchChange>)empty.NestedChanges).IsReadOnly
        )
        {
            throw new InvalidOperationException("Empty snapshots must remain read-only.");
        }
    }

    private static BenchInspectionItem Item(string id, int score) =>
        new() { Id = id, Score = score };

    private static BenchInspectionRecord.Patch Between(
        BenchInspectionRecord before,
        BenchInspectionRecord after
    ) =>
        BenchInspectionRecord.Patch.Between(
            Optional<BenchInspectionRecord.Fragment?>.Present(
                BenchInspectionRecord.Fragment.From(before)
            ),
            Optional<BenchInspectionRecord.Fragment?>.Present(
                BenchInspectionRecord.Fragment.From(after)
            )
        );

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> Empty() => _empty.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> Scalar() => _scalar.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> Nested() => _nested.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> KeyedEdits() => _keyed.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> DictionaryEdits() => _dictionary.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> NestedDictionaryEdits() => _nestedDictionary.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> KeyedMixed() => _keyedMixed.Changes;

    [Benchmark]
    public IReadOnlyList<SparsePatchChange> DictionaryMixed() => _dictionaryMixed.Changes;
}
