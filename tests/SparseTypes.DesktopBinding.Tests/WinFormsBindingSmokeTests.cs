using System.ComponentModel;
using System.Windows.Forms;

namespace SparseTypes.DesktopBinding.Tests;

/// <summary>WinForms smoke binding against the generated observable (issue #262).</summary>
/// <remarks>
/// Windows-only: other platforms return early and stay green, with the real
/// verification owned by Windows legs. Every body runs on a dedicated STA
/// thread because control creation requires a single-threaded apartment.
/// </remarks>
public sealed class WinFormsBindingSmokeTests
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
            using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
            using var box = new TextBox();
            form.Controls.Add(box);
            box.DataBindings.Add(
                nameof(TextBox.Text),
                session.Observable,
                nameof(DesktopOrder.Number),
                false,
                DataSourceUpdateMode.OnPropertyChanged
            );

            // Simple bindings activate with the host form. The form stays
            // invisible; showing only creates the handles the engine needs.
            form.Show();
            try
            {
                box.Text.ShouldBe("ORD-1");

                box.Text = "ORD-2";

                model.Number.ShouldBe("ORD-2");
                session.HasChanges.ShouldBeTrue();

                session.Observable.Number = "ORD-3";

                box.Text.ShouldBe("ORD-3");
            }
            finally
            {
                form.Close();
            }
        });
    }

    [Test]
    public void ListBoxBindsObservableLinesWithManualRefresh()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            using var source = new BindingSource { DataSource = session.Observable.Lines };
            using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
            using var list = new ListBox();
            form.Controls.Add(list);
            list.DataSource = source;
            form.Show();
            try
            {
                list.Items.Count.ShouldBe(2);

                // The list view exposes IList and INotifyCollectionChanged, not
                // IBindingList, so the WinForms currency manager does not track
                // adds on its own. The application refreshes explicitly.
                session.Observable.Lines.AddModel(
                    new DesktopOrderLine { Id = "c", Name = "third" }
                );
                source.ResetBindings(false);

                list.Items.Count.ShouldBe(3);
                model.Lines.Count.ShouldBe(3);
            }
            finally
            {
                form.Close();
            }
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
            var customer = session.Observable.Customer;
            customer.ShouldNotBeNull();
            using var form = new Form { ShowInTaskbar = false, Opacity = 0 };
            using var box = new TextBox();
            form.Controls.Add(box);
            box.DataBindings.Add(
                nameof(TextBox.Text),
                customer!,
                nameof(DesktopCustomer.Name),
                false,
                DataSourceUpdateMode.OnPropertyChanged
            );
            form.Show();
            try
            {
                box.Text.ShouldBe("Ada");

                box.Text = "Grace";

                model.Customer!.Name.ShouldBe("Grace");
                session.HasChanges.ShouldBeTrue();
            }
            finally
            {
                form.Close();
            }
        });
    }

    [Test]
    public void ChildSessionAcceptanceStaysExplicit()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var model = Order();
            var session = model.CreateEditSession();
            using var child = session.OpenChildSession(DesktopOrder.SparsePath.Lines.Key("b"));

            child.Session.Observable.Name = "draft";

            model.Lines.First(line => line.Id == "b").Name.ShouldBe("second");
            session.HasChanges.ShouldBeTrue();

            child.TryAcceptChanges(out var conflicts).ShouldBeTrue();

            conflicts.ShouldBeNull();
            model.Lines.First(line => line.Id == "b").Name.ShouldBe("draft");
            session.HasChanges.ShouldBeTrue();
        });
    }

    [Test]
    public void ChildSessionExposesNoEditableObjectContract()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        StaHelper.Run(() =>
        {
            var session = Order().CreateEditSession();
            using var child = session.OpenChildSession(DesktopOrder.SparsePath.Lines.Key("a"));

            child.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
            child.Session.GetType().GetInterfaces().ShouldNotContain(typeof(IEditableObject));
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
