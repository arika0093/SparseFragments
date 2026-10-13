using System.Linq.Expressions;
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

// Shared renderer harness for the headless component tests (issue #271): a
// dependency-free renderer driving hand-built trees through the real diff
// engine, plus generic fragment builders whose TModel/TValue/TPaths infer from
// the session and path exactly like the Razor call sites they mirror.
internal static class Headless
{
    // Third-party-style input: value/callback/expression triple with no vendor package.
    internal sealed class VendorField<TValue> : ComponentBase
    {
        [Parameter]
        public TValue? Value { get; set; }

        [Parameter]
        public EventCallback<TValue?> ValueChanged { get; set; }

        [Parameter]
        public Expression<Func<TValue?>>? ValueExpression { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddContent(1, Value);
            builder.CloseElement();
        }
    }

    internal sealed class Root : ComponentBase
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) =>
            builder.AddContent(0, ChildContent);
    }

    internal sealed class Counter : ComponentBase
    {
        [Parameter]
        public RenderFragment? ChildContent { get; set; }

        public int Renders { get; private set; }

        protected override void OnAfterRender(bool firstRender) => Renders++;

        protected override void BuildRenderTree(RenderTreeBuilder builder) =>
            builder.AddContent(0, ChildContent);
    }

    internal static RenderFragment Edit<TModel, TPaths>(
        ISparseEditSessionWithPaths<TModel, TPaths> session,
        Action<SparseEditContext<TModel, TPaths>> capture,
        RenderFragment<SparseEditContext<TModel, TPaths>>? child = null
    )
        where TModel : class
        where TPaths : ISparseTypedPath<TModel, TModel> =>
        builder =>
        {
            builder.OpenComponent<SparseEdit<TModel, TPaths>>(0);
            builder.AddAttribute(1, nameof(SparseEdit<TModel, TPaths>.Session), session);
            builder.AddAttribute(
                2,
                nameof(SparseEdit<TModel, TPaths>.ChildContent),
                (RenderFragment<SparseEditContext<TModel, TPaths>>)(
                    edit =>
                        inner =>
                        {
                            capture(edit);
                            if (child is not null)
                            {
                                inner.AddContent(0, child(edit));
                            }
                        }
                )
            );
            builder.CloseComponent();
        };

    internal static RenderFragment Field<TModel, TValue>(
        ISparseTypedPath<TModel, TValue> path,
        Action<SparseFieldContext<TModel, TValue>> capture,
        RenderFragment<SparseFieldContext<TModel, TValue>>? child = null
    )
        where TModel : class =>
        builder =>
        {
            builder.OpenComponent<SparseField<TModel, TValue>>(0);
            builder.AddAttribute(1, nameof(SparseField<TModel, TValue>.Path), path);
            builder.AddAttribute(
                2,
                nameof(SparseField<TModel, TValue>.ChildContent),
                (RenderFragment<SparseFieldContext<TModel, TValue>>)(
                    field =>
                        inner =>
                        {
                            capture(field);
                            if (child is not null)
                            {
                                inner.AddContent(0, child(field));
                            }
                        }
                )
            );
            builder.CloseComponent();
        };

    // Compact copy of the renderer harness in BlazorToolkitNeutralTests: runs
    // the real component diff on the default dispatcher and surfaces render
    // failures without throwing them off-thread.
    internal sealed class HeadlessRenderer : Renderer
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CreateDefault();
        private readonly List<Exception> _failures = new();

        public HeadlessRenderer(IServiceProvider services, ILoggerFactory loggers)
            : base(services, loggers) { }

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

        public Task DispatchAsync(Action work) => Dispatcher.InvokeAsync(work);

        public async Task<Exception?> TryShowAsync(int componentId, ParameterView parameters)
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
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }

    // Owns one rendered headless tree; re-showing replaces the root content so
    // session swaps and unmounts dispose and rewire exactly like Razor does.
    internal sealed class Tree : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HeadlessRenderer _renderer;
        private readonly int _rootId;
        private bool _disposed;

        private Tree(ServiceProvider services, HeadlessRenderer renderer, int rootId)
        {
            _services = services;
            _renderer = renderer;
            _rootId = rootId;
        }

        public static async Task<Tree> CreateAsync(RenderFragment fragment)
        {
            var services = new ServiceCollection().AddLogging().BuildServiceProvider();
            var renderer = new HeadlessRenderer(
                services,
                services.GetRequiredService<ILoggerFactory>()
            );
            var rootId = await renderer.RenderRootAsync(new Root(), RootParameters(fragment));
            await renderer.DispatchAsync(() => { });
            return new Tree(services, renderer, rootId);
        }

        public async Task ShowAsync(RenderFragment fragment)
        {
            await _renderer.RenderAsync(_rootId, RootParameters(fragment));
            await _renderer.DispatchAsync(() => { });
        }

        public async Task<Exception?> TryShowAsync(RenderFragment fragment) =>
            await _renderer.TryShowAsync(_rootId, RootParameters(fragment));

        public Task FlushAsync() => _renderer.DispatchAsync(() => { });

        public Task DispatchAsync(Func<Task> work) => _renderer.DispatchAsync(work);

        public Task DispatchAsync(Action work) => _renderer.DispatchAsync(work);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_dispatcher is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _services.Dispose();
        }

        private Dispatcher _dispatcher => _renderer.Dispatcher;

        private static ParameterView RootParameters(RenderFragment fragment) =>
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(Root.ChildContent)] = fragment }
            );
    }
}
