using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// A dependency-free test renderer inherently builds render trees by hand, so
// the experimental RenderTree API surface it needs is acknowledged here.
#pragma warning disable BL0006

namespace SparseTypes.Blazor.Tests;

// Session-scope lifecycle for the OrderDto fixture.
public sealed class SparseSessionScopeTests
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
            Tags = new() { "fragile" },
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    // Reads the cascaded session through trusted framework access (which keeps
    // Preserve the observable-change cache while rendering form values.
    private sealed class ScopeConsumer : ComponentBase
    {
        [CascadingParameter]
        public ISparseEditSession<OrderDto>? Session { get; set; }

        [CascadingParameter]
        public SparseSessionScopeContext<OrderDto>? ScopeContext { get; set; }

        public int RenderCount { get; private set; }

        public int RenderThreadId { get; private set; }

        public string ShownText { get; private set; } = string.Empty;

        public bool ShownHasChanges { get; private set; }

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (Session is ISparseEditSessionModelAccessor<OrderDto> accessor)
            {
                var model = accessor.GetModelForFrameworkAccess();
                ShownText =
                    model.Number
                    + "|"
                    + model.Customer.Name
                    + "|"
                    + string.Join(",", model.Lines.Select(static line => line.Sku));
            }
            else
            {
                ShownText = string.Empty;
            }

            ShownHasChanges = Session?.HasChanges ?? false;
        }

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            RenderCount++;
            RenderThreadId = Environment.CurrentManagedThreadId;
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "p");
            builder.AddContent(1, ShownText);
            builder.AddContent(2, ShownHasChanges ? "changed" : "clean");
            builder.CloseElement();
        }
    }

    // Posts continuations back to the owning dispatcher thread.
    private sealed class ScopeSynchronizationContext : SynchronizationContext
    {
        private readonly ScopeDispatcher _dispatcher;

        public ScopeSynchronizationContext(ScopeDispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            _ = _dispatcher.InvokeAsync(() => callback(state));
        }

        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (_dispatcher.CheckAccess())
            {
                callback(state);
            }
            else
            {
                _dispatcher.InvokeAsync(() => callback(state)).GetAwaiter().GetResult();
            }
        }

        public override SynchronizationContext CreateCopy() =>
            new ScopeSynchronizationContext(_dispatcher);
    }

    // Every queued callback runs on one dedicated thread, so InvokeAsync
    // marshalling is observable and thread identity stays deterministic.
    private sealed class ScopeDispatcher : Dispatcher, IDisposable
    {
        private sealed record WorkItem
        {
            public required Func<Task> Run { get; init; }

            public required TaskCompletionSource<object?> Completion { get; init; }
        }

        private readonly BlockingCollection<WorkItem> _queue = new();
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _started = new(false);
        private int _dispatcherThreadId;
        private bool _disposed;

        public ScopeDispatcher()
        {
            _thread = new Thread(RunLoop) { IsBackground = true };
            _thread.Start();
            _started.Wait();
        }

        public int DispatcherThreadId => _dispatcherThreadId;

        public override bool CheckAccess() =>
            Environment.CurrentManagedThreadId == _dispatcherThreadId;

        public override Task InvokeAsync(Action workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            if (CheckAccess())
            {
                try
                {
                    workItem();
                    return Task.CompletedTask;
                }
                catch (Exception exception)
                {
                    return Task.FromException(exception);
                }
            }

            return Enqueue(() =>
            {
                workItem();
                return Task.CompletedTask;
            });
        }

        public override Task InvokeAsync(Func<Task> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            if (CheckAccess())
            {
                try
                {
                    return workItem();
                }
                catch (Exception exception)
                {
                    return Task.FromException(exception);
                }
            }

            return Enqueue(workItem);
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            if (CheckAccess())
            {
                try
                {
                    return Task.FromResult(workItem());
                }
                catch (Exception exception)
                {
                    return Task.FromException<TResult>(exception);
                }
            }

            var completion = new TaskCompletionSource<TResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _ = Enqueue(() =>
            {
                try
                {
                    completion.SetResult(workItem());
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }

                return Task.CompletedTask;
            });
            return completion.Task;
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            ArgumentNullException.ThrowIfNull(workItem);
            if (CheckAccess())
            {
                try
                {
                    return workItem();
                }
                catch (Exception exception)
                {
                    return Task.FromException<TResult>(exception);
                }
            }

            var completion = new TaskCompletionSource<TResult>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _queue.Add(
                new WorkItem
                {
                    Run = async () =>
                    {
                        try
                        {
                            // Continuations post back through the dispatcher
                            // synchronization context of the loop thread.
                            completion.SetResult(await workItem());
                        }
                        catch (Exception exception)
                        {
                            completion.SetException(exception);
                        }
                    },
                    Completion = new TaskCompletionSource<object?>(),
                }
            );
            return completion.Task;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.CompleteAdding();
            _thread.Join();
            _queue.Dispose();
            _started.Dispose();
        }

        private Task Enqueue(Func<Task> run)
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            _queue.Add(new WorkItem { Run = run, Completion = completion });
            return completion.Task;
        }

        private void RunLoop()
        {
            _dispatcherThreadId = Environment.CurrentManagedThreadId;
            SynchronizationContext.SetSynchronizationContext(new ScopeSynchronizationContext(this));
            _started.Set();
            // Incomplete tasks complete through a continuation, so re-posted
            // continuations cannot deadlock the thread.
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                Task pending;
                try
                {
                    pending = item.Run();
                }
                catch (Exception exception)
                {
                    item.Completion.SetException(exception);
                    continue;
                }

                if (pending.IsCompleted)
                {
                    Propagate(item.Completion, pending);
                }
                else
                {
                    pending.ContinueWith(
                        static (task, state) =>
                            Propagate((TaskCompletionSource<object?>)state!, task),
                        item.Completion,
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                }
            }
        }

        private static void Propagate(TaskCompletionSource<object?> completion, Task pending)
        {
            if (pending.IsCanceled)
            {
                completion.SetCanceled();
            }
            else if (pending.IsFaulted)
            {
                completion.SetException(pending.Exception!.InnerExceptions);
            }
            else
            {
                completion.SetResult(null);
            }
        }
    }

    // Runs the real component diff on the dedicated dispatcher thread so
    // InvokeAsync marshalling is observable.
    private sealed class ScopeTestRenderer : Renderer
    {
        private readonly ScopeDispatcher _dispatcher;

        public ScopeTestRenderer(
            IServiceProvider services,
            ILoggerFactory loggers,
            ScopeDispatcher dispatcher
        )
            : base(services, loggers)
        {
            _dispatcher = dispatcher;
        }

        public override Dispatcher Dispatcher => _dispatcher;

        public Task<int> RenderRootAsync(
            Microsoft.AspNetCore.Components.IComponent component,
            ParameterView parameters
        ) =>
            Dispatcher.InvokeAsync(async () =>
            {
                var componentId = AssignRootComponentId(component);
                await RenderRootComponentAsync(componentId, parameters);
                return componentId;
            });

        public Task RenderAsync(int componentId, ParameterView parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(componentId, parameters));

        public Task DispatchAsync(Func<Task> work) => Dispatcher.InvokeAsync(work);

        public Task DispatchAsync(Action work) => Dispatcher.InvokeAsync(work);

        protected override void HandleException(Exception exception)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }

    // Owns one rendered scope tree: the scope under test plus its captured
    // consumer.
    private sealed class ScopeHarness : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly ScopeDispatcher _dispatcher;
        private readonly ScopeTestRenderer _renderer;
        private readonly RenderFragment _child;
        private readonly int _rootId;
        private bool _disposed;

        public SparseSessionScope<OrderDto> Scope { get; }

        public ScopeConsumer Consumer { get; }

        public int DispatcherThreadId => _dispatcher.DispatcherThreadId;

        private ScopeHarness(
            ServiceProvider services,
            ScopeDispatcher dispatcher,
            ScopeTestRenderer renderer,
            RenderFragment child,
            int rootId,
            SparseSessionScope<OrderDto> scope,
            ScopeConsumer consumer
        )
        {
            _services = services;
            _dispatcher = dispatcher;
            _renderer = renderer;
            _child = child;
            _rootId = rootId;
            Scope = scope;
            Consumer = consumer;
        }

        public static async Task<ScopeHarness> CreateAsync(
            ISparseEditSession<OrderDto> session,
            bool? autoAccept = null
        )
        {
            var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var dispatcher = new ScopeDispatcher();
            var renderer = new ScopeTestRenderer(
                services,
                services.GetRequiredService<ILoggerFactory>(),
                dispatcher
            );
            var scope = new SparseSessionScope<OrderDto>();
            ScopeConsumer? consumer = null;
            RenderFragment child = builder =>
            {
                builder.OpenComponent<ScopeConsumer>(0);
                builder.AddComponentReferenceCapture(
                    1,
                    component => consumer = (ScopeConsumer)component
                );
                builder.CloseComponent();
            };
            var rootId = await renderer.RenderRootAsync(
                scope,
                ParametersFor(session, child, autoAccept)
            );
            await renderer.DispatchAsync(() => { });
            if (consumer is null)
            {
                throw new InvalidOperationException("The test consumer was not captured.");
            }

            return new ScopeHarness(services, dispatcher, renderer, child, rootId, scope, consumer);
        }

        public async Task SwapSessionAsync(ISparseEditSession<OrderDto> session)
        {
            await _renderer.RenderAsync(_rootId, ParametersFor(session, _child));
            await _renderer.DispatchAsync(() => { });
        }

        public async Task SetAutoAcceptAsync(bool? autoAccept)
        {
            await _renderer.RenderAsync(
                _rootId,
                ParametersFor((ISparseEditSession<OrderDto>)Scope.Session!, _child, autoAccept)
            );
            await _renderer.DispatchAsync(() => { });
        }

        public Task FlushAsync() => _renderer.DispatchAsync(() => { });

        public Task DisposeScopeAsync() => _renderer.DispatchAsync(Scope.Dispose);

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _renderer.DispatchAsync(Scope.Dispose).GetAwaiter().GetResult();
                _dispatcher.Dispose();
                _services.Dispose();
            }
        }

        private static ParameterView ParametersFor(
            ISparseEditSession<OrderDto> session,
            RenderFragment child,
            bool? autoAccept = null
        ) =>
            ParameterView.FromDictionary(
                new Dictionary<string, object?>
                {
                    [nameof(SparseSessionScope<OrderDto>.Session)] = session,
                    [nameof(SparseSessionScope<OrderDto>.AutoAccept)] = autoAccept,
                    [nameof(SparseSessionScope<OrderDto>.ChildContent)] = child,
                }
            );
    }

    [Test]
    public async Task NestedEditRefreshesCascadedConsumerWithoutManualWiring()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var consumer = harness.Consumer;
        consumer.ShownText.ShouldBe("ORD-1|Ada|a,b");
        consumer.ShownHasChanges.ShouldBeFalse();
        var baselineRenders = consumer.RenderCount;

        // A deep nested edit through the observable proxy; the consumer owns no
        // TransitionObserved subscription and issues no InvokeAsync of its own.
        var session = (OrderDto.EditSession)harness.Scope.Session!;
        session.Observable.Customer!.Name = "Grace";
        await harness.FlushAsync();

        consumer.RenderCount.ShouldBe(baselineRenders + 1);
        consumer.ShownText.ShouldBe("ORD-1|Grace|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task BatchEditRefreshesOnceWithNetState()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var session = (OrderDto.EditSession)harness.Scope.Session!;
        var consumer = harness.Consumer;
        var notifications = 0;
        void Count(object? sender, PropertyChangedEventArgs args) => notifications++;
        var notifier = (INotifyPropertyChanged)session;
        notifier.PropertyChanged += Count;
        try
        {
            session.BatchEdit(() =>
            {
                session.Observable.Number = "ORD-2";
                session.Observable.Customer!.Name = "Grace";
                session.Observable.Tags.Add("heavy");
            });
        }
        finally
        {
            notifier.PropertyChanged -= Count;
        }

        // One outer batch publishes one notification however many edits it
        // holds, so the scope refreshes once for the whole user action.
        notifications.ShouldBe(1);
        await harness.FlushAsync();
        consumer.ShownText.ShouldBe("ORD-2|Grace|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void IndividualEditsNotifyPerEdit()
    {
        var session = Order().CreateEditSession();
        var notifications = 0;
        void Count(object? sender, PropertyChangedEventArgs args) => notifications++;
        var notifier = (INotifyPropertyChanged)session;
        notifier.PropertyChanged += Count;
        try
        {
            session.Observable.Number = "ORD-2";
            session.Observable.Customer!.Name = "Grace";
            session.Observable.Tags.Add("heavy");
        }
        finally
        {
            notifier.PropertyChanged -= Count;
        }

        // Without BatchEdit each edit refreshes on its own; batch user actions
        // instead.
        notifications.ShouldBe(3);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task CollectionMoveRefreshesInOrder()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var session = (OrderDto.EditSession)harness.Scope.Session!;
        var consumer = harness.Consumer;

        session.Observable.Lines.Move(0, 1);
        await harness.FlushAsync();

        consumer.ShownText.ShouldBe("ORD-1|Ada|b,a");
        consumer.ShownHasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task RevertAcceptReloadRefresh()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var session = (OrderDto.EditSession)harness.Scope.Session!;
        var consumer = harness.Consumer;

        session.Observable.Number = "ORD-2";
        await harness.FlushAsync();
        consumer.ShownHasChanges.ShouldBeTrue();

        session.RevertChanges();
        await harness.FlushAsync();
        consumer.ShownText.ShouldBe("ORD-1|Ada|a,b");
        consumer.ShownHasChanges.ShouldBeFalse();

        session.Observable.Number = "ORD-3";
        await harness.FlushAsync();
        session.AcceptChanges();
        session.HasChanges.ShouldBeFalse();
        await harness.FlushAsync();
        consumer.ShownText.ShouldBe("ORD-3|Ada|a,b");
        consumer.ShownHasChanges.ShouldBeFalse();

        // A disjoint server state merges in place; the live instance stays
        // bound.
        session.Observable.Number = "local";
        var server = Order();
        server.Number = "ORD-3";
        server.Customer.Name = "Server";
        var reload = session.Reload(server);
        reload.HasConflicts.ShouldBeFalse();
        await harness.FlushAsync();
        consumer.ShownText.ShouldBe("local|Server|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();

        // An overlapping server state conflicts and leaves everything
        // untouched.
        var conflicting = Order();
        conflicting.Number = "server";
        conflicting.Customer.Name = "Server";
        var conflicted = session.Reload(conflicting);
        conflicted.HasConflicts.ShouldBeTrue();
        await harness.FlushAsync();
        consumer.ShownText.ShouldBe("local|Server|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task SessionReplacementDropsOldSession()
    {
        var first = Order().CreateEditSession();
        using var harness = await ScopeHarness.CreateAsync(first);
        var consumer = harness.Consumer;

        var second = Order().CreateEditSession();
        second.Observable.Number = "ORD-2";
        await harness.SwapSessionAsync(second);
        ReferenceEquals(consumer.Session, second).ShouldBeTrue();
        consumer.ShownText.ShouldBe("ORD-2|Ada|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();

        // Stale edits on the replaced session no longer refresh this subtree.
        var renders = consumer.RenderCount;
        first.Observable.Number = "STALE";
        await harness.FlushAsync();
        consumer.RenderCount.ShouldBe(renders);
        consumer.ShownText.ShouldBe("ORD-2|Ada|a,b");

        // The active session still refreshes exactly once per edit, not twice.
        second.Observable.Number = "ORD-3";
        await harness.FlushAsync();
        consumer.RenderCount.ShouldBe(renders + 1);
        consumer.ShownText.ShouldBe("ORD-3|Ada|a,b");
    }

    [Test]
    public async Task DisposeStopsRefresh()
    {
        var session = Order().CreateEditSession();
        using var harness = await ScopeHarness.CreateAsync(session);
        var child = harness.Consumer.ScopeContext!.OpenChildSession(
            session,
            OrderDto.SparsePath.Customer
        );
        child.Session.Observable.Name = "draft";
        await harness.FlushAsync();
        var consumer = harness.Consumer;
        var renders = consumer.RenderCount;

        await harness.DisposeScopeAsync();
        harness.Scope.Dispose();
        session.Observable.Number = "ORD-2";
        await harness.FlushAsync();

        consumer.RenderCount.ShouldBe(renders);
        session.HasPendingChildSessions.ShouldBeFalse();
        Should.Throw<ObjectDisposedException>(() => child.Session);
        Should.Throw<ObjectDisposedException>(() => child.DiscardChanges());
    }

    [Test]
    public async Task TwoIndependentTreesStayIsolated()
    {
        using var first = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        using var second = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var firstSession = (OrderDto.EditSession)first.Scope.Session!;
        var secondSession = (OrderDto.EditSession)second.Scope.Session!;

        firstSession.Observable.Number = "FIRST";
        await first.FlushAsync();
        await second.FlushAsync();

        first.Consumer.ShownText.ShouldBe("FIRST|Ada|a,b");
        first.Consumer.ShownHasChanges.ShouldBeTrue();
        second.Consumer.ShownText.ShouldBe("ORD-1|Ada|a,b");
        second.Consumer.ShownHasChanges.ShouldBeFalse();

        secondSession.Observable.Number = "SECOND";
        await second.FlushAsync();
        await first.FlushAsync();

        second.Consumer.ShownText.ShouldBe("SECOND|Ada|a,b");
        first.Consumer.ShownText.ShouldBe("FIRST|Ada|a,b");
    }

    [Test]
    public async Task BackgroundThreadEditMarshalsToDispatcher()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var session = (OrderDto.EditSession)harness.Scope.Session!;
        var consumer = harness.Consumer;
        consumer.RenderThreadId.ShouldBe(harness.DispatcherThreadId);

        // A direct StateHasChanged here would throw off-thread; the scope
        // marshals instead.
        var editorThread = 0;
        await Task.Run(() =>
        {
            editorThread = Environment.CurrentManagedThreadId;
            session.Observable.Number = "ORD-9";
        });
        editorThread.ShouldNotBe(harness.DispatcherThreadId);
        await harness.FlushAsync();

        consumer.ShownText.ShouldBe("ORD-9|Ada|a,b");
        consumer.ShownHasChanges.ShouldBeTrue();
        consumer.RenderThreadId.ShouldBe(harness.DispatcherThreadId);
    }

    [Test]
    public async Task ScopeOwnedChildContextKeepsLocalHistoryAndReportsConflicts()
    {
        var live = Order();
        using var harness = await ScopeHarness.CreateAsync(live.CreateEditSession());
        var parent = (OrderDto.EditSession)harness.Scope.Session!;
        var typedChild = harness.Consumer.ScopeContext!.OpenChildSession(
            parent,
            OrderDto.SparsePath.Customer
        );
        ISparseChildEditContext<OrderCustomer, SparseConflict> child = typedChild;
        var childSession = typedChild.Session;
        ReferenceEquals(child.Session, childSession).ShouldBeTrue();

        childSession.Observable.Name = "child";
        child.CanUndo.ShouldBeTrue();
        child.Undo();
        childSession.Observable.Name.ShouldBe("Ada");
        child.Redo();

        parent.Observable.Customer!.Name = "parent";
        child.TryAcceptChanges(out var conflicts).ShouldBeFalse();
        conflicts.ShouldNotBeNull();
        child.HasChanges.ShouldBeTrue();
        live.Customer.Name.ShouldBe("parent");

        child.DiscardChanges();
        child.HasChanges.ShouldBeFalse();
        childSession.Observable.Name.ShouldBe("Ada");

        childSession.Observable.Email = "child@example.com";
        child.TryAcceptChanges(out conflicts).ShouldBeTrue();
        conflicts.ShouldBeNull();
        live.Customer.Email.ShouldBe("child@example.com");
        child.CanUndo.ShouldBeFalse();
    }

    [Test]
    public async Task AutoAcceptMergesChildBatchAsOneParentHistoryEntry()
    {
        var model = Order();
        var parent = model.CreateEditSession();
        var parentHistory = parent.EnableHistory();
        using var harness = await ScopeHarness.CreateAsync(parent, autoAccept: true);
        var scope = harness.Consumer.ScopeContext!;
        var child = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);
        var acceptedCount = 0;
        child.AutoAccepted += () => acceptedCount++;

        child.Session.BatchEdit(() =>
        {
            child.Session.Observable.Name = "Grace";
            child.Session.Observable.Email = "grace@example.com";
        });
        await harness.FlushAsync();

        model.Customer.Name.ShouldBe("Grace");
        model.Customer.Email.ShouldBe("grace@example.com");
        child.HasChanges.ShouldBeFalse();
        child.AutoAcceptConflicts.ShouldBeNull();
        scope.HasChanges.ShouldBeFalse();
        acceptedCount.ShouldBe(1);
        parentHistory.CanUndo.ShouldBeTrue();

        parentHistory.Undo();
        await harness.FlushAsync();
        model.Customer.Name.ShouldBe("Ada");
        model.Customer.Email.ShouldBe("ada@example.com");
        parent.HasChanges.ShouldBeFalse();
        child.HasChanges.ShouldBeFalse();
        scope.HasChanges.ShouldBeFalse();

        parentHistory.CanRedo.ShouldBeTrue();
        parentHistory.Redo();
        await harness.FlushAsync();
        model.Customer.Name.ShouldBe("Grace");
        model.Customer.Email.ShouldBe("grace@example.com");
        parent.HasChanges.ShouldBeTrue();
        child.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task AutoAcceptModeChangeIsRejectedWhileChildDraftIsPending()
    {
        var model = Order();
        var parent = model.CreateEditSession();
        using var harness = await ScopeHarness.CreateAsync(parent);
        var scope = harness.Consumer.ScopeContext!;
        var child = scope.OpenChildSession(parent, OrderDto.SparsePath.Customer);

        child.Session.Observable.Name = "Child";
        parent.Observable.Customer!.Name = "Parent";
        InvalidOperationException? rejection = null;
        try
        {
            await harness.SetAutoAcceptAsync(true);
        }
        catch (InvalidOperationException exception)
        {
            rejection = exception;
        }

        rejection.ShouldNotBeNull();
        rejection!.Message.ShouldContain("while child edits are pending");
        child.AutoAcceptConflicts.ShouldBeNull();
        child.HasChanges.ShouldBeTrue();
        scope.HasChanges.ShouldBeTrue();
        model.Customer.Name.ShouldBe("Parent");

        child.DiscardChanges();
        await harness.SetAutoAcceptAsync(true);
        child.HasChanges.ShouldBeFalse();
    }

    [Test]
    public async Task SessionReplacementReleasesScopeOwnedChildAndHistory()
    {
        using var harness = await ScopeHarness.CreateAsync(Order().CreateEditSession());
        var first = (OrderDto.EditSession)harness.Scope.Session!;
        var child = harness.Consumer.ScopeContext!.OpenChildSession(
            first,
            OrderDto.SparsePath.Customer
        );
        child.Session.Observable.Name = "draft";

        var second = Order().CreateEditSession();
        await harness.SwapSessionAsync(second);

        first.HasPendingChildSessions.ShouldBeFalse();
        first.History.ShouldBeNull();
        harness.Consumer.ScopeContext.ShouldNotBeNull();
        harness
            .Consumer.ScopeContext.OpenChildSession(second, OrderDto.SparsePath.Customer)
            .Session.Observable.Name.ShouldBe("Ada");
        Should.Throw<ObjectDisposedException>(() => child.DiscardChanges());
    }

    [Test]
    public async Task ScopePreservesObservableHasChangesCache()
    {
        var session = Order().CreateEditSession();
        session.Observable.Number = "ORD-2";
        session.HasChanges.ShouldBeTrue();
        CacheState(session).ShouldBe((Caching: true, Valid: true));

        using var harness = await ScopeHarness.CreateAsync(session);
        await harness.FlushAsync();
        session.Observable.Customer!.Name = "Grace";
        await harness.FlushAsync();
        session.HasChanges.ShouldBeTrue();

        // Subscribing and refreshing through the scope never touches the raw
        // model.
        CacheState(session).ShouldBe((Caching: true, Valid: true));
        harness.Consumer.ShownHasChanges.ShouldBeTrue();
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
