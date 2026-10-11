using EfCoreSample.Server;

namespace SparseFragments.EntityFrameworkCore.Tests;

/// <summary>Test DTO with key, concurrency, field-backed, and display members.</summary>
[SparseFragmentModel]
public partial class WidgetDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

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

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Baseline widget mapping: ignores the display member.</summary>
public sealed class WidgetMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Ignore(dto => dto.Display);
    }
}

/// <summary>Widget mapping without ignores, for unmapped-path tests.</summary>
public sealed class WidgetStrictMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        // Intentionally unconfigured: every changed path must fail explicitly.
    }
}

/// <summary>Widget mapping that writes the identity with explicit allowance.</summary>
public sealed class WidgetIdMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Member(dto => dto.Id, entity => entity.Id);
        map.Ignore(dto => dto.Display);
        map.AllowWrite(dto => dto.Id);
    }
}

/// <summary>Widget mapping that writes the identity without allowance.</summary>
public sealed class WidgetRawIdMapper : IEntityModelMapping<WidgetEntity, WidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, WidgetDto> map)
    {
        map.Member(dto => dto.Id, entity => entity.Id);
        map.Ignore(dto => dto.Display);
    }
}

/// <summary>DTO naming the shadow member.</summary>
[SparseFragmentModel]
public partial class ShadowedWidgetDto
{
    public string? TenantId { get; set; }
}

/// <summary>Mapping that collides with the shadow member.</summary>
public sealed class ShadowedWidgetMapper : IEntityModelMapping<WidgetEntity, ShadowedWidgetDto>
{
    public void Configure(SparseEntityMapBuilder<WidgetEntity, ShadowedWidgetDto> map) { }
}

/// <summary>DTO with two members for one entity member.</summary>
[SparseFragmentModel]
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
public sealed class GadgetAmbiguousMapper : IEntityModelMapping<GadgetEntity, GadgetDto>
{
    public void Configure(SparseEntityMapBuilder<GadgetEntity, GadgetDto> map)
    {
        map.Member(dto => dto.Title, entity => entity.Name);
        map.Member(dto => dto.Heading, entity => entity.Name);
    }
}

/// <summary>DTO naming an alternate-key member.</summary>
[SparseFragmentModel]
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
[SparseFragmentModel]
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
public sealed class CartMapper : IEntityModelMapping<CartEntity, CartDto>
{
    public void Configure(SparseEntityMapBuilder<CartEntity, CartDto> map)
    {
        map.Collection(dto => dto.Lines, entity => entity.Lines);
    }
}

/// <summary>DTO holding a scalar sequence.</summary>
[SparseFragmentModel]
public partial class TagsDto
{
    public List<string> Tags { get; set; } = [];
}

/// <summary>Entity holding a primitive collection.</summary>
public sealed class TagsEntity
{
    public int Id { get; set; }

    public List<string> Tags { get; set; } = [];
}

/// <summary>Second DTO configuration over the sample order entity.</summary>
[SparseFragmentModel]
public partial class OrderSummaryDto
{
    public string Title { get; set; } = string.Empty;

    public string? Notes { get; set; }
}

/// <summary>Second mapping over the same entity with different renames and a subset of members.</summary>
public sealed class OrderSummaryMapper : IEntityModelMapping<OrderEntity, OrderSummaryDto>
{
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderSummaryDto> map)
    {
        map.Member(dto => dto.Title, entity => entity.CustomerName);
    }
}
