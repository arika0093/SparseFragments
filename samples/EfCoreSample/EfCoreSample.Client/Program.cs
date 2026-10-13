using System.Net.Http.Json;
using EfCoreSample.Contracts;

const int orderId = 1;
var serverAddress =
    Environment.GetEnvironmentVariable("services__server__http__0")
    ?? throw new InvalidOperationException(
        "The Aspire server reference 'server' was not configured."
    );
using var client = new HttpClient { BaseAddress = new Uri(serverAddress) };

Console.WriteLine($"[Client] Loading order {orderId} from {client.BaseAddress}.");
var current =
    await client.GetFromJsonAsync<OrderDto>($"orders/{orderId}")
    ?? throw new InvalidOperationException("The server returned no order.");
var draft = current.DeepClone();
var session = draft.CreateEditSession();
var runId = Guid.NewGuid().ToString("N")[..8];
var random = Random.Shared;
draft.Notes = $"Client run {runId}";
if (draft.Lines.Count > 0)
{
    draft.Lines[random.Next(draft.Lines.Count)].Quantity = random.Next(1, 21);
}

var temporaryId = Guid.NewGuid();
var addedLine = new OrderLineDto
{
    Quantity = random.Next(1, 11),
    UnitPrice = random.Next(10, 101),
    TemporaryId = temporaryId,
};
draft.Lines.Add(addedLine);
var submitted = session.CreateChangeSet();
var payload = submitted.ToPayload();

Console.WriteLine(
    $"[Client] Sending update {runId}: notes, {draft.Lines.Count} lines, new line quantity={addedLine.Quantity}, unitPrice={addedLine.UnitPrice}."
);
using var response = await client.PutAsJsonAsync($"orders/{orderId}", payload);
response.EnsureSuccessStatusCode();
var authoritative =
    await response.Content.ReadFromJsonAsync<OrderDto>()
    ?? throw new InvalidOperationException("The server returned no updated order.");
var reconciled = session.TryReconcile(submitted, authoritative, out _);
if (!reconciled)
{
    Console.Error.WriteLine($"[Client] Update {runId} could not be reconciled.");
    return 2;
}

var persistedLine = authoritative.Lines.Find(line => line.TemporaryId == temporaryId);
if (persistedLine is null)
{
    Console.Error.WriteLine(
        $"[Client] Update {runId} succeeded but the new line was not returned."
    );
    return 3;
}

Console.WriteLine(
    $"[Client] Update {runId} reconciled. New line persisted as Id={persistedLine.Id}; order now has {authoritative.Lines.Count} lines."
);
return 0;
