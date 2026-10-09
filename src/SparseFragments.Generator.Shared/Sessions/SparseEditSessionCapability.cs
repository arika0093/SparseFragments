using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Compilation-scoped edit-session helpers requested by analyzed models.</summary>
/// <remarks>
/// The reusable session cores (<c>EditSessionCore</c> and
/// <c>EditSessionWithCurrentCore</c>) are model-independent: one definition per
/// compilation serves every typed per-model adapter. Flags de-duplicate the
/// request so many models still emit exactly one core pair.
/// </remarks>
[System.Flags]
internal enum SparseEditSessionCapability
{
    /// <summary>No session helper is required.</summary>
    None = 0,

    /// <summary>The observable-only session core is required.</summary>
    Core = 1,

    /// <summary>The current-view session core is required.</summary>
    WithCurrent = 2,

    /// <summary>Both session cores are required.</summary>
    All = Core | WithCurrent,
}

/// <summary>Semantic roles bound by the generic session cores.</summary>
/// <remarks>
/// Names the product-neutral contract between a reusable core and its typed
/// per-model adapters: state (<c>TModel</c>), state snapshot
/// (<c>TFragment</c>), baseline-free transition command (<c>TPatch</c>),
/// baseline-aware transition (<c>TChangeSet</c>), observable view
/// (<c>TObservable</c>) and read-only current view (<c>TCurrent</c>).
/// Templates and dialects resolve product types through these roles.
/// </remarks>
internal static class SparseEditSessionRoles
{
    /// <summary>Live editable model state.</summary>
    public const string Model = nameof(Model);

    /// <summary>Presence-aware snapshot of model state.</summary>
    public const string Fragment = nameof(Fragment);

    /// <summary>Baseline-free transition command.</summary>
    public const string Patch = nameof(Patch);

    /// <summary>Baseline-aware transition.</summary>
    public const string ChangeSet = nameof(ChangeSet);

    /// <summary>Bindable observable view.</summary>
    public const string Observable = nameof(Observable);

    /// <summary>Read-only current view.</summary>
    public const string Current = nameof(Current);
}

/// <summary>Connection contract between a session core and typed model adapters.</summary>
/// <remarks>
/// Documents the delegates a per-model <c>EditSession</c> must supply:
/// <c>FromModel</c>, <c>Between</c>, <c>ToPatch</c>, <c>IsEmpty</c>,
/// <c>AdvanceBaseline</c>, <c>ToObservable</c>, <c>ToCurrent</c>,
/// <c>TryApplyTo</c>, <c>WriteModel</c>, <c>Invert</c>, <c>Rebase</c>,
/// <c>EnumerateChangedPaths</c>, <c>RefreshObservable</c> and
/// <c>BaselineToModel</c>. Validation runs at generation time where the
/// required family is missing from <see cref="SparseEmissionFeatures"/>.
/// </remarks>
internal static class SparseEditSessionAdapterContract
{
    /// <summary>Delegate names the adapter must bind, in configuration order.</summary>
    public static ImmutableArray<string> RequiredMembers { get; } =
        ImmutableArray.Create(
            "FromModel",
            "Between",
            "ToPatch",
            "IsEmpty",
            "AdvanceBaseline",
            "ToObservable",
            "ToCurrent",
            "TryApplyTo",
            "WriteModel",
            "Invert",
            "Rebase",
            "EnumerateChangedPaths",
            "RefreshObservable",
            "BaselineToModel"
        );

    /// <summary>Validates that selected families can back a typed session adapter.</summary>
    /// <param name="features">Owning generator feature selection.</param>
    /// <returns>Dependency violations, empty when the adapter can be bound.</returns>
    public static ImmutableArray<string> ValidateAdapterFeatures(SparseEmissionFeatures features)
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (!features.EmitFragment)
        {
            errors.Add("EditSession adapters require EmitFragment (FromModel/BaselineToModel).");
        }

        if (!features.EmitPatch)
        {
            errors.Add("EditSession adapters require EmitPatch (ToPatch).");
        }

        if (!features.EmitChangeSet)
        {
            errors.Add("EditSession adapters require EmitChangeSet (Between/Invert/Rebase).");
        }

        if (!features.EmitObservable)
        {
            errors.Add("EditSession adapters require EmitObservable (ToObservable/ToCurrent).");
        }

        return errors.ToImmutable();
    }
}

/// <summary>Capability aggregation for compilation-scoped session cores.</summary>
/// <remarks>
/// Merge-time seam: the capability-driven Generated-Once pipeline (#178)
/// will aggregate these flags across explicit and promoted models; the
/// three-layer ownership design (#177) owns the Runtime/Generated-Once/
/// Per-Model boundary. Until then this helper keeps aggregation deterministic
/// and routes hint names through the configured dialect (the #176 placement
/// seam supplies per-model names; compilation-scoped helpers keep stable
/// dialect hint names).
/// </remarks>
internal static class SparseEditSessionCapabilities
{
    /// <summary>Aggregates per-model session needs into one deterministic request.</summary>
    /// <param name="hasSessionModels">Whether any model needs a typed session.</param>
    /// <param name="needsCurrentView">Whether the current-view core is needed.</param>
    /// <returns>The de-duplicated capability set.</returns>
    public static SparseEditSessionCapability ForCompilation(
        bool hasSessionModels,
        bool needsCurrentView
    )
    {
        if (!hasSessionModels)
        {
            return SparseEditSessionCapability.None;
        }

        return needsCurrentView
            ? SparseEditSessionCapability.All
            : SparseEditSessionCapability.Core | SparseEditSessionCapability.WithCurrent;
    }

    /// <summary>Validates prerequisites for the requested cores.</summary>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="capability">Requested capability set.</param>
    /// <returns>Actionable errors, empty when prerequisites hold.</returns>
    public static ImmutableArray<string> ValidatePrerequisites(
        SparseGeneratorConfig config,
        SparseEditSessionCapability capability
    )
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (capability == SparseEditSessionCapability.None)
        {
            return errors.ToImmutable();
        }

        if (config.EditSessionDialect is null)
        {
            errors.Add("EditSession cores require SparseGeneratorConfig.EditSessionDialect.");
        }

        if (config.RuntimeDialect is null)
        {
            errors.Add("EditSession cores require SparseGeneratorConfig.RuntimeDialect.");
        }

        if (config.PatchDialect is null)
        {
            errors.Add("EditSession cores require SparseGeneratorConfig.PatchDialect.");
        }

        foreach (
            var error in SparseEditSessionAdapterContract.ValidateAdapterFeatures(
                config.EffectiveEmissionFeatures
            )
        )
        {
            errors.Add(error);
        }

        return errors.ToImmutable();
    }

    /// <summary>Whether the requested set includes the observable-only core.</summary>
    /// <param name="capability">Requested capability set.</param>
    /// <returns>True when the core must be emitted.</returns>
    public static bool NeedsCore(SparseEditSessionCapability capability) =>
        (capability & SparseEditSessionCapability.Core) != 0;

    /// <summary>Whether the requested set includes the current-view core.</summary>
    /// <param name="capability">Requested capability set.</param>
    /// <returns>True when the current-view core must be emitted.</returns>
    public static bool NeedsWithCurrent(SparseEditSessionCapability capability) =>
        (capability & SparseEditSessionCapability.WithCurrent) != 0;
}
