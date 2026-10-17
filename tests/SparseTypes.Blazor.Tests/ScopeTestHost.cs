using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

// Minimal host for scope-context tests: renders one SparseSessionScope,
// captures it, and exposes mode switches without a form consumer.
internal sealed class ScopeTestHost : IDisposable
{
    private readonly Headless.Tree _tree;
    private bool _disposed;

    private ScopeTestHost(Headless.Tree tree, SparseSessionScope<OrderDto> scope)
    {
        _tree = tree;
        Scope = scope;
    }

    public SparseSessionScope<OrderDto> Scope { get; }

    public SparseSessionScopeContext<OrderDto> ScopeContext =>
        Scope.ScopeContext ?? throw new InvalidOperationException("The scope has no context.");

    public static async Task<ScopeTestHost> CreateAsync(
        ISparseEditSession<OrderDto> session,
        SparseChildEditMode? mode = null
    )
    {
        SparseSessionScope<OrderDto>? scope = null;
        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<SparseSessionScope<OrderDto>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<OrderDto>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<OrderDto>.Mode), mode);
            builder.AddComponentReferenceCapture(
                3,
                component => scope = (SparseSessionScope<OrderDto>)component
            );
            builder.CloseComponent();
        };
        var tree = await Headless.Tree.CreateAsync(fragment);
        if (scope is null)
        {
            throw new InvalidOperationException("The test scope was not captured.");
        }

        return new ScopeTestHost(tree, scope);
    }

    public async Task SetModeAsync(SparseChildEditMode? mode)
    {
        var session = Scope.Session ?? throw new InvalidOperationException("No session.");
        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<SparseSessionScope<OrderDto>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<OrderDto>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<OrderDto>.Mode), mode);
            builder.CloseComponent();
        };
        await _tree.ShowAsync(fragment);
    }

    public async Task<Exception?> TrySetModeAsync(SparseChildEditMode? mode)
    {
        var session = Scope.Session ?? throw new InvalidOperationException("No session.");
        RenderFragment fragment = builder =>
        {
            builder.OpenComponent<SparseSessionScope<OrderDto>>(0);
            builder.AddAttribute(1, nameof(SparseSessionScope<OrderDto>.Session), session);
            builder.AddAttribute(2, nameof(SparseSessionScope<OrderDto>.Mode), mode);
            builder.CloseComponent();
        };
        return await _tree.TryShowAsync(fragment);
    }

    public Task FlushAsync() => _tree.FlushAsync();

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _tree.Dispose();
        }
    }
}
