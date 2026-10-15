using System.ComponentModel.DataAnnotations;

namespace SparseTypes.Blazor.Tests;

[SparseModel]
public partial class OrderLine
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Quantity { get; set; }

    public decimal Price { get; set; }
}

[SparseModel]
public partial class OrderCustomer
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }
}

[SparseModel]
public partial class OrderDto
{
    [Required]
    public string Number { get; set; } = string.Empty;

    public OrderCustomer Customer { get; set; } = new();

    public List<OrderLine> Lines { get; set; } = new();

    public List<string> Tags { get; set; } = new();

    public Dictionary<string, OrderCustomer> Contacts { get; set; } = new();
}

[SparseModel]
public partial class TeamMember
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public List<string> Skills { get; set; } = new();
}

[SparseModel]
public partial class Team
{
    [SparseKey]
    public string Name { get; set; } = string.Empty;

    public List<TeamMember> Members { get; set; } = new();
}

[SparseModel]
public partial class OrgDto
{
    public List<Team> Teams { get; set; } = new();
}

[SparseModel]
public partial class BlazorUnassignedItem
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class BlazorDraftLine
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Title { get; set; } = string.Empty;
}

[SparseModel]
public partial class BlazorDraftBoard
{
    public string Name { get; set; } = string.Empty;

    public List<BlazorDraftLine> Drafts { get; set; } = new();
}

[SparseModel]
public partial class BlazorCounterForm
{
    public string Label { get; set; } = string.Empty;

    public int Total { get; set; }
}

[SparseModel]
public partial class BlazorUnassignedOrder
{
    public string Number { get; set; } = string.Empty;

    public List<BlazorUnassignedItem> Items { get; set; } = new();
}

[SparseModel]
public partial class GuidContact
{
    [SparseKey]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class GuidDirectory
{
    public Dictionary<Guid, GuidContact> ById { get; set; } = new();
}

[SparseModel]
public partial class ReadOnlyContactBook
{
    public IReadOnlyDictionary<string, OrderCustomer> ByName { get; set; } =
        new Dictionary<string, OrderCustomer>();

    public IReadOnlyDictionary<int, OrderCustomer> ByNumber { get; set; } =
        new Dictionary<int, OrderCustomer>();
}

[SparseModel]
public partial class ReadOnlyLineSheet
{
    public IReadOnlyList<OrderLine> Lines { get; set; } = new List<OrderLine>();
}

[SparseModel]
public partial class ScopeGrandchildLeaf
{
    public string Name { get; set; } = string.Empty;
}

[SparseModel]
public partial class ScopeGrandchildChild
{
    public ScopeGrandchildLeaf Leaf { get; set; } = new();
}

[SparseModel]
public partial class ScopeGrandchildRoot
{
    public ScopeGrandchildChild Child { get; set; } = new();
}
