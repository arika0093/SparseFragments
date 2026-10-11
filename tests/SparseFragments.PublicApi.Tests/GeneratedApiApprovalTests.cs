using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseFragments.ISparseEditSession<T> session)
        where T : class => session.Model;

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
                    "FragmentBuilder",
                }
            )
                AssertNested(model, nested);

            // Relocated stage (#190): UI/editing types live outside the model.
            model.GetNestedTypes().Select(static type => type.Name).ShouldNotContain("Observable");
            model
                .GetNestedTypes()
                .Select(static type => type.Name)
                .ShouldNotContain("ReadOnlyView");
            model.GetNestedTypes().Select(static type => type.Name).ShouldNotContain("EditSession");

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
                    "TryApplyInPlace",
                    "ApplyInPlace",
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

        var nestedReadOnlyView = new CatalogNested().CreateEditSession().Current.GetType();
        var metadataViewType = Nullable.GetUnderlyingType(
            nestedReadOnlyView.GetProperty(nameof(CatalogNested.Metadata))!.PropertyType
        );
        metadataViewType.ShouldNotBeNull();
        metadataViewType!.ShouldNotBe(typeof(CatalogPoco));
        var metadataElementType = Nullable.GetUnderlyingType(
            nestedReadOnlyView
                .GetProperty(nameof(CatalogNested.MetadataItems))!
                .PropertyType.GetGenericArguments()[0]
        );
        metadataElementType.ShouldBe(metadataViewType);
        metadataViewType.GetProperty(nameof(CatalogPoco.Label)).ShouldNotBeNull();

        var keyEntriesType = nestedReadOnlyView
            .GetProperty(nameof(CatalogNested.MetadataByKey))!
            .PropertyType;
        keyEntriesType
            .GetGenericTypeDefinition()
            .ShouldBe(typeof(System.Collections.Generic.IReadOnlyCollection<>));
        var entryKeyType = keyEntriesType.GetGenericArguments()[0].GetGenericArguments()[0];
        entryKeyType.ShouldNotBe(typeof(CatalogPoco));
        entryKeyType.GetProperty(nameof(CatalogPoco.Label)).ShouldNotBeNull();
    }

    [Test]
    public void CreateEditSessionReturnsTheModelSpecificSubclass()
    {
        var session = new CatalogScalar().CreateEditSession();

        session.GetType().Namespace.ShouldStartWith("SparseFragments.Generated");
        session.GetType().Name.ShouldBe("EditSession");
        session
            .GetType()
            .GetInterfaces()
            .ShouldContain(
                typeof(SparseFragments.ISparseEditSession<CatalogScalar, CatalogScalar.ChangeSet>)
            );
        session.Current.Name.ShouldBe(string.Empty);
        Raw(session).Name.ShouldBe(string.Empty);

        var baseline = new CatalogScalar { Name = "baseline" };
        var current = new CatalogScalar { Name = "current" };
        var separateBaselineSession = baseline.CreateEditSession(current);
        separateBaselineSession.GetType().ShouldBe(session.GetType());
        ReferenceEquals(Raw(separateBaselineSession), current).ShouldBeTrue();
        separateBaselineSession.Current.Name.ShouldBe("current");
    }

    [Test]
    public void PayloadImplementationDtosAreGroupedAndHiddenFromIntelliSense()
    {
        // Stage 3 (#192): typed DTOs live in the per-model implementation
        // namespace, not nested in the annotated model. PublicApiGenerator
        // filters EditorBrowsableAttribute from its API snapshots.
        foreach (var model in FixtureModels)
            model
                .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                .Where(static type => type.Name.StartsWith("__Internal_", StringComparison.Ordinal))
                .ShouldBeEmpty();

        var containers = FixtureModels
            .Select(static model => model.GetNestedType("ChangePayload")?.BaseType?.DeclaringType)
            .Where(static type => type is not null)
            .Cast<Type>()
            .ToArray();

        containers.Length.ShouldBe(FixtureModels.Length);
        foreach (var container in containers)
            container.Namespace.ShouldBe("SparseFragments.Generated");
        var implementationTypes = containers
            .SelectMany(static container =>
                container.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            )
            .ToArray();
        implementationTypes.ShouldNotBeEmpty();
        foreach (var container in containers)
            container
                .GetCustomAttribute<EditorBrowsableAttribute>()
                ?.State.ShouldBe(EditorBrowsableState.Never);
        foreach (var implementationType in implementationTypes)
            implementationType
                .GetCustomAttribute<EditorBrowsableAttribute>()
                ?.State.ShouldBe(EditorBrowsableState.Never);

        var scalarContainer = typeof(CatalogScalar)
            .GetNestedType("ChangePayload")!
            .BaseType!.DeclaringType!;
        scalarContainer.Name.ShouldStartWith("__Internal_");
        var scalarMemberChange = scalarContainer
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
            .Single(static type => type.Name.StartsWith("Change0_", StringComparison.Ordinal));
        scalarMemberChange.DeclaringType.ShouldBe(scalarContainer);
    }

    [Test]
    public void FragmentJsonConverterShellForwardsToImplementation()
    {
        // Stage 3 (#192): the model keeps a thin private shell; bodies live in
        // the per-model implementation converter it derives from.
        var shell = typeof(CatalogScalar.Fragment)
            .GetNestedType("FragmentJsonConverter", BindingFlags.Public | BindingFlags.NonPublic)
            .ShouldNotBeNull();
        shell.IsNestedPrivate.ShouldBeTrue();
        shell.BaseType.ShouldNotBeNull().Namespace.ShouldBe("SparseFragments.Generated");
        shell.BaseType!.Name.ShouldContain("FragmentJsonConverter");
    }

    [Test]
    public void FragmentJsonConverterIsPrivateImplementationDetail()
    {
        typeof(CatalogScalar.Fragment)
            .GetNestedType("FragmentJsonConverter", BindingFlags.Public | BindingFlags.NonPublic)
            .ShouldNotBeNull()
            .IsNestedPrivate.ShouldBeTrue();
    }

    [Test]
    public void ChangePayloadSupportsExternalSourceGeneration()
    {
        var before = new CatalogScalar { Name = "before", Count = 1 };
        var after = new CatalogScalar { Name = "after", Count = 1 };
        var payload = before.CreateChangeSet(after).ToPayload();
        var json = JsonSerializer.Serialize(
            payload,
            CatalogSerializerContext.Default.CatalogScalarChangePayload
        );

        var restored = JsonSerializer.Deserialize(
            json,
            CatalogSerializerContext.Default.CatalogScalarChangePayload
        );

        restored.ShouldNotBeNull();
        restored!.Changes.ShouldHaveSingleItem();
        restored.ToChangeSet().IsEmpty.ShouldBeFalse();
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

[JsonSerializable(
    typeof(CatalogScalar.ChangePayload),
    TypeInfoPropertyName = "CatalogScalarChangePayload"
)]
internal partial class CatalogSerializerContext : JsonSerializerContext { }
