using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SparseTypes.Blazor.Tests;

[SparseModel]
public partial class BindingForm
{
    public string Name { get; set; } = string.Empty;

    public string? Note { get; set; }

    public int Count { get; init; }

    public BindingNested Child { get; set; } = new();
}

[SparseModel]
public partial class BindingNested
{
    public string Label { get; set; } = string.Empty;
}

// Typed-path-first binding over Observable + original-model EditContext.
public sealed class SparseFieldBindingTests
{
    private static T Raw<T>(ISparseEditSession<T> session)
        where T : class => session.Model;

    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
                new OrderLine
                {
                    Sku = "b",
                    Quantity = 2,
                    Price = 20m,
                },
            },
        };

    [Test]
    public void ScalarBindWritesThroughObservableAndNotifiesOriginalContext()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();

        var binding = session.Bind(OrderDto.SparsePath.Number, context);

        binding.Value.ShouldBe("ORD-1");
        ReferenceEquals(context.Model, model).ShouldBeTrue();
        binding.TrySet("ORD-2", out var error).ShouldBeTrue();
        error.ShouldBeNull();

        session.HasChanges.ShouldBeTrue();
        session.Current.Number.ShouldBe("ORD-2");
        Raw(session).Number.ShouldBe("ORD-2");
        context.IsModified().ShouldBeTrue();
        ReferenceEquals(context.Model, model).ShouldBeTrue();
    }

    [Test]
    public void ValueExpressionTargetsOriginalModelForMemberPaths()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();

        var binding = session.Bind(OrderDto.SparsePath.Number, context);
        var expression = binding.ValueExpression.ShouldNotBeNull();
        expression.Compile()().ShouldBe("ORD-1");

        var created = FieldIdentifier.Create(expression);
        created.Model.ShouldBeSameAs(model);
        created.FieldName.ShouldBe(nameof(OrderDto.Number));
        created.ShouldBe(binding.Field);
    }

    [Test]
    public void KeyedPathsExposeNoValueExpressionButResolveLiveField()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();

        SparsePath<OrderDto, decimal> price = OrderDto.SparsePath.Lines.Key("b").Price;
        var binding = session.Bind(price, context);

        binding.ValueExpression.ShouldBeNull();
        binding.Value.ShouldBe(20m);
        binding.Field.FieldName.ShouldBe(nameof(OrderLine.Price));

        binding.TrySet(99m, out _).ShouldBeTrue();
        Raw(session).Lines.Single(line => line.Sku == "b").Price.ShouldBe(99m);
        context.IsModified().ShouldBeTrue();
    }

    [Test]
    public void EditThenRestoreKeepsTouchedButNoSemanticChange()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Number, context);

        binding.TrySet("changed", out _).ShouldBeTrue();
        binding.TrySet("ORD-1", out _).ShouldBeTrue();

        session.HasChanges.ShouldBeFalse();
        context.IsModified().ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeFalse();
    }

    [Test]
    public void NestedBindingFollowsReplacement()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Customer.Name, context);

        binding.Value.ShouldBe("Ada");
        var beforeOwner = binding.Field.Model;

        session.Descriptors.TryGet(nameof(OrderDto.Customer), out var customer).ShouldBeTrue();
        customer.TrySetValue(new OrderCustomer { Name = "Grace" }).ShouldBeTrue();

        binding.IsStale.ShouldBeFalse();
        binding.Value.ShouldBe("Grace");
        ReferenceEquals(binding.Field.Model, beforeOwner).ShouldBeFalse();
        ReferenceEquals(binding.Field.Model, Raw(session).Customer).ShouldBeTrue();

        binding.TrySet("Hopper", out _).ShouldBeTrue();
        Raw(session).Customer.Name.ShouldBe("Hopper");
        context.IsModified().ShouldBeTrue();
    }

    [Test]
    public void KeyedFieldIdentifierSurvivesReorder()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        SparsePath<OrderDto, int> quantity = OrderDto.SparsePath.Lines.Key("b").Quantity;
        var binding = session.Bind(quantity, context);
        var element = Raw(session).Lines.Single(line => line.Sku == "b");

        ReferenceEquals(binding.Field.Model, element).ShouldBeTrue();

        Raw(session).Lines = Raw(session).Lines.AsEnumerable().Reverse().ToList();
        Raw(session).Lines[0].Sku.ShouldBe("b");

        binding.IsStale.ShouldBeFalse();
        ReferenceEquals(binding.Field.Model, element).ShouldBeTrue();
        binding.Field.FieldName.ShouldBe(nameof(OrderLine.Quantity));
        binding.Value.ShouldBe(2);

        binding.TrySet(9, out _).ShouldBeTrue();
        element.Quantity.ShouldBe(9);
    }

    [Test]
    public void RemovedKeyReportsStaleAndRefusesWrites()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Lines.Key("b").Quantity, context);
        var modifiedBefore = context.IsModified();

        Raw(session).Lines.RemoveAll(line => line.Sku == "b");

        binding.IsStale.ShouldBeTrue();
        binding.TrySet(9, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        Should.Throw<InvalidOperationException>(() => binding.Set(9));
        Should.Throw<InvalidOperationException>(() => _ = binding.Value);
        Should.Throw<InvalidOperationException>(() => _ = binding.Field);
        context.IsModified().ShouldBe(modifiedBefore);
    }

    [Test]
    public void NullableAcceptsNullWhileNonNullableRejectsIt()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();

        var email = session.Bind(OrderDto.SparsePath.Customer.Email, context);
        email.IsNullable.ShouldBeTrue();
        email.TrySet(null, out _).ShouldBeTrue();
        Raw(session).Customer.Email.ShouldBeNull();

        var number = session.Bind(OrderDto.SparsePath.Number, context);
        number.IsNullable.ShouldBeFalse();
        number.TrySet(null, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        Raw(session).Number.ShouldBe("ORD-1");
        Should.Throw<InvalidOperationException>(() => number.Set(null));
    }

    [Test]
    public void ReadOnlyMemberRefusesWrites()
    {
        var session = new BindingForm { Name = "n", Count = 3 }.CreateEditSession();
        var context = session.CreateEditContext();

        var count = session.Bind(BindingForm.SparsePath.Count, context);
        count.IsReadOnly.ShouldBeTrue();
        count.Value.ShouldBe(3);
        count.TrySet(4, out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        Should.Throw<InvalidOperationException>(() => count.Set(4));
        context.IsModified().ShouldBeFalse();

        var name = session.Bind(BindingForm.SparsePath.Name, context);
        name.IsReadOnly.ShouldBeFalse();
        name.TrySet("m", out _).ShouldBeTrue();
    }

    [Test]
    public void DisabledBindingBlocksWritesButStillReads()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(
            OrderDto.SparsePath.Number,
            context,
            new SparseFieldBindingOptions { Disabled = true }
        );

        binding.Disabled.ShouldBeTrue();
        binding.Value.ShouldBe("ORD-1");
        binding.TrySet("ORD-2", out var error).ShouldBeFalse();
        error.ShouldNotBeNullOrEmpty();
        Raw(session).Number.ShouldBe("ORD-1");
        context.IsModified().ShouldBeFalse();

        binding.Disabled = false;
        binding.TrySet("ORD-2", out _).ShouldBeTrue();
        Raw(session).Number.ShouldBe("ORD-2");
    }

    [Test]
    public void StringAndUntypedOverloadsBindTheSamePath()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();

        var fromString = session.Bind(nameof(OrderDto.Number), context);
        fromString.Value.ShouldBe("ORD-1");
        fromString.TrySet("ORD-2", out _).ShouldBeTrue();

        SparsePath untyped = OrderDto.SparsePath.Number;
        var fromUntyped = session.Bind(untyped, context);
        fromUntyped.Value.ShouldBe("ORD-2");

        Should.Throw<ArgumentException>(() => session.Bind("Customer.Unknown", context));
        Should.Throw<ArgumentException>(() => session.Bind(SparsePath.Root<OrderDto>(), context));
    }

    [Test]
    public void BindRequiresTheSessionsOriginalModel()
    {
        var session = Order().CreateEditSession();
        var other = new EditContext(Order());

        Should.Throw<ArgumentException>(() => session.Bind(OrderDto.SparsePath.Number, other));
    }

    [Test]
    public void SessionReplacementDoesNotLeakAcrossContexts()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var oldBinding = session.Bind(OrderDto.SparsePath.Number, context);
        oldBinding.TrySet("ORD-2", out _).ShouldBeTrue();

        var persisted = Order();
        persisted.Number = "ORD-2";
        var nextSession = persisted.CreateEditSession();
        var nextContext = nextSession.CreateEditContext();
        var nextBinding = nextSession.Bind(OrderDto.SparsePath.Number, nextContext);

        // Old writes stay on the old graph and never touch the new context.
        oldBinding.TrySet("OLD", out _).ShouldBeTrue();
        Raw(session).Number.ShouldBe("OLD");
        persisted.Number.ShouldBe("ORD-2");
        nextContext.IsModified().ShouldBeFalse();

        nextBinding.TrySet("ORD-3", out _).ShouldBeTrue();
        persisted.Number.ShouldBe("ORD-3");
        nextContext.IsModified().ShouldBeTrue();
    }

    [Test]
    public void BatchingCoversBatchedAndExternalBatchWrites()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var batched = session.Bind(
            OrderDto.SparsePath.Number,
            context,
            new SparseFieldBindingOptions { UseBatchEdit = true }
        );
        batched.TrySet("ORD-2", out _).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        var plain = session.Bind(OrderDto.SparsePath.Customer.Name, context);
        session.BatchEdit(() =>
        {
            plain.TrySet("Grace", out _).ShouldBeTrue();
            batched.TrySet("ORD-3", out _).ShouldBeTrue();
        });
        Raw(session).Number.ShouldBe("ORD-3");
        Raw(session).Customer.Name.ShouldBe("Grace");
        context.IsModified().ShouldBeTrue();
    }

    [Test]
    public async Task CustomComponentWithDifferentParameterNamesBinds()
    {
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Number, context);

        // A third-party control rarely names its parameters Value/ValueChanged.
        var control = new FakeThirdPartyField
        {
            Current = binding.Value,
            CurrentChanged = EventCallback.Factory.Create<string?>(this, binding.SetAsync),
        };
        control.Current.ShouldBe("ORD-1");

        await control.CurrentChanged.InvokeAsync("ORD-9");

        Raw(session).Number.ShouldBe("ORD-9");
        session.HasChanges.ShouldBeTrue();
        context.IsModified().ShouldBeTrue();
        context.GetValidationMessages().ShouldBeEmpty();
    }

    [Test]
    public async Task RenderedInputTextBindsWithoutManualGlue()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new BindingTestRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var session = Order().CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Number, context);
        var view = new BindingInputView();
        var componentId = await renderer.RenderRootAsync(
            view,
            ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BindingInputView.FormContext)] = context,
                    [nameof(BindingInputView.Binding)] = binding,
                }
            )
        );

        view.Captured.ShouldNotBeNull();
        view.Captured!.Value.ShouldBe("ORD-1");

        // Drive the InputText through the binding write callback, exactly as
        // ValueChanged would fire from user input.
        await renderer.DispatchAsync(() => binding.SetAsync("ORD-2"));
        var failure = await renderer.TryRenderAsync(
            componentId,
            ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BindingInputView.FormContext)] = context,
                    [nameof(BindingInputView.Binding)] = binding,
                }
            )
        );
        failure.ShouldBeNull();
        Raw(session).Number.ShouldBe("ORD-2");
        context.IsModified().ShouldBeTrue();
        ReferenceEquals(context.Model, Raw(session)).ShouldBeTrue();
    }

    private sealed class FakeThirdPartyField
    {
        public string? Current { get; set; }

        public EventCallback<string?> CurrentChanged { get; set; }
    }

    private sealed class BindingInputView : ComponentBase
    {
        [Parameter]
        public EditContext FormContext { get; set; } = null!;

        [Parameter]
        public SparseFieldBinding<OrderDto, string> Binding { get; set; } = null!;

        public InputText? Captured { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.EditContext), FormContext);
            builder.AddAttribute(
                2,
                "ChildContent",
                (RenderFragment<EditContext>)(
                    _ =>
                        form =>
                        {
                            form.OpenComponent<InputText>(0);
                            form.AddAttribute(1, nameof(InputText.Value), Binding.Value);
                            form.AddAttribute(
                                2,
                                nameof(InputText.ValueChanged),
                                EventCallback.Factory.Create<string?>(this, Binding.SetAsync)
                            );
                            form.AddAttribute(
                                3,
                                nameof(InputText.ValueExpression),
                                Binding.ValueExpression
                            );
                            form.AddComponentReferenceCapture(
                                4,
                                component => Captured = (InputText)component
                            );
                            form.CloseComponent();
                        }
                )
            );
            builder.CloseComponent();
        }
    }

#pragma warning disable BL0006
    private sealed class BindingTestRenderer(IServiceProvider services, ILoggerFactory loggers)
        : Renderer(services, loggers)
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CreateDefault();
        private readonly List<Exception> _failures = new();

        public override Dispatcher Dispatcher => _dispatcher;

        public Task<int> RenderRootAsync(IComponent component, ParameterView parameters) =>
            Dispatcher.InvokeAsync(async () =>
            {
                var componentId = AssignRootComponentId(component);
                await RenderRootComponentAsync(componentId, parameters);
                return componentId;
            });

        public Task RenderAsync(int componentId, ParameterView parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(componentId, parameters));

        public Task DispatchAsync(Func<Task> work) => Dispatcher.InvokeAsync(work);

        public async Task<Exception?> TryRenderAsync(int componentId, ParameterView parameters)
        {
            _failures.Clear();
            try
            {
                await RenderAsync(componentId, parameters);
            }
            catch (Exception exception)
            {
                return exception;
            }

            return _failures.Count switch
            {
                0 => null,
                1 => _failures[0],
                _ => new AggregateException(_failures),
            };
        }

        protected override void HandleException(Exception exception)
        {
            _failures.Add(exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }
#pragma warning restore BL0006
}
