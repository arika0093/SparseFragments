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
/// Carries <c>[SparseFragmentMapper{TEntity, TDto}]</c> so the ordinary generator emits the
/// conventional defaults (same-name scalars) for this pair end to end; the
/// explicit ignores below replace the generated identity/token defaults.
/// </remarks>
[SparseFragmentMapper<WidgetEntity, WidgetDto>]
public sealed partial class WidgetMapper
{
    private void Configure()
    {
        Map.Ignore(dto => dto.Id);
        Map.Ignore(dto => dto.RowVersion);
        Map.Ignore(dto => dto.Display);
    }
}

/// <summary>Widget mapping ignoring everything except Display, for fail-fast unmapped-path tests.</summary>
[SparseFragmentMapper<WidgetEntity, WidgetDto>]
public sealed partial class WidgetStrictMapper
{
    private void Configure()
    {
        Map.Ignore(dto => dto.Id);
        Map.Ignore(dto => dto.Name);
        Map.Ignore(dto => dto.Status);
        Map.Ignore(dto => dto.DueDate);
        Map.Ignore(dto => dto.RowVersion);
    }
}

/// <summary>Widget mapping that writes the identity with explicit allowance.</summary>
[SparseFragmentMapper<WidgetEntity, WidgetDto>]
public sealed partial class WidgetIdMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Id, entity => entity.Id);
        Map.Ignore(dto => dto.Display);
        Map.Ignore(dto => dto.RowVersion);
        Map.AllowWrite(dto => dto.Id);
    }
}

/// <summary>Widget mapping that writes the identity without allowance.</summary>
[SparseFragmentMapper<WidgetEntity, WidgetDto>]
public sealed partial class WidgetRawIdMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Id, entity => entity.Id);
        Map.Ignore(dto => dto.Display);
        Map.Ignore(dto => dto.RowVersion);
    }
}

/// <summary>DTO naming the shadow member.</summary>
[SparseModel]
public partial class ShadowedWidgetDto
{
    public string? TenantId { get; set; }
}

/// <summary>Mapping that collides with the shadow member.</summary>
[SparseFragmentMapper<WidgetEntity, ShadowedWidgetDto>]
public sealed partial class ShadowedWidgetMapper
{
    private void Configure() { }
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
[SparseFragmentMapper<GadgetEntity, GadgetDto>]
public sealed partial class GadgetAmbiguousMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Title, entity => entity.Name);
        Map.Member(dto => dto.Heading, entity => entity.Name);
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
[SparseFragmentMapper<DeviceEntity, DeviceDto>]
public sealed partial class DeviceMapper
{
    private void Configure() { }
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
[SparseFragmentMapper<CartEntity, CartDto>]
public sealed partial class CartMapper
{
    private void Configure()
    {
        Map.Collection(dto => dto.Lines, entity => entity.Lines);
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
[SparseFragmentMapper<OrderEntity, OrderSummaryDto>]
public sealed partial class OrderSummaryMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Title, entity => entity.CustomerName);
    }
}
