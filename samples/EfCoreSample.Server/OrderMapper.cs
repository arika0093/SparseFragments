using EfCoreSample.Contracts;
using SparseFragments.EntityFrameworkCore;

namespace EfCoreSample.Server;

/// <summary>
/// Maps <see cref="OrderDto" /> changes onto <see cref="OrderEntity" />.
/// </summary>
/// <remarks>
/// Same-name scalars (<c>Notes</c>) are inferred. Everything else is declared
/// here: the renamed <c>Name</c>, the normalized <c>Code</c>, the flattened
/// <c>ShipCity</c>, the ignored <c>DisplayTotal</c>, the scoped nested
/// <c>Address</c> mapping whose <c>City</c> stays flattened, and the keyed
/// <c>Lines</c> collection whose child mapping, factory, removal, and order
/// policies are scoped to this parent path.
/// </remarks>
[SparseFragmentMapper]
public sealed class OrderMapper : IEntityModelMapping<OrderEntity, OrderDto>
{
    /// <inheritdoc />
    public void Configure(SparseEntityMapBuilder<OrderEntity, OrderDto> map)
    {
        map.Member(dto => dto.Name, entity => entity.CustomerName);
        map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code == null ? null : code.ToUpperInvariant()
        );
        map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        map.Ignore(dto => dto.DisplayTotal);
        map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child =>
            {
                child.Ignore(address => address.City);
            }
        );
        map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Ignore(line => line.Subtotal);
                });
                lines.Create(dto => new OrderLineEntity
                {
                    Quantity = dto.Quantity,
                    UnitPrice = dto.UnitPrice,
                });
                lines.Delete();
                lines.Order((entity, index) => entity.SortOrder = index);
            }
        );
    }
}
