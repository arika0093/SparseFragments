using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A dependency-free test renderer inherently builds render trees by hand, so
// the experimental RenderTree API surface it needs is acknowledged here.
#pragma warning disable BL0006

namespace SparseTypes.Blazor.Tests;

// Verify Bind, field/collection state, and history with standard and
// third-party-style inputs without depending on a vendor component.
public sealed class BlazorToolkitNeutralTests
{
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
    public async Task NumberBindingExposesInputTextWiring()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Number, context);

        // The triple Razor hands to InputText: value, the change delegate it
        // wraps in ValueChanged, and the original-model value expression.
        string componentValue = binding.Value;
        Func<string?, Task> componentChanged = binding.SetAsync;
        Expression<Func<string>>? componentExpression = binding.ValueExpression;
        componentValue.ShouldBe("ORD-1");
        componentExpression.ShouldNotBeNull();

        await componentChanged("ORD-2");

        model.Number.ShouldBe("ORD-2");
        context.IsModified().ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeTrue();
    }

    [Test]
    public async Task KeyedBindingExposesValueAndCallbackWithoutExpression()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Lines.Key("b").Quantity, context);

        // Keyed paths expose no value expression; value and callback suffice.
        binding.ValueExpression.ShouldBeNull();
        int componentValue = binding.Value;
        Func<int, Task> componentChanged = binding.SetAsync;
        componentValue.ShouldBe(2);

        await componentChanged(9);

        model.Lines.Single(line => line.Sku == "b").Quantity.ShouldBe(9);
        context.IsModified().ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Lines.Key("b").Quantity).IsChanged.ShouldBeTrue();
        session
            .CollectionState(OrderDto.SparsePath.Lines)
            .GetItemState("b")
            .IsEdited.ShouldBeTrue();
    }

    [Test]
    public async Task NestedBindingExposesInputWiring()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Customer.Name, context);

        string componentValue = binding.Value;
        Func<string?, Task> componentChanged = binding.SetAsync;
        Expression<Func<string>>? componentExpression = binding.ValueExpression;
        componentExpression.ShouldNotBeNull();

        await componentChanged("Grace");

        model.Customer.Name.ShouldBe("Grace");
        context.IsModified().ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Customer.Name).IsChanged.ShouldBeTrue();
    }

    [Test]
    public void OptInHistoryAndAcceptClearBlazorModifiedState()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var context = session.CreateEditContext();
        var history = session.EnableHistory();
        try
        {
            var binding = session.Bind(OrderDto.SparsePath.Number, context);

            history.UndoCount.ShouldBe(0);
            binding.Set("ORD-2");
            history.UndoCount.ShouldBe(1);
            history.CanUndo.ShouldBeTrue();
            context.IsModified().ShouldBeTrue();

            session.AcceptChanges(context);

            session.HasChanges.ShouldBeFalse();
            context.IsModified().ShouldBeFalse();
            history.UndoCount.ShouldBe(0);
            history.CanUndo.ShouldBeFalse();
        }
        finally
        {
            session.DisableHistory();
        }
    }

    [Test]
    public void SessionReplacementKeepsEditContextsSeparate()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var context = session.CreateEditContext();

        var persisted = Order();
        persisted.Number = "ORD-2";
        var fresh = persisted.CreateEditSession();

        // The stale context cannot back the fresh session.
        Should.Throw<ArgumentException>(() => fresh.Bind(OrderDto.SparsePath.Number, context));

        var freshContext = fresh.ReplaceEditContext(context);
        ReferenceEquals(freshContext.Model, persisted).ShouldBeTrue();
        ReferenceEquals(context.Model, live).ShouldBeTrue();

        var freshBinding = fresh.Bind(OrderDto.SparsePath.Number, freshContext);
        freshBinding.Value.ShouldBe("ORD-2");
        freshBinding.TrySet("ORD-3", out _).ShouldBeTrue();

        // The old session still owns the old model; no edits crossed over.
        var oldBinding = session.Bind(OrderDto.SparsePath.Number, context);
        oldBinding.TrySet("OLD", out _).ShouldBeTrue();
        live.Number.ShouldBe("OLD");
        persisted.Number.ShouldBe("ORD-3");
        session.HasChanges.ShouldBeTrue();
        fresh.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void ReplaceEditContextRequiresAFreshModelInstance()
    {
        var live = Order();
        var session = live.CreateEditSession();
        var context = session.CreateEditContext();

        Should.Throw<ArgumentException>(() => session.ReplaceEditContext(context));
    }

    [Test]
    public void SessionStateQueriesDriveHighlights()
    {
        var model = Order();
        var session = model.CreateEditSession();

        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeFalse();
        session.CollectionState(OrderDto.SparsePath.Lines).GetItemState("a").IsEmpty.ShouldBeTrue();

        session.Observable.Number = "ORD-2";
        session.Observable.Lines.AddModel(
            new OrderLine
            {
                Sku = "c",
                Quantity = 3,
                Price = 30m,
            }
        );

        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Customer.Name).IsChanged.ShouldBeFalse();
        var lines = session.CollectionState(OrderDto.SparsePath.Lines);
        lines.IsChanged.ShouldBeTrue();
        lines.GetItemState("c").IsAdded.ShouldBeTrue();
        lines.GetItemState("a").IsEmpty.ShouldBeTrue();

        model.Lines.Reverse();
        var reordered = session.CollectionState(OrderDto.SparsePath.Lines);
        reordered.OrderChanged.ShouldBeTrue();
        reordered.GetItemState("a").IsReordered.ShouldBeTrue();
    }

    // Third-party-style input: the same value/callback/expression triple as
    // the framework inputs, with no vendor package referenced.
    private sealed class VendorTextField : ComponentBase
    {
        [Parameter]
        public string? Value { get; set; }

        [Parameter]
        public EventCallback<string?> ValueChanged { get; set; }

        [Parameter]
        public Expression<Func<string?>>? ValueExpression { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "class", "vendor-field");
            builder.AddContent(2, Value);
            builder.CloseElement();
        }
    }

    // Renders a standard InputText and the vendor field side by side, both
    // wired through session Bind triples.
    private sealed class ToolkitNeutralView : ComponentBase
    {
        [Parameter]
        public OrderDto.EditSession Session { get; set; } = null!;

        [Parameter]
        public EditContext FormContext { get; set; } = null!;

        public InputText? CapturedInput { get; private set; }

        public VendorTextField? CapturedVendor { get; private set; }

        private SparseFieldBinding<OrderDto, string> _number = null!;
        private SparseFieldBinding<OrderDto, string> _name = null!;

        protected override void OnParametersSet()
        {
            _number = Session.Bind(OrderDto.SparsePath.Number, FormContext);
            _name = Session.Bind(OrderDto.SparsePath.Customer.Name, FormContext);
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.SetKey(FormContext);
            builder.AddAttribute(1, nameof(EditForm.EditContext), FormContext);
            builder.AddAttribute(
                2,
                "ChildContent",
                (RenderFragment<EditContext>)(
                    _ =>
                        form =>
                        {
                            form.OpenComponent<InputText>(0);
                            form.AddAttribute(1, nameof(InputText.Value), _number.Value);
                            form.AddAttribute(
                                2,
                                nameof(InputText.ValueChanged),
                                EventCallback.Factory.Create<string>(
                                    this,
                                    value => _number.SetAsync(value)
                                )
                            );
                            form.AddAttribute(
                                3,
                                nameof(InputText.ValueExpression),
                                _number.ValueExpression
                            );
                            form.AddComponentReferenceCapture(
                                4,
                                component => CapturedInput = (InputText)component
                            );
                            form.CloseComponent();
                            form.OpenComponent<VendorTextField>(5);
                            form.AddAttribute(6, nameof(VendorTextField.Value), _name.Value);
                            form.AddAttribute(
                                7,
                                nameof(VendorTextField.ValueChanged),
                                EventCallback.Factory.Create<string?>(
                                    this,
                                    value => _name.SetAsync(value)
                                )
                            );
                            form.AddAttribute(
                                8,
                                nameof(VendorTextField.ValueExpression),
                                _name.ValueExpression
                            );
                            form.AddComponentReferenceCapture(
                                9,
                                component => CapturedVendor = (VendorTextField)component
                            );
                            form.CloseComponent();
                        }
                )
            );
            builder.CloseComponent();
        }
    }

    // Compact copy of the renderer harness in BlazorEditFormRenderedTests:
    // runs the real component diff (including the InputBase context guard).
    private sealed class ToolkitNeutralRenderer : Renderer
    {
        private readonly Dispatcher dispatcher = Dispatcher.CreateDefault();
        private readonly List<Exception> failures = new();

        public ToolkitNeutralRenderer(IServiceProvider services, ILoggerFactory loggers)
            : base(services, loggers) { }

        public override Dispatcher Dispatcher => dispatcher;

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
            failures.Clear();
            try
            {
                await RenderAsync(componentId, parameters);
            }
            catch (Exception exception)
            {
                return exception;
            }

            return failures.Count switch
            {
                0 => null,
                1 => failures[0],
                _ => new AggregateException(failures),
            };
        }

        protected override void HandleException(Exception exception)
        {
            failures.Add(exception);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }

    private static ParameterView ToolkitViewParameters(
        ToolkitNeutralView view,
        OrderDto.EditSession session,
        EditContext context
    ) =>
        ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(ToolkitNeutralView.Session)] = session,
                [nameof(ToolkitNeutralView.FormContext)] = context,
            }
        );

    [Test]
    public async Task RenderedStandardAndVendorInputsShareTheBindingApi()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new ToolkitNeutralRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var live = Order();
        var session = live.CreateEditSession();
        var context = session.CreateEditContext();
        var view = new ToolkitNeutralView();
        var componentId = await renderer.RenderRootAsync(
            view,
            ToolkitViewParameters(view, session, context)
        );
        view.CapturedInput.ShouldNotBeNull();
        view.CapturedVendor.ShouldNotBeNull();

        await renderer.DispatchAsync(() => view.CapturedInput!.ValueChanged.InvokeAsync("ORD-2"));
        await renderer.DispatchAsync(() => view.CapturedVendor!.ValueChanged.InvokeAsync("Grace"));
        var failure = await renderer.TryRenderAsync(
            componentId,
            ToolkitViewParameters(view, session, context)
        );
        failure.ShouldBeNull();

        live.Number.ShouldBe("ORD-2");
        live.Customer.Name.ShouldBe("Grace");
        context.IsModified().ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Number).IsChanged.ShouldBeTrue();
        session.FieldState(OrderDto.SparsePath.Customer.Name).IsChanged.ShouldBeTrue();
    }

    [Test]
    public async Task RenderedInputsAreRecreatedAfterSessionReplacement()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new ToolkitNeutralRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var live = Order();
        var session = live.CreateEditSession();
        var context = session.CreateEditContext();
        var view = new ToolkitNeutralView();
        var componentId = await renderer.RenderRootAsync(
            view,
            ToolkitViewParameters(view, session, context)
        );
        var firstInput = view.CapturedInput;
        var firstVendor = view.CapturedVendor;

        await renderer.DispatchAsync(() => view.CapturedInput!.ValueChanged.InvokeAsync("ORD-2"));
        var staleContext = context;
        var staleLive = live;

        // Adopt the persisted row: fresh session and context, as the shipped
        // form does after a save. The keyed EditForm recreates both inputs.
        var persisted = Order();
        persisted.Number = "SAVED";
        live = persisted;
        session = live.CreateEditSession();
        context = session.CreateEditContext();
        var failure = await renderer.TryRenderAsync(
            componentId,
            ToolkitViewParameters(view, session, context)
        );
        failure.ShouldBeNull();
        view.CapturedInput.ShouldNotBeNull();
        view.CapturedInput.ShouldNotBeSameAs(firstInput);
        view.CapturedVendor.ShouldNotBeSameAs(firstVendor);

        await renderer.DispatchAsync(() => view.CapturedVendor!.ValueChanged.InvokeAsync("Grace"));

        live.Customer.Name.ShouldBe("Grace");
        staleLive.Number.ShouldBe("ORD-2");
        staleLive.Customer.Name.ShouldBe("Ada");
        context.IsModified().ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
    }
}
