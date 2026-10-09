using System.ComponentModel.DataAnnotations;

namespace SparseFragments.Blazor.Tests;

[SparseFragmentModel]
public partial class OrderLine
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }
}

[SparseFragmentModel]
public partial class OrderCustomer
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
}

[SparseFragmentModel]
public partial class OrderDto
{
    [Required]
    public string Number { get; set; } = string.Empty;

    public OrderCustomer Customer { get; set; } = new();

    public List<OrderLine> Lines { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, OrderCustomer> Contacts { get; set; } = new();
}

[SparseFragmentModel]
public partial class TeamMember
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public List<string> Skills { get; set; } = new();
}

[SparseFragmentModel]
public partial class Team
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<TeamMember> Members { get; set; } = new();
}

[SparseFragmentModel]
public partial class OrgDto
{
    public List<Team> Teams { get; set; } = new();
}

[SparseFragmentModel]
public partial class BlazorUnassignedItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class BlazorUnassignedOrder
{
    public string Number { get; set; } = string.Empty;

    public List<BlazorUnassignedItem> Items { get; set; } = new();
}
