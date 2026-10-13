using System.Net.Http.Json;
using EfCoreSample.Contracts;

var serverAddress =
    Environment.GetEnvironmentVariable("services__server__http__0")
    ?? throw new InvalidOperationException(
        "The Aspire server reference 'server' was not configured."
    );
using var client = new HttpClient { BaseAddress = new Uri(serverAddress) };

var current =
    await client.GetFromJsonAsync<OrderDto>("orders/1")
    ?? throw new InvalidOperationException("The server returned no order.");
var draft = Clone(current);
var session = draft.CreateEditSession();
draft.Notes = "updated";
draft.Lines.Single(static line => line.Quantity == 2).Quantity = 5;
draft.Lines.RemoveAll(static line => line.Quantity == 1);
var temporary = Guid.NewGuid();
draft.Lines.Add(
    new OrderLineDto
    {
        Quantity = 7,
        UnitPrice = 70m,
        TemporaryId = temporary,
    }
);
var submitted = session.CreateChangeSet();

using var response = await client.PutAsJsonAsync("orders/1", draft);
response.EnsureSuccessStatusCode();
var authoritative =
    await response.Content.ReadFromJsonAsync<OrderDto>()
    ?? throw new InvalidOperationException("The server returned no updated order.");
var reconciled = session.TryReconcile(submitted, authoritative, out _);
Console.WriteLine(
    $"Reconciled={reconciled}, addition persisted as Id={authoritative.Lines.Single(static line => line.Quantity == 7).Id}"
);
return reconciled ? 0 : 2;

static OrderDto Clone(OrderDto source) =>
    new()
    {
        Name = source.Name,
        Notes = source.Notes,
        Code = source.Code,
        ShipCity = source.ShipCity,
        DisplayTotal = source.DisplayTotal,
        Address = source.Address is null
            ? null
            : new AddressDto { Street = source.Address.Street, City = source.Address.City },
        Lines = source
            .Lines.Select(static line => new OrderLineDto
            {
                Id = line.Id,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice,
                Subtotal = line.Subtotal,
            })
            .ToList(),
    };
