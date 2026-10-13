using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchPathLeaf
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

[SparseModel]
public partial class BenchPathMid
{
    public string Name { get; set; } = string.Empty;

    public BenchPathLeaf? Detail { get; set; }
}

[SparseModel]
public partial class BenchPathItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseModel]
public partial class BenchPathGroup
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public List<BenchPathItem> Members { get; set; } = new();
}

[SparseModel]
public partial class BenchPathCluster
{
    public List<BenchPathGroup> Groups { get; set; } = new();
}

[SparseModel]
public partial class BenchPathSingle
{
    public string Title { get; set; } = string.Empty;
}

[SparseModel]
public partial class BenchPathWide
{
    public int Value00 { get; set; }

    public int Value01 { get; set; }

    public int Value02 { get; set; }

    public int Value03 { get; set; }

    public int Value04 { get; set; }

    public int Value05 { get; set; }

    public int Value06 { get; set; }

    public int Value07 { get; set; }

    public int Value08 { get; set; }

    public int Value09 { get; set; }

    public int Value10 { get; set; }

    public int Value11 { get; set; }

    public int Value12 { get; set; }

    public int Value13 { get; set; }

    public int Value14 { get; set; }

    public int Value15 { get; set; }
}

[SparseModel]
public partial class BenchPathRoot
{
    public string Label { get; set; } = string.Empty;

    public BenchPathMid? Nested { get; set; }

    public Dictionary<string, int> Scores { get; set; } = new();

    public List<BenchPathItem> Items { get; set; } = new();

    public Dictionary<string, BenchPathItem> Servers { get; set; } = new();
}

[SparseModel]
public partial class BenchPathComposite
{
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, string Id) Key => (TenantId, Id);
}

[SparseModel]
public partial class BenchPathCompositeHolder
{
    public List<BenchPathComposite> Items { get; set; } = new();
}

// Typed SparsePath traversal and lookup budgets (issue #222).
// Uses only the public generated typed path API plus the generic
// consumer interfaces (SparsePath, IDescriptorSet, ChangeSet.Find,
// conflicts Find) so the numbers describe the packed surface.
// The baseline workflow selects its 49 cases explicitly; this matrix
// stays available through --filter for focused investigation.
[MemoryDiagnoser]
public class SparsePathTraversalBenchmarks
{
    private const int UiRepeats = 100;
    private const string EscapedKey = "a\nb\"c\\d";
    private const string KeyedId = "k-1";
    private const string GroupId = "g-0";

    private SparsePath<BenchPathSingle, string> _scalar;
    private string _scalarText = string.Empty;
    private SparsePath<BenchPathRoot, string> _nested2;
    private SparsePath<BenchPathRoot, string> _nested3;
    private SparsePath<BenchPathRoot, string> _keyed;
    private SparsePath<BenchPathRoot, string> _indexed;
    private SparsePath<BenchPathRoot, int> _dictScalar;
    private SparsePath<BenchPathRoot, string> _dictStruct;
    private SparsePath<BenchPathCluster, string> _deep5a;
    private SparsePath<BenchPathCluster, string> _deep5b;
    private SparsePath<BenchPathCluster, string> _deep5Miss;
    private string _deep5Text = string.Empty;
    private SparsePath<BenchPathRoot, int> _escaped;
    private string _escapedText = string.Empty;
    private SparsePath<BenchPathCompositeHolder, string> _composite;
    private SparsePath _scalarUntyped;
    private SparsePath _wideMissing;
    private SparsePath _conflictMissing;

    private BenchPathSingle.ChangeSet _sparseChange = null!;
    private BenchPathWide.ChangeSet _wideChange = null!;
    private SparsePath<BenchPathWide, int> _wideLast;
    private BenchPathRoot.ChangeSet _keyedOne = null!;
    private BenchPathRoot.ChangeSet _keyedMany = null!;
    private BenchPathRoot.ChangeSet _escapedChange = null!;
    private BenchPathCluster.ChangeSet _clusterChange = null!;
    private BenchPathCompositeHolder.ChangeSet _compositeChange = null!;

    private SparseTypes.Generated.IDescriptorSet _descriptors = null!;
    private IReadOnlyList<SparseConflict> _conflicts = null!;

