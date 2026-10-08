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
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);
        editContext.OnValidationRequested += (sender, _) =>
        {
            store.Clear();
            if (string.IsNullOrEmpty(session.Model.Number))
            {
                store.Add(session.Field(nameof(OrderDto.Number)), "Number is required.");
            }
        };

        editContext.Validate().ShouldBeTrue();

        session.Model.Number = "";
        editContext.NotifyFieldChanged(new FieldIdentifier(session.Model, nameof(OrderDto.Number)));
        editContext.Validate().ShouldBeFalse();
        editContext.GetValidationMessages().ShouldContain("Number is required.");
    }

    [Test]
    public void KeyedCollectionAddRemoveEditSemantics()
    {
        var baseline = Order();
        var session = baseline.CreateEditSession();

        session.Model.Lines.Add(
            new OrderLine
            {
                Sku = "c",
                Quantity = 3,
                Price = 30m,
            }
        );
        session.Model.Lines.RemoveAll(line => line.Sku == "a");
        session.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;

        session.HasChanges.ShouldBeTrue();
        session.CreateChangeSet().IsEmpty.ShouldBeFalse();

        var patch = session.CreateChangeSet().ToPatch();
        var applied = OrderDto
            .Fragment.From(
                new OrderDto
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
                }
            )
            .Apply(patch);

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
                    Members = new()
                    {
                        new TeamMember
                        {
                            Id = "m1",
                            Skills = new() { "c#" },
                        },
                    },
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
        var editContext = session.CreateEditContext();

        // No EditContext.NotifyFieldChanged here: direct list mutation.
        session.Model.Lines.RemoveAt(0);

        editContext.IsModified().ShouldBeFalse();
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
        keyed.Model.Lines.Add(
            new OrderLine
            {
                Sku = "c",
                Quantity = 3,
                Price = 30m,
            }
        );
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
        session.Model.Lines.Add(
            new OrderLine
            {
                Sku = "c",
                Quantity = 3,
                Price = 30m,
            }
        );

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
        var editContext = session.CreateEditContext();

        session.Model.Number = "ORD-2";
        editContext.NotifyFieldChanged(new FieldIdentifier(session.Model, nameof(OrderDto.Number)));
        editContext.IsModified().ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();

        session.AcceptChanges(editContext);

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
        session.CreatePatch().IsEmpty.ShouldBeTrue();
        editContext.IsModified().ShouldBeFalse();

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
                }
            )
        );
        var currentFragment = Present(OrderDto.Fragment.From(session.Model));
        OrderDto
            .Patch.Between(second.ToPatch().Apply(acceptedBaseline), currentFragment)
            .IsEmpty.ShouldBeTrue();

        // Inverting the ChangeSet walks back to the accepted baseline.
        OrderDto
            .Patch.Between(second.Invert().ToPatch().Apply(currentFragment), acceptedBaseline)
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
                        new OrderLine
                        {
                            Sku = "c",
                            Quantity = 3,
                            Price = 30m,
                        },
                    },
                    Tags = new() { "fragile" },
                }
            )
        );
        var rebased = changes.RebaseOnto(authoritative);
        rebased.HasConflicts.ShouldBeFalse();
        var merged = rebased.Rebased.ToPatch().Apply(authoritative);
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
        // #87/#102: one representative end-to-end test over the typed payload
        // serialization boundary, not the full #86 matrix.
        var session = Order().CreateEditSession();
        session.Model.Number = "ORD-2";
        session.Model.Customer.Email = "new@example.com";
        session.Model.Lines.Single(line => line.Sku == "b").Quantity = 9;

        var changes = session.CreateChangeSet();
        changes.IsEmpty.ShouldBeFalse();

        var json = System.Text.Json.JsonSerializer.Serialize(changes.ToPayload());
        var restored = System
            .Text.Json.JsonSerializer.Deserialize<OrderDto.ChangeSetPayload>(json)!
            .ToChangeSet();
        restored.IsEmpty.ShouldBeFalse();

        // Same apply semantics from the session baseline.
        PatchesShouldBeEquivalent(changes.ToPatch(), restored.ToPatch());
        var baseline = Present(OrderDto.Fragment.From(Order()));
        OrderDto
            .Patch.Between(changes.ToPatch().Apply(baseline), restored.ToPatch().Apply(baseline))
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
                        new OrderLine
                        {
                            Sku = "c",
                            Quantity = 3,
                            Price = 30m,
                        },
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
            rebasedOriginal.Rebased.ToPatch(),
            rebasedRestored.Rebased.ToPatch()
        );
        var mergedOriginal = rebasedOriginal.Rebased.ToPatch().Apply(authoritative);
        var mergedRestored = rebasedRestored.Rebased.ToPatch().Apply(authoritative);
        OrderDto.Patch.Between(mergedOriginal, mergedRestored).IsEmpty.ShouldBeTrue();
        mergedRestored.Value!.Number.Value.ShouldBe("ORD-2");
        mergedRestored.Value!.Customer.Value!.Email.Value.ShouldBe("new@example.com");
        mergedRestored.Value!.Lines.Value!.Single(line => line.Sku == "b").Quantity.ShouldBe(9);
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
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        session.AddValidationError(
            store,
            session.Field(nameof(OrderDto.Number)),
            "Server rejected the order number."
        );

        editContext.GetValidationMessages().ShouldContain("Server rejected the order number.");
    }

    [Test]
    public void BlazorHelpersRequireTheSessionsRawModel()
    {
        var model = Order();
        var session = model.CreateEditSession();
        var editContext = session.CreateEditContext();

        ReferenceEquals(editContext.Model, model).ShouldBeTrue();

        var otherContext = new EditContext(Order());
        Should.Throw<ArgumentException>(() => session.CreateValidationStore(otherContext));

        session.Model.Number = "ORD-2";
        Should.Throw<ArgumentException>(() => session.AcceptChanges(otherContext));
        session.HasChanges.ShouldBeTrue();

        var store = session.CreateValidationStore(editContext);
        var otherField = new FieldIdentifier(Order(), nameof(OrderDto.Number));
        Should.Throw<ArgumentException>(() =>
            session.AddValidationError(store, otherField, "Wrong model.")
        );
    }

    [Test]
    public void AcceptedSubmittedChangesKeepLaterKeyedAndScalarEditsPending()
    {
        var session = Order().CreateEditSession();
        var observable = session.Observable;
        session.Model.Number = "ORD-2";
        var submitted = session.CreateChangeSet();

        // Live edits after capture: same-field plus disjoint keyed changes.
        session.Model.Number = "ORD-3";
        session.Model.Lines.Add(
            new OrderLine
            {
                Sku = "c",
                Quantity = 3,
                Price = 30m,
            }
        );
        session.AcceptChanges(submitted);

        ReferenceEquals(session.Observable, observable).ShouldBeTrue();
        session.HasChanges.ShouldBeTrue();
        var next = session.CreateChangeSet();
        next.Number.Before.Value.ShouldBe("ORD-2");
        next.Number.After.Value.ShouldBe("ORD-3");
        next.Lines.GetChange("c").IsAdded.ShouldBeTrue();
        next.Lines.GetChange("a").IsEmpty.ShouldBeTrue();

        // The accepted baseline is exactly the submitted state: replaying the
        // next transition onto it reaches the live model.
        var submittedBaseline = Present(OrderDto.Fragment.From(Order()).Apply(submitted.ToPatch()));
        OrderDto
            .Patch.Between(
                next.ToPatch().Apply(submittedBaseline),
                Present(OrderDto.Fragment.From(session.Model))
            )
            .IsEmpty.ShouldBeTrue();
    }

    [Test]
    public void StaleSubmittedChangesAreRejectedWithoutBaselineChange()
    {
        var session = Order().CreateEditSession();
        session.Model.Number = "ORD-2";
        var stale = session.CreateChangeSet();
        session.Model.Number = "ORD-3";
        session.AcceptChanges();

        Should.Throw<InvalidOperationException>(() => session.AcceptChanges(stale));

        session.HasChanges.ShouldBeFalse();
        session.CreateChangeSet().IsEmpty.ShouldBeTrue();
    }
}
