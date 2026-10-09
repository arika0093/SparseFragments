using SparseFragments;

namespace SparseFragments.GeneratedApiFixtures;

// Stable approval fixtures for issue #117. Type and namespace names feed the
// deterministic extension-container hash, so renames intentionally move the baseline.
// Keep this set small: each model covers one representative generated surface.

// Scalar, nullable, and presence-aware surface.
[SparseFragmentModel]
public partial class CatalogScalar
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }

    public string? Note { get; set; }

    public int? Retry { get; set; }

    public ISet<string> Labels { get; set; } = new HashSet<string>();
}

// Nested structural projection (child, sequence, and nullable reference).
[SparseFragmentModel]
public partial class CatalogNested
{
    public CatalogScalar Child { get; set; } = new();

    [SparseMerge(MergeMode.Replace)]
    public CatalogPoco? Metadata { get; set; }

    [SparseMerge(MergeMode.Replace)]
    public List<CatalogPoco?> MetadataItems { get; set; } = new();

    // Whole-value sequence: no key, replaced as a unit.
    [SparseMerge(MergeMode.Replace)]
    public List<CatalogScalar> Children { get; set; } = new();

    public CatalogScalar? Maybe { get; set; }
}

public sealed class CatalogPoco
{
    public string Label { get; set; } = string.Empty;

    public CatalogPoco? Nested { get; set; }
}

// Keyed element; the roster below exposes typed per-key projections.
[SparseFragmentModel]
public partial class CatalogKeyedItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int Count { get; set; }
}

// Keyed sequence holder.
[SparseFragmentModel]
public partial class CatalogKeyedRoster
{
    public List<CatalogKeyedItem> Items { get; set; } = new();
}

// Dictionary transitions over scalar and structural values.
[SparseFragmentModel]
public partial class CatalogDictionaries
{
    public Dictionary<string, int> Scores { get; set; } = new();

    public Dictionary<string, CatalogScalar> Details { get; set; } = new();
}

// Get-only/init-only construction binding.
[SparseFragmentModel]
public partial class CatalogImmutable
{
    public CatalogImmutable(int count, string name = "base", int[]? tags = null)
    {
        Count = count;
        Name = name;
        Tags = tags;
    }

    public int Count { get; }

    public string Name { get; init; }

    public int[]? Tags { get; init; }
}
