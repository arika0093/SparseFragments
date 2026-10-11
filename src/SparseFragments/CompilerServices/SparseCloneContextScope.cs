using System.ComponentModel;
using System.Threading;

namespace SparseFragments.CompilerServices;

/// <summary>Owns a reference-identity dictionary for one generated clone operation.</summary>
/// <remarks>The context must not be retained or accessed after the scope is disposed.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SparseCloneContextScope : IDisposable
{
    private Dictionary<object, object>? _context;

    internal SparseCloneContextScope(Dictionary<object, object> context) => _context = context;

    /// <summary>Gets the dictionary owned by this scope.</summary>
    /// <exception cref="ObjectDisposedException">The scope has been disposed.</exception>
    public Dictionary<object, object> Context =>
        _context ?? throw new ObjectDisposedException(nameof(SparseCloneContextScope));

    /// <summary>Clears references and returns a bounded context for reuse on the current thread.</summary>
    public void Dispose()
    {
        var context = Interlocked.Exchange(ref _context, null);
        if (context is not null)
            SparseCloneContextPool.Return(context);
    }
}
