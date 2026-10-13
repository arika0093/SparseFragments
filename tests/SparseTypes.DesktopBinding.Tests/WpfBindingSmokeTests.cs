using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using WpfBinding = System.Windows.Data.Binding;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace SparseTypes.DesktopBinding.Tests;

/// <summary>WPF smoke binding against the generated observable (issue #262).</summary>
/// <remarks>
/// Windows-only: other platforms return early and stay green, with the real
/// verification owned by Windows legs. Every body runs on a dedicated STA
/// thread because the runner itself is multithreaded.
/// </remarks>
public sealed class WpfBindingSmokeTests
{
    private static DesktopOrder Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new DesktopCustomer { Name = "Ada" },
            Lines =
            [
                new DesktopOrderLine { Id = "a", Name = "first" },
                new DesktopOrderLine { Id = "b", Name = "second" },
            ],
        };

    private static void Pump()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
    }

    private static WpfTextBox BoundNumberBox(object source)
    {
        var box = new WpfTextBox();
        box.SetBinding(
            WpfTextBox.TextProperty,
            new WpfBinding(nameof(DesktopOrder.Number))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            }
        );
        return box;
    }

    [Test]
    public void TextBoxTwoWayBindsObservableScalar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            var box = BoundNumberBox(session.Observable);
            Pump();

            box.Text.ShouldBe("ORD-1");

            box.Text = "ORD-2";
            Pump();

            model.Number.ShouldBe("ORD-2");
            session.HasChanges.ShouldBeTrue();

            session.Observable.Number = "ORD-3";
            Pump();

            box.Text.ShouldBe("ORD-3");
        });
    }

    [Test]
    public void ListBoxTracksObservableKeyedLines()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            var list = new WpfListBox { ItemsSource = session.Observable.Lines };
            Pump();

            list.Items.Count.ShouldBe(2);

            session.Observable.Lines.AddModel(new DesktopOrderLine { Id = "c", Name = "third" });
            Pump();

            list.Items.Count.ShouldBe(3);
            model.Lines.Count.ShouldBe(3);

            session.Observable.Lines.Move(2, 0);
            Pump();

            session.Observable.Lines[0].Id.ShouldBe("c");
            var lines = session.CollectionState(DesktopOrder.SparsePath.Lines);
            lines.OrderChanged.ShouldBeTrue();
            lines.GetItemState("c").IsAdded.ShouldBeTrue();

            // The survivor row keeps its value: no exact transition there.
            // (Ancestor change is expected while the member holds an add.)
            session
                .FieldState(DesktopOrder.SparsePath.Lines.Key("a").Name)
                .IsChanged.ShouldBeFalse();
        });
    }

    [Test]
    public void NestedBindingWritesThroughChildProxy()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            var box = new WpfTextBox();
            box.SetBinding(
                WpfTextBox.TextProperty,
                new WpfBinding(nameof(DesktopCustomer.Name))
                {
                    Source = session.Observable.Customer,
                    Mode = BindingMode.TwoWay,
                    UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
                }
            );
            Pump();

            box.Text.ShouldBe("Ada");

            box.Text = "Grace";
            Pump();

            model.Customer!.Name.ShouldBe("Grace");
            session.HasChanges.ShouldBeTrue();
        });
    }

    [Test]
    public void ScopeLifecycleStaysExplicit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            using var scope = session.OpenScope(DesktopOrder.SparsePath.Lines.Key("a"));

            scope.Observable.Name = "draft";

            model.Lines.First(line => line.Id == "a").Name.ShouldBe("first");
            session.HasChanges.ShouldBeFalse();

            scope.TryCommit(out var conflicts).ShouldBeTrue();

            conflicts.ShouldBeNull();
            model.Lines.First(line => line.Id == "a").Name.ShouldBe("draft");
            session.HasChanges.ShouldBeTrue();
        });
    }

    [Test]
    public void ScopeExposesNoEditableObjectContract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var session = Order().CreateEditSession();
            using var scope = session.OpenScope(DesktopOrder.SparsePath.Lines.Key("a"));

            scope.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
            session.Observable.Lines.ShouldNotBeAssignableTo<IBindingList>();
        });
    }

    [Test]
    public void ValidationStoreFeedsErrorDisplay()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var store = new SparseValidationStore<DesktopOrder>();
            INotifyDataErrorInfo notify = store;
            var events = new List<string?>();
            notify.ErrorsChanged += (_, args) => events.Add(args.PropertyName);

            store.SetErrors(
                DesktopOrder.SparsePath.Number,
                SparseValidationOrigins.LocalValidation,
                ["Number is required."]
            );

            notify.HasErrors.ShouldBeTrue();
            notify
                .GetErrors(nameof(DesktopOrder.Number))
                .Cast<object?>()
                .ShouldBe(["Number is required."]);
            events.ShouldBe([nameof(DesktopOrder.Number), string.Empty]);
        });
    }
}
