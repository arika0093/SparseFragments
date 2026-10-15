using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

namespace SparseTypes.Blazor.Tests;

public sealed class SparseFieldQueryTests
{
    private sealed class QuerySession
        : ISparseEditSession<BlazorCounterForm>,
            ISparseEditSessionWithFieldState<BlazorCounterForm>
    {
        private readonly BlazorCounterForm.EditSession _inner;

        public QuerySession(BlazorCounterForm.EditSession inner)
        {
            _inner = inner;
        }

        public bool FailStateQuery { get; set; }

        public BlazorCounterForm Model => _inner.GetModelForFrameworkAccess();

        public bool HasChanges => _inner.HasChanges;

        public void AcceptChanges() => _inner.AcceptChanges();

        public SparseFieldState<TValue> FieldState<TValue>(
            SparsePath<BlazorCounterForm, TValue> path
        )
        {
            if (FailStateQuery)
            {
                throw new InvalidOperationException("state backend unavailable");
            }

            return _inner.FieldState(path);
        }

        public SparseFieldState<object?> FieldState(SparsePath path) => _inner.FieldState(path);
    }

    private sealed class QueryScope(QuerySession session) : ISparseEditScope<BlazorCounterForm>
    {
        public ISparseEditSession<BlazorCounterForm> Session => session;

        public EditContext EditContext { get; } = session.CreateEditContext();
    }

    private static RenderFragment RenderField(
        ISparseEditScope<BlazorCounterForm> scope,
        Action<SparseFieldContext<BlazorCounterForm, int>> capture
    ) =>
        builder =>
        {
            builder.OpenComponent<CascadingValue<ISparseEditScope<BlazorCounterForm>>>(0);
            builder.AddAttribute(
                1,
                nameof(CascadingValue<ISparseEditScope<BlazorCounterForm>>.Value),
                scope
            );
            builder.AddAttribute(
                2,
                nameof(CascadingValue<ISparseEditScope<BlazorCounterForm>>.ChildContent),
                (RenderFragment)(
                    inner =>
                    {
                        inner.OpenComponent<SparseField<BlazorCounterForm, int>>(0);
                        inner.AddAttribute(
                            1,
                            nameof(SparseField<BlazorCounterForm, int>.Path),
                            BlazorCounterForm.SparsePath.Total
                        );
                        inner.AddAttribute(
                            2,
                            nameof(SparseField<BlazorCounterForm, int>.ChildContent),
                            (RenderFragment<SparseFieldContext<BlazorCounterForm, int>>)(
                                field => fieldBuilder => capture(field)
                            )
                        );
                        inner.CloseComponent();
                    }
                )
            );
            builder.CloseComponent();
        };

    [Test]
    public async Task StateQueryFailureIsVisibleAndClearedAfterRecovery()
    {
        var session = new QuerySession(
            new BlazorCounterForm { Label = "Count", Total = 4 }.CreateEditSession()
        )
        {
            FailStateQuery = true,
        };
        var scope = new QueryScope(session);
        SparseFieldContext<BlazorCounterForm, int>? field = null;
        var fragment = RenderField(scope, captured => field = captured);

        using var tree = await Headless.Tree.CreateAsync(fragment);

        field.ShouldNotBeNull();
        field!.HasStateQueryError.ShouldBeTrue();
        field.StateQueryError.ShouldBe("state backend unavailable");
        field.State.IsChanged.ShouldBeFalse();

        session.FailStateQuery = false;
        await tree.ShowAsync(fragment);

        field.HasStateQueryError.ShouldBeFalse();
        field.StateQueryError.ShouldBeNull();
        field.State.IsChanged.ShouldBeFalse();
    }
}
