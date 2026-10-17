using System.Collections;

namespace SparseTypes.Blazor.Tests;

// Element-visit accounting for the keyed lookup index: enumerations are
// counted so tests prove repeated resolution of N rows stays linear instead
// of quadratic.
public sealed class KeyedLookupIndexTests
{
    private static T Raw<T>(SparseTypes.ISparseEditSession<T> session)
        where T : class => session.Model;

    private sealed class CountingList<T> : IList<T>, IList
    {
        private readonly List<T> _inner = new();

        public int Enumerations { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            Enumerations++;
            return _inner.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            Enumerations++;
            return ((IEnumerable)_inner).GetEnumerator();
        }

        public T this[int index]
        {
            get => _inner[index];
            set => _inner[index] = value;
        }

        object? IList.this[int index]
        {
            get => _inner[index];
            set => _inner[index] = (T)value!;
        }

        public int Count => _inner.Count;

        public bool IsReadOnly => false;

        bool IList.IsFixedSize => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => ((IList)_inner).SyncRoot;

        public void Add(T item) => _inner.Add(item);

        int IList.Add(object? value)
        {
            _inner.Add((T)value!);
            return _inner.Count - 1;
        }

        public void Clear() => _inner.Clear();

        public bool Contains(T item) => _inner.Contains(item);

        bool IList.Contains(object? value) => value is T item && _inner.Contains(item);

        public void CopyTo(T[] array, int arrayIndex) => _inner.CopyTo(array, arrayIndex);

        void ICollection.CopyTo(Array array, int index) => ((IList)_inner).CopyTo(array, index);

        public bool Remove(T item) => _inner.Remove(item);

        void IList.Remove(object? value)
        {
            if (value is T item)
            {
                _inner.Remove(item);
            }
        }

        public int IndexOf(T item) => _inner.IndexOf(item);

        int IList.IndexOf(object? value) => value is T item ? _inner.IndexOf(item) : -1;

        public void Insert(int index, T item) => _inner.Insert(index, item);

        void IList.Insert(int index, object? value) => _inner.Insert(index, (T)value!);

        public void RemoveAt(int index) => _inner.RemoveAt(index);
    }

    private static CountedOrder CountedOrder(int size)
    {
        var order = new CountedOrder { Number = "ORD-1" };
        var lines = new CountingList<OrderLine>();
        for (var i = 0; i < size; i++)
        {
            lines.Add(
                new OrderLine
                {
                    Sku = $"k-{i:00000}",
                    Quantity = i,
                    Price = i,
                }
            );
        }

        order.Lines = lines;
        return order;
    }

    [Test]
    public void RepeatedKeyedResolutionStaysLinearInElementVisits()
    {
        foreach (var size in new[] { 10, 100, 1000, 10000 })
        {
            var order = CountedOrder(size);
            var session = order.CreateEditSession();
            var lines = (CountingList<OrderLine>)Raw(session).Lines;
            var expected = lines.ToDictionary(static line => line.Sku);

            var baseline = lines.Enumerations;
            for (var i = 0; i < size; i++)
            {
                var sku = $"k-{i:00000}";
                var field = session.Field($"Lines[\"{sku}\"].Quantity");
                ReferenceEquals(field.Model, expected[sku]).ShouldBeTrue();
                field.FieldName.ShouldBe(nameof(OrderLine.Quantity));
            }

            // One build scan plus the miss that triggered it; every later row
            // is a validated index hit with no enumeration.
            (lines.Enumerations - baseline).ShouldBeLessThanOrEqualTo(2);

            var steady = lines.Enumerations;
            for (var i = 0; i < size; i++)
            {
                session.Field($"Lines[\"k-{i:00000}\"].Quantity");
            }

            (lines.Enumerations - steady).ShouldBe(0);
        }
    }

