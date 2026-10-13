using SparseTypes.EntityFrameworkCore.Tests.Sample;

namespace SparseTypes.EntityFrameworkCore.Tests;

/// <summary>Test DTO with key, concurrency, field-backed, and display members.</summary>
[SparseModel]
public partial class WidgetDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public DateOnly DueDate { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public string? Display { get; set; }
}

/// <summary>Test entity exercising EF metadata corners.</summary>
public sealed class WidgetEntity
{
    public WidgetEntity() { }

    public WidgetEntity(string name, string status)
    {
        Name = name;
        _status = status;
    }

    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    private string _status = string.Empty;

    /// <summary>Field-backed read-only property: writes must go through the EF entry.</summary>
    public string Status => _status;

    public DateOnly DueDate { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Baseline widget mapping: ignores the display member.</summary>
/// <remarks>
/// Carries <c>[SparseFragmentMapper]</c> so the ordinary generator emits the
/// conventional defaults (same-name scalars) for this pair end to end; the
/// explicit ignores below replace the generated identity/token defaults.
/// </remarks>
[SparseFragmentMapper]
public sealed class WidgetMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Ignore(dto => dto.Id);
        map.Ignore(dto => dto.RowVersion);
        map.Ignore(dto => dto.Display);
    }
}

/// <summary>Widget mapping ignoring everything except Display, for fail-fast unmapped-path tests.</summary>
[SparseFragmentMapper]
public sealed class WidgetStrictMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Ignore(dto => dto.Id);
        map.Ignore(dto => dto.Name);
        map.Ignore(dto => dto.Status);
        map.Ignore(dto => dto.DueDate);
        map.Ignore(dto => dto.RowVersion);
    }
}

/// <summary>Widget mapping that writes the identity with explicit allowance.</summary>
[SparseFragmentMapper]
public sealed class WidgetIdMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Member(dto => dto.Id, entity => entity.Id);
        map.Ignore(dto => dto.Display);
        map.Ignore(dto => dto.RowVersion);
        map.AllowWrite(dto => dto.Id);
    }
}

/// <summary>Widget mapping that writes the identity without allowance.</summary>
[SparseFragmentMapper]
public sealed class WidgetRawIdMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Member(dto => dto.Id, entity => entity.Id);
        map.Ignore(dto => dto.Display);
        map.Ignore(dto => dto.RowVersion);
    }
}

/// <summary>DTO naming the shadow member.</summary>
[SparseModel]
public partial class ShadowedWidgetDto
{
    public string? TenantId { get; set; }
}

/// <summary>Mapping that collides with the shadow member.</summary>
[SparseFragmentMapper]
public sealed class ShadowedWidgetMapper : IEntityModelMapping<WidgetEntity, ShadowedWidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, ShadowedWidgetDto> map) { }
}

/// <summary>DTO with two members for one entity member.</summary>
[SparseModel]
public partial class GadgetDto
{
    public string Title { get; set; } = string.Empty;

    public string Heading { get; set; } = string.Empty;
}

/// <summary>Entity with a single writable name.</summary>
public sealed class GadgetEntity
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}

/// <summary>Ambiguous mapping: two DTO paths target the same entity member.</summary>
[SparseFragmentMapper]
public sealed class GadgetAmbiguousMapper : IEntityModelMapping<GadgetEntity, GadgetDto>
{
    public void Configure(SparseEntityMapBuilder<GadgetEntity, GadgetDto> map)
    {
        map.Member(dto => dto.Title, entity => entity.Name);
        map.Member(dto => dto.Heading, entity => entity.Name);
    }
}

/// <summary>DTO naming an alternate-key member.</summary>
[SparseModel]
public partial class DeviceDto
{
    public string Serial { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

/// <summary>Entity with an alternate key beside the primary key.</summary>
public sealed class DeviceEntity
{
    public int Id { get; set; }

    public string Serial { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
}

/// <summary>Baseline device mapping.</summary>
[SparseFragmentMapper]
public sealed class DeviceMapper : IEntityModelMapping<DeviceEntity, DeviceDto>
{
    public void Configure(SparseEntityMapBuilder<DeviceEntity, DeviceDto> map) { }
}

/// <summary>Keyed line DTO for collection diagnosis.</summary>
public partial class CartLineDto
{
    [SparseKey]
    public string Sku { get; set; } = string.Empty;

    public int Qty { get; set; }
}

/// <summary>DTO holding a keyed collection.</summary>
[SparseModel]
public partial class CartDto
{
    public List<CartLineDto> Lines { get; set; } = [];
}

/// <summary>Entity holding a collection navigation.</summary>
public sealed class CartEntity
{
    public int Id { get; set; }

    public List<CartLineEntity> Lines { get; set; } = [];
}

/// <summary>Collection line entity.</summary>
public sealed class CartLineEntity
{
    public int Id { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int Qty { get; set; }
}

/// <summary>Mapping that marks the collection path.</summary>
[SparseFragmentMapper]
public sealed class CartMapper : IEntityModelMapping<CartEntity, CartDto>
{
    public void Configure(SparseEntityMapBuilder<CartEntity, CartDto> map)
    {
        map.Collection(dto => dto.Lines, entity => entity.Lines);
    }
}

/// <summary>DTO holding a scalar sequence.</summary>
[SparseModel]
public partial class TagsDto
{
    public List<string>? Tags { get; set; } = [];
}

/// <summary>Entity holding a primitive collection.</summary>
public sealed class TagsEntity
{
    public int Id { get; set; }

    public List<string>? Tags { get; set; } = [];
}

/// <summary>Second DTO configuration over the sample order entity.</summary>
[SparseModel]
public partial class OrderSummaryDto
{
    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

/// <summary>Second mapping over the same entity with different renames and a subset of members.</summary>
[SparseFragmentMapper]
public sealed class OrderSummaryMapper : IEntityModelMapping<OrderEntity, OrderSummaryDto>
{
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderSummaryDto> map)
    {
        map.Member(dto => dto.Title, entity => entity.CustomerName);
    }
}
