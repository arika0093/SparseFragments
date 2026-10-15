using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;

namespace SparseTypes.Tests.Shared;

// Inventory the generated public/internal surface and detect leaked helpers.
// Payload DTOs remain public for trimming and source generation.
public sealed class GeneratedVisibilityInventoryTests
{
    private const string ProbeSource = """
        using SparseTypes;
        using System.Collections.Generic;
        namespace VisibilityProbe
        {
            [SparseModel]
            public partial class ProbeChild
            {
                public string Name { get; set; } = "";
            }
            [SparseModel]
            public partial class ProbeRoot
            {
                public string Label { get; set; } = "";
                public ProbeChild? Child { get; set; }
                public List<ProbeItem> Items { get; set; } = new();
                public Dictionary<string, string> Scores { get; set; } = new();
            }
            [SparseModel]
            public partial class ProbeItem
            {
                [SparseKey]
                public string Id { get; set; } = "";
                public string Name { get; set; } = "";
            }
        }
        """;

    private static Compilation RunProbe()
    {
        var tree = CSharpSyntaxTree.ParseText(ProbeSource, path: "Models.cs");
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "VisibilityProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        updated
            .GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return updated;
    }

    private static INamedTypeSymbol RequireRoot(Compilation compilation, string name)
    {
        var root = compilation.GetTypeByMetadataName($"VisibilityProbe.{name}");
        root.ShouldNotBeNull($"Model VisibilityProbe.{name} must exist.");
        return root!;
    }

    private static INamedTypeSymbol RequireNested(INamedTypeSymbol root, string name)
    {
        var nested = root.GetTypeMembers(name).FirstOrDefault();
        nested.ShouldNotBeNull($"Generated {root.Name}.{name} must exist.");
        return nested!;
    }

    private static string EffectiveAccessibility(ISymbol symbol)
    {
        // Effective visibility: a member inside a private nested type never
        // escapes the facade, even when declared internal.
        var current = symbol;
        var effective = symbol.DeclaredAccessibility;
        var container = symbol.ContainingType;
        while (container is not null)
        {
            if (
                container.DeclaredAccessibility == Accessibility.Private
                || container.DeclaredAccessibility == Accessibility.NotApplicable
            )
            {
                return "Private";
            }

            if (container.DeclaredAccessibility == Accessibility.Internal)
            {
                effective = Accessibility.Internal;
            }

            container = container.ContainingType;
        }

        return effective switch
        {
            Accessibility.Public => "Public",
            Accessibility.Internal => "Internal",
            Accessibility.ProtectedOrInternal => "Internal",
            Accessibility.Protected => "Protected",
            _ => "Private",
        };
    }

    [Test]
    public void PatchUsesPrivateStorageWithNestedCore()
    {
        var root = RequireRoot(RunProbe(), "ProbeRoot");
        var patch = RequireNested(root, "Patch");

        // Facades stay partial so the nested core shares private state.
        patch.IsPartial().ShouldBeTrue("Patch must be partial for the nested __Core.");

        var core = patch.GetTypeMembers("__Core").FirstOrDefault();
        core.ShouldNotBeNull("Patch.__Core must exist for state-bound algorithms.");
        core!.DeclaredAccessibility.ShouldBe(
            Accessibility.Private,
            "Patch.__Core must be private (state-bound, no cross-type seam)."
        );

        // Top-level storage is private; cross-family callers use typed APIs or
        // the narrow __Sparse* bridges below, never direct fields.
        var fields = patch
            .GetMembers()
            .Where(static m =>
                m.Name.StartsWith("__sparse_patch_member_", StringComparison.Ordinal)
            )
            .ToArray();
        fields.ShouldNotBeEmpty();
        foreach (var field in fields)
        {
            field.DeclaredAccessibility.ShouldBe(
                Accessibility.Private,
                $"Patch storage {field.Name} must be private."
            );
        }

        // Documented narrow seams: Patch<->ChangeSet, payload, collection.
        foreach (
            var bridge in new[]
            {
                "__SparseIsEmpty",
                "__SparseSet",
                "__SparseSetNull",
                "__SparseRemove",
            }
        )
        {
            var member = patch.GetMembers(bridge).FirstOrDefault();
            member.ShouldNotBeNull($"Bridge {bridge} must exist.");
            EffectiveAccessibility(member!).ShouldBe("Internal");
        }
    }

