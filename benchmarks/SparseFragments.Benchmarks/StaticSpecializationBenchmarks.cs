using BenchmarkDotNet.Attributes;
using SparseFragments;
using SparseFragments.CompilerServices;

[SparseFragmentModel]
public partial class BenchStaticCollections
{
    public List<int> Numbers { get; set; } = [];

    public HashSet<string> Unique { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> Scores { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Static specialization benchmarks (issue #187): statically bound concrete collection
/// overloads versus the dynamic object/interface fallbacks, including custom comparers,
/// nested collections, and small/large sizes. Allocations are measured: the static paths
/// avoid shape classification, comparer/count reflection, and enumerator boxing.
/// </summary>
[MemoryDiagnoser]
public class StaticSpecializationBenchmarks
{
    [Params(16, 2048)]
    public int Size { get; set; }

    private List<int> _listLeft = null!;
    private List<int> _listRight = null!;
    private int[] _arrayLeft = null!;
    private int[] _arrayRight = null!;
    private HashSet<string> _setLeft = null!;
    private HashSet<string> _setRight = null!;
    private Dictionary<string, int> _dictLeft = null!;
    private Dictionary<string, int> _dictRight = null!;
    private Dictionary<string, List<int>> _nestedLeft = null!;
    private Dictionary<string, List<int>> _nestedRight = null!;
    private BenchStaticCollections.Fragment _fragmentLeft = null!;
    private BenchStaticCollections.Fragment _fragmentRight = null!;

    [GlobalSetup]
    public void Setup()
    {
        var numbers = Enumerable.Range(0, Size).ToList();
        _listLeft = numbers;
        _listRight = new List<int>(numbers);
        _arrayLeft = numbers.ToArray();
        _arrayRight = numbers.ToArray();

        var names = Enumerable.Range(0, Size).Select(index => "item-" + index).ToList();
        _setLeft = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        _setRight = new HashSet<string>(
            names.Select(name => name.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase
        );

        _dictLeft = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        _dictRight = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Size; index++)
        {
            _dictLeft["key-" + index] = index;
            _dictRight["KEY-" + index] = index;
        }

        _nestedLeft = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        _nestedRight = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < Size; index++)
        {
            _nestedLeft["key-" + index] = [index, index + 1];
            _nestedRight["KEY-" + index] = [index, index + 1];
        }

        _fragmentLeft = new BenchStaticCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present(new List<int>(numbers)),
            Unique = Optional<HashSet<string>>.Present(
                new HashSet<string>(_setLeft, StringComparer.OrdinalIgnoreCase)
            ),
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(_dictLeft, StringComparer.OrdinalIgnoreCase)
            ),
        };
        _fragmentRight = new BenchStaticCollections.Fragment
        {
            Numbers = Optional<List<int>>.Present(new List<int>(numbers)),
            Unique = Optional<HashSet<string>>.Present(
                new HashSet<string>(_setRight, StringComparer.OrdinalIgnoreCase)
            ),
            Scores = Optional<Dictionary<string, int>>.Present(
                new Dictionary<string, int>(_dictRight, StringComparer.OrdinalIgnoreCase)
            ),
        };

        if (
            !SparseFragmentRuntime.AreSequenceEqual(_listLeft, _listRight)
            || !SparseFragmentRuntime.AreSequenceEqual(_arrayLeft, _arrayRight)
            || !SparseFragmentRuntime.AreSetEqual(_setLeft, _setRight)
            || !SparseFragmentRuntime.AreDictionaryEqual(_dictLeft, _dictRight)
            || !SparseFragmentRuntime.AreDictionaryEqual(
                _nestedLeft,
                _nestedRight,
                static (left, right) => SparseFragmentRuntime.AreSequenceEqual(left, right)
            )
        )
        {
            throw new InvalidOperationException(
                "Static specialization fixtures must compare equal."
            );
        }
    }

    [Benchmark(Description = "Sequence: static List overload")]
    public bool Sequence_StaticList() =>
        SparseFragmentRuntime.AreSequenceEqual(_listLeft, _listRight);

    [Benchmark(Description = "Sequence: object fallback")]
    public bool Sequence_ObjectFallback() =>
        SparseFragmentRuntime.AreEqual((object)_listLeft, (object)_listRight);

    [Benchmark(Description = "Sequence: interface fallback")]
    public bool Sequence_InterfaceFallback() =>
        SparseFragmentRuntime.AreSequenceEqual(
            (IEnumerable<int>)_listLeft,
            (IEnumerable<int>)_listRight
        );

    [Benchmark(Description = "Sequence: static array overload")]
    public bool Sequence_StaticArray() =>
        SparseFragmentRuntime.AreSequenceEqual(_arrayLeft, _arrayRight);

    [Benchmark(Description = "Sequence: array object fallback")]
    public bool Sequence_ArrayObjectFallback() =>
        SparseFragmentRuntime.AreEqual((object)_arrayLeft, (object)_arrayRight);

    [Benchmark(Description = "Set: static HashSet overload, custom comparer")]
    public bool Set_StaticCustomComparer() =>
        SparseFragmentRuntime.AreSetEqual(_setLeft, _setRight);

    [Benchmark(Description = "Set: object fallback, custom comparer")]
    public bool Set_ObjectFallbackCustomComparer() =>
        SparseFragmentRuntime.AreEqual((object)_setLeft, (object)_setRight);

    [Benchmark(Description = "Set: interface fallback, custom comparer")]
    public bool Set_InterfaceFallbackCustomComparer() =>
        SparseFragmentRuntime.AreSetEqual(
            (IEnumerable<string>)_setLeft,
            (IEnumerable<string>)_setRight
        );

    [Benchmark(Description = "Dictionary: static overload, custom comparer")]
    public bool Dictionary_StaticCustomComparer() =>
        SparseFragmentRuntime.AreDictionaryEqual(_dictLeft, _dictRight);

    [Benchmark(Description = "Dictionary: object fallback, custom comparer")]
    public bool Dictionary_ObjectFallbackCustomComparer() =>
        SparseFragmentRuntime.AreEqual((object)_dictLeft, (object)_dictRight);

    [Benchmark(Description = "Dictionary: static overload with nested value comparer")]
    public bool Dictionary_StaticNestedValues() =>
        SparseFragmentRuntime.AreDictionaryEqual(
            _nestedLeft,
            _nestedRight,
            static (left, right) => SparseFragmentRuntime.AreSequenceEqual(left, right)
        );

    [Benchmark(Description = "Dictionary: object fallback with nested values")]
    public bool Dictionary_ObjectFallbackNestedValues() =>
        SparseFragmentRuntime.AreEqual((object)_nestedLeft, (object)_nestedRight);

    [Benchmark(Description = "Fragment equality over concrete collections")]
    public bool Fragment_ConcreteCollections() =>
        BenchStaticCollections.Fragment.__SparseAreEqual(
            Optional<BenchStaticCollections.Fragment?>.Present(_fragmentLeft),
            Optional<BenchStaticCollections.Fragment?>.Present(_fragmentRight)
        );
}
