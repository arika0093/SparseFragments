using SparseTypes;

namespace SparseTypes.Tests;

[SparseModel]
public partial class ResOrder
{
    public string Number { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Note { get; set; }

    public int? Retry { get; set; }
}

[SparseModel]
public partial class ResCustomer
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class ResNestedRoot
{
    public string Label { get; set; } = string.Empty;

    public ResCustomer? Customer { get; set; }
}

[SparseModel]
public partial class ResItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public decimal Price { get; set; }
}

[SparseModel]
public partial class ResRoster
{
    public List<ResItem> Items { get; set; } = new();
}

[SparseModel]
public partial class ResDict
{
    public Dictionary<string, int> Scores { get; set; } = new();
}

[SparseModel]
public partial class ResLabels
{
    [SparseMerge(MergeMode.SetUnion)]
    public ISet<string> Labels { get; set; } = new HashSet<string>();
}

[SparseModel]
public partial class ResSecret
{
    public string DisplayName { get; set; } = string.Empty;

    [SparseRedactBefore]
    public string? Password { get; set; }
}
