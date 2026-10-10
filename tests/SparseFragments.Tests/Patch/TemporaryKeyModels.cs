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