    [GlobalSetup]
    public void Setup()
    {
        _scalar = BenchPathSingle.SparsePath.Title;
        _scalarText = _scalar.ToString();
        _scalarUntyped = _scalar;
        _nested2 = BenchPathRoot.SparsePath.Nested.Name;
        _nested3 = BenchPathRoot.SparsePath.Nested.Detail.Host;
        _keyed = BenchPathRoot.SparsePath.Items.Key(KeyedId).Name;
        _indexed = BenchPathRoot.SparsePath.Items.At(0).Name;
        _dictScalar = BenchPathRoot.SparsePath.Scores.Key(KeyedId);
        _dictStruct = BenchPathRoot.SparsePath.Servers.Key(KeyedId).Name;
        _deep5a = BenchPathCluster.SparsePath.Groups.Key(GroupId).Members.Key(KeyedId).Name;
        _deep5b = BenchPathCluster.SparsePath.Groups.Key(GroupId).Members.Key(KeyedId).Name;
        _deep5Miss = BenchPathCluster.SparsePath.Groups.Key("missing").Members.Key(KeyedId).Name;
        _deep5Text = _deep5a.ToString();
        _escaped = BenchPathRoot.SparsePath.Scores.Key(EscapedKey);
        _escapedText = _escaped.ToString();
        _composite = BenchPathCompositeHolder.SparsePath.Items.Key(("t-0", KeyedId)).Name;
        _wideLast = BenchPathWide.SparsePath.Value15;
        _wideMissing = SparsePath.Root<BenchPathWide>().Member("Missing");
        _conflictMissing = SparsePath.Root<BenchPathSingle>().Member("Missing");

        var sparseBefore = Optional<BenchPathSingle.Fragment?>.Present(
            BenchPathSingle.Fragment.From(new BenchPathSingle { Title = "before" })
        );
        var sparseAfter = Optional<BenchPathSingle.Fragment?>.Present(
            BenchPathSingle.Fragment.From(new BenchPathSingle { Title = "after" })
        );
        _sparseChange = BenchPathSingle.ChangeSet.Between(sparseBefore, sparseAfter);

        var wideBefore = Optional<BenchPathWide.Fragment?>.Present(
            BenchPathWide.Fragment.From(new BenchPathWide())
        );
        var wideModel = new BenchPathWide
        {
            Value00 = 1,
            Value01 = 1,
            Value02 = 1,
            Value03 = 1,
            Value04 = 1,
            Value05 = 1,
            Value06 = 1,
            Value07 = 1,
            Value08 = 1,
            Value09 = 1,
            Value10 = 1,
            Value11 = 1,
            Value12 = 1,
            Value13 = 1,
            Value14 = 1,
            Value15 = 1,
        };
        var wideAfter = Optional<BenchPathWide.Fragment?>.Present(
            BenchPathWide.Fragment.From(wideModel)
        );
        _wideChange = BenchPathWide.ChangeSet.Between(wideBefore, wideAfter);

        BenchPathRoot RootModel()
        {
            var items = new List<BenchPathItem>();
            var servers = new Dictionary<string, BenchPathItem>();
            for (var index = 0; index < 64; index++)
            {
                items.Add(
                    new BenchPathItem
                    {
                        Id = "k-" + index,
                        Name = "n-" + index,
                        Count = index,
                    }
                );
                servers["k-" + index] = new BenchPathItem
                {
                    Id = "k-" + index,
                    Name = "s-" + index,
                    Count = index,
                };
            }

            return new BenchPathRoot
            {
                Label = "label",
                Nested = new BenchPathMid
                {
                    Name = "mid",
                    Detail = new BenchPathLeaf { Host = "host", Port = 1 },
                },
                Scores = new Dictionary<string, int> { [KeyedId] = 1, [EscapedKey] = 2 },
                Items = items,
                Servers = servers,
            };
        }

        Optional<BenchPathRoot.Fragment?> RootState(BenchPathRoot model) =>
            Optional<BenchPathRoot.Fragment?>.Present(BenchPathRoot.Fragment.From(model));

        var rootBase = RootModel();
        var rootLabel = RootModel();
        rootLabel.Label = "changed";
        _keyedOne = BenchPathRoot.ChangeSet.Between(RootState(rootBase), RootState(rootLabel));

        var rootKeyedBase = RootModel();
        var rootKeyedEdited = RootModel();
        rootKeyedEdited.Items[1].Name = "edited";
        _keyedOne = BenchPathRoot.ChangeSet.Between(
            RootState(rootKeyedBase),
            RootState(rootKeyedEdited)
        );

        var rootMany = RootModel();
        for (var index = 0; index < 16; index++)
        {
            rootMany.Items[index].Name = "edited-" + index;
        }

        _keyedMany = BenchPathRoot.ChangeSet.Between(RootState(rootBase), RootState(rootMany));

        var rootEscapedBase = RootModel();
        var rootEscapedEdited = RootModel();
        rootEscapedEdited.Scores[EscapedKey] = 99;
        _escapedChange = BenchPathRoot.ChangeSet.Between(
            RootState(rootEscapedBase),
            RootState(rootEscapedEdited)
        );

        Optional<BenchPathCluster.Fragment?> ClusterState(string name)
        {
            return Optional<BenchPathCluster.Fragment?>.Present(
                BenchPathCluster.Fragment.From(
                    new BenchPathCluster
                    {
                        Groups = new List<BenchPathGroup>
                        {
                            new()
                            {
                                Name = GroupId,
                                Note = "note",
                                Members = new List<BenchPathItem>
                                {
                                    new()
                                    {
                                        Id = KeyedId,
                                        Name = name,
                                        Count = 1,
                                    },
                                },
                            },
                        },
                    }
                )
            );
        }

        _clusterChange = BenchPathCluster.ChangeSet.Between(
            ClusterState("before"),
            ClusterState("after")
        );

        Optional<BenchPathCompositeHolder.Fragment?> CompositeState(string name)
        {
            return Optional<BenchPathCompositeHolder.Fragment?>.Present(
                BenchPathCompositeHolder.Fragment.From(
                    new BenchPathCompositeHolder
                    {
                        Items = new List<BenchPathComposite>
                        {
                            new()
                            {
                                TenantId = "t-0",
                                Id = KeyedId,
                                Name = name,
                            },
                        },
                    }
                )
            );
        }

        _compositeChange = BenchPathCompositeHolder.ChangeSet.Between(
            CompositeState("before"),
            CompositeState("after")
        );

        _descriptors = RootModel().CreateEditSession().Descriptors;

        var conflictBase = Optional<BenchPathSingle.Fragment?>.Present(
            BenchPathSingle.Fragment.From(new BenchPathSingle { Title = "a" })
        );
        var conflictLocal = Optional<BenchPathSingle.Fragment?>.Present(
            BenchPathSingle.Fragment.From(new BenchPathSingle { Title = "b" })
        );
        var conflictRemote = Optional<BenchPathSingle.Fragment?>.Present(
            BenchPathSingle.Fragment.From(new BenchPathSingle { Title = "c" })
        );
        var rebased = BenchPathSingle
            .ChangeSet.Between(conflictBase, conflictLocal)
            .RebaseOnto(conflictRemote);
        _conflicts = rebased.Conflicts;
        if (
            _sparseChange.Find(_scalar) is null
            || _sparseChange.Find(_scalarUntyped) is null
            || _wideChange.Find(_wideLast) is null
            || _wideChange.Find(_wideMissing) is not null
            || _keyedOne.Find(_keyed) is null
            || _keyedMany.Find(_keyed) is null
            || _escapedChange.Find(_escaped) is null
            || _clusterChange.Find(_deep5a) is null
            || _compositeChange.Find(_composite) is null
            || _descriptors.Find(_nested3) is null
            || _descriptors.Find(_keyed) is null
            || _descriptors.Find(_dictStruct) is null
            || _conflicts.Find(_scalar) is null
            || _conflicts.Find(BenchPathSingle.SparsePath.Title) is null
            || _conflicts.Find(_conflictMissing) is not null
            || SparsePath.Parse<BenchPathSingle>(_scalarText) != _scalarUntyped
            || SparsePath.Parse<BenchPathCluster>(_deep5Text) != (SparsePath)_deep5a
            || !_deep5a.Equals(_deep5b)
            || _deep5a.Equals(_deep5Miss)
            || _deep5a.GetHashCode() != _deep5b.GetHashCode()
        )
        {
            throw new InvalidOperationException(
                "Typed path fixtures must resolve through the public lookup APIs."
            );
        }
    }

