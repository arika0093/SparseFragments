namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes how a sparse patch contribution changes a dictionary property.
/// Instances are produced by generated patch inspection code; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: patch inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDictionaryInspection
{
    /// <summary>Initializes a new instance of the <see cref="SparseDictionaryInspection"/> class.</summary>
    /// <param name="setEntries">The entries assigned by the contribution.</param>
    /// <param name="removedKeys">The removed entry keys (boxed).</param>
    /// <param name="edited">The edited entries with their values or nested changes.</param>
    public SparseDictionaryInspection(
        IReadOnlyList<SparseDictionaryEntry> setEntries,
        IReadOnlyList<object?> removedKeys,
        IReadOnlyList<SparseDictionaryEdit> edited
    )
    {
        ArgumentNullException.ThrowIfNull(setEntries);
        ArgumentNullException.ThrowIfNull(removedKeys);
        ArgumentNullException.ThrowIfNull(edited);

        SetEntries = Array.AsReadOnly(setEntries.ToArray());
        RemovedKeys = Array.AsReadOnly(removedKeys.ToArray());
        Edited = Array.AsReadOnly(edited.ToArray());
    }

    /// <summary>Gets the entries assigned by the contribution.</summary>
    public IReadOnlyList<SparseDictionaryEntry> SetEntries { get; }

    /// <summary>Gets the removed entry keys (boxed).</summary>
    public IReadOnlyList<object?> RemovedKeys { get; }

    /// <summary>Gets the edited entries with their values or nested changes.</summary>
    public IReadOnlyList<SparseDictionaryEdit> Edited { get; }
}
