using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

/// <summary>
/// Direct shared-unit tests plus generator-level parity coverage for the
/// centralized validation helpers (issue #29).
/// </summary>
public sealed class ShapeValidationTests
{
    private static CSharpCompilation CreateCompilation(string source)
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
        return CSharpCompilation.Create(
            "SparseShapeProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> Sources
    ) RunGenerator(string source)
    {
        var compilation = CreateCompilation(source);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        return (
            runResult.Diagnostics,
            runResult.Results.SelectMany(static r => r.GeneratedSources).ToImmutableArray()
        );
    }

    private static SparseMemberModel ScalarMember(
        int id,
        string name,
        string? wireName = null,
        bool ignored = false
    )
    {
        var type = new SparseTypeModel(
            "global::System.String?",
            "global::System.String",
            "string",
            true,
            false,
            null
        );
        var property = new SparsePropertyModel(
            name,
            type,
            IsInitOnly: false,
            IsRequired: false,
            IsReadOnly: false,
            JsonPropertyName: wireName ?? name,
            HasExplicitJsonPropertyName: wireName is not null,
            JsonIgnoreCondition: ignored ? 1 : 0
        );
        return new SparseMemberModel(
            id,
            property,
            null,
            0,
            SparseCollectionInfo.Unsupported,
            null,
            null,
            false,
            true
        );
    }