    [Benchmark]
    public int Segments_Indexed()
    {
        var checksum = 0;
        for (var index = 0; index < _nested3.Path.Segments.Count; index++)
        {
            checksum += (int)_nested3.Path.Segments[index].Kind;
        }
        return checksum;
    }

    [Benchmark]
    public int Segments_Foreach()
    {
        var checksum = 0;
        foreach (var segment in _nested3.Path.Segments)
        {
            checksum += (int)segment.Kind;
        }
        return checksum;
    }

    [Benchmark(Description = "Construct root path")]
    public SparsePath Construct_Root() => SparsePath.Root<BenchPathSingle>();

    [Benchmark(Description = "Construct scalar path (1 segment)")]
    public SparsePath<BenchPathSingle, string> Construct_Scalar() =>
        BenchPathSingle.SparsePath.Title;

    [Benchmark(Description = "Construct nested path (2 segments)")]
    public SparsePath<BenchPathRoot, string> Construct_Nested2() =>
        BenchPathRoot.SparsePath.Nested.Name;

    [Benchmark(Description = "Construct nested path (3 segments)")]
    public SparsePath<BenchPathRoot, string> Construct_Nested3() =>
        BenchPathRoot.SparsePath.Nested.Detail.Host;

    [Benchmark(Description = "Construct keyed path (3 segments)")]
    public SparsePath<BenchPathRoot, string> Construct_Keyed3() =>
        BenchPathRoot.SparsePath.Items.Key(KeyedId).Name;

