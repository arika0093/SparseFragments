namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes a single entry assigned by a dictionary contribution.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDictionaryEntry
{
    /// <summary>Initializes a new instance of the <see cref="SparseDictionaryEntry"/> class.</summary>
    /// <param name="key">The entry key (boxed; may be null).</param>
    /// <param name="value">The assigned value (boxed; may be null).</param>
    public SparseDictionaryEntry(object? key, object? value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>Gets the entry key (boxed; may be null).</summary>
    public object? Key { get; }

    /// <summary>Gets the assigned value (boxed; may be null).</summary>
    public object? Value { get; }
}
