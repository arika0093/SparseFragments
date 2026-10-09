using SparseFragments.Generated;

namespace SparseFragments.Tests;

/// <summary>Tabular contract matrix over the primary descriptor shapes.</summary>
/// <remarks>
/// Each row pins one shape: which live accessors exist, which static shapes
/// are reported, and whether the member is editable. Per-issue suites cover
/// deeper behavior; this matrix locks the cross-cutting contract in one place,
/// including intentionally unsupported capabilities.
/// </remarks>
public sealed class DescriptorBehaviorMatrixTests
{
    private sealed record MatrixRow
    {
        public required string Case { get; init; }

        public required Func<IDescriptor> Resolve { get; init; }

        public required bool LiveChild { get; init; }

        public required bool LiveArray { get; init; }

        public required bool LiveDictionary { get; init; }

        public required bool LiveSet { get; init; }

        public required bool ShapeChild { get; init; }

        public required bool ShapeArray { get; init; }

        public required bool ShapeDictionary { get; init; }

        public required bool ShapeSet { get; init; }

        public required bool Editable { get; init; }

        public required bool Keyed { get; init; }
    }

    private static IReadOnlyList<MatrixRow> Rows()
    {
        var scalarModel = new ObservableHolder { Secret = "s" };
        var scalarSession = scalarModel.CreateEditSession();
        var childModel = new ObservableHolder { Child = new ObservableChild { Name = "n" } };
        var childSession = childModel.CreateEditSession();
        var nullChildSession = new ObservableHolder { MaybeChild = null }.CreateEditSession();
        var listSession = new ObservableHolder { ReplacementTags = ["a"] }.CreateEditSession();
        var arraySession = new ObservableHolder { Labels = ["a"] }.CreateEditSession();
        var readOnlySession = new ReadOnlySequenceHolder().CreateEditSession();
        var backing = new List<string> { "x" };
        var snapshotSession = new ReadOnlySequenceHolder
        {
            Tags = backing.Select(item => item),
        }.CreateEditSession();
        var dictSession = new ObservableHolder
        {
            Metadata = new() { ["k"] = "v" },
        }.CreateEditSession();
        var readOnlyDictSession = new ReadOnlyDictHolder().CreateEditSession();
        var sortedDictSession = new ReadOnlyDictHolder().CreateEditSession();
        var setSession = new SetHolder().CreateEditSession();
        var readOnlySetSession = new SetHolder
        {
            ReadOnly = new CustomReadOnlySet<string>(["x"]),
        }.CreateEditSession();
        var keyedSession = new KeyedServerHolder
        {
            Items = [new KeyedServer { Id = "a", Name = "a" }],
        }.CreateEditSession();
        var requiredSession = new RequiredHolder
        {
            Name = "n",
            Nickname = null,
            Count = 1,
            Code = "c",
        }.CreateEditSession();
        var collisionSession = new ObservableCollision
        {
            Observable = "o",
            Model = "m",
            PropertyChanged = "p",
        }.CreateEditSession();
        var initSession = new ObservableHolder { OwningCount = 1 }.CreateEditSession();

        IDescriptor Get(object session, string name)
        {
            // Generated edit sessions share no common Descriptors contract, so the
            // matrix resolves through the public duck-typed surface instead.
            dynamic duck = session;
            IDescriptorSet descriptors = duck.Descriptors;
            descriptors.TryGet(name, out IDescriptor? found).ShouldBeTrue();
            return found!;
        }

        return
        [
            new MatrixRow
            {
                Case = "scalar editable",
                Resolve = () => Get(scalarSession, nameof(ObservableHolder.Secret)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "child live",
                Resolve = () => Get(childSession, nameof(ObservableHolder.Child)),
                LiveChild = true,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = true,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "child null keeps shape",
                Resolve = () => Get(nullChildSession, nameof(ObservableHolder.MaybeChild)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = true,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "mutable list",
                Resolve = () => Get(listSession, nameof(ObservableHolder.ReplacementTags)),
                LiveChild = false,
                LiveArray = true,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = true,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "csharp array immutable",
                Resolve = () => Get(arraySession, nameof(ObservableHolder.Labels)),
                LiveChild = false,
                LiveArray = true,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = true,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "readonly list immutable",
                Resolve = () => Get(readOnlySession, nameof(ReadOnlySequenceHolder.Names)),
                LiveChild = false,
                LiveArray = true,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = true,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "pure enumerable snapshot",
                Resolve = () => Get(snapshotSession, nameof(ReadOnlySequenceHolder.Tags)),
                LiveChild = false,
                LiveArray = true,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = true,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "mutable dictionary",
                Resolve = () => Get(dictSession, nameof(ObservableHolder.Metadata)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = true,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = true,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "readonly dictionary immutable",
                Resolve = () => Get(readOnlyDictSession, nameof(ReadOnlyDictHolder.ReadOnlyScores)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = true,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = true,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "sorted dictionary immutable",
                Resolve = () => Get(sortedDictSession, nameof(ReadOnlyDictHolder.SortedReadOnly)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = true,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = true,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "mutable set",
                Resolve = () => Get(setSession, nameof(SetHolder.Tags)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = true,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = true,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "readonly set immutable",
                Resolve = () => Get(readOnlySetSession, nameof(SetHolder.ReadOnly)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = true,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = true,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "keyed list",
                Resolve = () => Get(keyedSession, nameof(KeyedServerHolder.Items)),
                LiveChild = false,
                LiveArray = true,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = true,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = true,
            },
            new MatrixRow
            {
                Case = "required member",
                Resolve = () => Get(requiredSession, nameof(RequiredHolder.Name)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "propertychanged collision",
                Resolve = () => Get(collisionSession, "PropertyChanged"),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = true,
                Keyed = false,
            },
            new MatrixRow
            {
                Case = "init-only readonly",
                Resolve = () => Get(initSession, nameof(ObservableHolder.OwningCount)),
                LiveChild = false,
                LiveArray = false,
                LiveDictionary = false,
                LiveSet = false,
                ShapeChild = false,
                ShapeArray = false,
                ShapeDictionary = false,
                ShapeSet = false,
                Editable = false,
                Keyed = false,
            },
        ];
    }

    [Test]
    public void MatrixHasSixteenPrimaryShapes()
    {
        Rows().Count.ShouldBe(16);
    }

    [Test]
    public void MatrixLiveAccessorsMatchExpectations()
    {
        foreach (var row in Rows())
        {
            var descriptor = row.Resolve();
            (descriptor.Child is not null).ShouldBe(row.LiveChild, row.Case);
            (descriptor.Array is not null).ShouldBe(row.LiveArray, row.Case);
            (descriptor.Dictionary is not null).ShouldBe(row.LiveDictionary, row.Case);
            (descriptor.Set is not null).ShouldBe(row.LiveSet, row.Case);
        }
    }

    [Test]
    public void MatrixShapesMatchExpectations()
    {
        foreach (var row in Rows())
        {
            var descriptor = row.Resolve();
            descriptor.Shape.HasChild.ShouldBe(row.ShapeChild, row.Case);
            descriptor.Shape.HasArray.ShouldBe(row.ShapeArray, row.Case);
            descriptor.Shape.HasDictionary.ShouldBe(row.ShapeDictionary, row.Case);
            descriptor.Shape.HasSet.ShouldBe(row.ShapeSet, row.Case);
        }
    }

    [Test]
    public void MatrixLiveImpliesShape()
    {
        foreach (var row in Rows())
        {
            var descriptor = row.Resolve();
            if (descriptor.Child is not null)
            {
                descriptor.Shape.HasChild.ShouldBeTrue(row.Case);
            }

            if (descriptor.Array is not null)
            {
                descriptor.Shape.HasArray.ShouldBeTrue(row.Case);
            }

            if (descriptor.Dictionary is not null)
            {
                descriptor.Shape.HasDictionary.ShouldBeTrue(row.Case);
            }

            if (descriptor.Set is not null)
            {
                descriptor.Shape.HasSet.ShouldBeTrue(row.Case);
            }
        }
    }

    [Test]
    public void MatrixMetadataIsComplete()
    {
        foreach (var row in Rows())
        {
            var descriptor = row.Resolve();
            descriptor.Name.ShouldNotBeNullOrEmpty(row.Case);
            descriptor.Path.ShouldNotBeNullOrEmpty(row.Case);
            descriptor.Type.ShouldNotBeNull(row.Case);
            descriptor.ViewType.ShouldNotBeNull(row.Case);
            descriptor.Attributes.ShouldNotBeNull(row.Case);
            descriptor.Shape.ShouldNotBeNull(row.Case);
            descriptor.IsEditable.ShouldBe(row.Editable, row.Case);
            descriptor.IsReadOnly.ShouldBe(!row.Editable, row.Case);
            (descriptor.Array?.IsKeyed == true).ShouldBe(row.Keyed, row.Case);
        }
    }

    [Test]
    public void MatrixInvalidInputsFailPredictably()
    {
        foreach (var row in Rows())
        {
            var descriptor = row.Resolve();
            // Wrong-typed values never mutate through the generic contract.
            descriptor.TrySetValue(new object()).ShouldBeFalse(row.Case);
            if (descriptor.Array is not null)
            {
                descriptor.Array.TryAdd(new object()).ShouldBeFalse(row.Case);
            }

            if (descriptor.Dictionary is not null)
            {
                descriptor.Dictionary.TryAdd(new object(), new object()).ShouldBeFalse(row.Case);
            }

            if (descriptor.Set is not null)
            {
                descriptor.Set.TryAdd(new object()).ShouldBeFalse(row.Case);
            }
        }
    }
}
