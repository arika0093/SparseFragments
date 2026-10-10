using SparseFragments;

namespace SparseFragments.GeneratedApiFixtures;

// Stable approval fixtures for issue #117. Type and namespace names feed the
// deterministic extension-container hash, so renames intentionally move the baseline.
// Keep this set small: each model covers one representative generated surface.

// Scalar, nullable, and presence-aware surface.
/// <summary>Scalar, nullable, and presence-aware catalog fixture.</summary>
[SparseFragmentModel]
public partial class CatalogScalar
{
    /// <summary>Gets or sets the catalog name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the catalog count.</summary>
    public int Count { get; set; }

    /// <summary>Gets or sets the optional note.</summary>
    public string? Note { get; set; }

    /// <summary>Gets or sets the optional retry count.</summary>
    public int? Retry { get; set; }

    /// <summary>Gets or sets the label set.</summary>
    public ISet<string> Labels { get; set; } = new HashSet<string>();
}

// Nested structural projection (child, sequence, and nullable reference).
/// <summary>Nested structural projection fixture.</summary>
[SparseFragmentModel]
public partial class CatalogNested
{
    /// <summary>Gets or sets the child scalar model.</summary>
    public CatalogScalar Child { get; set; } = new();

    /// <summary>Gets or sets the replaced metadata value.</summary>
    [SparseMerge(MergeMode.Replace)]
    public CatalogPoco? Metadata { get; set; }

    /// <summary>Gets or sets the replaced metadata items.</summary>
    [SparseMerge(MergeMode.Replace)]
    public List<CatalogPoco?> MetadataItems { get; set; } = new();

    /// <summary>Gets or sets the replaced metadata by key.</summary>
    [SparseMerge(MergeMode.Replace)]
    public Dictionary<CatalogPoco, string> MetadataByKey { get; set; } = new();

    /// <summary>Gets or sets the whole-value child sequence.</summary>
    // Whole-value sequence: no key, replaced as a unit.
    [SparseMerge(MergeMode.Replace)]
    public List<CatalogScalar> Children { get; set; } = new();

    /// <summary>Gets or sets the optional nested scalar model.</summary>
    public CatalogScalar? Maybe { get; set; }
}

/// <summary>Plain catalog metadata value.</summary>
public sealed class CatalogPoco
{
    /// <summary>Gets or sets the metadata label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the nested metadata value.</summary>
    public CatalogPoco? Nested { get; set; }
}

// Keyed element; the roster below exposes typed per-key projections.
/// <summary>Keyed catalog element fixture.</summary>
[SparseFragmentModel]
public partial class CatalogKeyedItem
{
    /// <summary>Gets or sets the element key.</summary>
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the element title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the element count.</summary>
    public int Count { get; set; }
}

// Keyed sequence holder.
/// <summary>Keyed sequence holder fixture.</summary>
[SparseFragmentModel]
public partial class CatalogKeyedRoster
{
    /// <summary>Gets or sets the roster items.</summary>
    public List<CatalogKeyedItem> Items { get; set; } = new();
}

// Dictionary transitions over scalar and structural values.
/// <summary>Dictionary transition fixture.</summary>
[SparseFragmentModel]
public partial class CatalogDictionaries
{
    /// <summary>Gets or sets the scalar scores by key.</summary>
    public Dictionary<string, int> Scores { get; set; } = new();

    /// <summary>Gets or sets the structural details by key.</summary>
    public Dictionary<string, CatalogScalar> Details { get; set; } = new();
}

// Get-only/init-only construction binding.
/// <summary>Get-only and init-only construction fixture.</summary>
[SparseFragmentModel]
public partial class CatalogImmutable
{
    /// <summary>Initializes a new immutable catalog value.</summary>
    /// <param name="count">The get-only count.</param>
    /// <param name="name">The init-only name.</param>
    /// <param name="tags">The init-only tags.</param>
    public CatalogImmutable(int count, string name = "base", int[]? tags = null)
    {
        Count = count;
        Name = name;
        Tags = tags;
    }

    /// <summary>Gets the count.</summary>
    public int Count { get; }

    /// <summary>Gets the name.</summary>
    public string Name { get; init; }

    /// <summary>Gets the tags.</summary>
    public int[]? Tags { get; init; }
}

// Members colliding with ChangeSet enumeration helpers share one resolver.
/// <summary>Reserved-name collision fixture.</summary>
[SparseFragmentModel]
public partial class CatalogReservedEnumeration
{
    /// <summary>Gets or sets the value colliding with the changes enumerator.</summary>
    public string EnumerateChanges { get; set; } = string.Empty;

    /// <summary>Gets or sets the value colliding with the changed-paths enumerator.</summary>
    public string EnumerateChangedPaths { get; set; } = string.Empty;

    /// <summary>Gets or sets the value colliding with the change-info helper.</summary>
    public string ChangeInfo { get; set; } = string.Empty;

    /// <summary>Gets or sets the value colliding with the change-kind helper.</summary>
    public string ChangeKind { get; set; } = string.Empty;
}
