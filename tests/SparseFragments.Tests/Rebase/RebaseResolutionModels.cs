using SparseFragments;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class ResOrder
{
    public string Number { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Note { get; set; }

    public int? Retry { get; set; }
}

[SparseFragmentModel]
public partial class ResCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class ResNestedRoot
{
    public string Label { get; set; } = string.Empty;

    public ResCustomer? Customer { get; set; }
}

[SparseFragmentModel]
public partial class ResItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

[SparseFragmentModel]
public partial class ResRoster
{
    public List<ResItem> Items { get; set; } = new();
}

[SparseFragmentModel]
public partial class ResDict
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseFragmentModel]
public partial class ResLabels
{
    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Labels { get; set; } = new HashSet<string>();
}

[SparseFragmentModel]
public partial class ResSecret
{
    public string DisplayName { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string? Password { get; set; }
}
