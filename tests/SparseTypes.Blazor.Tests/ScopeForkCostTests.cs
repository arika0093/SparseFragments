namespace SparseTypes.Blazor.Tests;

// Fork-cost instrumentation (#313): repeated unrelated root edits must not
// reconstruct clean children. Each reconstruction forks the whole root, so a
// zero count plus flat allocations across child counts proves the win.
public sealed class ScopeForkCostTests
{
    private static OrderDto OrderWithLines(int lineCount)
    {
        var order = new OrderDto
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new(),
        };
        for (var index = 0; index < lineCount; index++)
        {
            order.Lines.Add(
                new OrderLine
                {
                    Sku = $"sku-{index:0000}",
                    Quantity = index,
                    Price = index,
                }
            );
        }

        return order;
    }

    [Test]
    public async Task RepeatedUnrelatedEditsDoNotReforkRetainedChildren()
    {
        var cells = new (string Name, int Lines, int Children)[]
        {
            ("small/1", 100, 1),
            ("small/10", 100, 10),
            ("small/100", 100, 100),
            ("large/1", 2000, 1),
            ("large/10", 2000, 10),
            ("large/100", 2000, 100),
        };

        foreach (var (name, lines, childCount) in cells)
        {
            var parent = OrderWithLines(lines).CreateEditSession();
            using var host = await ScopeTestHost.CreateAsync(
                parent,
                mode: SparseChildEditMode.PassThrough
            );
            var scope = host.ScopeContext;
            var sessions = new List<object>(childCount);
            for (var index = 0; index < childCount; index++)
            {
                var path = OrderDto.SparsePath.Lines.Key($"sku-{index:0000}");
                sessions.Add(scope.OpenChildSession(parent, path).Session);
            }

            // Warm up snapshots, caches, and rendering once.
            parent.Observable.Number = "ORD-warmup";
            await host.FlushAsync();
            for (var index = 0; index < childCount; index++)
            {
                var path = OrderDto.SparsePath.Lines.Key($"sku-{index:0000}");
                sessions[index] = scope.OpenChildSession(parent, path).Session;
            }

            var notifications = 0;
            void OnChanged() => notifications++;
            scope.Changed += OnChanged;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            const int edits = 20;
            for (var edit = 0; edit < edits; edit++)
            {
                parent.Observable.Number = $"ORD-{edit + 2}";
            }

            await host.FlushAsync();
            stopwatch.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            scope.Changed -= OnChanged;

            var reconstructed = 0;
            for (var index = 0; index < childCount; index++)
            {
                var path = OrderDto.SparsePath.Lines.Key($"sku-{index:0000}");
                if (!ReferenceEquals(sessions[index], scope.OpenChildSession(parent, path).Session))
                {
                    reconstructed++;
                }
            }

            Console.WriteLine(
                $"fork-cost {name}: reconstructed={reconstructed}/{childCount} "
                    + $"notifications={notifications} "
                    + $"elapsed-ms={stopwatch.ElapsedMilliseconds} "
                    + $"allocated-bytes={allocated}"
            );
            reconstructed.ShouldBe(0);
            notifications.ShouldBe(0);
        }
    }
}
