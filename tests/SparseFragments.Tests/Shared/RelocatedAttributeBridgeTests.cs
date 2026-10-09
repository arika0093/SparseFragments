using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;
using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Shared;

// Regression tests for the attribute-bridge gating mismatch (reloc-1 review,
// #190/#191): the surface used to emit __SparseAttributes_* bridges only with
// a descriptor dialect present, while the relocation rewrite referenced them
// whenever an implementation namespace was set. Both sides now use the same
// gate (emission whenever relocation can reference a bridge).
public sealed class RelocatedAttributeBridgeTests
{
    private const string AttributeExpression =
        "new global::System.ComponentModel.DescriptionAttribute(\"bridge-probe\")";

    private static SparseTypeModel TextType() =>
        new(
            "global::System.String?",
            "global::System.String",
            "global::System.String",
            IsReferenceType: true,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );

    private static SparseTypeModel IntType() =>
        new(
            "global::System.Int32",
            "global::System.Int32",
            "global::System.Int32",
            IsReferenceType: false,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );

    private static ImmutableArray<SparseMemberModel> AttributedMembers() =>
        ImmutableArray.Create(
            new SparseMemberModel(
                0,
                new SparsePropertyModel(
                    "Label",
                    TextType(),
                    JsonPropertyName: "Label",
                    HasExplicitJsonPropertyName: false,
                    JsonIgnoreCondition: 0,
                    IsNullable: true,
                    AttributeExpressions: AttributeExpression
                ),
                null,
                SparseMergeModes.Replace,
                SparseCollectionInfo.Unsupported,
                null,
                null,
                ChildIsStructural: false,
                ChildIsReferenceType: true
            ),
            new SparseMemberModel(
                1,
                new SparsePropertyModel(
                    "RetryCount",
                    IntType(),
                    JsonPropertyName: "RetryCount",
                    HasExplicitJsonPropertyName: false,
                    JsonIgnoreCondition: 0,
                    IsNullable: false
                ),
                null,
                SparseMergeModes.Replace,
                SparseCollectionInfo.Unsupported,
                null,
                null,
                ChildIsStructural: false,
                ChildIsReferenceType: true
            )
        );

    private static SparseModelInfo BridgeModel() =>
        new(
            "ReadModel",
            "global::Ns.ReadModel",
            "Ns",
            IsGlobalNamespace: false,
            IsStruct: false,
            IsRecord: false,
            HintName: "ReadModel.SparseFragments.g.cs",
            Constructor: null,
            IgnoredSettablePropertyNames: ImmutableArray<string>.Empty,
            IsPublic: true
        );

    private static SparseGeneratorConfig ProductConfigWithoutDescriptors()
    {
        var configuration = (SparseGeneratorConfig)(
            typeof(SparseFragmentsGenerator)
                .GetField("Configuration", BindingFlags.NonPublic | BindingFlags.Static)
                ?.GetValue(null)
            ?? throw new InvalidOperationException("Missing generator configuration.")
        );
        return configuration with { DescriptorDialect = null };
    }

    private static string BuildSurface(
        SparseGeneratorConfig config,
        ImmutableArray<SparseMemberModel> members
    ) =>
        SparseFragmentEmitter.BuildSource(
            BridgeModel(),
            members,
            ImmutableArray<SparsePocoCloneModel>.Empty,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            ImmutableArray<SparseStructuralModel>.Empty,
            bclHashSetImplementsReadOnlySet: false,
            bclHashSetSupportsCapacity: false,
            CancellationToken.None,
            config
        );

    [Test]
    public void RewriteReferencesTheSameBridgeTheSurfaceEmits()
    {
        var config = ProductConfigWithoutDescriptors();
        var members = AttributedMembers();
        var legacy =
            "var attributes = new global::System.Attribute[] { " + AttributeExpression + " };";
        var relocated = SparseModelImplementationEmitter.RelocateUiReferences(
            legacy,
            "ReadModel",
            members,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            config,
            CancellationToken.None
        );
        relocated.ShouldContain("ReadModel.__SparseAttributes_0()");

        var surface = BuildSurface(config, members);
        surface.ShouldContain("internal static global::System.Attribute[] __SparseAttributes_0()");
    }

    [Test]
    public void NoDescriptorDialect_RelocatedOutputsCompileWithoutMissingBridges()
    {
        var config = ProductConfigWithoutDescriptors();
        var members = AttributedMembers();
        var surface = BuildSurface(config, members);
        var implementations = SparseFragmentEmitter.BuildImplementationSources(
            BridgeModel(),
            members,
            ImmutableArray<SparseReadOnlyViewModel>.Empty,
            CancellationToken.None,
            config
        );
        implementations.ShouldNotBeEmpty();

        // Every bridge call in the relocated outputs must resolve to a bridge
        // the surface defines; otherwise the compilation below fails.
        var defined = new HashSet<string>(
            Regex
                .Matches(
                    surface,
                    @"__SparseAttributes_(\d+)\(\) =>",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                )
                .Select(static match => match.Groups[1].Value)
        );
        var referenced = implementations
            .SelectMany(static source =>
                Regex
                    .Matches(
                        source.Source,
                        @"__SparseAttributes_(\d+)\(\)",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(1)
                    )
                    .Select(static match => match.Groups[1].Value)
            )
            .Distinct()
            .ToArray();
        referenced.ShouldBeSubsetOf(defined);

        var cores = SparseEditSessionEmitter.RenderCoreSources(
            config.EditSessionDialect!,
            config.RuntimeDialect!,
            config.PatchDialect!.Value
        );
        const string stub = """
            namespace Ns
            {
                public partial class ReadModel
                {
                    [System.ComponentModel.Description("bridge-probe")]
                    public string? Label { get; set; }
                    public int RetryCount { get; set; }
                }
            }
            """;
        var trees = implementations
            .Select(static source => CSharpSyntaxTree.ParseText(source.Source))
            .Prepend(CSharpSyntaxTree.ParseText(surface, path: "Surface.cs"))
            .Append(CSharpSyntaxTree.ParseText(cores.Core, path: "EditSessionCore.cs"))
            .Append(
                CSharpSyntaxTree.ParseText(cores.CurrentCore, path: "EditSessionWithCurrentCore.cs")
            )
            .Append(CSharpSyntaxTree.ParseText(stub, path: "Models.cs"))
            .ToArray();
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragments.Optional<>).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "NoDescriptorBridgeProbe",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        compilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }
}
