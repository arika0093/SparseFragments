extern alias Playground;

using BlazorMonaco.Editor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Playground::SparseTypes.Playground.Components;
using Playground::SparseTypes.Playground.Models;

// A dependency-free test renderer builds render trees by hand, so the
// experimental RenderTree API surface it needs is acknowledged here.
#pragma warning disable BL0006

namespace SparseTypes.Tests.Components;

// Headless lifecycle tests for #301: MonacoDiffView owns the two models it
// creates through Global.CreateModel and must release them on unmount,
// including navigation-during-init races and creation failures.
public sealed class MonacoDiffViewDisposalTests
{
    private sealed class JsCallGate : IDisposable
    {
        private readonly TaskCompletionSource<bool> _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource<bool> _exited = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task Entered => _entered.Task;

        public Task Exited => _exited.Task;

        public void Release() => _release.TrySetResult(true);

        public void Dispose() => Release();

        public async Task WaitAsync()
        {
            _entered.TrySetResult(true);
            await _release.Task;
            _exited.TrySetResult(true);
        }
    }

    private sealed class FakeJsRuntime : IJSRuntime
    {
        private readonly object _gate = new();
        private int _models;
        private readonly Dictionary<(string Identifier, int CallIndex), JsCallGate> _callGates = [];

        public Func<int, Task<TextModel>>? CreateModelAsync { get; set; }
        public int FailCreateModelAt { get; set; } = -1;
        public bool DisconnectOnModelDispose { get; set; }

        private readonly List<string> _identifiers = [];
        private readonly HashSet<string> _liveModels = [];
        private readonly List<string> _disposedUris = [];

        public int CreateCount => Count("blazorMonaco.editor.createModel");

        public int SetValueCount => Count("blazorMonaco.editor.model.setValue");

        public bool SawSetModel => Contains("blazorMonaco.editor.setInstanceDiffModel");

        public bool SawDetachDiff => Contains("pgMonaco.detachDiff");

        public bool SawSetTheme => Contains("blazorMonaco.editor.setTheme");

        public int DisposeCount
        {
            get
            {
                lock (_gate)
                {
                    return _disposedUris.Count;
                }
            }
        }

        public int LiveCount
        {
            get
            {
                lock (_gate)
                {
                    return _liveModels.Count;
                }
            }
        }

        public IReadOnlyList<string> DisposedUris
        {
            get
            {
                lock (_gate)
                {
                    return _disposedUris.ToList();
                }
            }
        }

        public List<string> TakeIdentifiers()
        {
            lock (_gate)
            {
                return _identifiers.ToList();
            }
        }

        public JsCallGate HoldCall(string identifier, int callIndex)
        {
            var callGate = new JsCallGate();
            lock (_gate)
            {
                _callGates.Add((identifier, callIndex), callGate);
            }

            return callGate;
        }

        public TextModel CreateTrackedModel()
        {
            var number = Interlocked.Increment(ref _models);
            var model = new TextModel
            {
                JsRuntime = this,
                Uri = $"test://model-{number}",
                Id = $"model-{number}",
            };
            lock (_gate)
            {
                _liveModels.Add(model.Uri);
            }

            return model;
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeCoreAsync<TValue>(identifier, args);

        public ValueTask<TValue> InvokeAsync<TValue>(
            string identifier,
            CancellationToken cancellationToken,
            object?[]? args
        ) => InvokeCoreAsync<TValue>(identifier, args);

        private async ValueTask<TValue> InvokeCoreAsync<TValue>(string identifier, object?[]? args)
        {
            JsCallGate? callGate;
            lock (_gate)
            {
                _identifiers.Add(identifier);
                var callIndex = _identifiers.Count(call => call == identifier) - 1;
                _callGates.TryGetValue((identifier, callIndex), out callGate);
            }

            if (callGate is not null)
            {
                await callGate.WaitAsync();
            }

            if (identifier == "blazorMonaco.editor.createModel")
            {
                var index = CreateCount - 1;
                if (index == FailCreateModelAt)
                {
                    throw new JSException("Injected second-model failure.");
                }

                var created = CreateModelAsync is null
                    ? CreateTrackedModel()
                    : await CreateModelAsync(index);
                return (TValue)(object)created;
            }

            if (identifier == "blazorMonaco.editor.model.dispose")
            {
                var uri = (string)args![0]!;
                if (DisconnectOnModelDispose)
                {
                    throw new JSDisconnectedException("Circuit is gone.");
                }

                lock (_gate)
                {
                    _disposedUris.Add(uri);
                    _liveModels.Remove(uri);
                }
            }

            return default!;
        }

        private int Count(string identifier)
        {
            lock (_gate)
            {
                return _identifiers.Count(id => id == identifier);
            }
        }

        private bool Contains(string identifier)
        {
            lock (_gate)
            {
                return _identifiers.Contains(identifier);
            }
        }
    }

    private sealed class TestRenderer : Renderer
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CreateDefault();

        public TestRenderer(IServiceProvider services, ILoggerFactory loggers)
            : base(services, loggers) { }

