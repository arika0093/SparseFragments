using BenchmarkDotNet.Attributes;
using SparseTypes;

public sealed class BenchValidationModel
{
    public string Name { get; set; } = string.Empty;
}

// Validation-store budgets: single-path updates must not sort unrelated
// buckets, and bulk clears must not sort once per path.
[MemoryDiagnoser]
public class ValidationStoreBenchmarks
{
    [Params(1, 100, 1000, 10000)]
    public int PathCount { get; set; }

    [Params(1, 2)]
    public int OriginCount { get; set; }

    private SparseValidationStore<BenchValidationModel> _store = null!;
    private SparsePath _target = default;
    private List<SparseConflict> _conflicts = [];
    private int _notifications;

    [GlobalSetup]
    public void Setup()
    {
        _target = PathAt(0);
        _conflicts = Enumerable
            .Range(0, 100)
            .Select(index => new SparseConflict(
                PathAt(index),
                SparseConflictKind.Reconciliation,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                Optional<object?>.Missing,
                "conflict"
            ))
            .ToList();
        Refill();
    }

    [IterationSetup(
        Targets = new[]
        {
            nameof(SetErrors_Changed),
            nameof(SetErrors_Changed_Notified),
            nameof(ClearOrigin_Hit),
            nameof(Clear),
            nameof(Clear_Notified),
            nameof(DisposeStore),
            nameof(AddConflicts_Replace),
        }
    )]
    public void Refill() => _store = FilledStore();

    [Benchmark(Description = "Unchanged SetErrors (one path)")]
    public int SetErrors_Unchanged()
    {
        _store.SetErrors(_target, "origin-0", ["same"]);
        return _notifications;
    }

    [Benchmark(Description = "Changed SetErrors (one path)")]
    public int SetErrors_Changed()
    {
        _store.SetErrors(_target, "origin-0", ["changed-" + _notifications]);
        return _notifications;
    }

    [Benchmark(Description = "Changed SetErrors with subscriber")]
    public int SetErrors_Changed_Notified()
    {
        _store.ErrorsChanged += OnChanged;
        try
        {
            _store.SetErrors(_target, "origin-0", ["changed-" + _notifications]);
            return _notifications;
        }
        finally
        {
            _store.ErrorsChanged -= OnChanged;
        }
    }

    [Benchmark(Description = "ClearOrigin missing everywhere")]
    public int ClearOrigin_Missing()
    {
        _store.ClearOrigin("absent");
        return _notifications;
    }

    [Benchmark(Description = "ClearOrigin hit")]
    public int ClearOrigin_Hit()
    {
        _store.ClearOrigin("origin-0");
        return _notifications;
    }

    [Benchmark(Description = "Clear all")]
    public int Clear()
    {
        _store.Clear();
        return _notifications;
    }

    [Benchmark(Description = "Clear all with subscriber")]
    public int Clear_Notified()
    {
        _store.ErrorsChanged += OnChanged;
        try
        {
            _store.Clear();
            return _notifications;
        }
        finally
        {
            _store.ErrorsChanged -= OnChanged;
        }
    }

    [Benchmark(Description = "Dispose")]
    public int DisposeStore()
    {
        _store.Dispose();
        return _notifications;
    }

    [Benchmark(Description = "AddConflicts with replace-first")]
    public int AddConflicts_Replace()
    {
        return _store.AddConflicts(
            _conflicts,
            new SparseValidationConflictOptions { Origin = "origin-0", ReplaceOriginFirst = true }
        );
    }

    private void OnChanged(object? sender, System.ComponentModel.DataErrorsChangedEventArgs args) =>
        _notifications++;

    private SparsePath PathAt(int index) =>
        SparsePath.Root<BenchValidationModel>().Member("F" + index);

    private SparseValidationStore<BenchValidationModel> FilledStore()
    {
        var store = new SparseValidationStore<BenchValidationModel>();
        for (var index = 0; index < PathCount; index++)
        {
            for (var origin = 0; origin < OriginCount; origin++)
            {
                var message = index == 0 && origin == 0 ? "same" : "e-" + index + "-" + origin;
                store.SetErrors(PathAt(index), "origin-" + origin, [message]);
            }
        }

        return store;
    }
}
