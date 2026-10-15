using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SparseTypes.Blazor;
using SparseTypes.GeneratedApiFixtures;

namespace SparseTypes.PublicApi.Tests;

/// <summary>Approval tests for generated user APIs and the Blazor surface.</summary>
public sealed class GeneratedApiApprovalTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseTypes.ISparseEditSession<T> session)
        where T : class => session.Model;

    private const string BlazorApprovalFile = "SparseTypes.Blazor.approved.txt";
    private const string EntityFrameworkCoreApprovalFile =
        "SparseTypes.EntityFrameworkCore.approved.txt";

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
    public void EntityFrameworkCoreSurface() =>
        PublicApiCheck.CheckAssembly(
            typeof(SparseTypes.EntityFrameworkCore.SparseEntityMapper<,>).Assembly,
            EntityFrameworkCoreApprovalFile
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
                    "EditSession",
                }
            )
                AssertNested(model, nested);

            // Builders and paths live in the per-model Generated container.
            model
                .GetNestedTypes()
                .Select(static type => type.Name)
                .ShouldNotContain("FragmentBuilder");
            model.GetNestedTypes().Select(static type => type.Name).ShouldNotContain("SparsePaths");

            var changeSet = AssertNested(model, "ChangeSet");
            var empty = changeSet.GetProperty("Empty", BindingFlags.Public | BindingFlags.Static);
            empty.ShouldNotBeNull($"Expected {model.Name}.ChangeSet.Empty.");
            empty!.PropertyType.ShouldBe(changeSet);
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
    public void GeneratedEditSessionsExposeClassifiedSessionContracts()
    {
        // Generated sessions implement the runtime capability contracts. Pin the
        // generic surfaces here so the generator cannot emit a session contract
        // that the audience classification does not cover.
        foreach (var model in FixtureModels)
        {
            var changeSet = AssertNested(model, "ChangeSet");
            var session = AssertNested(model, "EditSession");
            var withChangeSet =
                typeof(SparseTypes.ISparseEditSessionWithChangeSet<,>).MakeGenericType(
                    model,
                    changeSet
                );
            session
                .GetInterfaces()
                .ShouldContain(
                    withChangeSet,
                    $"{model.Name}.EditSession must expose {withChangeSet}."
                );
            var withChildStatus =
                typeof(SparseTypes.ISparseEditSessionWithChildStatus<,>).MakeGenericType(
                    model,
                    typeof(SparseTypes.SparseConflict)
                );
            session
                .GetInterfaces()
                .ShouldContain(
                    withChildStatus,
                    $"{model.Name}.EditSession must expose {withChildStatus}."
                );
        }

        foreach (
            var definition in new[]
            {
                typeof(SparseTypes.ISparseEditSessionWithChangeSet<,>),
                typeof(SparseTypes.ISparseEditSessionWithChildStatus<,>),
            }
        )
            definition
                .GetCustomAttribute<EditorBrowsableAttribute>()
                ?.State.ShouldBe(EditorBrowsableState.Advanced);
    }

    [Test]
    public void CreateEditSessionReturnsTheModelSpecificSubclass()
    {
        var session = new CatalogScalar().CreateEditSession();

        session.GetType().Namespace.ShouldBe("SparseTypes.GeneratedApiFixtures");
        session.GetType().Name.ShouldBe("EditSession");
        session
            .GetType()
            .GetInterfaces()
            .ShouldContain(
                typeof(SparseTypes.ISparseEditSession<CatalogScalar, CatalogScalar.ChangeSet>)
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
    public void EditSessionExposesCreateHistoryFactory()
    {
        // each generated session offers a typed opt-in history factory bound to
        // its own change set, without reflection.
        foreach (var model in FixtureModels)
        {
            var session = AssertNested(model, "EditSession");
            var factory = session.GetMethod(
                "CreateHistory",
                BindingFlags.Public | BindingFlags.Instance
            );
            factory.ShouldNotBeNull($"Expected {model.Name}.EditSession.CreateHistory.");
            factory!.ReturnType.IsGenericType.ShouldBeTrue();
            factory
                .ReturnType.GetGenericTypeDefinition()
                .ShouldBe(typeof(SparseTypes.SparseEditHistory<>));
            factory.ReturnType.GetGenericArguments()[0].ShouldBe(model.GetNestedType("ChangeSet"));
            var options = factory.GetParameters();
            options.Length.ShouldBe(1);
            options[0].ParameterType.ShouldBe(typeof(SparseTypes.EditHistoryOptions));
            options[0].HasDefaultValue.ShouldBeTrue();
        }

        // The factory result supports the documented undo/redo surface.
        var scalarHistory = new CatalogScalar().CreateEditSession().CreateHistory();
        scalarHistory.CanUndo.ShouldBeFalse();
        scalarHistory.CanRedo.ShouldBeFalse();
        scalarHistory.MaxEntries.ShouldBeGreaterThan(0);
        scalarHistory.Dispose();
    }

    [Test]
    public void EditSessionExposesSingleOwnedHistory()
    {
        // each generated session owns one opt-in history behind the
        // framework-neutral control contract.
        foreach (var model in FixtureModels)
        {
            var session = AssertNested(model, "EditSession");

            var historyProperty = session.GetProperty(
                "History",
                BindingFlags.Public | BindingFlags.Instance
            );
            historyProperty.ShouldNotBeNull($"Expected {model.Name}.EditSession.History.");
            historyProperty!.PropertyType.ShouldBe(typeof(SparseTypes.ISparseEditHistory));

            var enable = session.GetMethod(
                "EnableHistory",
                BindingFlags.Public | BindingFlags.Instance
            );
            enable.ShouldNotBeNull($"Expected {model.Name}.EditSession.EnableHistory.");
            enable!.ReturnType.ShouldBe(typeof(SparseTypes.ISparseEditHistory));
            var options = enable.GetParameters();
            options.Length.ShouldBe(1);
            options[0].ParameterType.ShouldBe(typeof(SparseTypes.EditHistoryOptions));
            options[0].HasDefaultValue.ShouldBeTrue();

            var disable = session.GetMethod(
                "DisableHistory",
                BindingFlags.Public | BindingFlags.Instance
            );
            disable.ShouldNotBeNull($"Expected {model.Name}.EditSession.DisableHistory.");
            disable!.GetParameters().Length.ShouldBe(0);

            var availability = session.GetEvent(
                "HistoryChanged",
                BindingFlags.Public | BindingFlags.Instance
            );
            availability.ShouldNotBeNull($"Expected {model.Name}.EditSession.HistoryChanged.");
            availability!.EventHandlerType.ShouldBe(typeof(Action));
        }

        var ownedSession = new CatalogScalar().CreateEditSession();
        ownedSession.History.ShouldBeNull();
        var history = ownedSession.EnableHistory();
        history.ShouldNotBeNull();
        ReferenceEquals(history, ownedSession.History).ShouldBeTrue();
        (history is IDisposable).ShouldBeFalse();
        ownedSession.DisableHistory();
        ownedSession.History.ShouldBeNull();
    }

    [Test]
    public void PayloadImplementationDtosAreGroupedAndHiddenFromIntelliSense()
    {
        // Stage 3: typed DTOs live in the per-model implementation namespace,
        // not nested in the annotated model.
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
            container.Namespace.ShouldBe("SparseTypes.Generated");
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
        // Stage 3: the model keeps a thin private shell; bodies live in
        // the per-model implementation converter it derives from.
        var shell = typeof(CatalogScalar.Fragment)
            .GetNestedType("FragmentJsonConverter", BindingFlags.Public | BindingFlags.NonPublic)
            .ShouldNotBeNull();
        shell.IsNestedPrivate.ShouldBeTrue();
        shell.BaseType.ShouldNotBeNull().Namespace.ShouldBe("SparseTypes.Generated");
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
