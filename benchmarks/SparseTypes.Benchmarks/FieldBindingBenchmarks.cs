using BenchmarkDotNet.Attributes;
using Microsoft.AspNetCore.Components.Forms;
using SparseTypes;
using SparseTypes.Blazor;

[SparseModel]
public partial class BenchFieldStateItem2
{
    public string Name { get; set; } = string.Empty;
}

// Wraps a list behind IReadOnlyList only, so reads prove the read-only
// resolution branch instead of the IList one.
public sealed class BenchReadOnlyListStub<T>(IReadOnlyList<T> inner) : IReadOnlyList<T>
{
    public T this[int index] => inner[index];

    public int Count => inner.Count;

    public IEnumerator<T> GetEnumerator() => inner.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

// Wraps a dictionary behind IReadOnlyDictionary only, so reads prove the
// read-only resolution branch instead of the IDictionary one.
public sealed class BenchReadOnlyDictStub(IReadOnlyDictionary<string, int> inner)
    : IReadOnlyDictionary<string, int>
{
    public int this[string key] => inner[key];

    public IEnumerable<string> Keys => inner.Keys;

    public IEnumerable<int> Values => inner.Values;

    public int Count => inner.Count;

    public bool ContainsKey(string key) => inner.ContainsKey(key);

    public IEnumerator<KeyValuePair<string, int>> GetEnumerator() => inner.GetEnumerator();

    public bool TryGetValue(string key, out int value) => inner.TryGetValue(key, out value!);

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();
}

[SparseModel]
public partial class BenchReadOnlySheet
{
    public IReadOnlyList<BenchFieldStateItem> Lines { get; set; } = new List<BenchFieldStateItem>();

    public IReadOnlyDictionary<string, int> Map { get; set; } = new Dictionary<string, int>();
}

// Warmed repeated binding reads: stable reflection metadata and the member
// expression tree are reused instead of rediscovered per read.
[MemoryDiagnoser]
public class FieldBindingBenchmarks
{
    private BenchFieldStateRoot.EditSession _session = null!;
    private EditContext _context = null!;
    private SparseFieldBinding<BenchFieldStateRoot, string> _scalar = null!;
    private SparseFieldBinding<BenchFieldStateRoot, string> _nested = null!;
    private SparseFieldBinding<BenchFieldStateRoot, string> _keyed = null!;
    private SparseFieldBinding<BenchFieldStateRoot, string> _temporary = null!;
    private SparseFieldBinding<BenchFieldStateRoot, int> _dictionary = null!;
    private SparseFieldBinding<BenchReadOnlySheet, string> _readOnlyList = null!;
    private BenchReadOnlySheet.EditSession _readOnlySession = null!;

    [GlobalSetup]
    public void Setup()
    {
        var temporary = Guid.NewGuid();
        var model = new BenchFieldStateRoot
        {
            Title = "before",
            Count = 1,
            Nested = new BenchFieldStateNested { Host = "host", Port = 1 },
            Scores = new Dictionary<string, int> { ["a"] = 1 },
            Items = new List<BenchFieldStateItem>
            {
                new()
                {
                    Id = "k-1",
                    Name = "n-1",
                    Count = 1,
                },
            },
            Lines = new List<BenchFieldStateTempLine>
            {
                new() { Name = "pending", TemporaryId = temporary },
            },
        };
        _session = model.CreateEditSession();
        _context = _session.CreateEditContext();
        _scalar = _session.Bind(BenchFieldStateRoot.SparsePath.Title, _context);
        _nested = _session.Bind(BenchFieldStateRoot.SparsePath.Nested.Host, _context);
        _keyed = _session.Bind(BenchFieldStateRoot.SparsePath.Items.Key("k-1").Name, _context);
        _temporary = _session.Bind(
            BenchFieldStateRoot.SparsePath.Lines.TemporaryKey(temporary).Name,
            _context
        );
        _dictionary = _session.Bind(BenchFieldStateRoot.SparsePath.Scores.Key("a"), _context);

        var readOnlySession = new BenchReadOnlySheet
        {
            Lines = new BenchReadOnlyListStub<BenchFieldStateItem>(
                new List<BenchFieldStateItem>
                {
                    new()
                    {
                        Id = "k-1",
                        Name = "n-1",
                        Count = 1,
                    },
                }
            ),
            Map = new BenchReadOnlyDictStub(new Dictionary<string, int> { ["a"] = 1 }),
        }.CreateEditSession();
        var readOnlyContext = readOnlySession.CreateEditContext();
        _readOnlySession = readOnlySession;
        _readOnlyList = readOnlySession.Bind(
            BenchReadOnlySheet.SparsePath.Lines.At(0).Name,
            readOnlyContext
        );

        if (
            _readOnlyList.Field.FieldName != "Name"
            || _readOnlyList.Value != "n-1"
            || readOnlySession.Field("Lines[0].Name").FieldName != "Name"
            || readOnlySession.Field("Map[\"a\"]").FieldName != "Map"
        )
        {
            throw new InvalidOperationException("Read-only binding fixtures must resolve.");
        }

        if (
            _scalar.Field.FieldName != "Title"
            || _nested.Field.FieldName != "Host"
            || _keyed.Field.FieldName != "Name"
            || _temporary.Field.FieldName != "Name"
            || _dictionary.Field.FieldName != "Scores"
            || !ReferenceEquals(_scalar.ValueExpression, _scalar.ValueExpression)
            || !ReferenceEquals(_nested.ValueExpression, _nested.ValueExpression)
        )
        {
            throw new InvalidOperationException(
                "Binding fixtures must resolve and reuse member expression trees."
            );
        }
    }

    [Benchmark(Description = "Field scalar")]
    public string Field_Scalar() => _scalar.Field.FieldName;

    [Benchmark(Description = "Field nested")]
    public string Field_Nested() => _nested.Field.FieldName;

    [Benchmark(Description = "Field keyed")]
    public string Field_Keyed() => _keyed.Field.FieldName;

    [Benchmark(Description = "Field temporary key")]
    public string Field_Temporary() => _temporary.Field.FieldName;

    [Benchmark(Description = "Field dictionary entry")]
    public string Field_Dictionary() => _dictionary.Field.FieldName;

    [Benchmark(Description = "Field read-only list")]
    public string Field_ReadOnlyList() => _readOnlyList.Field.FieldName;

    [Benchmark(Description = "Resolve read-only list entry")]
    public string Resolve_ReadOnlyList() => _readOnlySession.Field("Lines[0].Name").FieldName;

    [Benchmark(Description = "Resolve read-only dictionary entry")]
    public string Resolve_ReadOnlyDictionary() => _readOnlySession.Field("Map[\"a\"]").FieldName;

    [Benchmark(Description = "Value scalar")]
    public string Value_Scalar() => _scalar.Value;

    [Benchmark(Description = "IsModified scalar")]
    public bool IsModified_Scalar() => _context.IsModified(_scalar.Field);

    [Benchmark(Description = "ValidationMessages scalar")]
    public int ValidationMessages_Scalar() => _context.GetValidationMessages(_scalar.Field).Count();

    [Benchmark(Description = "ValueExpression scalar (reused tree)")]
    public int ValueExpression_Scalar() => _scalar.ValueExpression is null ? 0 : 1;

    [Benchmark(Description = "ValueExpression nested (reused tree)")]
    public int ValueExpression_Nested() => _nested.ValueExpression is null ? 0 : 1;
}
