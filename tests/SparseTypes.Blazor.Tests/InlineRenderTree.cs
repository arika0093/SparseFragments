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

// Inline headless tree: processes every dispatcher post synchronously, so
// each session notification renders its own batch immediately instead of
// coalescing. This exposes the child-before-parent ordering where a disposed
// descendant context would otherwise be masked by a coalescing dispatcher.
internal sealed class InlineTree : IDisposable
{
    private readonly ServiceProvider _services;
    private readonly InlineRenderer _renderer;
    private readonly int _rootId;
    private bool _disposed;

    private InlineTree(ServiceProvider services, InlineRenderer renderer, int rootId)
    {
        _services = services;
        _renderer = renderer;
        _rootId = rootId;
    }

    public static async Task<InlineTree> CreateAsync(RenderFragment fragment)
    {
        var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var renderer = new InlineRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var rootId = await renderer.RenderRootAsync(
            new Headless.Root(),
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(Headless.Root.ChildContent)] = fragment }
            )
        );
        return new InlineTree(services, renderer, rootId);
    }

    public Task ShowAsync(RenderFragment fragment) =>
        _renderer.RenderAsync(
            _rootId,
            ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(Headless.Root.ChildContent)] = fragment }
            )
        );

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _services.Dispose();
        }
    }

    private sealed class InlineDispatcher : Dispatcher
    {
        public override bool CheckAccess() => true;

        public override Task InvokeAsync(Action workItem)
        {
            workItem();
            return Task.CompletedTask;
        }

        public override Task InvokeAsync(Func<Task> workItem) => workItem();

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) =>
            Task.FromResult(workItem());

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) =>
            workItem();
    }

    private sealed class InlineRenderer : Renderer
    {
        private readonly InlineDispatcher _dispatcher = new();

        public InlineRenderer(IServiceProvider services, ILoggerFactory loggers)
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

        protected override void HandleException(Exception exception) =>
            ExceptionDispatchInfo.Capture(exception).Throw();

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }
}