    [Benchmark(Description = "Construct indexed path (3 segments)")]
    public SparsePath<BenchPathRoot, string> Construct_Indexed3() =>
        BenchPathRoot.SparsePath.Items.At(0).Name;

    [Benchmark(Description = "Construct scalar-dict path (2 segments)")]
    public SparsePath<BenchPathRoot, int> Construct_DictScalar2() =>
        BenchPathRoot.SparsePath.Scores.Key(KeyedId);

    [Benchmark(Description = "Construct struct-dict path (3 segments)")]
    public SparsePath<BenchPathRoot, string> Construct_DictStruct3() =>
        BenchPathRoot.SparsePath.Servers.Key(KeyedId).Name;

    [Benchmark(Description = "Construct deep keyed path (5 segments)")]
    public SparsePath<BenchPathCluster, string> Construct_Deep5() =>
        BenchPathCluster.SparsePath.Groups.Key(GroupId).Members.Key(KeyedId).Name;

    [Benchmark(Description = "Construct escaped-key path")]
    public SparsePath<BenchPathRoot, int> Construct_EscapedKey() =>
        BenchPathRoot.SparsePath.Scores.Key(EscapedKey);

    [Benchmark(Description = "Construct composite-key path")]
    public SparsePath<BenchPathCompositeHolder, string> Construct_CompositeKey() =>
        BenchPathCompositeHolder.SparsePath.Items.Key(("t-0", KeyedId)).Name;

    [Benchmark(Description = "Equals equal deep paths")]
    public bool Equals_Same() => _deep5a.Equals(_deep5b);

    [Benchmark(Description = "Equals different deep paths")]
    public bool Equals_Different() => _deep5a.Equals(_deep5Miss);

    [Benchmark(Description = "GetHashCode deep path")]
    public int GetHashCode_Deep5() => _deep5a.GetHashCode();

    [Benchmark(Description = "ToString scalar path")]
    public string ToString_Scalar() => _scalar.ToString();

    [Benchmark(Description = "ToString deep path (5 segments)")]
    public string ToString_Deep5() => _deep5a.ToString();

    [Benchmark(Description = "ToString escaped-key path")]
    public string ToString_Escaped() => _escaped.ToString();

    [Benchmark(Description = "Parse scalar path")]
    public SparsePath Parse_Scalar() => SparsePath.Parse<BenchPathSingle>(_scalarText);

    [Benchmark(Description = "Parse deep path (5 segments)")]
    public SparsePath Parse_Deep5() => SparsePath.Parse<BenchPathCluster>(_deep5Text);

    [Benchmark(Description = "Parse escaped-key path")]
    public SparsePath Parse_Escaped() => SparsePath.Parse<BenchPathRoot>(_escapedText);

    [Benchmark(Description = "ChangeSet Find sparse 1-prop hit (typed, cached)")]
    public BenchPathSingle.ChangeSet.ChangeInfo? ChangeSet_Find_Sparse_Hit_Cached() =>
        _sparseChange.Find(_scalar);

    [Benchmark(Description = "ChangeSet Find sparse 1-prop hit (typed, cold)")]
    public BenchPathSingle.ChangeSet.ChangeInfo? ChangeSet_Find_Sparse_Hit_Cold() =>
        _sparseChange.Find(BenchPathSingle.SparsePath.Title);

