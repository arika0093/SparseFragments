using SparseFragments;

// Canonical compile-checked mirror of docs/keyed-collections.md (#45).
// Covers the representative element-identity mechanisms users are expected to
// copy: single-property [SparseKey], computed tuple keys, and normalized
// value-object keys, plus keyed Between/Apply add/remove/edit semantics and
// reorder-by-final-key-order. Temporary Guid identity ([SparseTemporaryKey])
// and the DTO reconcile flow mirror the pending-addition sections of the same
// guide.
public static class KeyedCollectionsSamples
{
    public static void Run()
    {
        SinglePropertyKeyAddRemoveEdit();
        TypedCollectionTransitions();
        ReorderByFinalKeyOrder();
        TupleKey();
        NormalizedKey();
        TemporaryIdentityStaysEditable();
        TemporaryAssignedKeyWins();
        TemporaryReconcileAssignsKeys();
    }

    private static void SinglePropertyKeyAddRemoveEdit()
    {
        var before = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new()
                {
                    Id = "a",
                    Host = "A",
                    Port = 1,
                },
                new()
                {
                    Id = "b",
                    Host = "B",
                    Port = 2,
                },
            },
        };
        var after = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new()
                {
                    Id = "b",
                    Host = "B2",
                    Port = 2,
                },
                new()
                {
                    Id = "c",
                    Host = "C",
                    Port = 3,
                },
            },
        };

        var changes = before.CreateChangeSet(after);
        DocsCheck.Require(!changes.IsEmpty, "keyed Between detects add/remove/edit");
        if (!changes.TryApplyTo(before, out var applied))
        {
            throw new InvalidOperationException("The keyed changes conflict.");
        }
        var servers = applied.Servers;
        DocsCheck.Require(
            servers.Select(server => server.Id).SequenceEqual(new[] { "b", "c" }),
            "keyed apply holds [b, c]: b edited in place, a removed, c added"
        );
        DocsCheck.Require(
            servers.Single(server => server.Id == "b").Host == "B2",
            "keyed edit patches only changed members"
        );
        DocsCheck.Require(
            servers.Single(server => server.Id == "b").Port == 2,
            "keyed edit keeps unchanged members"
        );
    }

    private static void TypedCollectionTransitions()
    {
        var before = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new()
                {
                    Id = "a",
                    Host = "A",
                    Port = 1,
                },
                new()
                {
                    Id = "b",
                    Host = "B",
                    Port = 2,
                },
            },
        };
        var after = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new()
                {
                    Id = "b",
                    Host = "B2",
                    Port = 2,
                },
                new()
                {
                    Id = "c",
                    Host = "C",
                    Port = 3,
                },
            },
        };

        // Typed observation mirrors docs/keyed-collections.md "Observe typed
        // collection transitions": Added/Removed/Edited projections,
        // BeforeOrder/AfterOrder/OrderChanged, per-item enumeration, and
        // keyed GetChange lookup.
        var servers = before.CreateChangeSet(after).Servers;
        DocsCheck.Require(servers.IsChanged, "collection transition is non-empty");
        DocsCheck.Require(
            servers.Added.Count == 1 && servers.Added.Single().Id == "c",
            "Added carries the new element"
        );
        DocsCheck.Require(
            servers.Removed.Count == 1 && servers.Removed.Single().Id == "a",
            "Removed carries the old element"
        );
        DocsCheck.Require(
            servers.Edited.Count == 1
                && servers.Edited["b"].Host.IsChanged
                && servers.Edited["b"].Host.Before.Value == "B"
                && servers.Edited["b"].Host.After.Value == "B2",
            "Edited carries the nested member transition"
        );
        DocsCheck.Require(
            servers.BeforeOrder.SequenceEqual(new[] { "a", "b" }),
            "BeforeOrder preserved"
        );
        DocsCheck.Require(
            servers.AfterOrder.SequenceEqual(new[] { "b", "c" }),
            "AfterOrder preserved"
        );
        DocsCheck.Require(servers.OrderChanged, "membership change flips OrderChanged");

        var editedKeys = new List<string>();
        foreach (var item in servers)
        {
            if (item.IsEdited)
            {
                editedKeys.Add(item.Key);
            }
        }
        DocsCheck.Require(editedKeys.SequenceEqual(new[] { "b" }), "enumeration visits edits");

        var edited = servers.GetChange("b");
        DocsCheck.Require(edited.IsEdited, "GetChange observes the edited key");
        DocsCheck.Require(
            edited.Edit.Host.After.Value == "B2",
            "GetChange carries the nested change"
        );
        DocsCheck.Require(!edited.IsAdded && !edited.IsRemoved, "edit is neither add nor remove");
        DocsCheck.Require(servers.GetChange("absent").IsEmpty, "unknown key is empty");
    }

    private static void ReorderByFinalKeyOrder()
    {
        var first = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new() { Id = "a", Host = "A" },
                new() { Id = "b", Host = "B" },
            },
        };
        var reordered = new DocsInventory
        {
            Servers = new List<DocsServer>
            {
                new() { Id = "b", Host = "B" },
                new() { Id = "a", Host = "A" },
            },
        };

        // The final key order determines the resulting order: reversing
        // ["a", "b"] is a real (non-empty) patch.
        var changes = first.CreateChangeSet(reordered);
        DocsCheck.Require(!changes.IsEmpty, "keyed reorder is a non-empty patch");
        var applied = changes.ToPatch().ApplyTo(first);
        DocsCheck.Require(
            applied.Servers.Select(server => server.Id).SequenceEqual(new[] { "b", "a" }),
            "keyed reorder replay reproduces the new order"
        );
    }

    private static void TupleKey()
    {
        var before = new DocsTenantInventory
        {
            Servers = new List<DocsTenantServer>
            {
                new()
                {
                    TenantId = "t1",
                    Id = "a",
                    Host = "A",
                },
            },
        };
        var after = new DocsTenantInventory
        {
            Servers = new List<DocsTenantServer>
            {
                new()
                {
                    TenantId = "t1",
                    Id = "a",
                    Host = "A2",
                },
            },
        };

        // Computed tuple key: the property type is the key type, compared whole.
        var applied = before.CreateChangeSet(after).ToPatch().ApplyTo(before);
        DocsCheck.Require(applied.Servers.Single().Host == "A2", "tuple key element edit");
    }

    private static void NormalizedKey()
    {
        var before = new DocsNormalizedInventory
        {
            Servers = new List<DocsNormalizedServer>
            {
                new()
                {
                    Tenant = "acme",
                    Id = 1,
                    Host = "A",
                },
            },
        };
        var after = new DocsNormalizedInventory
        {
            Servers = new List<DocsNormalizedServer>
            {
                new()
                {
                    Tenant = "acme",
                    Id = 1,
                    Host = "A2",
                },
                new()
                {
                    Tenant = "acme",
                    Id = 2,
                    Host = "B",
                },
            },
        };

        // Computed value-object key: normalized (case-folded) identity.
        var applied = before.CreateChangeSet(after).ToPatch().ApplyTo(before);
        var servers = applied.Servers;
        DocsCheck.Require(servers.Count == 2, "computed key add");
        DocsCheck.Require(
            servers.Single(server => server.Id == 1).Host == "A2",
            "computed key edit"
        );
    }

    private static void TemporaryIdentityStaysEditable()
    {
        var book = new DocsTempOrder { Lines = [new DocsTempLine { Id = 7, Name = "saved" }] };
        var session = book.CreateEditSession();
        var added = new DocsTempLine { Name = "new", TemporaryId = Guid.NewGuid() };
        book.Lines.Add(added);

        var pending = session.CreateChangeSet();
        DocsCheck.Require(
            pending.Lines.GetTemporaryChange(added.TemporaryId!.Value).IsAdded,
            "pending addition identified by temporary Guid"
        );
        session.AcceptChanges(pending);

        book.Lines.Single(item => item.TemporaryId == added.TemporaryId).Name = "new v2";
        DocsCheck.Require(
            session.CreateChangeSet().Lines.GetTemporaryChange(added.TemporaryId!.Value).IsEdited,
            "pending addition stays editable by temporary Guid"
        );

        var path = DocsTempOrder.SparsePath.Lines.TemporaryKey(added.TemporaryId!.Value).Name;
        DocsCheck.Require(
            path.ToString().StartsWith("Lines[temp:", StringComparison.Ordinal),
            "typed path carries a temporary segment"
        );
    }

    private static void TemporaryAssignedKeyWins()
    {
        var before = Optional<DocsTempOrder.Fragment?>.Present(
            DocsTempOrder.Fragment.From(
                new DocsTempOrder { Lines = [new DocsTempLine { Id = 7, Name = "a" }] }
            )
        );
        var after = Optional<DocsTempOrder.Fragment?>.Present(
            DocsTempOrder.Fragment.From(
                new DocsTempOrder
                {
                    Lines =
                    [
                        new DocsTempLine
                        {
                            Id = 7,
                            Name = "b",
                            TemporaryId = Guid.NewGuid(),
                        },
                    ],
                }
            )
        );

        // Same permanent identity: a plain edit, not a new identity.
        var item = DocsTempOrder.ChangeSet.Between(before, after).Lines.GetChange(7);
        DocsCheck.Require(item.IsEdited, "assigned key keeps element identity");
        DocsCheck.Require(
            item.TemporaryKey is null,
            "temporary value is not identity when assigned"
        );
    }

    private static void TemporaryReconcileAssignsKeys()
    {
        var draft = new DocsTempOrder { Lines = [new DocsTempLine { Id = 7, Name = "saved" }] };
        var session = draft.CreateEditSession();
        var added = new DocsTempLine { Name = "new", TemporaryId = Guid.NewGuid() };
        draft.Lines.Add(added);
        var submitted = session.CreateChangeSet();

        var persisted = new DocsTempOrder
        {
            Lines =
            [
                new DocsTempLine { Id = 7, Name = "saved" },
                new DocsTempLine
                {
                    Id = 11,
                    Name = "new",
                    TemporaryId = added.TemporaryId,
                },
            ],
        };

        DocsCheck.Require(
            session.TryReconcile(submitted, persisted, out var error),
            "reconcile correlates temporary Guids: " + error
        );
        DocsCheck.Require(!session.HasChanges, "reconciled session adopts the persisted baseline");
        DocsCheck.Require(
            draft.Lines.Single(item => item.TemporaryId == added.TemporaryId).Id == 11,
            "authoritative key lands in the live model"
        );
    }
}

