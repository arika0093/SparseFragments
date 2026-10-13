using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;

// A dependency-free test renderer inherently builds render trees by hand, so
// the experimental RenderTree API surface it needs is acknowledged here.
#pragma warning disable BL0006

namespace SparseTypes.Blazor.Tests;

// Headless component behavior for issue #271: Session-only SparseEdit roots,
// Path-only SparseField inference, third-party and InputBase wiring against
// the original model, nested/keyed navigation, and session-owned history.
// Razor generic inference itself is proven by the compile-only
// SparseHeadless.razor package consumer; these trees use the same shapes
// through the inference-preserving Headless fragment builders.
public sealed class SparseHeadlessComponentTests
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
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        };

    [Test]
    public async Task EditRootBindsOriginalModelWithSessionOnlyParameters()
    {
        var live = Order();
        var session = live.CreateEditSession();
        ISparseEditScope<OrderDto>? scope = null;
        EditContext? editContext = null;
        var numberPathText = string.Empty;
        var keyedPathText = string.Empty;
        var rootPathText = string.Empty;
        var hasChanges = true;
        var historyMissing = false;
        var canUndo = true;
        Action undo = () =>
            throw new InvalidOperationException("The edit context was not captured.");

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                edit =>
                {
                    // The lambda parameter carries the concrete generated path
                    // builder, so navigation below compiles exactly like Razor.
                    scope = edit;
                    editContext = edit.EditContext;
                    rootPathText = edit.Path.UntypedPath.ToString();
                    numberPathText = edit.Path.Number.ToString();
                    keyedPathText = edit.Path.Lines.Key("b").Price.ToString();
                    hasChanges = edit.HasChanges;
                    historyMissing = edit.History is null;
                    canUndo = edit.CanUndo;
                    undo = edit.Undo;
                }
            )
        );

        scope.ShouldNotBeNull();
        ReferenceEquals(scope!.Session, session).ShouldBeTrue();
        editContext.ShouldNotBeNull();
        ReferenceEquals(editContext!.Model, live).ShouldBeTrue();
        rootPathText.ShouldBe("$root");
        numberPathText.ShouldBe("Number");
        keyedPathText.ShouldBe("Lines[\"b\"].Price");
        hasChanges.ShouldBeFalse();
        historyMissing.ShouldBeTrue();
        canUndo.ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => undo());

        session.Observable.Number = "ORD-2";
        await tree.FlushAsync();
        hasChanges.ShouldBeTrue();
    }

    [Test]
    public async Task FieldWritesThroughThirdPartyValueChanged()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, string>? field = null;
        var shown = string.Empty;
        var changed = true;
        Headless.VendorField<string>? vendor = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Number,
                        captured =>
                        {
                            field = captured;
                            shown = captured.Value;
                            changed = captured.IsChanged;
                        },
                        captured =>
                            builder =>
                            {
                                builder.OpenComponent<Headless.VendorField<string>>(0);
                                builder.AddAttribute(1, "Value", captured.Value);
                                builder.AddAttribute(2, "ValueChanged", captured.ValueChanged);
                                builder.AddComponentReferenceCapture(
                                    3,
                                    component => vendor = (Headless.VendorField<string>)component
                                );
                                builder.CloseComponent();
                            }
                    )
            )
        );

        field.ShouldNotBeNull();
        vendor.ShouldNotBeNull();
        shown.ShouldBe("ORD-1");
        changed.ShouldBeFalse();
        field!.ValueExpression.ShouldNotBeNull();

        // Invoking the component callback is what a third-party input does on user input.
        await tree.DispatchAsync(() => vendor!.ValueChanged.InvokeAsync("ORD-2"));
        await tree.FlushAsync();

        live.Number.ShouldBe("ORD-2");
        shown.ShouldBe("ORD-2");
        changed.ShouldBeTrue();
        field.Binding.EditContext.IsModified().ShouldBeTrue();
        field.IsModified.ShouldBeTrue();
    }

    [Test]
    public async Task InputTextTripleValidatesAgainstOriginalModel()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, string>? field = null;
        EditContext? editContext = null;
        InputText? input = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                edit => editContext = edit.EditContext,
                edit =>
                    Headless.Field(
                        edit.Path.Number,
                        captured => field = captured,
                        captured =>
                            builder =>
                                BuildInputForm(
                                    builder,
                                    editContext!,
                                    captured.Value,
                                    captured.ValueChanged,
                                    captured.ValueExpression,
                                    component => input = component
                                )
                    )
            )
        );

        field.ShouldNotBeNull();
        input.ShouldNotBeNull();
        editContext.ShouldNotBeNull();
        var nameField = field!;
        var formContext = editContext!;
        var expression = nameField.ValueExpression.ShouldNotBeNull();
        var identifier = FieldIdentifier.Create(expression);
        identifier.Model.ShouldBeSameAs(live);
        identifier.FieldName.ShouldBe(nameof(OrderDto.Number));
        identifier.ShouldBe(nameField.Field);

        await tree.DispatchAsync(() => input!.ValueChanged.InvokeAsync(string.Empty));
        await tree.FlushAsync();
        live.Number.ShouldBe(string.Empty);
        var invalid = false;
        await tree.DispatchAsync(() => invalid = formContext.Validate());
        invalid.ShouldBeFalse();
        formContext
            .GetValidationMessages(identifier)
            .ShouldContain("The Number field is required.");
        nameField.ValidationMessages.ShouldContain("The Number field is required.");
        nameField.IsChanged.ShouldBeTrue();

        await tree.DispatchAsync(() => input!.ValueChanged.InvokeAsync("ORD-9"));
        await tree.FlushAsync();
        var valid = false;
        await tree.DispatchAsync(() => valid = formContext.Validate());
        valid.ShouldBeTrue();
        nameField.ValidationMessages.ShouldBeEmpty();
    }

    [Test]
    public async Task InputNumberTripleEditsMemberValue()
    {
        var live = new BlazorCounterForm { Label = "Count", Total = 7 };
        var session = live.CreateEditSession();
        SparseFieldContext<BlazorCounterForm, int>? field = null;
        InputNumber<int>? input = null;

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Total,
                        captured => field = captured,
                        captured =>
                            builder =>
                            {
                                builder.OpenComponent<InputNumber<int>>(0);
                                builder.AddAttribute(
                                    1,
                                    nameof(InputNumber<int>.Value),
                                    captured.Value
                                );
                                builder.AddAttribute(
                                    2,
                                    nameof(InputNumber<int>.ValueChanged),
                                    captured.ValueChanged
                                );
                                builder.AddAttribute(
                                    3,
                                    nameof(InputNumber<int>.ValueExpression),
                                    captured.ValueExpression
                                );
                                builder.AddComponentReferenceCapture(
                                    4,
                                    component => input = (InputNumber<int>)component
                                );
                                builder.CloseComponent();
                            }
                    )
            )
        );

        var version = field.ShouldNotBeNull();
        input.ShouldNotBeNull();
        version.Value.ShouldBe(7);
        var expression = version.ValueExpression.ShouldNotBeNull();
        FieldIdentifier.Create(expression).Model.ShouldBeSameAs(live);

        await tree.DispatchAsync(() => input!.ValueChanged.InvokeAsync(8));
        await tree.FlushAsync();
        live.Total.ShouldBe(8);
        version.IsChanged.ShouldBeTrue();
    }

    [Test]
    public async Task KeyedFieldEditsWithoutExpressionAndReportsStaleRemoval()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, decimal>? field = null;
        var removed = live.Lines.Single(line => line.Sku == "b");

        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Lines.Key("b").Price,
                        captured => field = captured,
                        captured =>
                            builder =>
                            {
                                // Stale-safe display: removed rows report instead of throwing.
                                if (captured.TryGetValue(out var value, out var error))
                                {
                                    builder.AddContent(0, value);
                                }
                                else
                                {
                                    builder.AddContent(0, "stale:" + error);
                                }
                            }
                    )
            )
        );

        field.ShouldNotBeNull();
        field!.ValueExpression.ShouldBeNull();
        field.Value.ShouldBe(20m);

        await tree.DispatchAsync(() => field.ValueChanged.InvokeAsync(99m));
        await tree.FlushAsync();
        removed.Price.ShouldBe(99m);
        field.IsChanged.ShouldBeTrue();

        session
            .EditCollection<OrderLine, string>(OrderDto.SparsePath.Lines)
            .TryRemoveByKey("b", out _)
            .ShouldBeTrue();
        await tree.FlushAsync();

        field.IsStale.ShouldBeTrue();
        field.TrySet(1m, out var staleError).ShouldBeFalse();
        staleError.ShouldNotBeNull();
        removed.Price.ShouldBe(99m);
        live.Lines.Any(line => line.Sku == "b").ShouldBeFalse();
    }

    [Test]
    public async Task NestedSelectNavigatesWithRootPreservingTypes()
    {
        var live = Order();
        var session = live.CreateEditSession();
        SparseFieldContext<OrderDto, string?>? email = null;
        SparseFieldContext<OrderDto, string>? nestedName = null;
        using var tree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit =>
                    Headless.Field(
                        edit.Path.Customer,
                        static _ => { },
                        customer =>
                            Headless.Field(
                                customer.Select(c => c.Email),
                                captured => email = captured
                            )
                    )
            )
        );

        var emailField = email.ShouldNotBeNull();
        emailField.Value.ShouldBe("ada@example.com");
        emailField.Path.UntypedPath.RootType.ShouldBe(typeof(OrderDto));

        using var nestedTree = await Headless.Tree.CreateAsync(
            Headless.Edit(
                session,
                static _ => { },
                edit => Headless.Field(edit.Path.Customer.Name, captured => nestedName = captured)
            )
        );

        // Nested member access identifies its owner, exactly like a hand-written
        // () => live.Customer.Name expression: the original nested instance.
        var nameField = nestedName.ShouldNotBeNull();
        var nameIdentifier = FieldIdentifier.Create(nameField.ValueExpression.ShouldNotBeNull());
        nameIdentifier.Model.ShouldBeSameAs(live.Customer);
        nameIdentifier.ShouldBe(nameField.Field);

        var customer = live.Customer;
        Should.Throw<ArgumentException>(() => emailField.Select(c => c!.ToString()));
        Should.Throw<ArgumentException>(() => emailField.Select(c => c!));
        customer.Name.ShouldBe("Ada");
    }

    private static void BuildInputForm(
        RenderTreeBuilder builder,
        EditContext editContext,
        string value,
        EventCallback<string> changed,
        Expression<Func<string>>? expression,
        Action<InputText> capture
    )
    {
        builder.OpenComponent<EditForm>(0);
        builder.AddAttribute(1, nameof(EditForm.EditContext), editContext);
        builder.AddAttribute(
            2,
            nameof(EditForm.ChildContent),
            (RenderFragment<EditContext>)(
                _ =>
                    form =>
                    {
                        form.OpenComponent<DataAnnotationsValidator>(0);
                        form.CloseComponent();
                        form.OpenComponent<InputText>(1);
                        form.AddAttribute(2, nameof(InputText.Value), value);
                        form.AddAttribute(3, nameof(InputText.ValueChanged), changed);
                        form.AddAttribute(4, nameof(InputText.ValueExpression), expression);
                        form.AddComponentReferenceCapture(
                            5,
                            component => capture((InputText)component)
                        );
                        form.CloseComponent();
                    }
            )
        );
        builder.CloseComponent();
    }
}
