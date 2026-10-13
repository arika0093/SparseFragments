using Microsoft.EntityFrameworkCore;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Parent key with a non-injective display form (issue #246).</summary>
/// <remarks>
/// Value equality distinguishes instances, but the default
/// <see cref="ValueType.ToString" /> returns the type name for every value, so
/// scope paths built from display text alone cannot tell two bins apart.
/// </remarks>
public readonly struct BinCode : IEquatable<BinCode>
{
    /// <summary>Creates a bin code.</summary>
    /// <param name="aisle">The aisle name.</param>
    /// <param name="bay">The bay number.</param>
    public BinCode(string aisle, int bay)
    {
        Aisle = aisle;
        Bay = bay;
    }

    /// <summary>Gets the aisle name.</summary>
    public string Aisle { get; }

    /// <summary>Gets the bay number.</summary>
    public int Bay { get; }

    /// <summary>Determines whether two bin codes have equal components.</summary>
    /// <param name="other">The code to compare.</param>
    /// <returns>True for equal aisle and bay values.</returns>
    public bool Equals(BinCode other) => Aisle == other.Aisle && Bay == other.Bay;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BinCode other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Aisle, Bay);
}

/// <summary>DTO line nested under a struct-keyed bin.</summary>
public partial class BinLineDto
{
    /// <summary>Gets or sets the persisted identity. Null means unassigned.</summary>
    public int? Id { get; set; }

    /// <summary>Gets the stable key.</summary>
    [SparseKey(Unassigned = 0)]
    public int Key => Id ?? 0;

    /// <summary>Gets or sets the collection-scoped temporary identity.</summary>
    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }
}

/// <summary>DTO bin keyed by a custom struct.</summary>
public partial class BinDto
{
    /// <summary>Gets or sets the stable struct key.</summary>
    [SparseKey]
    public BinCode Code { get; set; }

    /// <summary>Gets or sets the nested lines.</summary>
    public List<BinLineDto> Lines { get; set; } = [];
}

/// <summary>DTO holding struct-keyed bins.</summary>
[SparseModel]
public partial class BinHolderDto
{
    /// <summary>Gets or sets the bins.</summary>
    public List<BinDto> Bins { get; set; } = [];
}

/// <summary>Entity holding struct-keyed bins.</summary>
public sealed class BinHolderEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the bins.</summary>
    public List<BinEntity> Bins { get; set; } = [];
}

/// <summary>Bin entity with scalar key components.</summary>
public sealed class BinEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the aisle name.</summary>
    public string Aisle { get; set; } = string.Empty;

    /// <summary>Gets or sets the bay number.</summary>
    public int Bay { get; set; }

    /// <summary>Gets or sets the nested lines.</summary>
    public List<BinLineEntity> Lines { get; set; } = [];

    /// <summary>Gets or sets the owning holder identity.</summary>
    public int HolderId { get; set; }

    /// <summary>Gets or sets the owning holder.</summary>
    public BinHolderEntity? Holder { get; set; }
}

/// <summary>Bin line entity.</summary>
public sealed class BinLineEntity
{
    /// <summary>Gets or sets the identity.</summary>
    public int Id { get; set; }

    /// <summary>Gets or sets the quantity.</summary>
    public int Qty { get; set; }

    /// <summary>Gets or sets the owning bin identity.</summary>
    public int BinId { get; set; }

    /// <summary>Gets or sets the owning bin.</summary>
    public BinEntity? Bin { get; set; }
}

/// <summary>Struct-keyed holder mapping with a nested temporary-keyed collection.</summary>
[SparseFragmentMapper]
public sealed class BinHolderMapper : IEntityModelMapping<BinHolderEntity, BinHolderDto>
{
    /// <inheritdoc />
    public void Configure(SparseEntityMapBuilder<BinHolderEntity, BinHolderDto> map)
    {
        map.Collection<BinDto, BinEntity>(
            dto => dto.Bins,
            entity => entity.Bins,
            bins =>
            {
                bins.Key(dto => dto.Code, entity => new BinCode(entity.Aisle, entity.Bay));
                bins.Element(child =>
                {
                    child.Collection<BinLineDto, BinLineEntity>(
                        dto => dto.Lines,
                        entity => entity.Lines,
                        lines =>
                        {
                            lines.Element(static line => { });
                            lines.Delete();
                        }
                    );
                });
                bins.Delete();
            }
        );
    }
}