// Single-property key: one parameterless [SparseKey] property.
public partial class DocsServer
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; }
}

[SparseFragmentModel]
public partial class DocsInventory
{
    public List<DocsServer> Servers { get; set; } = new();
}

// Tuple key: the computed property type is the key type.
public partial class DocsTenantServer
{
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    [SparseKey]
    public (string TenantId, string Id) Key => (TenantId, Id);
}

[SparseFragmentModel]
public partial class DocsTenantInventory
{
    public List<DocsTenantServer> Servers { get; set; } = new();
}

public readonly record struct DocsNormalizedKey(string Tenant, int Id);

// Computed value-object key for identity that cannot be expressed as a
// plain key property, such as normalized (case-folded) identity.
public partial class DocsNormalizedServer
{
    public string Tenant { get; set; } = string.Empty;

    public int Id { get; set; }

    public string Host { get; set; } = string.Empty;

    [SparseKey]
    public DocsNormalizedKey Key => new(Tenant.ToUpperInvariant(), Id);
}

[SparseFragmentModel]
public partial class DocsNormalizedInventory
{
    public List<DocsNormalizedServer> Servers { get; set; } = new();
}

// Temporary identity: an opt-in Guid? property beside a property-level
// [SparseKey] with unassigned semantics. New rows are built with object
// initializers; the property itself carries no initializer.
[SparseFragmentModel]
public partial class DocsTempOrder
{
    public List<DocsTempLine> Lines { get; set; } = new();
}

public partial class DocsTempLine
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [SparseKey(Unassigned = 0)]
    public int Key => Id;

    [SparseTemporaryKey]
    public Guid? TemporaryId { get; set; }
}