    [Test]
    public void TemporaryKeyResolutionStaysLinearInElementVisits()
    {
        const int size = 500;
        var board = new CountedBoard { Name = "Board" };
        var drafts = new CountingList<BlazorDraftLine>();
        var keys = new List<Guid>();
        for (var i = 0; i < size; i++)
        {
            var temporary = Guid.NewGuid();
            keys.Add(temporary);
            drafts.Add(
                new BlazorDraftLine
                {
                    Id = 0,
                    Title = $"draft-{i}",
                    TemporaryId = temporary,
                }
            );
        }

        board.Drafts = drafts;
        var session = board.CreateEditSession();
        var live = (CountingList<BlazorDraftLine>)Raw(session).Drafts;
        var expected = live.ToDictionary(static line => line.TemporaryId!.Value);
        var root = CountedBoard.SparsePath.Drafts;

        foreach (var temporary in keys)
        {
            var field = session.Field(root.TemporaryKey(temporary).Title);
            ReferenceEquals(field.Model, expected[temporary]).ShouldBeTrue();
        }

        // Re-resolving every temporary row performs no additional enumeration.
        var steady = live.Enumerations;
        foreach (var temporary in keys)
        {
            session.Field(root.TemporaryKey(temporary).Title);
        }

        (live.Enumerations - steady).ShouldBe(0);
    }

    [Test]
    public void StringNumericAndGuidKeysResolveToCurrentOwners()
    {
        var stringSession = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
        }.CreateEditSession();
        var stringField = stringSession.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(stringField.Model, Raw(stringSession).Lines[0]).ShouldBeTrue();

        var numericSession = new BlazorUnassignedOrder
        {
            Number = "ORD-1",
            Items = new()
            {
                new BlazorUnassignedItem { Id = 7, Name = "existing" },
            },
        }.CreateEditSession();
        var numericField = numericSession.Field(BlazorUnassignedOrder.SparsePath.Items.Key(7).Name);
        ReferenceEquals(numericField.Model, Raw(numericSession).Items[0]).ShouldBeTrue();

