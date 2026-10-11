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
}
