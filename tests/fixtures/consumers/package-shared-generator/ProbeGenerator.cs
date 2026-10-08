using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using SparseFragments.Generator.Shared;

namespace PackageShared.Generator;

/// <summary>
/// Package-only downstream generator probe (#70).
/// Exercises representative Shared categories so a missing Shared source file
/// fails compilation: model/collection analysis, IR/model types, naming
/// infrastructure, emitter/helpers, and the downstream policy/config surface
/// (#121: feature selection, member transport and rebase policies, write
/// contracts). All Shared sources arrive via the
/// SparseFragments.Generator.Shared NuGet package (contentFiles +
/// build/SparseFragments.Generator.Shared.props); there is no sibling-source
/// fallback in PackageShared.Generator.csproj.
/// </summary>
[Generator]
public sealed class ProbeGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
        {
            var source = ProbeSurface.BuildProbeSource(CancellationToken.None);
            ctx.AddSource("PackageShared.Probe.g.cs", source);
        });
    }
}

internal static class ProbeSurface
{
    internal static string BuildProbeSource(CancellationToken cancellationToken)
    {
        // Naming infrastructure (Naming/).
        var escaped = SparseNaming.EscapeIdentifier("class");
        var apiPrefix = SparseNaming.PatchApiPrefix(ImmutableArray<string>.Empty);
        var mergeFieldPrefix = SparseWellKnownNames.MergeStrategyFieldPrefix;
        var jsonIgnoreNever = SparseJsonNaming.JsonIgnoreNever;

        // Shared IR/model types (Models/).
        var elementType = new SparseTypeModel(
            "global::System.String",
            "global::System.String",
            "global::System.String",
            IsReferenceType: true,
            IsFragmentModel: false,
            PocoCloneHelperName: null
        );
        var collection = new SparseCollectionInfo(
            SparseCollectionKind.List,
            SparseCloneCollectionKind.List,
            elementType,
            null,
            null,
            SparseCollectionSemantic.ScalarSequence,
            ImmutableArray<string>.Empty,
            null,
            SparseKeyKind.None
        );
        var property = new SparsePropertyModel("Name", elementType);
        var member = new SparseMemberModel(
            1,
            property,
            null,
            SparseMergeModes.Replace,
            collection,
            null,
            null,
            ChildIsStructural: false,
            ChildIsReferenceType: true
        );
        var key = new SparseKeyInfo(
            SparseKeyKind.None,
            ImmutableArray<string>.Empty,
            "global::System.String"
        );

        // Shared model/collection analysis (Analysis/). Referencing the analyzer
        // types proves their sources were compiled from the package.
        var analysisTypes = new[]
        {
            typeof(SparseCollectionAnalyzer),
            typeof(SparseKeyAnalyzer),
            typeof(SparseModelAnalyzer),
        };

        // Downstream product contract (issue #121): feature selection, member
        // transport and rebase policies, and write contracts. Touching these
        // types keeps the probe covering the policy/config surface.
        var features = SparseEmissionFeatures.Standalone;
        var transport = SparseMemberTransport.RedactedBefore;
        var memberPolicy = new SparseMemberPolicy("Secret", transport);
        var rebase = new SparseRebasePolicy(SparseRedactedBeforeBehavior.Passthrough);
        var write = new SparseWriteContract(
            "global::PackageShared.WriteCmd",
            ImmutableArray.Create(new SparseWriteMember("Secret", "NewSecret"))
        );
        var dependencyErrors = features.ValidateDependencies();
        var emittedNames = features.GetEmittedTypeNames();

        // Emitter/helpers (Emitters/, Infrastructure/).
        var code = new SharedIndentedBuilder(cancellationToken);
        SparseFragmentEmitHelpers.AppendNullGuard(code, 1, "value");
        var expressions = new SparseFragmentExpressions(
            "__cloneContext",
            "global::PackageShared.Runtime",
            "global::PackageShared.Runtime"
        );
        var clone = expressions.CloneValueExpression(elementType, "value");
        var observable = SparseObservableEmitter.ObservableTypeName(
            ImmutableArray<SparseMemberModel>.Empty
        );
        var incrementalTypes = new[]
        {
            typeof(SparseLocationSnapshot),
            typeof(SparseExternalInit),
            typeof(RoslynSymbolCompat),
        };

        code.AppendLineAt(
            0,
            "// PackageShared probe: "
                + escaped
                + " prefix="
                + apiPrefix
                + " merge="
                + mergeFieldPrefix
                + " json="
                + jsonIgnoreNever
        );
        code.AppendLineAt(
            0,
            "// member=" + member.Id + " key=" + key.KeyTypeName + " clone=" + clone
        );
        code.AppendLineAt(
            0,
            "// analysis=" + analysisTypes.Length + " incremental=" + incrementalTypes.Length
        );
        code.AppendLineAt(0, "// observable=" + observable + " collection=" + collection.Kind);
        code.AppendLineAt(
            0,
            "// policy="
                + memberPolicy.MemberName
                + ":"
                + memberPolicy.Transport
                + " rebase="
                + rebase.RedactedBefore
                + " write="
                + write.WriteModelType
                + " deps="
                + dependencyErrors.Length
                + " emitted="
                + emittedNames.Length
        );
        return code.ToString();
    }
}
