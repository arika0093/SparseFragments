using System.Linq.Expressions;
using System.Net.Http;
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

// Render the documented form flow with a minimal Renderer to exercise rerenders
// through the real diff engine, including explicit conflict actions.
public sealed class BlazorEditFormRenderedTests
{
    private abstract record SaveResult;

    private sealed record Saved : SaveResult
    {
        public OrderDto Persisted { get; init; } = new();
    }

    private sealed record Conflicted : SaveResult
    {
        public OrderDto ServerState { get; init; } = new();
    }

    private enum SaveMode
    {
        Saved,
        Conflict,
    }

    private sealed class StubSaveService
    {
        public SaveMode Mode { get; set; } = SaveMode.Saved;

        public OrderDto PersistedState { get; set; } = Order();

        public OrderDto ServerState { get; set; } = Order();

        public List<OrderDto.ChangePayload> Received { get; } = new();

        public async Task<SaveResult> SaveAsync(
            OrderDto.ChangePayload payload,
            CancellationToken cancellationToken
        )
        {
            Received.Add(payload);
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return Mode == SaveMode.Conflict
                ? new Conflicted { ServerState = ServerState }
                : new Saved { Persisted = PersistedState };
        }
    }

    // Mirror the Razor handlers so rendered tests cover the same adopt,
    // discard, and disjoint-merge flow.
    private sealed class OrderFormDriver
    {
        public OrderDto Live = Order();

        public OrderDto.EditSession Session = null!;

        public EditContext Context = null!;

        public ValidationMessageStore Store = null!;

        public OrderDto? LatestServer;

        public string? Notice;

        public string? TransportError;

        public bool IsSaving;

        public StubSaveService SaveService = new();

        public bool HasChanges => Session.HasChanges;

        public string CurrentNumber => Session.Current.Number;

        public OrderFormDriver() => Adopt(Live.CreateEditSession());

        public void OnNumberChanged(string value)
        {
            Session.Observable.Number = value;
            Context.NotifyFieldChanged(Session.Field(nameof(OrderDto.Number)));
        }

        public async Task SubmitAsync()
        {
            IsSaving = true;
            TransportError = null;
            try
            {
                var submitted = Session.CreateChangeSet();
                var result = await SaveService.SaveAsync(
                    submitted.ToPayload(),
                    CancellationToken.None
                );
                if (result is Saved saved)
                {
                    LatestServer = null;
                    Notice = null;
                    Adopt(saved.Persisted.CreateEditSession());
                }
                else if (result is Conflicted conflicted)
                {
                    LatestServer = conflicted.ServerState;
                    var probe = CopyServerState(conflicted.ServerState);
                    if (submitted.TryApplyTo(probe, out _, out var conflicts))
                    {
                        var reload = Session.Reload(conflicted.ServerState);
                        if (!reload.HasConflicts)
                        {
                            LatestServer = null;
                            Notice =
                                "Server changed other fields. Merged the latest values; review and save again.";
                        }
                        else
                        {
                            Notice =
                                "Server reported a conflict. Use the server value or edit to resolve.";
                        }
                    }
                    else
                    {
                        foreach (var conflict in conflicts)
                        {
                            Session.AddValidationError(
                                Store,
                                conflict.PathText,
                                "Server kept a newer value."
                            );
                        }
                        Notice =
                            "Server kept a newer value. Keep editing, or use the server value to discard your change.";
                        Context.NotifyValidationStateChanged();
                    }
                }
            }
            catch (HttpRequestException)
            {
                TransportError = "Save failed. Retry without losing edits.";
            }
            finally
            {
                IsSaving = false;
            }
        }

        public void UseServerValue()
        {
            if (LatestServer is null)
            {
                return;
            }
            Adopt(LatestServer.CreateEditSession());
            LatestServer = null;
            Notice = null;
        }

        private void Adopt(OrderDto.EditSession next)
        {
            if (Context is not null)
            {
                Context.OnValidationRequested -= OnValidate;
            }
            Session = next;
            Live = Session.GetModelForFrameworkAccess();
            Context = Session.CreateEditContext();
            Store = Session.CreateValidationStore(Context);
            Context.OnValidationRequested += OnValidate;
        }

