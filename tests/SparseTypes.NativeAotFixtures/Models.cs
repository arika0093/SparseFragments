namespace SparseTypes.NativeAotFixtures;

[SparseModel]
public partial class PayloadChild
{
    public string? Name { get; set; }

    public int Count { get; set; }

    public AotOrderStatus Status { get; set; } = AotOrderStatus.Unknown;
}

/// <summary>Enum member shape for the source-generated endpoint codec.</summary>
/// <remarks>Endpoint values travel through <c>JsonSerializerOptions.GetTypeInfo</c> metadata.</remarks>
public enum AotOrderStatus
{
    Unknown,

    Open,

    Closed,
}

[SparseModel]
public partial class PayloadRoot
{
    public string? Label { get; set; }

    public PayloadChild? Child { get; set; }
}

[SparseModel]
public partial class PayloadItem
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class PayloadCollection
{
    public List<PayloadItem> Items { get; set; } = [];
}

[SparseModel]
public partial class PayloadSecret
{
    public string? Label { get; set; }

    [SparseRedactBefore]
    public string? Token { get; set; }
}