    [Test]
    public void ChangeSetUsesPrivateCanonicalStorageWithNestedCore()
    {
        var root = RequireRoot(RunProbe(), "ProbeRoot");
        var changeSet = RequireNested(root, "ChangeSet");

        changeSet.IsPartial().ShouldBeTrue("ChangeSet must be partial for the nested __Core.");
        var core = changeSet.GetTypeMembers("__Core").FirstOrDefault();
        core.ShouldNotBeNull("ChangeSet.__Core must exist.");
        core!.DeclaredAccessibility.ShouldBe(Accessibility.Private);

        // Canonical sparse storage is private; __Core reads it directly.
        foreach (var name in new[] { "__sparse_hasWhole", "__sparse_has_0", "__sparse_nested_1" })
        {
            var member = changeSet.GetMembers(name).FirstOrDefault();
            if (member is null)
            {
                continue;
            }

            member.DeclaredAccessibility.ShouldBe(
                Accessibility.Private,
                $"ChangeSet storage {name} must be private."
            );
        }

        // Canonical constructor is private; only __Core builds instances.
        var ctors = changeSet
            .GetMembers(".ctor")
            .OfType<IMethodSymbol>()
            .Where(static c => c.Parameters.Length > 3)
            .ToArray();
        ctors.ShouldNotBeEmpty();
        foreach (var ctor in ctors)
        {
            ctor.DeclaredAccessibility.ShouldBe(
                Accessibility.Private,
                "Canonical ChangeSet constructor must be private."
            );
        }
    }

    [Test]
    public void ImplementationContainerHostsOnlyPublicBuilderAndPaths()
    {
        var compilation = RunProbe();
        // The per-model container must not host internal operation owners;
        // Builder/Paths stay public, ops moved to private nested cores.
        var allTypes = compilation
            .GlobalNamespace.GetTypeMembers("SparseTypes.Generated")
            .SelectMany(static ns => ns.GetTypeMembers())
            .SelectMany(static container => container.GetTypeMembers())
            .ToArray();
        var patchOps = allTypes.Where(static t => t.Name == "PatchOperations").ToArray();
        var changeSetOps = allTypes.Where(static t => t.Name == "ChangeSetOperations").ToArray();
        patchOps.ShouldBeEmpty("No external PatchOperations may remain (moved to Patch.__Core).");
        changeSetOps.ShouldBeEmpty(
            "No external ChangeSetOperations may remain (moved to ChangeSet.__Core)."
        );
    }

    [Test]
    public void RemainingInternalBridgesStayDocumentedAndNarrow()
    {
        var compilation = RunProbe();
        var root = RequireRoot(compilation, "ProbeRoot");
        var fragment = RequireNested(root, "Fragment");

        // Cross-family seams (Patch/ChangeSet reading Fragment strategies and
        // equality): genuinely cross-type, minimum internal.
        foreach (var name in new[] { "__SparseAreEqual", "__SparseWriteWritableTo" })
        {
            var member = fragment.GetMembers(name).FirstOrDefault();
            member.ShouldNotBeNull($"Fragment seam {name} must exist.");
            EffectiveAccessibility(member!).ShouldBe("Internal");
        }

        // Observable bridges (DescriptorFactory/EditSession): inter-model
        // descriptor and session boundaries, retained as necessary seams.
        var observable =
            root.GetTypeMembers("Observable").FirstOrDefault()
            ?? root.GetTypeMembers("SparseObservable").FirstOrDefault();
        if (observable is not null)
        {
            foreach (var name in new[] { "__SparseTarget", "__SparseRefresh", "__Raise" })
            {
                var member = observable.GetMembers(name).FirstOrDefault();
                if (member is null)
                {
                    continue;
                }

                EffectiveAccessibility(member).ShouldBe("Internal");
            }
        }

        // Model attribute bridges (impl reading model-private attributes):
        // same-assembly factory seam, minimum internal.
        var bridges = root.GetMembers()
            .Where(static m => m.Name.StartsWith("__SparseAttributes_", StringComparison.Ordinal))
            .ToArray();
        foreach (var bridge in bridges)
        {
            EffectiveAccessibility(bridge).ShouldBe("Internal");
        }
    }
}

internal static class PartialHelper
{
    internal static bool IsPartial(this INamedTypeSymbol symbol)
    {
        // Roslyn does not expose 'partial' directly; partial types have
        // multiple declarations.
        var refs = symbol.DeclaringSyntaxReferences;
        if (refs.Length > 1)
        {
            return true;
        }

        foreach (var r in refs)
        {
            var syntax = r.GetSyntax();
            var text = syntax.ToString();
            if (text.Contains("partial", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
