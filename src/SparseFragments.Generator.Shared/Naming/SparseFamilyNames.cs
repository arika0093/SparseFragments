namespace SparseFragments.Generator.Shared;

/// <summary>Product-owned public names for the generated state, operation, and view families.</summary>
/// <remarks>
/// Shared analysis and emitters describe what a generated type means through
/// semantic roles (state, operation, transition, payload, observable view,
/// read-only view). This record carries the product-specific names bound to
/// those roles. The standalone defaults preserve the existing
/// <c>Fragment</c>/<c>Patch</c>/<c>ChangeSet</c> vocabulary; a downstream
/// generator supplies its own names and the shared reference resolver
/// (<see cref="SparseSemanticReference"/>) derives every nested and child
/// reference from them instead of rebuilding product literals in emitters.
/// </remarks>
internal sealed record SparseFamilyNames
{
    public SparseFamilyNames(
        string Fragment = SparseWellKnownNames.FragmentTypeName,
        string FragmentBuilder = "FragmentBuilder",
        string Patch = "Patch",
        string ChangeSet = "ChangeSet",
        string ChangePayload = "ChangePayload",
        string Observable = "Observable",
        string ReadOnlyView = "ReadOnlyView"
    )
    {
        this.Fragment = Fragment;
        this.FragmentBuilder = FragmentBuilder;
        this.Patch = Patch;
        this.ChangeSet = ChangeSet;
        this.ChangePayload = ChangePayload;
        this.Observable = Observable;
        this.ReadOnlyView = ReadOnlyView;
    }

    /// <summary>Name bound to the state role (presence-aware snapshot).</summary>
    public string Fragment { get; init; }

    /// <summary>Name bound to the mutable state-builder role.</summary>
    public string FragmentBuilder { get; init; }

    /// <summary>Name bound to the baseline-free operation role.</summary>
    public string Patch { get; init; }

    /// <summary>Name bound to the baseline-aware transition role.</summary>
    public string ChangeSet { get; init; }

    /// <summary>Name bound to the transport payload role.</summary>
    public string ChangePayload { get; init; }

    /// <summary>Name bound to the bindable observable-view role.</summary>
    public string Observable { get; init; }

    /// <summary>Name bound to the read-only view role.</summary>
    public string ReadOnlyView { get; init; }

    /// <summary>Standalone defaults preserving the existing public vocabulary.</summary>
    public static SparseFamilyNames Standalone { get; } = new SparseFamilyNames();
}
