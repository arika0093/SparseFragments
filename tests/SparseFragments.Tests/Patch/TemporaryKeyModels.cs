using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>Keyed element models with opt-in temporary identity for issue #207.</summary>
[SparseFragmentModel]
public partial class TempOrderLine
{
    public int Id { get; set; }

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class TempOrderHolder
{
    public List<TempOrderLine> Lines { get; set; } = new();
}

[SparseFragmentModel]
public partial class TempCompositeLine
{
    public string TenantId { get; set; } = string.Empty;

    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, int Number)? Key => Unassigned ? null : (TenantId, Number);

    public bool Unassigned { get; set; } = true;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}

[SparseFragmentModel]
public partial class TempCompositeHolder
{
    public List<TempCompositeLine> Lines { get; set; } = new();
}

[SparseFragmentModel]
public partial class TempCustomer
{
    [SparseKey(Unassigned = 0)]
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    public List<TempOrderLine> Orders { get; set; } = new();
}

[SparseFragmentModel]
public partial class TempOrderBook
{
    public TempCustomer Customer { get; set; } = new();

    public List<TempOrderLine> Lines { get; set; } = new();
}

/// <summary>Init-only keyed list holder for issue #221.</summary>
/// <remarks>
/// The list instance itself is mutable, but the property cannot be
/// replaced after construction, so reconcile must reject a dirty
/// retarget before mutating the backing list.
/// </remarks>
[SparseFragmentModel]
public partial class TempInitHolder
{
    public List<TempOrderLine> Lines { get; init; } = new();
}

/// <summary>Keyed customers with nested orders for issue #216.</summary>
/// <remarks>
/// Sibling <c>Orders</c> collections under distinct assigned parents must
/// correlate independently even when they reuse a temporary Guid.
/// </remarks>
[SparseFragmentModel]
public partial class TempCustomerHolder
{
    public List<TempCustomer> Customers { get; set; } = new();
}