    [Test]
    public void ReservedCollisions_FindSparseFragmentsNames()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "Name"),
            ScalarMember(1, "JsonConverter"),
            ScalarMember(2, "FragmentJsonConverter")
        );
        SparseShapeValidation
            .FindFirstReservedNameCollision(
                members,
                SparseShapeValidation.SparseFragmentsReservedNames
            )
            .ShouldBe("JsonConverter");
        SparseShapeValidation
            .FindReservedNameCollisions(
                members.Select(static member => member.Property.Name),
                SparseShapeValidation.SparseFragmentsReservedNames
            )
            .ShouldBe(["JsonConverter", "FragmentJsonConverter"]);
    }

    [Test]
    public void ReservedCollisions_ComposeCoreAndProductNames()
    {
        var reserved = SparseShapeValidation.CoreGeneratedNames.Add("CustomReserved");
        SparseShapeValidation
            .FindReservedNameCollisions(["Name", "Merge", "CustomReserved"], reserved)
            .ShouldBe(["Merge", "CustomReserved"]);
        // Without the core set, product-only collisions still resolve.
        SparseShapeValidation
            .FindReservedNameCollisions(["Name", "Merge"], ImmutableArray.Create("CustomReserved"))
            .ShouldBeEmpty();
    }

    [Test]
    public void DuplicateWireNames_DetectsExactDuplicates()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "First", "dup"),
            ScalarMember(1, "Second", "dup"),
            ScalarMember(2, "Plain"),
            ScalarMember(3, "Ignored", "dup", ignored: true)
        );
        // Ignored members never participate, so only one duplicate remains.
        SparseShapeValidation.FindDuplicateWireNames(members).ShouldBe(["dup"]);
    }

    [Test]
    public void DuplicateWireNames_AcceptsDistinctNames()
    {
        var members = ImmutableArray.Create(
            ScalarMember(0, "First", "a"),
            ScalarMember(1, "Second", "b"),
            ScalarMember(2, "Plain")
        );
        SparseShapeValidation.FindDuplicateWireNames(members).ShouldBeEmpty();
    }

    private static ITypeSymbol GetPropertyType(string source, string holder, string property)
    {
        var compilation = CreateCompilation(source);
        var type =
            compilation.GetTypeByMetadataName(holder)
            ?? throw new InvalidOperationException($"Type '{holder}' not found.");
        return ((IPropertySymbol)type.GetMembers(property).Single()).Type;
    }

    [Test]
    public void MemberReason_AcceptsRepresentableShapes()
    {
        const string source = """
            using System.Collections.Generic;
            public class ShapeHolder
            {
                public int Count { get; set; }
                public string? Name { get; set; }
                public List<string> Tags { get; set; } = new();
                public int[] Scores { get; set; } = [];
                public int? Maybe { get; set; }
            }
            """;
        foreach (var property in new[] { "Count", "Name", "Tags", "Scores", "Maybe" })
        {
            SparseShapeValidation
                .GetUnsupportedMemberReason(GetPropertyType(source, "ShapeHolder", property))
                .ShouldBeNull(property);
        }
    }

    [Test]
    public void MemberReason_RejectsUnrepresentableLeaves()
    {
        const string source = """
            using System;
            public class BadHolder
            {
                public dynamic Dyn { get; set; } = null!;
                public unsafe int* Pointer { get; set; }
                public Span<int> Span { get; set; }
            }
            """;
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Dyn"))
            .ShouldBe(SparseMemberShapeProblem.Dynamic);
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Pointer"))
            .ShouldBe(SparseMemberShapeProblem.Pointer);
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "BadHolder", "Span"))
            .ShouldBe(SparseMemberShapeProblem.RefLike);
    }

    [Test]
    public void MemberReason_RecursesThroughWrappers()
    {
        const string source = """
            using System.Collections.Generic;
            public class WrapHolder
            {
                public unsafe List<int*> Pointers { get; set; } = new();
                public unsafe int*[] PointerArray { get; set; } = [];
            }
            """;
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "WrapHolder", "Pointers"))
            .ShouldBe(SparseMemberShapeProblem.Pointer);
        SparseShapeValidation
            .GetUnsupportedMemberReason(GetPropertyType(source, "WrapHolder", "PointerArray"))
            .ShouldBe(SparseMemberShapeProblem.Pointer);
    }

    [Test]
    public void RootShape_ValidPartialPasses()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class ShapeRoot
            {
                public string? Name { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        var model = compilation.GetTypeByMetadataName("ShapeRoot")!;
        var declaration = (Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax)
            model.DeclaringSyntaxReferences.Single().GetSyntax();
        SparseShapeValidation
            .ValidateRootShape(model, declaration, CancellationToken.None)
            .ShouldBeNull();
    }

    [Test]
    public void RootShape_RejectsNonPartialNestedRefLikeAndFileLocal()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public class NonPartialRoot
            {
                public string? Name { get; set; }
            }
            public class Outer
            {
                [SparseFragmentModel]
                public partial class NestedRoot
                {
                    public string? Name { get; set; }
                }
            }
            [SparseFragmentModel]
            public ref partial struct RefRoot
            {
                public int Count { get; set; }
            }
            [SparseFragmentModel]
            public file partial class FileRoot
            {
                public string? Name { get; set; }
            }
            """;
        var compilation = CreateCompilation(source);
        SparseRootShapeProblem? Check(string identifier)
        {
            var tree = compilation.SyntaxTrees.Single();
            var semantic = compilation.GetSemanticModel(tree);
            var declaration = tree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
                .Single(node => node.Identifier.Text == identifier);
            var model = (INamedTypeSymbol)semantic.GetDeclaredSymbol(declaration)!;
            return SparseShapeValidation.ValidateRootShape(
                model,
                declaration,
                CancellationToken.None
            );
        }

        Check("NonPartialRoot").ShouldBe(SparseRootShapeProblem.MustBePartial);
        Check("NestedRoot").ShouldBe(SparseRootShapeProblem.UnsupportedModel);
        Check("RefRoot").ShouldBe(SparseRootShapeProblem.RefLikeModel);
        Check("FileRoot").ShouldBe(SparseRootShapeProblem.FileLocalModel);
    }

    [Test]
    public void Generator_RefLikeRootReportsSpf002()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public ref partial struct RefModel
            {
                public int Count { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF002").ToImmutableArray();
        match.Length.ShouldBe(1);
        match[0].GetMessage().ShouldContain("RefModel");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_PointerMemberReportsSingleSpf008()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class PointerModel
            {
                public unsafe int* Value { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        match.Length.ShouldBe(
            1,
            "expected exactly one SPF008, got: "
                + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage()))
        );
        match[0].GetMessage().ShouldContain("Value");
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_DynamicMemberReportsSingleSpf008()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class DynamicModel
            {
                public dynamic Value { get; set; } = null!;
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF008").ToImmutableArray();
        match.Length.ShouldBe(
            1,
            "expected exactly one SPF008, got: "
                + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage()))
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Generator_DuplicateWireNameReportsSpf021()
    {
        const string source = """
            using System.Text.Json.Serialization;
            using SparseFragments;
            [SparseFragmentModel]
            public partial class WireModel
            {
                [JsonPropertyName("dup")]
                public string? First { get; set; }
                [JsonPropertyName("dup")]
                public string? Second { get; set; }
            }
            """;
        var (diagnostics, sources) = RunGenerator(source);
        var match = diagnostics.Where(d => d.Id == "SPF021").ToImmutableArray();
        match.Length.ShouldBe(
            1,
            "expected exactly one SPF021, got: "
                + string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage()))
        );
        match[0].GetMessage().ShouldContain("dup");
        match[0].Location.IsInSource.ShouldBeTrue();
        sources.ShouldBeEmpty();
    }
}
