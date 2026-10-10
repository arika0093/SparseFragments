using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Feature families a product generator opts into for one model.</summary>
/// <remarks>
/// Shared owns the emission; the owning generator selects which families appear
/// so downstream products avoid unused public APIs. The standalone defaults
/// emit every family except fragment origins, which stay opt-in so downstream
/// runtimes without provenance support keep compiling. Selections are validated
/// with <see cref="ValidateDependencies"/> before emission.
/// </remarks>
internal sealed record SparseEmissionFeatures
{
    public SparseEmissionFeatures(
        bool EmitFragment = true,
        bool EmitPatch = true,
        bool EmitChangeSet = true,
        bool EmitChangePayload = true,
        bool EmitObservable = true,
        bool EmitJsonConverters = true,
        bool EmitFragmentOrigins = false
    )
    {
        this.EmitFragment = EmitFragment;
        this.EmitPatch = EmitPatch;
        this.EmitChangeSet = EmitChangeSet;
        this.EmitChangePayload = EmitChangePayload;
        this.EmitObservable = EmitObservable;
        this.EmitJsonConverters = EmitJsonConverters;
        this.EmitFragmentOrigins = EmitFragmentOrigins;
    }

    /// <summary>Whether the sparse <c>Fragment</c> state type is emitted.</summary>
    public bool EmitFragment { get; init; }

    /// <summary>Whether the baseline-free <c>Patch</c> operation type is emitted.</summary>
    public bool EmitPatch { get; init; }

    /// <summary>Whether the baseline-aware <c>ChangeSet</c> transition type is emitted.</summary>
    public bool EmitChangeSet { get; init; }

    /// <summary>Whether the transport payload DTOs are emitted.</summary>
    public bool EmitChangePayload { get; init; }

    /// <summary>Whether the bindable <c>Observable</c> proxy is emitted.</summary>
    public bool EmitObservable { get; init; }

    /// <summary>Whether the <c>FragmentJsonConverter</c> helpers are emitted.</summary>
    public bool EmitJsonConverters { get; init; }

    /// <summary>Whether fragments carry optional origin attribution through merge with origin query APIs.</summary>
    public bool EmitFragmentOrigins { get; init; }

    /// <summary>Standalone defaults: every family is emitted except fragment origins.</summary>
    public static SparseEmissionFeatures Standalone { get; } = new SparseEmissionFeatures();

    /// <summary>Dependency violations in this selection, if any.</summary>
    /// <remarks>
    /// A patch needs its fragment, a change set needs its patch, a payload
    /// needs its change set, and observable and converters need the fragment.
    /// Returns an empty array when the selection is coherent.
    /// </remarks>
    public ImmutableArray<string> ValidateDependencies()
    {
        var errors = ImmutableArray.CreateBuilder<string>();
        if (!EmitFragment)
        {
            if (EmitPatch)
                errors.Add("EmitPatch requires EmitFragment.");
            if (EmitChangeSet)
                errors.Add("EmitChangeSet requires EmitFragment.");
            if (EmitChangePayload)
                errors.Add("EmitChangePayload requires EmitFragment.");
            if (EmitObservable)
                errors.Add("EmitObservable requires EmitFragment.");
            if (EmitJsonConverters)
                errors.Add("EmitJsonConverters requires EmitFragment.");
            if (EmitFragmentOrigins)
                errors.Add("EmitFragmentOrigins requires EmitFragment.");
        }
        if (EmitChangeSet && !EmitPatch)
            errors.Add("EmitChangeSet requires EmitPatch.");
        if (EmitChangePayload && !EmitChangeSet)
            errors.Add("EmitChangePayload requires EmitChangeSet.");
        return errors.ToImmutable();
    }

    /// <summary>Generated type names this selection emits.</summary>
    /// <remarks>
    /// Covers the family root names used for generated-name collision checks.
    /// Member-prefixed payload DTO names cannot collide with member names by
    /// construction and are not listed.
    /// </remarks>
    public ImmutableArray<string> GetEmittedTypeNames() =>
        GetEmittedTypeNames(SparseFamilyNames.Standalone);

    /// <summary>Generated type names this selection emits under one product vocabulary.</summary>
    /// <param name="family">Product family-name bindings.</param>
    /// <returns>The family root names in the product vocabulary.</returns>
    public ImmutableArray<string> GetEmittedTypeNames(SparseFamilyNames family)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        if (EmitFragment)
        {
            names.Add(family.Fragment);
            names.Add(family.FragmentBuilder);
        }
        if (EmitPatch)
            names.Add(family.Patch);
        if (EmitChangeSet)
            names.Add(family.ChangeSet);
        if (EmitChangePayload)
            names.Add(family.ChangePayload);
        if (EmitObservable)
        {
            names.Add(family.Observable);
            names.Add("Sparse" + family.Observable);
        }
        return names.ToImmutable();
    }
}
