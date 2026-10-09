using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class BlazorSessionCacheTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
        };

    private sealed class AccessorSession(OrderDto model)
        : ISparseEditSession<OrderDto>,
            ISparseEditSessionModelAccessor<OrderDto>
    {
        public int RawAccessCount { get; private set; }

        public int SafeAccessCount { get; private set; }

        public bool CacheValid { get; private set; } = true;

        public OrderDto Model
        {
            get
            {
                RawAccessCount++;
                CacheValid = false;
                return model;
            }
        }

        public bool HasChanges => CacheValid;

        public void AcceptChanges() { }

        public OrderDto GetModelForFrameworkAccess()
        {
            SafeAccessCount++;
            return model;
        }
    }

    private sealed class LegacySession(OrderDto model) : ISparseEditSession<OrderDto>
    {
        public int RawAccessCount { get; private set; }

        public OrderDto Model
        {
            get
            {
                RawAccessCount++;
                return model;
            }
        }

        public bool HasChanges => false;

        public void AcceptChanges() { }
    }

    [Test]
    public void HelpersUseTrustedAccessAndPreserveTheCache()
    {
        var session = new AccessorSession(Order());

        var editContext = session.CreateEditContext();
        var field = session.Field("Customer.Name");
        var store = session.CreateValidationStore(editContext);
        session.AddValidationError(store, field, "Required.");
        session.AddValidationError(store, "Customer.Name", "Required.");
        session.AcceptChanges(editContext);

        session.RawAccessCount.ShouldBe(0);
        session.SafeAccessCount.ShouldBeGreaterThan(0);
        session.CacheValid.ShouldBeTrue();
    }

    [Test]
    public void SessionsWithoutTheAccessorFallBackToTheRawModel()
    {
        var session = new LegacySession(Order());

        var editContext = session.CreateEditContext();
        session.Field("Customer.Name").FieldName.ShouldBe(nameof(OrderCustomer.Name));
        session.CreateValidationStore(editContext).ShouldNotBeNull();

        session.RawAccessCount.ShouldBeGreaterThan(0);
    }

    [Test]
    public void GeneratedSessionKeepsObservableCacheAcrossHelperCalls()
    {
        var session = Order().CreateEditSession();
        session.Observable.Number = "ORD-2";
        session.HasChanges.ShouldBeTrue();
        CacheState(session).ShouldBe((Caching: true, Valid: true));

        var editContext = session.CreateEditContext();
        session.Field("Customer.Name");
        session.CreateValidationStore(editContext);

        // The helpers above must not have disabled the observable-change cache.
        CacheState(session).ShouldBe((Caching: true, Valid: true));
        session.HasChanges.ShouldBeTrue();

        session.Observable.Number = "ORD-1";
        session.HasChanges.ShouldBeFalse();
    }

    private static (bool Caching, bool Valid) CacheState(object session)
    {
        var coreField = session
            .GetType()
            .GetField("_session", BindingFlags.NonPublic | BindingFlags.Instance);
        coreField.ShouldNotBeNull();
        var core = coreField!.GetValue(session);
        core.ShouldNotBeNull();
        // The cache fields are private to the session-core base class, so the
        // lookup walks up the hierarchy instead of stopping at the leaf type.
        return (
            ReadCoreFlag(core!, "_cacheObservableChanges"),
            ReadCoreFlag(core, "_hasChangesCacheValid")
        );
    }

    private static bool ReadCoreFlag(object core, string name)
    {
        for (var type = core.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(core) is bool value)
            {
                return value;
            }
        }

        throw new InvalidOperationException($"Session core field '{name}' was not found.");
    }
}
