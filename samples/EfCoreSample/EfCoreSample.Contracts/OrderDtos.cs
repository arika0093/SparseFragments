using SparseFragments;

namespace EfCoreSample.Contracts;

/// <summary>
/// The address DTO. Knows nothing about EF Core.
/// </summary>
[SparseFragmentModel]
public partial class AddressDto
{
    /// <summary>Gets or sets the street.</summary>
    public string Street { get; set; } = string.Empty;

    /// <summary>Gets or sets the city. Mapped through the flattened ShipCity member.</summary>
    public string City { get; set; } = string.Empty;
}

/// <summary>
/// The order DTO. The change set over this type is the write mask.
/// </summary>
[SparseFragmentModel]
public partial class OrderDto
{
    /// <summary>Gets or sets the customer name. Renamed from the entity member.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the notes. Same-name scalar mapping.</summary>
    public string? Notes { get; set; }

    /// <summary>Gets or sets the code. Normalized through a conversion callback.</summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>Gets or sets the shipping city. Flattened onto the nested address.</summary>
    public string ShipCity { get; set; } = string.Empty;

    /// <summary>Gets or sets the display total. Read-only presentation state, never written.</summary>
    public string? DisplayTotal { get; set; }

    /// <summary>Gets or sets the nested address.</summary>
    public AddressDto? Address { get; set; }

    /// <summary>Gets or sets the keyed order lines.</summary>
    public List<OrderLineDto> Lines { get; set; } = [];
}

/// <summary>
/// The order line DTO. Knows nothing about EF Core.
/// </summary>
public partial class OrderLineDto
{
    /// <summary>Gets or sets the persisted identity (unassigned for new rows).</summary>
    public int Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    /// <summary>Gets or sets the collection-scoped temporary identity for new rows.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Gets or sets the display subtotal. Read-only presentation state, never written.</summary>
    public decimal Subtotal { get; set; }
}
