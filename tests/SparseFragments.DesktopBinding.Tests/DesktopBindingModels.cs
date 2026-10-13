namespace SparseFragments.DesktopBinding.Tests;

/// <summary>Order edited through desktop bindings in the smoke tests.</summary>
[SparseFragmentModel]
public partial class DesktopOrder
{
    /// <summary>Gets or sets the order number bound to text inputs.</summary>
    public string Number { get; set; } = string.Empty;

    /// <summary>Gets or sets the nested customer edited in place.</summary>
    public DesktopCustomer? Customer { get; set; }

    /// <summary>Gets or sets the keyed lines shown in item controls.</summary>
    public List<DesktopOrderLine> Lines { get; set; } = [];
}

/// <summary>Nested customer edited through child bindings.</summary>
[SparseFragmentModel]
public partial class DesktopCustomer
{
    /// <summary>Gets or sets the customer name.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>Keyed order line shown in item controls.</summary>
[SparseFragmentModel]
public partial class DesktopOrderLine
{
    /// <summary>Gets or sets the stable line key.</summary>
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the line name.</summary>
    public string Name { get; set; } = string.Empty;
}
