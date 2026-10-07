using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class SparseEditSessionTests
{
    private static OrderDto Order() =>
        new()
        {
            Number = "ORD-1",
            Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
            Lines = new()
            {
                new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
            },
            Tags = new() { "fragile" },
        };

    private static Optional<OrderDto.Fragment?> Present(OrderDto.Fragment fragment) =>
        Optional<OrderDto.Fragment?>.Present(fragment);

    private static void PatchesShouldBeEquivalent(OrderDto.Patch first, OrderDto.Patch second)
    {
        var baseline = Present(OrderDto.Fragment.From(Order()));
        var appliedFirst = first.Apply(baseline);
        var appliedSecond = second.Apply(baseline);
        OrderDto.Patch.Between(appliedFirst, appliedSecond).IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void InitialSessionHasNoChanges()
    {
        var session = Order().CreateEditSession();

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void GeneratedCreateEditSessionRequiresNoManualWiring()
    {
        // Compile-time proof: the parameterless generated factory yields a session
        // exposing both ChangeSet and Patch operations without reflection.
        var session = Order().CreateEditSession();

        OrderDto.ChangeSet changes = session.CreateChangeSet();
        OrderDto.Patch patch = session.CreatePatch();
        changes.IsEmpty.ShouldBeTrue();
        patch.IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ScalarFieldEditProducesChangeSet()
    {
        var session = Order().CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session.Model.Number = "ORD-2";
        session.HasChanges.ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.IsEmpty.ShouldBeFalse();

        var patch = session.CreatePatch();
        patch.IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void NestedStructuralEditDetected()
    {
        var session = Order().CreateEditSession();

        session.Model.Customer.Email = "new@example.com";
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
        session.CreatePatch().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void EditThenRestoreHasNoSemanticChanges()
    {
        var session = Order().CreateEditSession();

        session.Model.Number = "changed";
        session.Model.Number = "ORD-1";

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void ValidationBehaviorThroughEditContext()
    {
        var session = Order().CreateEditSession();
        var store = session.CreateValidationStore();
        session.EditContext.OnValidationRequested += (sender, _) =>
        {
            store.Clear();
            if (string.IsNullOrEmpty(session.Model.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };

        session.EditContext.Validate().ShouldBeTrue();

        session.Model.Number = "";
        session.EditContext.NotifyFieldChanged(
            new FieldIdentifier(session.Model, nameof(OrderDto.Number))
        );
        session.EditContext.Validate().ShouldBeFalse();
        session.EditContext.GetValidationMessages().ShouldContain("Number is required.");
    }

    [Test]
    public void KeyedCollectionAddRemoveEditSemantics()
    {
        var baseline = Order();
        var session = baseline.CreateEditSession();

        session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });
        session.Model.Lines.RemoveAll(line => line.Sku == "a");
        session.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();

        var patch = session.CreateChangeSet().ToPatch();
        var applied = OrderDto.Fragment.From(
            new OrderDto
            {
                Number = "ORD-1",
                Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                Lines = new()
                {
                    new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                    new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
                },
                Tags = new() { "fragile" },
            }
        ).Apply(patch);

        var lines = applied.Lines.Value!;
        lines.Select(line => line.Sku).ShouldBe(["b", "c"]);
        lines.Single(line => line.Sku == "b").Quantity.ShouldBe(9);
    }

    [Test]
    public void KeyedCollectionReorder()
    {
        var session = Order().CreateEditSession();

        session.Model.Lines = session.Model.Lines.AsEnumerable().Reverse().ToList();

        session.HasChanges.ShouldBeTrue();
        var changes = session.CreateChangeSet();
        changes.IsEmpty.ShouldBeFalse();
        var applied = OrderDto.Fragment.From(Order()).Apply(changes.ToPatch());
        applied.Lines.Value!.Select(line => line.Sku).ShouldBe(["b", "a"]);
    }

    [Test]
    public void ScalarCollectionReplacement()
    {
        var session = Order().CreateEditSession();

        session.Model.Tags.Add("heavy");
        session.HasChanges.ShouldBeTrue();

        var changes = session.CreateChangeSet();
        var applied = OrderDto.Fragment.From(Order()).Apply(changes.ToPatch());
        applied.Tags.Value!.ShouldBe(["fragile", "heavy"]);

        session.Model.Tags = session.Model.Tags.AsEnumerable().Reverse().ToList();
        var reorderChanges = session.CreateChangeSet();
        var reordered = OrderDto.Fragment.From(Order()).Apply(reorderChanges.ToPatch());
        reordered.Tags.Value!.ShouldBe(["heavy", "fragile"]);
    }

    [Test]
    public void NestedKeyedCollections()
    {
        var model = new OrgDto
        {
            Teams = new()
            {
                new Team
                {
                    Name = "t1",
                    Members = new() { new TeamMember { Id = "m1", Skills = new() { "c#" } } },
                },
            },
        };
        var session = model.CreateEditSession();
        session.HasChanges.ShouldBeFalse();

        session.Model.Teams.Single(t => t.Name == "t1").Members.Add(new TeamMember { Id = "m2" });
        session.HasChanges.ShouldBeTrue();

        var changes = session.CreateChangeSet();
        changes.IsEmpty.ShouldBeFalse();
        changes.ToPatch().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void CollectionMutationWithoutFieldNotificationDetected()
    {
        var session = Order().CreateEditSession();

        // No EditContext.NotifyFieldChanged here: direct list mutation.
        session.Model.Lines.RemoveAt(0);

        session.EditContext.IsModified().ShouldBeFalse();
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void CreatePatchEqualsCreateChangeSetToPatch()
    {
        // Scalar edit.
        var scalar = Order().CreateEditSession();
        scalar.Model.Number = "ORD-2";
        PatchesShouldBeEquivalent(scalar.CreatePatch(), scalar.CreateChangeSet().ToPatch());
        scalar.CreatePatch().IsEmpty.ShouldBe(scalar.CreateChangeSet().ToPatch().IsEmpty);

        // Nested edit.
        var nested = Order().CreateEditSession();
        nested.Model.Customer.Email = "new@example.com";
        PatchesShouldBeEquivalent(nested.CreatePatch(), nested.CreateChangeSet().ToPatch());

        // Keyed add/remove/edit.
        var keyed = Order().CreateEditSession();
        keyed.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });
        keyed.Model.Lines.RemoveAll(line => line.Sku == "a");
        keyed.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;
        PatchesShouldBeEquivalent(keyed.CreatePatch(), keyed.CreateChangeSet().ToPatch());

        // Reorder.
        var reorder = Order().CreateEditSession();
        reorder.Model.Lines = reorder.Model.Lines.AsEnumerable().Reverse().ToList();
        PatchesShouldBeEquivalent(reorder.CreatePatch(), reorder.CreateChangeSet().ToPatch());

        // Scalar collection.
        var tags = Order().CreateEditSession();
        tags.Model.Tags.Add("heavy");
        PatchesShouldBeEquivalent(tags.CreatePatch(), tags.CreateChangeSet().ToPatch());

        // Empty session.
        var empty = Order().CreateEditSession();
        PatchesShouldBeEquivalent(empty.CreatePatch(), empty.CreateChangeSet().ToPatch());
        empty.CreatePatch().IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void RepeatedCreateCallsAreStable()
    {
        var session = Order().CreateEditSession();
        session.Model.Number = "ORD-2";
        session.Model.Lines.Add(new OrderLine { Sku = "c", Quantity = 3, Price = 30m });

        var firstChanges = session.CreateChangeSet();
        var secondChanges = session.CreateChangeSet();
        PatchesShouldBeEquivalent(firstChanges.ToPatch(), secondChanges.ToPatch());
        firstChanges.IsEmpty.ShouldBe(secondChanges.IsEmpty);

        PatchesShouldBeEquivalent(session.CreatePatch(), session.CreatePatch());
        PatchesShouldBeEquivalent(session.CreatePatch(), session.CreateChangeSet().ToPatch());

        var firstHasChanges = session.HasChanges;
        var secondHasChanges = session.HasChanges;
        firstHasChanges.ShouldBe(secondHasChanges);
        firstHasChanges.ShouldBeTrue();
    }

    [Test]
    public void AcceptChangesResetsBaselineAndBlazorState()
    {
        var session = Order().CreateEditSession();

        session.Model.Number = "ORD-2";
        session.EditContext.NotifyFieldChanged(
            new FieldIdentifier(session.Model, nameof(OrderDto.Number))
        );
        session.EditContext.IsModified().ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        session.AcceptChanges();

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
        session.EditContext.IsModified().ShouldBeFalse();

        session.Model.Number = "ORD-3";
        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();
    }

    [Test]
    public void AcceptChangesEstablishesNewBeforeState()
    {
        var session = Order().CreateEditSession();

        session.Model.Number = "ORD-2";
        session.AcceptChanges();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();

        // Only touch the nested email after the accept.
        session.Model.Customer.Email = "new@example.com";
        var second = session.CreateChangeSet();
        second.IsEmpty.ShouldBeFalse();

        // Applied to the ORIGINAL pre-accept baseline, Number must stay ORD-1:
        // the second ChangeSet carries no Number change, proving its before-state
        // is the accepted ORD-2 state rather than the stale ORD-1 baseline.
        var originalBaseline = Present(OrderDto.Fragment.From(Order()));
        var appliedToOriginal = second.ToPatch().Apply(originalBaseline);
        appliedToOriginal.Value!.Number.Value.ShouldBe("ORD-1");
        appliedToOriginal.Value!.Customer.Value!.Email.Value.ShouldBe("new@example.com");

        // Applied to the accepted baseline, the patch reaches the current state.
        var acceptedBaseline = Present(
            OrderDto.Fragment.From(
                new OrderDto
                {
                    Number = "ORD-2",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                    Lines = new()
                    {
                        new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                        new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
                    },
                    Tags = new() { "fragile" },
                }
            )
        );
        var currentFragment = Present(OrderDto.Fragment.From(session.Model));
        OrderDto.Patch.Between(second.ToPatch().Apply(acceptedBaseline), currentFragment)
            .IsEmpty.ShouldBeTrue();

        // Inverting the ChangeSet walks back to the accepted baseline.
        OrderDto.Patch.Between(second.Invert().ToPatch().Apply(currentFragment), acceptedBaseline)
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void SessionChangeSetSupportsRebaseOnto()
    {
        var session = Order().CreateEditSession();
        session.Model.Number = "ORD-2";
        var changes = session.CreateChangeSet();

        // Disjoint authoritative update (adds a line); local scalar edit replays cleanly.
        var authoritative = Present(
            OrderDto.Fragment.From(
                new OrderDto
                {
                    Number = "ORD-1",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                    Lines = new()
                    {
                        new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                        new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
                        new OrderLine { Sku = "c", Quantity = 3, Price = 30m },
                    },
                    Tags = new() { "fragile" },
                }
            )
        );
        var rebased = changes.RebaseOnto(authoritative);
        rebased.HasConflicts.ShouldBeFalse();
        var merged = rebased.Patch.ToPatch().Apply(authoritative);
        merged.Value!.Number.Value.ShouldBe("ORD-2");
        merged.Value!.Lines.Value!.Select(line => line.Sku).ShouldBe(["a", "b", "c"]);

        // No automatic live-model replacement: the session still owns its own model.
        session.Model.Number.ShouldBe("ORD-2");
        session.Model.Lines.Count.ShouldBe(2);
        session.HasChanges.ShouldBeTrue();
    }

    [Test]
    public void EditSessionChangeSet_SerializeDeserialize_PreservesRebaseAndApplySemantics()
    {
        // #87/#102: one representative end-to-end test over the serialization
        // boundary (ordinary System.Text.Json), not the full #86 matrix.
        var session = Order().CreateEditSession();
        session.Model.Number = "ORD-2";
        session.Model.Customer.Email = "new@example.com";
        session.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;

        var changes = session.CreateChangeSet();
        changes.IsEmpty.ShouldBeFalse();

        var json = System.Text.Json.JsonSerializer.Serialize(changes);
        var restored = System.Text.Json.JsonSerializer.Deserialize<OrderDto.ChangeSet>(json)!;
        restored.IsEmpty.ShouldBeFalse();

        // Same apply semantics from the session baseline.
        PatchesShouldBeEquivalent(changes.ToPatch(), restored.ToPatch());
        var baseline = Present(OrderDto.Fragment.From(Order()));
        OrderDto.Patch.Between(
            changes.ToPatch().Apply(baseline),
            restored.ToPatch().Apply(baseline)
        )
            .IsEmpty.ShouldBeTrue();

        // Same rebase semantics onto a disjoint authoritative state.
        var authoritative = Present(
            OrderDto.Fragment.From(
                new OrderDto
                {
                    Number = "ORD-1",
                    Customer = new OrderCustomer { Name = "Ada", Email = "ada@example.com" },
                    Lines = new()
                    {
                        new OrderLine { Sku = "a", Quantity = 1, Price = 10m },
                        new OrderLine { Sku = "b", Quantity = 2, Price = 20m },
                        new OrderLine { Sku = "c", Quantity = 3, Price = 30m },
                    },
                    Tags = new() { "fragile" },
                }
            )
        );
        var rebasedOriginal = changes.RebaseOnto(authoritative);
        var rebasedRestored = restored.RebaseOnto(authoritative);
        rebasedRestored.HasConflicts.ShouldBe(rebasedOriginal.HasConflicts);
        rebasedRestored.HasConflicts.ShouldBeFalse();
        PatchesShouldBeEquivalent(
            rebasedOriginal.Patch.ToPatch(),
            rebasedRestored.Patch.ToPatch()
        );
        var mergedOriginal = rebasedOriginal.Patch.ToPatch().Apply(authoritative);
        var mergedRestored = rebasedRestored.Patch.ToPatch().Apply(authoritative);
        OrderDto.Patch.Between(mergedOriginal, mergedRestored).IsEmpty.ShouldBeTrue();
        mergedRestored.Value!.Number.Value.ShouldBe("ORD-2");
        mergedRestored.Value!.Customer.Value!.Email.Value.ShouldBe("new@example.com");
        mergedRestored.Value!.Lines.Value!.Single(line => line.Sku == "b")
            .Quantity.ShouldBe(9);
    }

    [Test]
    public void RepeatedCreateAcceptEditCycles()
    {
        var session = Order().CreateEditSession();

        for (var i = 0; i < 3; i++)
        {
            session.Model.Number = "ORD-" + i;
            session.HasChanges.ShouldBeTrue();
            session.CreateChangeSet().IsEmpty.ShouldBeFalse();
            session.CreatePatch().IsEmpty.ShouldBeFalse();
            session.AcceptChanges();
            session.HasChanges.ShouldBeFalse();
            session.CreateChangeSet().IsEmpty.ShouldBeTrue();
            session.CreatePatch().IsEmpty.ShouldBeTrue();
        }
    }

    [Test]
    public void ValidationMessageStorePrimitive()
    {
        var session = Order().CreateEditSession();
        var store = session.CreateValidationStore();

        SparseEditSession<
            OrderDto,
            OrderDto.Fragment,
            OrderDto.Patch,
            OrderDto.ChangeSet
        >.AddValidationError(
            store,
            session.Field(nameof(OrderDto.Number)),
            "Server rejected the order number."
        );

        session.EditContext.GetValidationMessages().ShouldContain("Server rejected the order number.");
    }
}
