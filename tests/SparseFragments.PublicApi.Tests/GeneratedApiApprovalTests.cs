using System.Reflection;
using SparseFragments.Blazor;
using SparseFragments.GeneratedApiFixtures;

namespace SparseFragments.PublicApi.Tests;

/// <summary>
/// Approval tests for generated user APIs and the Blazor surface (issue #117).
/// The snapshot holds the whole fixture assembly: PublicApiGenerator renders nested
/// contracts under their declaring models, so the hand-written fixture shells stay
/// included. Fixtures are frozen minimal scaffolding; generated contract diffs appear
/// in the nested blocks. See README.md for the update workflow.
/// </summary>
public sealed class GeneratedApiApprovalTests
{
    private const string BlazorApprovalFile = "SparseFragments.Blazor.approved.txt";

    private static readonly Type[] FixtureModels =
    [
        typeof(CatalogScalar),
        typeof(CatalogNested),
        typeof(CatalogKeyedItem),
        typeof(CatalogKeyedRoster),
        typeof(CatalogDictionaries),
        typeof(CatalogImmutable),
    ];

    // Blazor ships net8.0 and net10.0 from identical sources (no #if in
    // SparseEditSessionExtensions.cs); one snapshot covers both target builds.
    [Test]
    public void BlazorSurface() =>
        PublicApiCheck.CheckAssembly(
            typeof(SparseEditSessionExtensions).Assembly,
            BlazorApprovalFile
        );

    [Test]
    public void GeneratedUserApi() => PublicApiCheck.Check<CatalogScalar>();

    [Test]
    public void GeneratedApiCoversRepresentativeShapes()
    {
        foreach (var model in FixtureModels)
        {
            foreach (
                var nested in new[]
                {
                    "Fragment",
                    "Patch",
                    "ChangeSet",
                    "ChangePayload",
                    "Observable",
                    "ReadOnlyView",
                    "FragmentBuilder",
                }
            )
                AssertNested(model, nested);

            var changeSet = AssertNested(model, "ChangeSet");
            AssertMethods(
                changeSet,
                [
                    "Between",
                    "FromPatch",
                    "ToPatch",
                    "Invert",
                    "Compose",
                    "RebaseOnto",
                    "TryApplyTo",
                    "ApplyToBaseline",
                    "EnumerateChangedPaths",
                    "ToPayload",
                    "FromPayload",
                ]
            );

            var patch = AssertNested(model, "Patch");
            AssertMethods(patch, ["Between", "Apply", "ApplyTo"]);

            // The unified transport converts both directions explicitly.
            var changePayload = AssertNested(model, "ChangePayload");
            AssertMethods(changePayload, ["ToChangeSet", "ToPatch", "FromPatch"]);

            var extensions = AssertExtensionContainer(model);
            AssertMethods(extensions, ["CreateChangeSet", "CreateEditSession", "ToObservable"]);
        }

        // Typed per-key and per-member projections stay addressable by name.
        AssertNested(AssertNested(typeof(CatalogKeyedRoster), "ChangeSet"), "ItemsTransition");
        var dictionaryChanges = AssertNested(typeof(CatalogDictionaries), "ChangeSet");
        AssertNested(dictionaryChanges, "ScoresTransition");
        AssertNested(dictionaryChanges, "DetailsTransition");
        AssertNested(AssertNested(typeof(CatalogKeyedItem), "ChangeSet"), "IdTransition");
        AssertMethods(
            AssertNested(typeof(CatalogScalar), "ChangeSet"),
            ["TryApplyInPlace", "ApplyInPlace"]
        );
    }

    [Test]
    public void SubmitApisStayAbsent()
    {
        var fixture = typeof(CatalogScalar).Assembly;
        var offenders = fixture
            .GetExportedTypes()
            .SelectMany(static type => type.GetMethods().Select(method => new { type, method }))
            .Where(static entry => entry.method.DeclaringType == entry.type)
            .Where(static entry => entry.method.Name == "Submit")
            .Select(static entry => $"{entry.type.FullName}.{entry.method.Name}")
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        offenders.ShouldBeEmpty();

        var blazor = typeof(SparseEditSessionExtensions).Assembly;
        var blazorOffenders = blazor
            .GetExportedTypes()
            .SelectMany(static type =>
                type.GetMethods().Select(method => $"{type.FullName}.{method.Name}")
            )
            .Where(static name => name.Contains("Submit", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        blazorOffenders.ShouldBeEmpty();
    }

    private static Type AssertNested(Type declaring, string name)
    {
        var nested = declaring.GetNestedType(name);
        nested.ShouldNotBeNull($"Generated {declaring.Name}.{name} must stay public.");
        nested!.IsNestedPublic.ShouldBeTrue();
        return nested;
    }

    private static Type AssertExtensionContainer(Type model)
    {
        var container = model
            .Assembly.GetExportedTypes()
            .Where(candidate =>
                candidate.DeclaringType is null
                && candidate.Name.StartsWith(model.Name + "Extensions_", StringComparison.Ordinal)
            )
            .ToArray();
        container.Length.ShouldBe(1);
        return container[0];
    }

    private static void AssertMethods(Type type, string[] names)
    {
        var methods = type.GetMethods(
                BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            )
            .Select(static method => method.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var name in names)
            methods.Contains(name).ShouldBeTrue($"Expected {type.Name}.{name} to stay public.");
    }
}
