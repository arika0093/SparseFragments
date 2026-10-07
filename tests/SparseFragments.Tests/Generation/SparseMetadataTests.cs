using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments;
using SparseFragments.Generator;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class MetaScalar
{
    public int Count { get; set; }

    public string? Label { get; set; }

    public int? Maybe { get; set; }

    public bool Enabled { get; set; } = true;
}

[SparseFragmentModel]
public partial class MetaNested
{
    public string Host { get; set; } = "localhost";

    public int Port { get; set; } = 5432;
}

[SparseFragmentModel]
public partial class MetaHolder
{
    public string Title { get; set; } = string.Empty;

    public MetaNested? Child { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaCollections
{
    public List<string> Tags { get; set; } = new();

    public int[] Numbers { get; set; } = [];

    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> Unique { get; set; } = new();

    public Dictionary<string, int> Scores { get; set; } = new();
}

public sealed class MetaSumStrategy : FragmentMergeStrategy<List<int>>
{
    public override Optional<List<int>> Merge(
        Optional<List<int>> lowerPriority,
        Optional<List<int>> higherPriority
    )
    {
        if (!higherPriority.IsPresent)
        {
            return lowerPriority;
        }

        if (!lowerPriority.IsPresent)
        {
            return higherPriority;
        }

        return Optional<List<int>>.Present(
            lowerPriority.Value!.Zip(higherPriority.Value!, (a, b) => a + b).ToList()
        );
    }

    public override bool AreEqual(List<int>? left, List<int>? right) =>
        (left is null && right is null)
        || (left is not null && right is not null && left.SequenceEqual(right));
}

[SparseFragmentModel]
public partial class MetaMergeModes
{
    public string Plain { get; set; } = string.Empty;

    public MetaNested DeepChild { get; set; } = new();

    [SparseMerge(MergeMode.Append)]
    public List<string> Appended { get; set; } = new();

    [SparseMerge(MergeMode.SetUnion)]
    public HashSet<string> United { get; set; } = new();

    [SparseMerge(typeof(MetaSumStrategy))]
    public List<int> Custom { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaKeyedElement
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseKey("TenantId", "Id")]
[SparseFragmentModel]
public partial class MetaCompositeElement
{
    public string TenantId { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

[SparseFragmentModel]
public partial class MetaIfaceElement : ISparseKeyed<string>
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string SparseKey => Id;
}

[SparseFragmentModel]
public partial class MetaKeyedHolder
{
    public List<MetaKeyedElement> Items { get; set; } = new();

    public List<MetaCompositeElement> Composites { get; set; } = new();

    public List<MetaIfaceElement> Ifaces { get; set; } = new();

    public List<string> Scalars { get; set; } = new();

    public Dictionary<string, int> Dict { get; set; } = new();
}

public partial class MetaPromotedChild
{
    public string Name { get; set; } = string.Empty;

    public int Count { get; set; }
}

[SparseFragmentModel]
public partial class MetaPromotedHolder
{
    public MetaPromotedChild Child { get; set; } = new();
}

[SparseFragmentModel]
public partial class MetaOrdered
{
    public string Zebra { get; set; } = string.Empty;

    public string Apple { get; set; } = string.Empty;

    public string Mango { get; set; } = string.Empty;
}

public sealed class SparseMetadataTests
{
    [Test]
    public void ScalarPropertyMetadata()
    {
        var byName = MetaScalar.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Count"].PropertyType.ShouldBe(typeof(int));
        byName["Count"].IsNullable.ShouldBeFalse();
        byName["Count"].IsNestedModel.ShouldBeFalse();
        byName["Count"].NestedModelType.ShouldBeNull();
        byName["Count"].MergeMode.ShouldBe(MergeMode.Replace);
        byName["Count"].CollectionKind.ShouldBe(SparseCollectionKind.None);
        byName["Count"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.None);
    }

    [Test]
    public void NullablePropertyMetadata()
    {
        var byName = MetaScalar.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Label"].PropertyType.ShouldBe(typeof(string));
        byName["Label"].IsNullable.ShouldBeTrue();
        byName["Maybe"].PropertyType.ShouldBe(typeof(int?));
        byName["Maybe"].IsNullable.ShouldBeTrue();
        byName["Count"].IsNullable.ShouldBeFalse();
    }

    [Test]
    public void NestedStructuralMetadata()
    {
        var byName = MetaHolder.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Child"].IsNestedModel.ShouldBeTrue();
        byName["Child"].NestedModelType.ShouldBe(typeof(MetaNested));
        byName["Child"].MergeMode.ShouldBe(MergeMode.Deep);
        byName["Title"].IsNestedModel.ShouldBeFalse();
    }

    [Test]
    public void ScalarSequenceMetadata()
    {
        var byName = MetaCollections.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Tags"].CollectionKind.ShouldBe(SparseCollectionKind.List);
        byName["Tags"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.ScalarSequence);
        byName["Tags"].KeyKind.ShouldBe(SparseKeyKind.None);
        byName["Tags"].KeyPropertyNames.ShouldBeEmpty();
        byName["Tags"].KeyType.ShouldBeNull();
        byName["Numbers"].CollectionKind.ShouldBe(SparseCollectionKind.Array);
        byName["Numbers"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.ScalarSequence);
    }

    [Test]
    public void DictionaryMetadata()
    {
        var byName = MetaCollections.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Scores"].CollectionKind.ShouldBe(SparseCollectionKind.Dictionary);
        byName["Scores"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.Dictionary);
    }

    [Test]
    public void MergeModesIncludingCustom()
    {
        var byName = MetaMergeModes.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Plain"].MergeMode.ShouldBe(MergeMode.Replace);
        byName["Plain"].MergeStrategyType.ShouldBeNull();
        byName["DeepChild"].MergeMode.ShouldBe(MergeMode.Deep);
        byName["Appended"].MergeMode.ShouldBe(MergeMode.Append);
        byName["United"].MergeMode.ShouldBe(MergeMode.SetUnion);
        byName["Custom"].MergeMode.ShouldBe(MergeMode.Custom);
        byName["Custom"].MergeStrategyType.ShouldBe(typeof(MetaSumStrategy));
    }

    [Test]
    public void KeyedStructuralSequenceMetadata()
    {
        var byName = MetaKeyedHolder.Sparse.Properties.ToDictionary(p => p.Name);
        var items = byName["Items"];
        items.CollectionSemantic.ShouldBe(SparseCollectionSemantic.KeyedSequence);
        items.KeyKind.ShouldBe(SparseKeyKind.Property);
        items.KeyPropertyNames.ShouldBe(["Id"]);
        items.KeyType.ShouldBe(typeof(string));
    }

    [Test]
    public void CompositeKeyMetadata()
    {
        var byName = MetaKeyedHolder.Sparse.Properties.ToDictionary(p => p.Name);
        var composites = byName["Composites"];
        composites.CollectionSemantic.ShouldBe(SparseCollectionSemantic.KeyedSequence);
        composites.KeyKind.ShouldBe(SparseKeyKind.Composite);
        composites.KeyPropertyNames.ShouldBe(["TenantId", "Id"]);
        composites.KeyType.ShouldNotBeNull();
        composites.KeyType.ShouldBe(typeof((string, string)));
    }

    [Test]
    public void InterfaceKeyMetadata()
    {
        var byName = MetaKeyedHolder.Sparse.Properties.ToDictionary(p => p.Name);
        var ifaces = byName["Ifaces"];
        ifaces.CollectionSemantic.ShouldBe(SparseCollectionSemantic.KeyedSequence);
        ifaces.KeyKind.ShouldBe(SparseKeyKind.Interface);
        ifaces.KeyType.ShouldBe(typeof(string));
    }

    [Test]
    public void ScalarSequenceAndDictionaryOnSameHolder()
    {
        var byName = MetaKeyedHolder.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Scalars"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.ScalarSequence);
        byName["Scalars"].KeyKind.ShouldBe(SparseKeyKind.None);
        byName["Dict"].CollectionKind.ShouldBe(SparseCollectionKind.Dictionary);
        byName["Dict"].CollectionSemantic.ShouldBe(SparseCollectionSemantic.Dictionary);
    }

    [Test]
    public void PromotedNestedModelExposesMetadata()
    {
        var byName = MetaPromotedChild.Sparse.Properties.ToDictionary(p => p.Name);
        byName.ContainsKey("Name").ShouldBeTrue();
        byName.ContainsKey("Count").ShouldBeTrue();
        var holder = MetaPromotedHolder.Sparse.Properties.ToDictionary(p => p.Name);
        holder["Child"].IsNestedModel.ShouldBeTrue();
    }

    [Test]
    public void PropertyOrderingIsDeterministic()
    {
        var names = MetaOrdered.Sparse.Properties.Select(p => p.Name).ToArray();
        names.ShouldBe(["Apple", "Mango", "Zebra"]);
        var first = MetaOrdered.Sparse.Properties.Select(p => p.Name).ToArray();
        first.ShouldBe(names);
    }

    [Test]
    public void PropertiesIsFixedReadOnlyList()
    {
        var properties = MetaScalar.Sparse.Properties;
        properties.ShouldBeAssignableTo<IReadOnlyList<SparsePropertyInfo>>();
        properties.Count.ShouldBe(4);
        foreach (var property in properties)
        {
            property.ShouldNotBeNull();
        }

        // Same fixed instance across accesses (not a lazy re-enumeration).
        ReferenceEquals(MetaScalar.Sparse.Properties, MetaScalar.Sparse.Properties).ShouldBeTrue();
    }

    [Test]
    public void PropertiesCannotBeCastToArray()
    {
        (MetaScalar.Sparse.Properties is SparsePropertyInfo[]).ShouldBeFalse();
        (MetaKeyedHolder.Sparse.Properties is SparsePropertyInfo[]).ShouldBeFalse();
    }

    [Test]
    public void PropertiesRejectMutationThroughListInterface()
    {
        var properties = (IList<SparsePropertyInfo>)MetaScalar.Sparse.Properties;
        properties.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => properties.Add(MetaScalar.Sparse.Properties[0]));
        Should.Throw<NotSupportedException>(() => properties.RemoveAt(0));
        // Failed mutation leaves the shared instance intact.
        MetaScalar.Sparse.Properties.Count.ShouldBe(4);
        MetaScalar.Sparse.Properties[0].Name.ShouldBe("Count");
    }

    [Test]
    public void KeyPropertyNamesCannotBeCastToArray()
    {
        var items = MetaKeyedHolder.Sparse.Properties.Single(p => p.Name == "Items");
        (items.KeyPropertyNames is string[]).ShouldBeFalse();
        var composites = MetaKeyedHolder.Sparse.Properties.Single(p => p.Name == "Composites");
        (composites.KeyPropertyNames is string[]).ShouldBeFalse();
    }

    [Test]
    public void KeyPropertyNamesRejectMutation()
    {
        var items = MetaKeyedHolder.Sparse.Properties.Single(p => p.Name == "Items");
        var names = (IList<string>)items.KeyPropertyNames;
        names.IsReadOnly.ShouldBeTrue();
        Should.Throw<NotSupportedException>(() => names.Add("evil"));
        Should.Throw<NotSupportedException>(() => names.RemoveAt(0));
        items.KeyPropertyNames.ShouldBe(["Id"]);
    }

    [Test]
    public void ConstructorInputMutationsDoNotLeakIntoDescriptors()
    {
        var input = new List<string> { "Id" };
        var info = new SparsePropertyInfo(
            "Items",
            typeof(List<MetaKeyedElement>),
            false,
            false,
            null,
            MergeMode.Replace,
            null,
            SparseCollectionKind.List,
            SparseCollectionSemantic.KeyedSequence,
            SparseKeyKind.Property,
            input,
            typeof(string),
            "Items",
            false,
            false,
            false
        );
        input.Add("evil");
        info.KeyPropertyNames.ShouldBe(["Id"]);
    }

    [Test]
    public void DescriptorsAreImmutableAndNoPerPropertyMembers()
    {
        var properties = MetaScalar.Sparse.Properties;
        foreach (var property in properties)
        {
            property.Name.ShouldNotBeNullOrWhiteSpace();
            property.PropertyType.ShouldNotBeNull();
            property.JsonPropertyName.ShouldNotBeNullOrWhiteSpace();
            property.KeyPropertyNames.ShouldNotBeNull();
        }

        // No typed per-property handles: Sparse exposes only Properties.
        typeof(MetaScalar.Sparse).GetProperty("Properties").ShouldNotBeNull();
        typeof(MetaScalar.Sparse).GetProperties().Length.ShouldBe(1);
    }

    [Test]
    public void NoReflectionDiscoveryNeeded()
    {
        // Consumers enumerate semantics without reflection discovery.
        var names = new List<string>();
        foreach (var property in MetaKeyedHolder.Sparse.Properties)
        {
            names.Add(property.Name);
        }

        names.ShouldContain("Items");
        var items = MetaKeyedHolder.Sparse.Properties.Single(p => p.Name == "Items");
        // Type identity via typeof, semantics via enums — no PropertyInfo.
        items.PropertyType.ShouldBeAssignableTo<Type>();
        (items.PropertyType == typeof(List<MetaKeyedElement>)).ShouldBeTrue();
    }

    [Test]
    public void JsonNamesAndFlagsAreExposed()
    {
        var byName = MetaScalar.Sparse.Properties.ToDictionary(p => p.Name);
        byName["Count"].JsonPropertyName.ShouldBe("Count");
        byName["Count"].IsRequired.ShouldBeFalse();
        byName["Count"].IsReadOnly.ShouldBeFalse();
    }

    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> Sources
    ) RunGenerator(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = tpa.Select(path =>
                (MetadataReference)MetadataReference.CreateFromFile(path)
            )
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "SparseMetadataProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (
            runResult.Diagnostics,
            runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray()
        );
    }

    [Test]
    public void SparseMemberCollisionReportsSpf009()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class MetaSparseCollision
            {
                public string? Sparse { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var matches = diagnostics.Where(d => d.Id == "SPF009").ToImmutableArray();
        matches.Length.ShouldBe(1);
        matches[0].GetMessage().ShouldContain("Sparse");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void SparseNestedTypeCollisionReportsSpf009()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class MetaSparseTypeCollision
            {
                public string? Name { get; set; }
                public sealed class Sparse
                {
                }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var matches = diagnostics.Where(d => d.Id == "SPF009").ToImmutableArray();
        matches.Length.ShouldBe(1);
        matches[0].GetMessage().ShouldContain("Sparse");
        sources.ShouldBeEmpty();
    }
}
