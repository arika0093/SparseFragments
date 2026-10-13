using System.ComponentModel;

namespace SparseTypes;

/// <summary>One stored validation error with its typed path and origin.</summary>
/// <remarks>
/// Snapshot plain data returned by
/// <see cref="SparseValidationStore{TModel}.GetAllErrors"/>; mutating the
/// store never mutates previously returned entries.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public readonly record struct SparseValidationError
{
    /// <summary>Creates an empty entry; prefer object-initializer construction.</summary>
    public SparseValidationError()
    {
        Path = default;
        Origin = string.Empty;
        Error = null;
    }

    /// <summary>Gets the typed path the error was recorded at.</summary>
    public SparsePath Path { get; init; }

    /// <summary>Gets the wire-compatible text of <see cref="Path"/>.</summary>
    public string PathText => Path.ToString();

    /// <summary>Gets the origin that recorded the error.</summary>
    public string Origin { get; init; } = string.Empty;

    /// <summary>Gets the recorded error message or object.</summary>
    public object? Error { get; init; }
}