        private void OnValidate(object? sender, ValidationRequestedEventArgs args)
        {
            Store.Clear();
            if (string.IsNullOrWhiteSpace(Session.Current.Number))
            {
                Store.Add(Session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        }
    }

    // Replicates the OrderEditForm.razor input subtree in code.
    private sealed class OrderFormView : ComponentBase
    {
        [Parameter]
        public EditContext FormContext { get; set; } = null!;

        [Parameter]
        public OrderDto Live { get; set; } = null!;

        [Parameter]
        public EventCallback<string> NumberChanged { get; set; }

        [Parameter]
        public EventCallback<EditContext> Save { get; set; }

        [Parameter]
        public EventCallback UseServerValue { get; set; }

        [Parameter]
        public bool ShowServerAction { get; set; }

        [Parameter]
        public string? Notice { get; set; }

        public InputText? CapturedInput { get; private set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<EditForm>(0);
            builder.SetKey(FormContext);
            builder.AddAttribute(1, nameof(EditForm.EditContext), FormContext);
            builder.AddAttribute(2, nameof(EditForm.OnValidSubmit), Save);
            builder.AddAttribute(
                3,
                "ChildContent",
                (RenderFragment<EditContext>)(
                    _ =>
                        form =>
                        {
                            form.OpenComponent<InputText>(0);
                            form.AddAttribute(1, nameof(InputText.Value), Live.Number);
                            form.AddAttribute(2, nameof(InputText.ValueChanged), NumberChanged);
                            form.AddAttribute(
                                3,
                                nameof(InputText.ValueExpression),
                                (Expression<Func<string>>)(() => Live.Number)
                            );
                            form.AddComponentReferenceCapture(
                                4,
                                component => CapturedInput = (InputText)component
                            );
                            form.CloseComponent();
                            if (ShowServerAction)
                            {
                                form.OpenElement(5, "button");
                                form.AddAttribute(6, "type", "button");
                                form.AddAttribute(7, "onclick", UseServerValue);
                                form.AddContent(8, "Use server value");
                                form.CloseElement();
                            }
                            if (Notice is not null)
                            {
                                form.OpenElement(9, "p");
                                form.AddContent(10, Notice);
                                form.CloseElement();
                            }
                        }
                )
            );
            builder.CloseComponent();
        }
    }

    // Bare cascaded input without EditForm's region teardown.
    private sealed class BareInputView : ComponentBase
    {
        [Parameter]
        public EditContext FormContext { get; set; } = null!;

        [Parameter]
        public string Value { get; set; } = string.Empty;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<EditContext>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<EditContext>.Value), FormContext);
            builder.AddAttribute(2, nameof(CascadingValue<EditContext>.IsFixed), false);
            builder.AddAttribute(
                3,
                nameof(CascadingValue<EditContext>.ChildContent),
                (RenderFragment)(
                    input =>
                    {
                        input.OpenComponent<InputText>(0);
                        input.AddAttribute(1, nameof(InputText.Value), Value);
                        input.AddAttribute(
                            2,
                            nameof(InputText.ValueExpression),
                            (Expression<Func<string>>)(() => Value)
                        );
                        input.CloseComponent();
                    }
                )
            );
            builder.CloseComponent();
        }
    }

    // Dependency-free test renderer.
    private sealed class OrderFormRenderer : Renderer
    {
        private readonly Dispatcher dispatcher = Dispatcher.CreateDefault();
        private readonly List<Exception> failures = new();
        private readonly List<string> renderedText = new();

        public OrderFormRenderer(IServiceProvider services, ILoggerFactory loggers)
            : base(services, loggers) { }

        public override Dispatcher Dispatcher => dispatcher;

        public IReadOnlyList<string> RenderedText => renderedText;

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

        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            var frames = batch.ReferenceFrames;
            for (var index = 0; index < frames.Count; index++)
            {
                var frame = frames.Array[index];
                if (
                    frame.FrameType is RenderTreeFrameType.Text or RenderTreeFrameType.Markup
                    && frame.TextContent is string text
                )
                {
                    renderedText.Add(text);
                }
                else if (
                    frame.FrameType == RenderTreeFrameType.Attribute
                    && frame.AttributeValue is string attribute
                )
                {
                    renderedText.Add(attribute);
                }
            }

            return Task.CompletedTask;
        }
    }

    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
        };

    private static OrderDto CopyServerState(OrderDto source) =>
        new()
        {
            Number = source.Number,
            Customer = new OrderCustomer
            {
                Name = source.Customer.Name,
                Email = source.Customer.Email,
            },
            Tags = new List<string>(source.Tags),
        };

    private static Task EditNumberAsync(
        OrderFormRenderer renderer,
        OrderFormView view,
        string value
    ) => renderer.DispatchAsync(() => view.NumberChanged.InvokeAsync(value));

    private static Task SaveFormAsync(OrderFormRenderer renderer, OrderFormView view) =>
        renderer.DispatchAsync(() => view.Save.InvokeAsync());

    private static Task UseServerValueAsync(OrderFormRenderer renderer, OrderFormView view) =>
        renderer.DispatchAsync(() => view.UseServerValue.InvokeAsync());

    private static ParameterView ViewParameters(OrderFormView view, OrderFormDriver driver) =>
        ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(OrderFormView.FormContext)] = driver.Context,
                [nameof(OrderFormView.Live)] = driver.Live,
                [nameof(OrderFormView.NumberChanged)] = EventCallback.Factory.Create<string>(
                    view,
                    driver.OnNumberChanged
                ),
                [nameof(OrderFormView.Save)] = EventCallback.Factory.Create<EditContext>(
                    view,
                    (EditContext _) => driver.SubmitAsync()
                ),
                [nameof(OrderFormView.UseServerValue)] = EventCallback.Factory.Create(
                    view,
                    driver.UseServerValue
                ),
                [nameof(OrderFormView.ShowServerAction)] = driver.LatestServer is not null,
                [nameof(OrderFormView.Notice)] = driver.Notice,
            }
        );

    [Test]
    public async Task KeyedFormSurvivesSaveAcrossTwoEditCycles()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new OrderFormRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var driver = new OrderFormDriver
        {
            SaveService =
            {
                PersistedState = new OrderDto
                {
                    Number = "ORD-2",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                },
            },
        };
        var view = new OrderFormView();
        var componentId = await renderer.RenderRootAsync(view, ViewParameters(view, driver));
        var firstInput = view.CapturedInput;

        await EditNumberAsync(renderer, view, "ord-2");
        driver.Context.IsModified().ShouldBeTrue();

        await SaveFormAsync(renderer, view);
        var failure = await renderer.TryRenderAsync(componentId, ViewParameters(view, driver));
        failure.ShouldBeNull();
        view.CapturedInput.ShouldNotBeNull();
        view.CapturedInput.ShouldNotBeSameAs(firstInput);
        ReferenceEquals(driver.Context.Model, driver.Live).ShouldBeTrue();
        driver.Live.Number.ShouldBe("ORD-2");
        driver.HasChanges.ShouldBeFalse();
        driver.IsSaving.ShouldBeFalse();
        driver.SaveService.Received.Count.ShouldBe(1);

        await EditNumberAsync(renderer, view, "ORD-3");
        driver.SaveService.PersistedState = new OrderDto
        {
            Number = "ORD-3",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
        };
        await SaveFormAsync(renderer, view);
        var secondFailure = await renderer.TryRenderAsync(
            componentId,
            ViewParameters(view, driver)
        );
        secondFailure.ShouldBeNull();
        driver.Live.Number.ShouldBe("ORD-3");
        driver.HasChanges.ShouldBeFalse();
        driver.Context.IsModified().ShouldBeFalse();
        driver.SaveService.Received.Count.ShouldBe(2);
    }

    [Test]
    public async Task BareInputThrowsWhenCascadedContextChangesOnSameInstance()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new OrderFormRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var first = new EditContext(Order());
        var second = new EditContext(Order());
        var view = new BareInputView();
        var componentId = await renderer.RenderRootAsync(
            view,
            ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BareInputView.FormContext)] = first,
                    [nameof(BareInputView.Value)] = "ORD-1",
                }
            )
        );

        var failure = await renderer.TryRenderAsync(
            componentId,
            ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(BareInputView.FormContext)] = second,
                    [nameof(BareInputView.Value)] = "ORD-2",
                }
            )
        );
        failure.ShouldBeOfType<InvalidOperationException>();
    }

    [Test]
    public async Task OldValidationSubscriptionDoesNotLeakIntoNewForm()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new OrderFormRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var driver = new OrderFormDriver
        {
            SaveService =
            {
                PersistedState = new OrderDto
                {
                    Number = "ORD-2",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                },
            },
        };
        var view = new OrderFormView();
        var componentId = await renderer.RenderRootAsync(view, ViewParameters(view, driver));
        var oldContext = driver.Context;

        await EditNumberAsync(renderer, view, "ord-2");
        await SaveFormAsync(renderer, view);
        var failure = await renderer.TryRenderAsync(componentId, ViewParameters(view, driver));
        failure.ShouldBeNull();

        driver.Store.Add(driver.Session.Field(nameof(OrderDto.Number)), "Probe survives.");
        oldContext.Validate();
        driver.Context.GetValidationMessages().ShouldContain("Probe survives.");
    }

    [Test]
    public async Task UseServerValueDiscardsLocalEditAndResolvesConflict()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new OrderFormRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var driver = new OrderFormDriver
        {
            SaveService =
            {
                Mode = SaveMode.Conflict,
                ServerState = new OrderDto
                {
                    Number = "SERVER",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                },
            },
        };
        var view = new OrderFormView();
        var componentId = await renderer.RenderRootAsync(view, ViewParameters(view, driver));

        await EditNumberAsync(renderer, view, "ORD-2");
        await SaveFormAsync(renderer, view);
        var conflictFailure = await renderer.TryRenderAsync(
            componentId,
            ViewParameters(view, driver)
        );
        conflictFailure.ShouldBeNull();
        driver.LatestServer.ShouldNotBeNull();
        driver.Notice.ShouldNotBeNull();
        renderer.RenderedText.ShouldContain("Use server value");
        driver.Context.GetValidationMessages().ShouldContain("Server kept a newer value.");
        driver.HasChanges.ShouldBeTrue();
        driver.CurrentNumber.ShouldBe("ORD-2");

        await UseServerValueAsync(renderer, view);
        var resolveFailure = await renderer.TryRenderAsync(
            componentId,
            ViewParameters(view, driver)
        );
        resolveFailure.ShouldBeNull();
        driver.Live.Number.ShouldBe("SERVER");
        driver.HasChanges.ShouldBeFalse();
        driver.Context.GetValidationMessages().ShouldBeEmpty();
        driver.LatestServer.ShouldBeNull();
        driver.Notice.ShouldBeNull();

        await EditNumberAsync(renderer, view, "ORD-9");
        driver.SaveService.Mode = SaveMode.Saved;
        driver.SaveService.PersistedState = new OrderDto
        {
            Number = "ORD-9",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
        };
        await SaveFormAsync(renderer, view);
        var followOnFailure = await renderer.TryRenderAsync(
            componentId,
            ViewParameters(view, driver)
        );
        followOnFailure.ShouldBeNull();
        driver.Live.Number.ShouldBe("ORD-9");
        driver.HasChanges.ShouldBeFalse();
        driver.SaveService.Received.Count.ShouldBe(2);
    }

    [Test]
    public async Task DisjointRejectionMergesAndRetriesWithExplicitNotice()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new OrderFormRenderer(
            services,
            services.GetRequiredService<ILoggerFactory>()
        );
        var serverState = new OrderDto
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Grace", Email = "ada@example.com" },
        };
        var driver = new OrderFormDriver
        {
            SaveService = { Mode = SaveMode.Conflict, ServerState = serverState },
        };
        var view = new OrderFormView();
        var componentId = await renderer.RenderRootAsync(view, ViewParameters(view, driver));

        await EditNumberAsync(renderer, view, "ORD-2");
        await SaveFormAsync(renderer, view);
        var mergeFailure = await renderer.TryRenderAsync(componentId, ViewParameters(view, driver));
        mergeFailure.ShouldBeNull();
        driver.LatestServer.ShouldBeNull();
        driver.Notice!.ShouldContain("Merged");
        driver.Context.GetValidationMessages().ShouldBeEmpty();
        driver.Live.Number.ShouldBe("ORD-2");
        driver.Live.Customer.Name.ShouldBe("Grace");
        driver.HasChanges.ShouldBeTrue();
        serverState.Number.ShouldBe("ORD-1");
        serverState.Customer.Name.ShouldBe("Grace");

        driver.SaveService.Mode = SaveMode.Saved;
        driver.SaveService.PersistedState = new OrderDto
        {
            Number = "ORD-2",
            Customer = new OrderCustomer { Name = "Grace", Email = "ada@example.com" },
        };
        await SaveFormAsync(renderer, view);
        var retryFailure = await renderer.TryRenderAsync(componentId, ViewParameters(view, driver));
        retryFailure.ShouldBeNull();
        driver.Live.Number.ShouldBe("ORD-2");
        driver.Live.Customer.Name.ShouldBe("Grace");
        driver.HasChanges.ShouldBeFalse();
        driver.SaveService.Received.Count.ShouldBe(2);
    }
}
