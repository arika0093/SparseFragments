using System.Text;

namespace SparseTypes;

internal static class SparsePathTextBuilderPool
{
    private const int MaximumRetainedCapacity = 1024;

    [ThreadStatic]
    private static StringBuilder? _cached;

    internal static StringBuilder Rent()
    {
        var builder = _cached ?? new StringBuilder();
        _cached = null;
        return builder;
    }

    internal static void Return(StringBuilder builder)
    {
        if (builder.Capacity > MaximumRetainedCapacity)
            return;
        builder.Clear();
        _cached ??= builder;
    }
}
