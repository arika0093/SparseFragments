namespace SparseFragments.NativeAotFixtures;

[SparseFragmentModel]
public partial class PayloadChild
{
    public string? Name { get; set; }

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class PayloadRoot
{
    public string? Label { get; set; }

    public PayloadChild? Child { get; set; }
}

[SparseFragmentModel]
public partial class PayloadItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class PayloadCollection
{
    public List<PayloadItem> Items { get; set; } = [];
}

[SparseFragmentModel]
public partial class PayloadSecret
{
    public string? Label { get; set; }

    [SparseRedactBefore]
    public string? Token { get; set; }
}
