namespace SparseFragments.CompilerServices;

internal static class SparseCloneContextPool
{
    private const int MaximumRetainedEntries = 8192;

    [ThreadStatic]
    private static Dictionary<object, object>? _cachedContext;

    internal static SparseCloneContextScope Rent()
    {
        var context = _cachedContext ?? SparseFragmentRuntime.CreateCloneContext();
        _cachedContext = null;
        return new SparseCloneContextScope(context);
    }

    internal static void Return(Dictionary<object, object> context)
    {
        if (context.Count > MaximumRetainedEntries)
            return;
        context.Clear();
        _cachedContext ??= context;
    }
}