        public List<Exception> Failures { get; } = [];

        public override Dispatcher Dispatcher => _dispatcher;

        public Task<int> RenderRootAsync(IComponent component) =>
            Dispatcher.InvokeAsync(async () =>
            {
                var id = AssignRootComponentId(component);
                await RenderRootComponentAsync(id);
                return id;
            });

        public Task ShowAsync(int rootId, ParameterView parameters) =>
            Dispatcher.InvokeAsync(() => RenderRootComponentAsync(rootId, parameters));

        public Task FlushAsync() => Dispatcher.InvokeAsync(() => { });

        public IReadOnlyList<Exception> TakeFailures()
        {
            lock (Failures)
            {
                return Failures.ToList();
            }
        }

        protected override void HandleException(Exception exception)
        {
            lock (Failures)
            {
                Failures.Add(exception);
            }
        }

        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
    }

    private sealed class TestServices : IServiceProvider
    {
        private readonly FakeJsRuntime _js;
        private readonly PlaygroundThemeState _themes = new();

        public TestServices(FakeJsRuntime js) => _js = js;

        public PlaygroundThemeState Themes => _themes;

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IJSRuntime))
            {
                return _js;
            }

            if (serviceType == typeof(PlaygroundThemeState))
            {
                return _themes;
            }

            return null;
        }
    }

    private sealed class Host : ComponentBase
    {
        public MonacoDiffView? DiffView { get; private set; }

        [Parameter]
        public bool Show { get; set; }

        [Parameter]
        public string Original { get; set; } = "original";

        [Parameter]
        public string Modified { get; set; } = "modified";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Show)
            {
                builder.OpenComponent<MonacoDiffView>(0);
                builder.AddAttribute(1, nameof(MonacoDiffView.Original), Original);
                builder.AddAttribute(2, nameof(MonacoDiffView.Modified), Modified);
                builder.AddComponentReferenceCapture(
                    3,
                    instance => DiffView = (MonacoDiffView)instance
                );
                builder.CloseComponent();
            }
        }
    }

    private static ParameterView HostParameters(
        bool show,
        string? original = null,
        string? modified = null
    ) =>
        ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(Host.Show)] = show,
                [nameof(Host.Original)] = original ?? "original",
                [nameof(Host.Modified)] = modified ?? "modified",
            }
        );

    private static async Task PollAsync(Func<bool> ready)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ready())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Timed out waiting for the diff lifecycle.");
            }

            await Task.Delay(15);
        }
    }

    private static void AssertBothModelsDisposedExactlyOnce(FakeJsRuntime js)
    {
        js.DisposeCount.ShouldBe(2);
        js.DisposedUris.Count.ShouldBe(2);
        js.DisposedUris.Distinct().Count().ShouldBe(2);
        js.LiveCount.ShouldBe(0);
    }

    [Test]
    public async Task UnmountDisposesBothOwnedModels()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.CreateCount == 2);
        await renderer.FlushAsync();
        js.LiveCount.ShouldBe(2);

        // Content and theme updates work while mounted.
        await renderer.ShowAsync(rootId, HostParameters(true, "original v2"));
        await PollAsync(() => js.SetValueCount >= 1);
        services.Themes.SetMode(PlaygroundThemeMode.Dark);
        await PollAsync(() => js.SawSetTheme);

        await renderer.ShowAsync(rootId, HostParameters(false));
        await PollAsync(() => js.DisposeCount == 2);
        await renderer.FlushAsync();

        js.LiveCount.ShouldBe(0);
        js.DisposedUris.Count.ShouldBe(2);

        // Detaching precedes both model disposals: Monaco reports disposing
        // an attached model, so the detach call must come first.
        var calls = js.TakeIdentifiers();
        var detachAt = calls.IndexOf("pgMonaco.detachDiff");
        detachAt.ShouldBeGreaterThanOrEqualTo(0);
        calls
            .Select((identifier, index) => (identifier, index))
            .Where(call => call.identifier == "blazorMonaco.editor.model.dispose")
            .Select(call => call.index)
            .ShouldAllBe(index => index > detachAt);

        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task RepeatedMountCyclesLeaveNoModels()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        for (var cycle = 0; cycle < 20; cycle++)
        {
            var creates = js.CreateCount;
            await renderer.ShowAsync(rootId, HostParameters(true, $"original {cycle}"));
            await PollAsync(() => js.CreateCount == creates + 2);

            var disposes = js.DisposeCount;
            await renderer.ShowAsync(rootId, HostParameters(false));
            await PollAsync(() => js.DisposeCount == disposes + 2);
        }

        await renderer.FlushAsync();
        js.CreateCount.ShouldBe(40);
        js.DisposeCount.ShouldBe(40);
        js.LiveCount.ShouldBe(0);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task SecondModelFailureDisposesFirst()
    {
        var js = new FakeJsRuntime { FailCreateModelAt = 1 };
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.DisposeCount == 1);
        await renderer.FlushAsync();

        js.CreateCount.ShouldBe(2);
        js.LiveCount.ShouldBe(0);
        js.SawSetModel.ShouldBeFalse();

        // The creation failure still surfaces instead of being swallowed.
        var flattened = renderer
            .TakeFailures()
            .SelectMany(static failure =>
                failure is AggregateException aggregate ? aggregate.InnerExceptions : [failure]
            )
            .ToList();
        flattened.Count.ShouldBe(1);
        flattened[0].ShouldBeOfType<JSException>();
    }

    [Test]
    public async Task DisposeDuringInitReleasesLateModel()
    {
        var gate = new TaskCompletionSource<TextModel>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var js = new FakeJsRuntime();
        js.CreateModelAsync = index =>
            index == 0 ? gate.Task : Task.FromResult(js.CreateTrackedModel());
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.CreateCount == 1);

        // Navigation wins while the first creation is still pending.
        await renderer.ShowAsync(rootId, HostParameters(false));
        await renderer.FlushAsync();
        js.DisposeCount.ShouldBe(0);

        gate.SetResult(js.CreateTrackedModel());
        await PollAsync(() => js.DisposeCount == 1);
        await renderer.FlushAsync();

        js.SawSetModel.ShouldBeFalse();
        js.LiveCount.ShouldBe(0);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task UnmountDuringOriginalSetValueDoesNotUpdateModifiedModel()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var host = new Host();
        var rootId = await renderer.RenderRootAsync(host);
        using var setValueGate = js.HoldCall("blazorMonaco.editor.model.setValue", 0);

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.SawSetModel);
        var updateTask = renderer.ShowAsync(
            rootId,
            HostParameters(true, "original v2", "modified v2")
        );
        await setValueGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        js.SetValueCount.ShouldBe(1);

        await host.DiffView!.DisposeAsync();
        await PollAsync(() => js.DisposeCount == 2);
        setValueGate.Release();
        await setValueGate.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        await updateTask;
        await renderer.ShowAsync(rootId, HostParameters(false));
        await renderer.FlushAsync();

        js.SetValueCount.ShouldBe(1);
        AssertBothModelsDisposedExactlyOnce(js);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task UnmountDuringModifiedSetValueDoesNotRepeatUpdates()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var host = new Host();
        var rootId = await renderer.RenderRootAsync(host);
        using var setValueGate = js.HoldCall("blazorMonaco.editor.model.setValue", 1);

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.SawSetModel);
        var updateTask = renderer.ShowAsync(
            rootId,
            HostParameters(true, "original v2", "modified v2")
        );
        await setValueGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        js.SetValueCount.ShouldBe(2);

        await host.DiffView!.DisposeAsync();
        await PollAsync(() => js.DisposeCount == 2);
        setValueGate.Release();
        await setValueGate.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        await updateTask;
        await renderer.ShowAsync(rootId, HostParameters(false));
        await renderer.FlushAsync();

        js.SetValueCount.ShouldBe(2);
        AssertBothModelsDisposedExactlyOnce(js);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task UnmountDuringSecondModelCreationDisposesBothModelsOnce()
    {
        var js = new FakeJsRuntime();
        using var secondCreateGate = js.HoldCall("blazorMonaco.editor.createModel", 1);
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await secondCreateGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        js.CreateCount.ShouldBe(2);

        await renderer.ShowAsync(rootId, HostParameters(false));
        await PollAsync(() => js.DisposeCount == 1);
        secondCreateGate.Release();
        await secondCreateGate.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        await PollAsync(() => js.DisposeCount == 2);
        await renderer.FlushAsync();

        js.SawSetModel.ShouldBeFalse();
        AssertBothModelsDisposedExactlyOnce(js);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task UnmountDuringSetModelWaitDisposesOwnedModelsOnce()
    {
        var js = new FakeJsRuntime();
        using var setModelGate = js.HoldCall("blazorMonaco.editor.setInstanceDiffModel", 0);
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await setModelGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        js.CreateCount.ShouldBe(2);

        await renderer.ShowAsync(rootId, HostParameters(false));
        await PollAsync(() => js.DisposeCount == 2);
        setModelGate.Release();
        await setModelGate.Exited.WaitAsync(TimeSpan.FromSeconds(10));
        await renderer.FlushAsync();

        js.SetValueCount.ShouldBe(0);
        AssertBothModelsDisposedExactlyOnce(js);
        renderer.TakeFailures().ShouldBeEmpty();
    }

    [Test]
    public async Task DisposeToleratesDisconnectedJsRuntime()
    {
        var js = new FakeJsRuntime();
        var services = new TestServices(js);
        using var loggers = LoggerFactory.Create(static builder => { });
        var renderer = new TestRenderer(services, loggers);
        var rootId = await renderer.RenderRootAsync(new Host());

        await renderer.ShowAsync(rootId, HostParameters(true));
        await PollAsync(() => js.CreateCount == 2);

        js.DisconnectOnModelDispose = true;
        await renderer.ShowAsync(rootId, HostParameters(false));
        await renderer.FlushAsync();

        renderer.TakeFailures().ShouldBeEmpty();
    }
}
