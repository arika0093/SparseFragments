using EfCoreSample.Contracts;
using SparseTypes.EntityFrameworkCore;

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
// Same-name defaults are generated; keep explicit overrides and policies here.
[SparseFragmentMapper<OrderEntity, OrderDto>]
public sealed partial class OrderMapper
{
    private void Configure()
    {
        Map.Member(dto => dto.Name, entity => entity.CustomerName);
        Map.Member(
            dto => dto.Code,
            entity => entity.Code,
            code => code == null ? null : code.ToUpperInvariant()
        );
        Map.Read(dto => dto.Code, entity => entity.Code);
        Map.Member(dto => dto.ShipCity, entity => entity.Address!.City);
        Map.Ignore(dto => dto.DisplayTotal);
        Map.Object(
            dto => dto.Address,
            entity => entity.Address,
            child =>
            {
                child.Ignore(address => address.City);
                child.Read(address => address.City, address => address.City);
            }
        );
        Map.Collection<OrderLineDto, OrderLineEntity>(
            dto => dto.Lines,
            entity => entity.Lines,
            lines =>
            {
                lines.Element(child =>
                {
                    child.Read(line => line.Id, entity => entity.Id);
                    child.Ignore(line => line.Subtotal);
                    child.Read(line => line.Subtotal, entity => entity.Quantity * entity.UnitPrice);
                });
                lines.Delete();
                lines.Order(entity => entity.SortOrder);
            }
        );
    }
}