    [Benchmark(Description = "ChangeSet Find sparse 1-prop hit (parsed baseline)")]
    public BenchPathSingle.ChangeSet.ChangeInfo? ChangeSet_Find_Sparse_Hit_Parsed() =>
        _sparseChange.Find(SparsePath.Parse<BenchPathSingle>(_scalarText));

    [Benchmark(Description = "ChangeSet Find sparse 1-prop hit (untyped, cached)")]
    public BenchPathSingle.ChangeSet.ChangeInfo? ChangeSet_Find_Sparse_Untyped_Cached() =>
        _sparseChange.Find(_scalarUntyped);

    [Benchmark(Description = "ChangeSet Find wide 16-prop hit (typed, cached)")]
    public BenchPathWide.ChangeSet.ChangeInfo? ChangeSet_Find_Wide_Hit_Cached() =>
        _wideChange.Find(_wideLast);

    [Benchmark(Description = "ChangeSet Find wide 16-prop miss (cached)")]
    public BenchPathWide.ChangeSet.ChangeInfo? ChangeSet_Find_Wide_Miss_Cached() =>
        _wideChange.Find(_wideMissing);

    [Benchmark(Description = "ChangeSet Find keyed 1-change hit")]
    public BenchPathRoot.ChangeSet.ChangeInfo? ChangeSet_Find_Keyed_One_Hit() =>
        _keyedOne.Find(_keyed);

    [Benchmark(Description = "ChangeSet Find keyed 16-change hit")]
    public BenchPathRoot.ChangeSet.ChangeInfo? ChangeSet_Find_Keyed_Many_Hit() =>
        _keyedMany.Find(_keyed);

    [Benchmark(Description = "ChangeSet Find deep 5-segment hit")]
    public BenchPathCluster.ChangeSet.ChangeInfo? ChangeSet_Find_Deep5_Hit() =>
        _clusterChange.Find(_deep5a);

    [Benchmark(Description = "ChangeSet Find escaped-key hit")]
    public BenchPathRoot.ChangeSet.ChangeInfo? ChangeSet_Find_Escaped_Hit() =>
        _escapedChange.Find(_escaped);

    [Benchmark(Description = "ChangeSet Find composite-key hit")]
    public BenchPathCompositeHolder.ChangeSet.ChangeInfo? ChangeSet_Find_Composite_Hit() =>
        _compositeChange.Find(_composite);

    [Benchmark(Description = "Descriptor Find nested (cached)")]
    public SparseTypes.Generated.IDescriptor? Descriptor_Find_Nested_Cached() =>
        _descriptors.Find(_nested3);

    [Benchmark(Description = "Descriptor Find nested (cold)")]
    public SparseTypes.Generated.IDescriptor? Descriptor_Find_Nested_Cold() =>
        _descriptors.Find(BenchPathRoot.SparsePath.Nested.Detail.Host);

    [Benchmark(Description = "Descriptor Find keyed (cached)")]
    public SparseTypes.Generated.IDescriptor? Descriptor_Find_Keyed_Cached() =>
        _descriptors.Find(_keyed);

    [Benchmark(Description = "Descriptor Find struct-dict (cached)")]
    public SparseTypes.Generated.IDescriptor? Descriptor_Find_DictStruct_Cached() =>
        _descriptors.Find(_dictStruct);

    [Benchmark(Description = "Conflicts Find hit (cached)")]
    public SparseConflict? Conflicts_Find_Hit_Cached() => _conflicts.Find(_scalar);

    [Benchmark(Description = "Conflicts Find miss (cached)")]
    public SparseConflict? Conflicts_Find_Miss_Cached() => _conflicts.Find(_conflictMissing);

    [Benchmark(Description = "Conflicts Find miss (cold)")]
    public SparseConflict? Conflicts_Find_Miss_Cold() =>
        _conflicts.Find(SparsePath.Root<BenchPathSingle>().Member("Missing"));

    [Benchmark(Description = "UI 100 repeated ChangeSet Finds (cached)")]
    public int Ui_Repeat100_Cached()
    {
        var count = 0;
        for (var index = 0; index < UiRepeats; index++)
        {
            if (_sparseChange.Find(_scalar) is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark(Description = "UI 100 repeated ChangeSet Finds (parsed baseline)")]
    public int Ui_Repeat100_Parsed()
    {
        var count = 0;
        for (var index = 0; index < UiRepeats; index++)
        {
            if (_sparseChange.Find(SparsePath.Parse<BenchPathSingle>(_scalarText)) is not null)
            {
                count++;
            }
        }

        return count;
    }
}
