using System.ComponentModel.DataAnnotations;
using SparseFragments.Generated;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class MetadataSeparationChild
{
    public string Value { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class MetadataSeparationModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Nickname { get; set; }

    public string Title => Name;

    public string Code { get; init; } = string.Empty;

    public MetadataSeparationChild Child { get; set; } = new();

    public List<string> Tags { get; set; } = [];

    public Dictionary<string, int> Scores { get; set; } = [];
}

/// <summary>Static/instance/change-access separation regressions (issue #185).</summary>
public sealed class DescriptorMetadataSeparationTests
{
    [Test]
    public void StaticMetadata_InspectsWithoutObservable()
    {
        // Simulates the once-per-model emitted static entry: no session or
        // observable instance is created to read declared metadata.
        var shape = new SparseDescriptorShape { HasChild = true };
        var entry = new SparseStaticPropertyMetadata(
            "Name",
            typeof(string),
            typeof(string),
            isNullable: false,
            isEditable: true,
            attributes: [new RequiredAttribute()],
            shape: shape
        );
        var model = new SparseStaticModelMetadata(typeof(MetadataSeparationModel), [entry]);

        model.ModelType.ShouldBe(typeof(MetadataSeparationModel));
        model.TryGet("Name", out var metadata).ShouldBeTrue();
        metadata.Name.ShouldBe("Name");
        metadata.DeclaredType.ShouldBe(typeof(string));
        metadata.ViewType.ShouldBe(typeof(string));
        metadata.IsNullable.ShouldBeFalse();
        metadata.IsRequired.ShouldBeFalse();
        metadata.IsEditable.ShouldBeTrue();
        metadata.IsReadOnly.ShouldBeFalse();
        metadata.Attributes.OfType<RequiredAttribute>().Count().ShouldBe(1);
        ReferenceEquals(metadata.Shape, shape).ShouldBeTrue();
        model.TryGet("Missing", out _).ShouldBeFalse();
    }

    [Test]
    public void LiveDescriptors_ExposeSeparatedContracts()
    {
        var model = new MetadataSeparationModel { Name = "before", Code = "code" };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Name), out var name)
            .ShouldBeTrue();
        var metadata = (ISparsePropertyMetadata)name;
        var access = (ISparseInstanceAccess)name;
        metadata.DeclaredType.ShouldBe(typeof(string));
        ((SparseDescriptor)name).DeclaredType.ShouldBe(name.Type);
        metadata.Attributes.OfType<RequiredAttribute>().Count().ShouldBe(1);
        access.GetValue().ShouldBe("before");
        access.TrySetValue("after").ShouldBeTrue();
        model.Name.ShouldBe("after");

        // Nullable, read-only and init-only members keep their static flags.
        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Nickname), out var nickname)
            .ShouldBeTrue();
        ((ISparsePropertyMetadata)nickname).IsNullable.ShouldBeTrue();
        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Code), out var code)
            .ShouldBeTrue();
        code.IsEditable.ShouldBeFalse();
        code.IsReadOnly.ShouldBeTrue();
    }

    [Test]
    public void InstanceAccess_EditsNestedListDictionary()
    {
        var model = new MetadataSeparationModel
        {
            Child = new MetadataSeparationChild { Value = "old" },
            Tags = ["one"],
            Scores = new() { ["a"] = 1 },
        };
        var session = model.CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Child), out var child)
            .ShouldBeTrue();
        var childSet = ((ISparseInstanceAccess)child).Child.ShouldNotBeNull();
        childSet!.TryGet(nameof(MetadataSeparationChild.Value), out var nested).ShouldBeTrue();
        nested.TrySetValue("new").ShouldBeTrue();
        model.Child.Value.ShouldBe("new");

        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Tags), out var tags)
            .ShouldBeTrue();
        var array = ((ISparseInstanceAccess)tags).Array.ShouldNotBeNull();
        array!.TryAdd("two").ShouldBeTrue();
        model.Tags.ShouldBe(["one", "two"]);

        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Scores), out var scores)
            .ShouldBeTrue();
        var dictionary = ((ISparseInstanceAccess)scores).Dictionary.ShouldNotBeNull();
        dictionary!.TryAdd("b", 2).ShouldBeTrue();
        model.Scores["b"].ShouldBe(2);
    }

    [Test]
    public void ChangeInspector_ProjectsChangeSetWithoutModelTypes()
    {
        var baseline = new MetadataSeparationModel { Name = "before" };
        var current = new MetadataSeparationModel { Name = "after" };
        var changes = baseline.CreateChangeSet(current);

        // Merge-time seam (#176 DescriptorFactory): per-model ChangeInfo
        // entries project into the model-independent record contract. Numeric
        // kinds align (Changed=0, Added=1, Removed=2, Order=3); no reflection.
        var records = changes
            .EnumerateChanges()
            .Select(entry => new SparseChangeRecord(
                entry.Path,
                entry.Before.IsPresent
                    ? Optional<object?>.Present(entry.Before.Value)
                    : Optional<object?>.Missing,
                entry.After.IsPresent
                    ? Optional<object?>.Present(entry.After.Value)
                    : Optional<object?>.Missing,
                (SparseChangeKind)(int)entry.Kind
            ))
            .ToList();

        ISparseChangeInspector inspector = new ListChangeInspector(records);
        var projected = inspector.EnumerateChangeRecords();
        projected.Count.ShouldBeGreaterThan(0);
        projected
            .Any(record => record.PathText == nameof(MetadataSeparationModel.Name))
            .ShouldBeTrue();
        var name = projected.Single(record =>
            record.PathText == nameof(MetadataSeparationModel.Name)
        );
        name.Kind.ShouldBe(SparseChangeKind.Changed);
        name.Before.IsPresent.ShouldBeTrue();
        name.After.IsPresent.ShouldBeTrue();
    }

    [Test]
    public void InvariantMetadata_SharedAcrossAccesses()
    {
        var session = new MetadataSeparationModel().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Name), out var first)
            .ShouldBeTrue();
        session
            .Descriptors.TryGet(nameof(MetadataSeparationModel.Name), out var second)
            .ShouldBeTrue();
        ReferenceEquals(first.Attributes, second.Attributes).ShouldBeTrue();
        ReferenceEquals(first.Shape, second.Shape).ShouldBeTrue();
    }

    [Test]
    public void DescriptorCapabilities_AggregateAndValidate()
    {
        SparseDescriptorCapabilities
            .ForCompilation(false, false, false)
            .ShouldBe(SparseDescriptorCapability.None);
        var capability = SparseDescriptorCapabilities.ForCompilation(true, true, true);
        capability.HasFlag(SparseDescriptorCapability.StaticMetadata).ShouldBeTrue();
        capability.HasFlag(SparseDescriptorCapability.InstanceBridges).ShouldBeTrue();
        capability.HasFlag(SparseDescriptorCapability.ChangeProjection).ShouldBeTrue();

        var withoutDialect = new SparseGeneratorConfig(
            ModelAttributeMetadataName: "ModelAttribute",
            IgnoreAttributeMetadataName: "IgnoreAttribute",
            RedactBeforeAttributeMetadataName: "RedactBeforeAttribute",
            MergeAttributeMetadataName: "MergeAttribute",
            MergeStrategyBaseMetadataName: "MergeStrategyBase",
            CloneReferenceSafeAttributeMetadataName: "CloneSafeAttribute",
            KeyAttributeMetadataName: "KeyAttribute",
            MergeModeMap: new SparseMergeModeMap(0, 1, 2, 3, 4, 5),
            DiagnosticIds: new SparseDiagnosticIdMap(
                "T001",
                "T002",
                "T003",
                "T004",
                "T005",
                "T006",
                "T007",
                "T008",
                "T009",
                "T010",
                "T011",
                "T013",
                "T014",
                "T017",
                "T019",
                "T021",
                "T022",
                "T023"
            ),
            HintNameSuffix: ".Downstream.g.cs",
            PromotedHintNameSuffix: ".DownstreamPromoted.g.cs",
            StructuralHostPrefix: "__DownstreamHost_"
        );
        SparseDescriptorCapabilities
            .ValidatePrerequisites(withoutDialect, capability)
            .ShouldNotBeEmpty();
    }

    private sealed class ListChangeInspector : ISparseChangeInspector
    {
        private readonly IReadOnlyList<SparseChangeRecord> _records;

        public ListChangeInspector(IReadOnlyList<SparseChangeRecord> records) => _records = records;

        public IReadOnlyList<SparseChangeRecord> EnumerateChangeRecords() => _records;
    }
}