        var contactId = Guid.NewGuid();
        var guidSession = new GuidRoster
        {
            Members = new()
            {
                new GuidContact { Id = contactId, Name = "Ada" },
            },
        }.CreateEditSession();
        var guidField = guidSession.Field(GuidRoster.SparsePath.Members.Key(contactId).Name);
        ReferenceEquals(guidField.Model, Raw(guidSession).Members[0]).ShouldBeTrue();
    }

    [Test]
    public void TemporaryAndAssignedKeysKeepTheirDistinction()
    {
        var temporary = Guid.NewGuid();
        var session = new BlazorDraftBoard
        {
            Name = "Board",
            Drafts = new()
            {
                new BlazorDraftLine
                {
                    Id = 0,
                    Title = "draft",
                    TemporaryId = temporary,
                },
            },
        }.CreateEditSession();
        var root = BlazorDraftBoard.SparsePath.Drafts;

        var byTemporary = session.Field(root.TemporaryKey(temporary).Title);
        var byUnassigned = session.Field(root.Key(0).Title);
        ReferenceEquals(byTemporary.Model, byUnassigned.Model).ShouldBeTrue();

        Raw(session).Drafts[0].Id = 5;
        var assigned = session.Field(root.Key(5).Title);
        ReferenceEquals(assigned.Model, Raw(session).Drafts[0]).ShouldBeTrue();
        var stillTemporary = session.Field(root.TemporaryKey(temporary).Title);
        ReferenceEquals(stillTemporary.Model, Raw(session).Drafts[0]).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => session.Field(root.Key(0).Title));

        var orphan = Raw(session).Drafts[0];
        var reissued = new BlazorDraftLine
        {
            Id = 0,
            Title = "reissued",
            TemporaryId = temporary,
        };
        Raw(session).Drafts[0] = reissued;
        var replaced = session.Field(root.TemporaryKey(temporary).Title);
        ReferenceEquals(replaced.Model, reissued).ShouldBeTrue();
        ReferenceEquals(replaced.Model, orphan).ShouldBeFalse();

        Raw(session).Drafts.Clear();
        Should.Throw<ArgumentException>(() => session.Field(root.TemporaryKey(temporary).Title));
    }

    [Test]
    public void NestedKeyedCollectionsResolve()
    {
        var session = new OrgDto
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
                            Skills = new() { "csharp" },
                        },
                        new TeamMember
                        {
                            Id = "m2",
                            Skills = new() { "rust" },
                        },
                    },
                },
            },
        }.CreateEditSession();

        var first = session.Field("Teams[\"t1\"].Members[\"m1\"].Id");
        ReferenceEquals(first.Model, Raw(session).Teams[0].Members[0]).ShouldBeTrue();
        var second = session.Field(OrgDto.SparsePath.Teams.Key("t1").Members.Key("m2").Id);
        ReferenceEquals(second.Model, Raw(session).Teams[0].Members[1]).ShouldBeTrue();

        Raw(session).Teams[0].Members.Reverse();
        var moved = session.Field("Teams[\"t1\"].Members[\"m1\"].Id");
        ReferenceEquals(moved.Model, Raw(session).Teams[0].Members[1]).ShouldBeTrue();
    }

    [Test]
    public void DictionaryAndReadOnlyShapesResolve()
    {
        var order = new OrderDto
        {
            Number = "ORD-1",
            Contacts = new() { ["billing"] = new OrderCustomer { Name = "Ada" } },
        }.CreateEditSession();
        var contact = order.Field("Contacts[\"billing\"].Name");
        ReferenceEquals(contact.Model, Raw(order).Contacts["billing"]).ShouldBeTrue();

        var book = new ReadOnlyContactBook
        {
            ByName = new ReadOnlyDictionaryStub<string, OrderCustomer>(
                new Dictionary<string, OrderCustomer>
                {
                    ["billing"] = new OrderCustomer { Name = "Ada" },
                }
            ),
        }.CreateEditSession();
        var readOnlyContact = book.Field("ByName[\"billing\"].Name");
        ReferenceEquals(readOnlyContact.Model, Raw(book).ByName["billing"]).ShouldBeTrue();

        var sheet = new ReadOnlyLineSheet
        {
            Lines = new ReadOnlyListStub<OrderLine>(
                new List<OrderLine>
                {
                    new()
                    {
                        Sku = "a",
                        Quantity = 1,
                        Price = 10m,
                    },
                    new()
                    {
                        Sku = "b",
                        Quantity = 2,
                        Price = 20m,
                    },
                }
            ),
        }.CreateEditSession();
        var readOnlyLine = sheet.Field("Lines[\"b\"].Quantity");
        ReferenceEquals(readOnlyLine.Model, Raw(sheet).Lines[1]).ShouldBeTrue();

        Raw(sheet).Lines = new ReadOnlyListStub<OrderLine>(
            new List<OrderLine>
            {
                new()
                {
                    Sku = "b",
                    Quantity = 3,
                    Price = 30m,
                },
            }
        );
        var replaced = sheet.Field("Lines[\"b\"].Quantity");
        ReferenceEquals(replaced.Model, Raw(sheet).Lines[0]).ShouldBeTrue();
        ((OrderLine)replaced.Model).Quantity.ShouldBe(3);
    }

    [Test]
    public void DuplicateKeysKeepFirstMatchSemantics()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "dup",
                    Quantity = 1,
                    Price = 10m,
                },
                new OrderLine
                {
                    Sku = "dup",
                    Quantity = 2,
                    Price = 20m,
                },
            },
        }.CreateEditSession();

        var first = session.Field("Lines[\"dup\"].Quantity");
        ReferenceEquals(first.Model, Raw(session).Lines[0]).ShouldBeTrue();

        Raw(session).Lines.RemoveAt(0);
        var second = session.Field("Lines[\"dup\"].Quantity");
        ReferenceEquals(second.Model, Raw(session).Lines[0]).ShouldBeTrue();
        ((OrderLine)second.Model).Quantity.ShouldBe(2);
    }

    [Test]
    public void InvalidKeysThrowWithoutPoisoningTheIndex()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
        }.CreateEditSession();

        Should.Throw<ArgumentException>(() => session.Field("Lines[\"missing\"].Quantity"));

        var survivor = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(survivor.Model, Raw(session).Lines[0]).ShouldBeTrue();
        Should.Throw<ArgumentException>(() => session.Field("Lines[\"missing\"].Quantity"));
    }

    [Test]
    public void SameKeyReplacementRemovalAndReorderTargetLiveRows()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
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
        }.CreateEditSession();
        session.Field("Lines[\"a\"].Quantity");
        session.Field("Lines[\"b\"].Quantity");

        var replacement = new OrderLine
        {
            Sku = "a",
            Quantity = 11,
            Price = 110m,
        };
        var orphan = Raw(session).Lines[0];
        Raw(session).Lines[0] = replacement;
        var replaced = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(replaced.Model, replacement).ShouldBeTrue();
        ReferenceEquals(replaced.Model, orphan).ShouldBeFalse();

        Raw(session).Lines = Raw(session).Lines.AsEnumerable().Reverse().ToList();
        var reordered = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(reordered.Model, replacement).ShouldBeTrue();

        Raw(session).Lines.RemoveAll(line => line.Sku == "a");
        Should.Throw<ArgumentException>(() => session.Field("Lines[\"a\"].Quantity"));
        var survivor = session.Field("Lines[\"b\"].Quantity");
        ReferenceEquals(survivor.Model, Raw(session).Lines.Single(line => line.Sku == "b"))
            .ShouldBeTrue();
    }

    [Test]
    public void CollectionInstanceReplacementStartsANewScope()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
        }.CreateEditSession();
        session.Field("Lines[\"a\"].Quantity");

        var fresh = new OrderLine
        {
            Sku = "a",
            Quantity = 7,
            Price = 70m,
        };
        Raw(session).Lines = new List<OrderLine> { fresh };
        var field = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(field.Model, fresh).ShouldBeTrue();
    }

    [Test]
    public void RawKeyEditsMoveIdentity()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
        }.CreateEditSession();
        session.Field("Lines[\"a\"].Quantity");

        Raw(session).Lines[0].Sku = "a2";
        Should.Throw<ArgumentException>(() => session.Field("Lines[\"a\"].Quantity"));
        var moved = session.Field("Lines[\"a2\"].Quantity");
        ReferenceEquals(moved.Model, Raw(session).Lines[0]).ShouldBeTrue();
    }

    [Test]
    public void UndoAndRedoFollowLiveRows()
    {
        var live = new OrderDto
        {
            Number = "ORD-1",
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
        };
        var session = live.CreateEditSession();
        var history = session.EnableHistory();
        session.Field("Lines[\"a\"].Quantity");

        session.Observable.Lines.RemoveAt(0);
        Should.Throw<ArgumentException>(() => session.Field("Lines[\"a\"].Quantity"));

        history.Undo();
        var restored = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(restored.Model, live.Lines.Single(line => line.Sku == "a")).ShouldBeTrue();

        history.Redo();
        Should.Throw<ArgumentException>(() => session.Field("Lines[\"a\"].Quantity"));
        var survivor = session.Field("Lines[\"b\"].Quantity");
        ReferenceEquals(survivor.Model, live.Lines.Single(line => line.Sku == "b")).ShouldBeTrue();
    }

    [Test]
    public void ReconcileKeepsKeyedResolution()
    {
        var live = OrderBaseline();
        var session = live.CreateEditSession();
        session.Field("Lines[\"a\"].Quantity");
        session.Observable.Lines.Move(0, 1);

        var submitted = session.CreateChangeSet();
        submitted
            .TryApplyTo(OrderBaseline(), out var persisted, out var applyConflicts)
            .ShouldBeTrue();
        applyConflicts.ShouldBeNull();
        session.TryReconcile(submitted, persisted!, out var error).ShouldBeTrue();
        error.ShouldBeNull();

        var moved = session.Field("Lines[\"a\"].Quantity");
        ReferenceEquals(moved.Model, live.Lines.Single(line => line.Sku == "a")).ShouldBeTrue();
    }

    [Test]
    public void BindingReadsAndWritesFollowTheLiveRow()
    {
        var session = new OrderDto
        {
            Number = "ORD-1",
            Lines = new()
            {
                new OrderLine
                {
                    Sku = "a",
                    Quantity = 1,
                    Price = 10m,
                },
            },
        }.CreateEditSession();
        var editContext = session.CreateEditContext();
        var binding = session.Bind(OrderDto.SparsePath.Lines.Key("a").Quantity, editContext);
        binding.Value.ShouldBe(1);

        var replacement = new OrderLine
        {
            Sku = "a",
            Quantity = 11,
            Price = 110m,
        };
        Raw(session).Lines[0] = replacement;
        binding.IsStale.ShouldBeFalse();
        binding.Value.ShouldBe(11);
        ReferenceEquals(binding.Field.Model, replacement).ShouldBeTrue();

        binding.TrySet(12, out var setError).ShouldBeTrue();
        setError.ShouldBeNull();
        replacement.Quantity.ShouldBe(12);

        Raw(session).Lines.Clear();
        binding.IsStale.ShouldBeTrue();
    }

    private static OrderDto OrderBaseline() =>
        new()
        {
            Number = "ORD-1",
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
        };
}
