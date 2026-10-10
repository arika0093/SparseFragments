using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Diagnostics;

public sealed class SparseGeneratorDiagnosticTests
{
    private static (
        ImmutableArray<Diagnostic> Diagnostics,
        ImmutableArray<GeneratedSourceResult> Sources
    ) Run(string source)
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
            "SparseDiagnosticsProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var diagnostics = runResult.Diagnostics;
        var sources = runResult
            .Results.SelectMany(static r => r.GeneratedSources)
            .ToImmutableArray();
        return (diagnostics, sources);
    }

    private static Diagnostic AssertSingleSpf(
        ImmutableArray<Diagnostic> diagnostics,
        string expectedId,
        string expectedArg,
        string expectedHelpAnchor,
        bool expectInSource
    )
    {
        var matches = diagnostics.Where(d => d.Id == expectedId).ToImmutableArray();
        matches.Length.ShouldBe(
            1,
            $"expected exactly one {expectedId}, got: {string.Join(", ", diagnostics.Select(d => d.Id + ":" + d.GetMessage()))}"
        );
        var diagnostic = matches[0];
        diagnostic.Severity.ShouldBe(DiagnosticSeverity.Error);
        diagnostic.GetMessage().ShouldContain(expectedArg);
        diagnostic.Descriptor.HelpLinkUri.ShouldBe(
            "https://github.com/arika0093/SparseFragments/blob/main/docs/analyzer.md"
                + expectedHelpAnchor
        );
        if (expectInSource)
        {
            diagnostic.Location.IsInSource.ShouldBeTrue(
                $"expected {expectedId} to have a source location"
            );
            var tree = diagnostic.Location.SourceTree!;
            var text = tree.GetText().ToString(diagnostic.Location.SourceSpan);
            text.Contains(expectedArg)
                .ShouldBeTrue($"location span '{text}' should contain '{expectedArg}'");
        }
        else
        {
            diagnostic.Location.IsInSource.ShouldBeFalse(
                $"expected {expectedId} to have Location.None"
            );
        }
        // No other SPF diagnostics.
        diagnostics
            .Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal) && d.Id != expectedId)
            .ShouldBeEmpty();
        return diagnostic;
    }

    [Test]
    public void Spf001_NonPartialModelReportsErrorWithNoSource()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public class NonPartialModel
            {
                public string? Label { get; set; }
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF001",
            "NonPartialModel",
            "#spf001-sparse-fragment-model-must-be-partial",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("nested")]
    [Arguments("generic")]
    [Arguments("abstract")]
    public void Spf002_UnsupportedShapeReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "nested" => """
                using SparseFragments;
                public partial class Outer
                {
                    [SparseFragmentModel]
                    public partial class NestedModel
                    {
                        public string? Label { get; set; }
                    }
                }
                """,
            "generic" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class GenericModel<T>
                {
                    public T? Value { get; set; }
                }
                """,
            "abstract" => """
                using SparseFragments;
                [SparseFragmentModel]
                public abstract partial class AbstractModel
                {
                    public string? Label { get; set; }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var expectedArg = kind switch
        {
            "nested" => "NestedModel",
            "generic" => "GenericModel",
            "abstract" => "AbstractModel",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF002",
            expectedArg,
            "#spf002-unsupported-sparse-fragment-model",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("missing")]
    [Arguments("privateSetter")]
    public void Spf003_MissingConstructorReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "missing" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class NoUsableCtor
                {
                    public NoUsableCtor(string missing) { }
                    public string Label { get; set; } = "";
                }
                """,
            "privateSetter" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class PrivateSetterCtor
                {
                    public PrivateSetterCtor(string label) { Label = label; }
                    public string Label { get; private set; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var expectedArg = kind switch
        {
            "missing" => "NoUsableCtor",
            "privateSetter" => "PrivateSetterCtor",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF003",
            expectedArg,
            "#spf003-model-needs-a-supported-constructor",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("wrongType")]
    [Arguments("abstractStrategy")]
    [Arguments("noParameterless")]
    [Arguments("nestedTarget")]
    public void Spf004_InvalidMergeStrategyReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "wrongType" => """
                using SparseFragments;
                using System.Collections.Generic;
                public sealed class WrongStrategy : FragmentMergeStrategy<List<int>>
                {
                    public override Optional<List<int>> Merge(Optional<List<int>> lower, Optional<List<int>> higher) => higher.IsPresent ? higher : lower;
                    public override bool AreEqual(List<int>? left, List<int>? right) => left == right;
                }
                [SparseFragmentModel]
                public partial class BadStrategyModel
                {
                    [SparseMerge(typeof(WrongStrategy))]
                    public string Name { get; set; } = "";
                }
                """,
            "abstractStrategy" => """
                using SparseFragments;
                public abstract class AbstractStrategy : FragmentMergeStrategy<string>
                {
                    public override Optional<string> Merge(Optional<string> lower, Optional<string> higher) => higher.IsPresent ? higher : lower;
                    public override bool AreEqual(string? left, string? right) => left == right;
                }
                [SparseFragmentModel]
                public partial class AbstractStrategyModel
                {
                    [SparseMerge(typeof(AbstractStrategy))]
                    public string Name { get; set; } = "";
                }
                """,
            "noParameterless" => """
                using SparseFragments;
                public sealed class NoCtorStrategy : FragmentMergeStrategy<string>
                {
                    public NoCtorStrategy(int seed) { }
                    public override Optional<string> Merge(Optional<string> lower, Optional<string> higher) => higher.IsPresent ? higher : lower;
                    public override bool AreEqual(string? left, string? right) => left == right;
                }
                [SparseFragmentModel]
                public partial class NoCtorStrategyModel
                {
                    [SparseMerge(typeof(NoCtorStrategy))]
                    public string Name { get; set; } = "";
                }
                """,
            "nestedTarget" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class Spf004Child
                {
                    public string? Value { get; set; }
                }
                public sealed class ChildStrategy : FragmentMergeStrategy<Spf004Child>
                {
                    public override Optional<Spf004Child> Merge(Optional<Spf004Child> lower, Optional<Spf004Child> higher) => higher.IsPresent ? higher : lower;
                    public override bool AreEqual(Spf004Child? left, Spf004Child? right) => left == right;
                }
                [SparseFragmentModel]
                public partial class NestedStrategyModel
                {
                    [SparseMerge(typeof(ChildStrategy))]
                    public Spf004Child Child { get; set; } = new();
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        var expectedArg = kind == "nestedTarget" ? "Child" : "Name";
        AssertSingleSpf(
            diagnostics,
            "SPF004",
            expectedArg,
            "#spf004-invalid-custom-merge-strategy",
            expectInSource: true
        );
        if (kind == "nestedTarget")
        {
            // The valid sibling root still generates; only the offending model is skipped.
            sources
                .Any(s => s.HintName.Contains("Spf004Child", StringComparison.Ordinal))
                .ShouldBeTrue();
            sources
                .Any(s => s.HintName.Contains("NestedStrategyModel", StringComparison.Ordinal))
                .ShouldBeFalse();
        }
        else
        {
            sources.ShouldBeEmpty();
        }
    }

    [Test]
    [Arguments("wrongType")]
    [Arguments("nestedTarget")]
    [Arguments("keyedTarget")]
    [Arguments("appendTarget")]
    public void Spf027_InvalidRebasePolicyReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "wrongType" => """
                using SparseFragments;
                public sealed class WrongPolicy : FragmentMergeStrategy<string>
                {
                    public override Optional<string> Merge(Optional<string> lower, Optional<string> higher) => higher.IsPresent ? higher : lower;
                    public override bool AreEqual(string? left, string? right) => left == right;
                }
                [SparseFragmentModel]
                public partial class BadPolicyModel
                {
                    [SparseRebasePolicy(typeof(WrongPolicy))]
                    public string Name { get; set; } = "";
                }
                """,
            "nestedTarget" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class Spf027Child
                {
                    public string? Value { get; set; }
                }
                public sealed class Spf027ChildPolicy : FragmentRebasePolicy<Spf027Child>
                {
                    public override bool AreEqual(Spf027Child? left, Spf027Child? right) => left == right;
                    public override bool TryRebase(Optional<Spf027Child> editBase, Optional<Spf027Child> desired, Optional<Spf027Child> current, out Optional<Spf027Child> rebased, out string? reason)
                    {
                        rebased = current;
                        reason = null;
                        return true;
                    }
                }
                [SparseFragmentModel]
                public partial class NestedPolicyModel
                {
                    [SparseRebasePolicy(typeof(Spf027ChildPolicy))]
                    public Spf027Child Child { get; set; } = new();
                }
                """,
            "keyedTarget" => """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class Spf027Item
                {
                    [SparseKey]
                    public string Id { get; set; } = "";
                    public string Name { get; set; } = "";
                }
                public sealed class Spf027ListPolicy : FragmentRebasePolicy<List<Spf027Item>>
                {
                    public override bool AreEqual(List<Spf027Item>? left, List<Spf027Item>? right) => left == right;
                    public override bool TryRebase(Optional<List<Spf027Item>> editBase, Optional<List<Spf027Item>> desired, Optional<List<Spf027Item>> current, out Optional<List<Spf027Item>> rebased, out string? reason)
                    {
                        rebased = current;
                        reason = null;
                        return true;
                    }
                }
                [SparseFragmentModel]
                public partial class KeyedPolicyModel
                {
                    [SparseRebasePolicy(typeof(Spf027ListPolicy))]
                    public List<Spf027Item> Items { get; set; } = new();
                }
                """,
            "appendTarget" => """
                using SparseFragments;
                using System.Collections.Generic;
                public sealed class Spf027AppendPolicy : FragmentRebasePolicy<List<string>>
                {
                    public override bool AreEqual(List<string>? left, List<string>? right) => left == right;
                    public override bool TryRebase(Optional<List<string>> editBase, Optional<List<string>> desired, Optional<List<string>> current, out Optional<List<string>> rebased, out string? reason)
                    {
                        rebased = current;
                        reason = null;
                        return true;
                    }
                }
                [SparseFragmentModel]
                public partial class AppendPolicyModel
                {
                    [SparseMerge(MergeMode.Append)]
                    [SparseRebasePolicy(typeof(Spf027AppendPolicy))]
                    public List<string> Tags { get; set; } = new();
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        var expectedArg = kind switch
        {
            "nestedTarget" => "Child",
            "keyedTarget" => "Items",
            "appendTarget" => "Tags",
            _ => "Name",
        };
        AssertSingleSpf(
            diagnostics,
            "SPF027",
            expectedArg,
            "#spf027-invalid-custom-rebase-policy",
            expectInSource: true
        );
        if (kind == "nestedTarget")
        {
            sources
                .Any(s => s.HintName.Contains("Spf027Child", StringComparison.Ordinal))
                .ShouldBeTrue();
            sources
                .Any(s => s.HintName.Contains("NestedPolicyModel", StringComparison.Ordinal))
                .ShouldBeFalse();
        }
        else if (kind == "keyedTarget")
        {
            sources
                .Any(s => s.HintName.Contains("Spf027Item", StringComparison.Ordinal))
                .ShouldBeTrue();
            sources
                .Any(s => s.HintName.Contains("KeyedPolicyModel", StringComparison.Ordinal))
                .ShouldBeFalse();
        }
        else
        {
            sources.ShouldBeEmpty();
        }
    }

    [Test]
    [Arguments("deepOnScalar")]
    [Arguments("appendOnSet")]
    [Arguments("appendOnScalar")]
    [Arguments("setUnionOnScalar")]
    [Arguments("outOfRange")]
    public void Spf005_UnsupportedMergeModeReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "deepOnScalar" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class DeepScalarModel
                {
                    [SparseMerge(MergeMode.Deep)]
                    public string Name { get; set; } = "";
                }
                """,
            "appendOnSet" => """
                using SparseFragments;
                using System.Collections.Generic;
                [SparseFragmentModel]
                public partial class AppendSetModel
                {
                    [SparseMerge(MergeMode.Append)]
                    public ISet<string> Tags { get; set; } = new HashSet<string>();
                }
                """,
            "appendOnScalar" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class AppendScalarModel
                {
                    [SparseMerge(MergeMode.Append)]
                    public string Name { get; set; } = "";
                }
                """,
            "setUnionOnScalar" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class SetUnionScalarModel
                {
                    [SparseMerge(MergeMode.SetUnion)]
                    public string Name { get; set; } = "";
                }
                """,
            "outOfRange" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class OutOfRangeModel
                {
                    [SparseMerge((MergeMode)99)]
                    public string Name { get; set; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var expectedArg = kind == "appendOnSet" ? "Tags" : "Name";
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF005",
            expectedArg,
            "#spf005-unsupported-merge-mode",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("privateSetter")]
    [Arguments("getterOnly")]
    public void Spf006_RequiredMemberReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "privateSetter" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class RequiredPrivateSetterModel
                {
                    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                    public RequiredPrivateSetterModel() { }
                    public required string Label { get; private set; } = "";
                }
                """,
            "getterOnly" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class RequiredGetterOnlyModel
                {
                    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
                    public RequiredGetterOnlyModel() { }
                    public required string Label { get; } = "";
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF006",
            "Label",
            "#spf006-required-member-cannot-be-constructed",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf007_UnsupportedStructuralMemberReportsErrorWithNoSource()
    {
        const string source = """
            using SparseFragments;
            public sealed class PlainPoco
            {
                public string? Name { get; set; }
            }
            [SparseFragmentModel]
            public partial class StructuralHolder
            {
                public PlainPoco Child { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF007",
            "Child",
            "#spf007-unsupported-structural-member-construction",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf007_ReplaceOptOutGeneratesSourceWithoutDiagnostics()
    {
        const string source = """
            using SparseFragments;
            public sealed class ReplacePoco
            {
                public string? Name { get; set; }
            }
            [SparseFragmentModel]
            public partial class ReplaceHolder
            {
                [SparseMerge(MergeMode.Replace)]
                public ReplacePoco Child { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources.Length.ShouldBeGreaterThan(0);
        sources
            .Any(s => s.HintName.EndsWith(".SparseFragments.g.cs", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    [Arguments("unsupportedType")]
    [Arguments("ctorBoundCycle")]
    public void Spf008_UnsupportedCloneMemberReportsErrorWithNoSource(string kind)
    {
        var source = kind switch
        {
            "unsupportedType" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class ObjectCloneModel
                {
                    public object Value { get; set; } = new object();
                }
                """,
            "ctorBoundCycle" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class CycleRoot
                {
                    public CycleRoot(CycleRoot next) { Next = next; }
                    public CycleRoot Next { get; }
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var expectedArg = kind == "unsupportedType" ? "Value" : "Next";
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF008",
            expectedArg,
            "#spf008-unsupported-deep-clone-member",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("JsonConverter")]
    [Arguments("FragmentJsonConverter")]
    [Arguments("ChangeSet")]
    [Arguments("ChangePayload")]
    public void Spf009_NameCollisionReportsErrorWithNoSource(string memberName)
    {
        var source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class CollisionModel
            {
                public string? NAME { get; set; }
            }
            """.Replace("NAME", memberName);
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF009",
            memberName,
            "#spf009-member-conflicts-with-generated-api",
            expectInSource: false
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("Fragment")]
    [Arguments("ChangeSet")]
    public void Spf009_NestedTypeCollisionReportsErrorWithNoSource(string nestedTypeName)
    {
        var source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class CollisionModel
            {
                public sealed class NAME { }

                public string? Name { get; set; }
            }
            """.Replace("NAME", nestedTypeName);
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF009",
            nestedTypeName,
            "#spf009-member-conflicts-with-generated-api",
            expectInSource: false
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf009_HashedInternalContainerCollisionReportsErrorWithNoSource()
    {
        const string modelTypeName = "global::CollisionModel";
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var character in modelTypeName)
            hash = unchecked((hash ^ character) * prime);
        var nestedTypeName = "__Internal_" + hash.ToString("X8");
        var source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class CollisionModel
            {
                public sealed class NAME { }

                public string? Name { get; set; }
            }
            """.Replace("NAME", nestedTypeName);

        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF009",
            nestedTypeName,
            "#spf009-member-conflicts-with-generated-api",
            expectInSource: false
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf010_DifferentReferenceMergeStillSharesPromotedModel()
    {
        // The promoted model's semantics are derived from the shared type
        // itself, not from each root's reference-level merge mode, so roots
        // referencing the same partial type with different merge modes share
        // one promoted fragment without SPF010.
        const string source = """
            using SparseFragments;
            public partial class Spf010Shared
            {
                public string A { get; set; } = "";
                public string B { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class Spf010RootOne
            {
                public Spf010Shared Child { get; set; } = new();
            }
            [SparseFragmentModel]
            public partial class Spf010RootTwo
            {
                [SparseMerge(MergeMode.Replace)]
                public Spf010Shared Child { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("Spf010RootOne", StringComparison.Ordinal))
            .ShouldBeTrue();
        sources
            .Any(s => s.HintName.Contains("Spf010RootTwo", StringComparison.Ordinal))
            .ShouldBeTrue();
        sources
            .Any(s => s.HintName.Contains("Spf010Shared", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void Spf010_PromotedDescriptorMatchesDocs()
    {
        // SPF010's incompatible-content path is defensive: promotion is a pure
        // function of the shared type, so the same fully-qualified name always
        // yields identical semantics. Cover the diagnostic definition itself
        // (ID, severity, message, help link) so docs/analyzer.md drift fails loudly.
        var descriptor = GetDescriptorById("SPF010");
        descriptor.DefaultSeverity.ShouldBe(DiagnosticSeverity.Error);
        descriptor.MessageFormat.ToString().ShouldContain("{0}");
        descriptor.MessageFormat.ToString().ShouldContain("incompatible");
        descriptor.HelpLinkUri.ShouldBe(
            "https://github.com/arika0093/SparseFragments/blob/main/docs/analyzer.md#spf010-incompatible-promoted-fragment-model"
        );
    }

    [Test]
    public void AllSpfDescriptors_HaveSynchronizedHelpLinks()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SPF001"] = "#spf001-sparse-fragment-model-must-be-partial",
            ["SPF002"] = "#spf002-unsupported-sparse-fragment-model",
            ["SPF003"] = "#spf003-model-needs-a-supported-constructor",
            ["SPF004"] = "#spf004-invalid-custom-merge-strategy",
            ["SPF005"] = "#spf005-unsupported-merge-mode",
            ["SPF006"] = "#spf006-required-member-cannot-be-constructed",
            ["SPF007"] = "#spf007-unsupported-structural-member-construction",
            ["SPF008"] = "#spf008-unsupported-deep-clone-member",
            ["SPF009"] = "#spf009-member-conflicts-with-generated-api",
            ["SPF010"] = "#spf010-incompatible-promoted-fragment-model",
            ["SPF011"] = "#spf011-structural-sequence-without-usable-key",
            ["SPF013"] = "#spf013-multiple-sparsekey-properties",
            ["SPF014"] = "#spf014-invalid-sparsekey-declaration",
            ["SPF017"] = "#spf017-inaccessible-sparsekey-property",
            ["SPF019"] = "#spf019-unsupported-sparsekey-shape",
            ["SPF021"] = "#spf021-duplicate-json-property-name",
            ["SPF022"] = "#spf022-sparseignore-on-key",
            ["SPF023"] = "#spf023-sparseignore-on-unsupported-property",
            ["SPF024"] = "#spf024-invalid-unassigned-key-sentinel",
            ["SPF026"] = "#spf026-in-place-submit-is-unavailable",
            ["SPF027"] = "#spf027-invalid-custom-rebase-policy",
            ["SPF028"] = "#spf028-invalid-downstream-emission-plan",
            ["SPF029"] = "#spf029-unknown-product-member",
            ["SPF030"] = "#spf030-invalid-comparison-strategy",
        };
        var descriptors = typeof(SparseFragmentsGenerator)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)
            .ToDictionary(static descriptor => descriptor.Id, StringComparer.Ordinal);
        descriptors.Keys.OrderBy(static id => id).ShouldBe(expected.Keys.OrderBy(static id => id));
        foreach (var (id, anchor) in expected)
        {
            var descriptor = descriptors[id];
            descriptor.DefaultSeverity.ShouldBe(
                id == "SPF026" ? DiagnosticSeverity.Info : DiagnosticSeverity.Error
            );
            descriptor.Category.ShouldBe("SparseFragments");
            descriptor.HelpLinkUri.ShouldBe(
                "https://github.com/arika0093/SparseFragments/blob/main/docs/analyzer.md" + anchor
            );
            descriptor.MessageFormat.ToString().ShouldContain("{0}");
        }
    }

    [Test]
    public void Spf026_InitOnlyModelWarnsAndGeneratesApplyInPlace()
    {
        const string source = """
            using SparseFragments;
            [SparseFragmentModel]
            public partial class ReadOnlyEditModel
            {
                public string Name { get; init; } = "";
            }
            """;

        var (diagnostics, sources) = Run(source);

        var info = diagnostics.Single(static diagnostic => diagnostic.Id == "SPF026");
        info.Severity.ShouldBe(DiagnosticSeverity.Info);
        info.GetMessage().ShouldContain("ReadOnlyEditModel");
        info.GetMessage().ShouldContain("patch and try-apply APIs");
        info.GetMessage().ShouldContain("structured failures");
        info.Location.IsInSource.ShouldBeTrue();
        sources.ShouldNotBeEmpty();
        sources
            .Any(static generated =>
                generated
                    .SourceText.ToString()
                    .Contains("public bool TryApplyInPlace", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
        sources
            .Any(static generated =>
                generated
                    .SourceText.ToString()
                    .Contains("InPlaceWriteUnavailable", StringComparison.Ordinal)
            )
            .ShouldBeTrue();
    }

    private static DiagnosticDescriptor GetDescriptorById(string id) =>
        typeof(SparseFragmentsGenerator)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Static)
            .Where(static field => field.FieldType == typeof(DiagnosticDescriptor))
            .Select(static field => (DiagnosticDescriptor)field.GetValue(null)!)
            .Single(descriptor => descriptor.Id == id);

    [Test]
    public void Spf010_SamePromotedSemanticsGenerateWithoutDiagnostics()
    {
        const string source = """
            using SparseFragments;
            public partial class Spf010SameShared
            {
                public string A { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class Spf010SameRootOne
            {
                public Spf010SameShared Child { get; set; } = new();
            }
            [SparseFragmentModel]
            public partial class Spf010SameRootTwo
            {
                public Spf010SameShared Child { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("Spf010SameRootOne", StringComparison.Ordinal))
            .ShouldBeTrue();
        sources
            .Any(s => s.HintName.Contains("Spf010SameRootTwo", StringComparison.Ordinal))
            .ShouldBeTrue();
        sources
            .Any(s => s.HintName.Contains("Spf010SameShared", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void Spf011_UnkeyedStructuralSequenceReportsErrorWithNoSource()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class UnkeyedItem
            {
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class UnkeyedHolder
            {
                public List<UnkeyedItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF011",
            "Items",
            "#spf011-structural-sequence-without-usable-key",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf011_KeyedSequenceGeneratesSourceWithoutDiagnostics()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class KeyedItem
            {
                [SparseKey]
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class KeyedHolder
            {
                public List<KeyedItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("KeyedHolder", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void Spf011_AppendEscapeHatchGeneratesSourceWithoutDiagnostics()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class UnkeyedEscapeItem
            {
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class UnkeyedEscapeHolder
            {
                [SparseMerge(MergeMode.Append)]
                public List<UnkeyedEscapeItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("UnkeyedEscapeHolder", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void Spf011_ExplicitReplaceEscapeHatchGeneratesUnkeyedSequence()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class ExplicitReplaceItem
            {
                public string Name { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class ExplicitReplaceHolder
            {
                [SparseMerge(MergeMode.Replace)]
                public List<ExplicitReplaceItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("ExplicitReplaceHolder", StringComparison.Ordinal))
            .ShouldBeTrue();
    }

    [Test]
    public void Spf022_SparseIgnoreOnPropertyKeyIsRejected()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class IgnoredKeyItem
            {
                [SparseKey, SparseIgnore]
                public string Id { get; set; } = "";
            }
            [SparseFragmentModel]
            public partial class IgnoredKeyHolder
            {
                public List<IgnoredKeyItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF022",
            "Id",
            "#spf022-sparseignore-on-key",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void Spf022_SparseIgnoreOnTupleKeyPropertyIsRejected()
    {
        const string source = """
            using SparseFragments;
            using System.Collections.Generic;
            public partial class IgnoredTupleItem
            {
                public string Tenant { get; set; } = "";
                public string Id { get; set; } = "";
                [SparseKey]
                [SparseIgnore]
                public (string Tenant, string Id) Key => (Tenant, Id);
            }
            [SparseFragmentModel]
            public partial class IgnoredTupleHolder
            {
                public List<IgnoredTupleItem> Items { get; set; } = new();
            }
            """;
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF022",
            "Key",
            "#spf022-sparseignore-on-key",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    [Arguments("required")]
    [Arguments("initOnly")]
    [Arguments("constructor")]
    public void Spf023_SparseIgnoreOnUnconstructiblePropertyIsRejected(string kind)
    {
        var source = kind switch
        {
            "required" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class IgnoredRequiredModel
                {
                    [SparseIgnore]
                    public required string Hidden { get; set; }
                }
                """,
            "initOnly" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class IgnoredInitModel
                {
                    [SparseIgnore]
                    public string Hidden { get; init; } = "";
                }
                """,
            "constructor" => """
                using SparseFragments;
                [SparseFragmentModel]
                public partial class IgnoredConstructorModel
                {
                    [SparseIgnore]
                    public string Hidden { get; }
                    public IgnoredConstructorModel(string hidden) => Hidden = hidden;
                }
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var (diagnostics, sources) = Run(source);
        AssertSingleSpf(
            diagnostics,
            "SPF023",
            "Hidden",
            "#spf023-sparseignore-on-unsupported-property",
            expectInSource: true
        );
        sources.ShouldBeEmpty();
    }

    [Test]
    public void SparseIgnoreAvoidsFalseDuplicateJsonNameAndCanCombineWithJsonIgnore()
    {
        const string source = """
            using SparseFragments;
            using System.Text.Json.Serialization;
            [SparseFragmentModel]
            public partial class IgnoredJsonNameModel
            {
                [SparseIgnore, JsonPropertyName("same")]
                public string Hidden { get; set; } = "";
                [JsonPropertyName("same")]
                public string Visible { get; set; } = "";
                [SparseIgnore, JsonIgnore]
                public string AlsoHidden { get; set; } = "";
            }
            """;
        var (diagnostics, sources) = Run(source);
        diagnostics.Where(d => d.Id.StartsWith("SPF", StringComparison.Ordinal)).ShouldBeEmpty();
        sources
            .Any(s => s.HintName.Contains("IgnoredJsonNameModel", StringComparison.Ordinal))
            .ShouldBeTrue();
    }
}
