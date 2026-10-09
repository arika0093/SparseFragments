using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Model-independent Patch/ChangeSet kernel families.</summary>
[System.Flags]
internal enum SparsePatchKernelKind
{
    /// <summary>No kernel is required.</summary>
    None = 0,

    /// <summary>Canonical path helpers (ancestor checks, key formatting).</summary>
    PathKernels = 1,

    /// <summary>Empty-operation identities.</summary>
    EmptyIdentities = 2,

    /// <summary>Keyed add/remove composition and inversion.</summary>
    KeyedMembership = 4,

    /// <summary>Order-only keyed transitions.</summary>
    OrderTransitions = 8,

    /// <summary>Conflict-path reporting.</summary>
    ConflictPaths = 16,
}

/// <summary>Inventory of extracted kernels versus model-specific code.</summary>
/// <remarks>
/// Extracted (pure, model-independent): presence composition, empty-operation
/// identities, keyed add/remove composition and inversion parameterized by
/// key/comparer, order-only flags, canonical bracket-key formatting and
/// segment-aware ancestor checks (mirroring the runtime mixed-operation path
/// algebra). Remaining model-specific: typed nested fragment/patch/changeset
/// operations, sparse canonical storage, keyed/dictionary sparse transitions,
/// unassigned-key rules, ownership boundaries and custom rebase/comparison
/// policies. Rationale: the former depends only on keys, kinds, orders and
/// comparers; the latter needs member types, storage layout and policies.
/// </remarks>
internal static class SparsePatchKernelInventory
{
    /// <summary>Names of the extracted reusable kernels.</summary>
    public static ImmutableArray<string> Extracted { get; } =
        ImmutableArray.Create(
            "TryComposePresence",
            "IsEmptyTransition",
            "IndexOfKey",
            "IsUnassignedKey",
            "ComposeKeyedSets",
            "InvertKeyedSets",
            "IsEmptyKeyedSet",
            "EscapeKeySegment",
            "FormatKeyedPath",
            "IsAncestorOrDescendant"
        );

    /// <summary>Areas that stay model-specific, with rationale.</summary>
    public static ImmutableArray<string> RemainingModelSpecific { get; } =
        ImmutableArray.Create(
            "Typed nested fragment/patch/changeset operations (member types differ).",
            "Sparse canonical keyed/dictionary storage (layout differs).",
            "Unassigned-key rules (sentinel type differs).",
            "Ownership and aliasing boundaries (lifetime differs).",
            "Custom rebase/comparison policies (callerComparer delegates differ)."
        );
}

/// <summary>Capability aggregation for model-independent operation kernels.</summary>
/// <remarks>
/// Merge-time seam: the Generated-Once pipeline (#178) aggregates these flags
/// across model families; the ownership design (#177) owns the
/// Runtime/Generated-Once/Per-Model boundary. Model-specific emitters invoke
/// the helpers while preserving typed nested operations; keyed/dictionary
/// canonical transitions, order-only changes, unassigned-key rules, ownership
/// boundaries and custom policies stay model-specific. Per-model placement
/// keeps using the #176 seam; kernel helpers keep one stable hint name per
/// compilation.
/// </remarks>
internal static class SparsePatchKernelCapabilities
{
    /// <summary>Stable hint name for the once-per-compilation kernel helpers.</summary>
    public const string KernelHintName = "Generated.PatchOperationKernels.g.cs";

    /// <summary>Aggregates kernel needs for one compilation.</summary>
    /// <param name="hasKeyedCollections">Whether keyed sequences occur.</param>
    /// <param name="hasDictionaries">Whether dictionaries occur.</param>
    /// <param name="hasChangeAlgebra">Whether compose/invert/rebase is emitted.</param>
    /// <returns>The de-duplicated kernel set.</returns>
    public static SparsePatchKernelKind ForCompilation(
        bool hasKeyedCollections,
        bool hasDictionaries,
        bool hasChangeAlgebra
    )
    {
        if (!hasChangeAlgebra && !hasKeyedCollections && !hasDictionaries)
        {
            return SparsePatchKernelKind.None;
        }

        var kind =
            SparsePatchKernelKind.PathKernels
            | SparsePatchKernelKind.EmptyIdentities
            | SparsePatchKernelKind.ConflictPaths;
        if (hasKeyedCollections || hasDictionaries)
        {
            kind |= SparsePatchKernelKind.KeyedMembership | SparsePatchKernelKind.OrderTransitions;
        }

        return kind;
    }
}

/// <summary>Product-neutral Generated-Once source for operation kernels.</summary>
/// <remarks>
/// Emits BCL-only generic helpers (no product runtime references) so
/// downstream generators reuse the same semantics without a SparseFragments
/// dependency. The standalone runtime kernels mirror this source for the
/// default product.
/// </remarks>
internal static class SparsePatchKernelEmitter
{
    /// <summary>Renders the once-per-compilation kernel helper source.</summary>
    /// <param name="rootNamespace">Owning generator helper namespace.</param>
    /// <param name="kinds">Requested kernel families.</param>
    /// <returns>Deterministic C# source.</returns>
    public static string RenderHelperSource(string rootNamespace, SparsePatchKernelKind kinds)
    {
        var code = new SharedIndentedBuilder(System.Threading.CancellationToken.None);
        code.AppendLineAt(0, "// <auto-generated />");
        code.AppendLineAt(0, "#nullable enable");
        code.AppendLineAt(0, "using System;");
        code.AppendLineAt(0, "using System.Collections.Generic;");
        code.AppendLineAt(0, string.Empty);
        code.AppendLineAt(0, "namespace " + rootNamespace);
        code.AppendLineAt(0, "{");
        code.AppendLineAt(
            1,
            "/// <summary>Model-independent patch operation kernels (Generated-Once).</summary>"
        );
        code.AppendLineAt(1, "internal static class PatchOperationKernels");
        code.AppendLineAt(1, "{");
        if ((kinds & SparsePatchKernelKind.EmptyIdentities) != 0)
        {
            code.AppendLineAt(
                2,
                "internal static bool IsEmptyTransition(bool beforePresent, bool afterPresent, bool orderChanged) => beforePresent == afterPresent && !orderChanged;"
            );
        }

        if ((kinds & SparsePatchKernelKind.KeyedMembership) != 0)
        {
            code.AppendLineAt(
                2,
                "internal static int IndexOfKey<TKey>(IReadOnlyList<TKey> keys, TKey key, IEqualityComparer<TKey>? comparer = null)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "comparer ??= EqualityComparer<TKey>.Default;");
            code.AppendLineAt(
                3,
                "for (var i = 0; i < keys.Count; i++) if (comparer.Equals(keys[i], key)) return i;"
            );
            code.AppendLineAt(3, "return -1;");
            code.AppendLineAt(2, "}");
        }

        if (
            (kinds & (SparsePatchKernelKind.PathKernels | SparsePatchKernelKind.ConflictPaths)) != 0
        )
        {
            code.AppendLineAt(
                2,
                "internal static string EscapeKeySegment(string keyText) => \"\\\"\" + keyText.Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"\\\"\", \"\\\\\\\"\") + \"\\\"\";"
            );
            code.AppendLineAt(
                2,
                "internal static string FormatKeyedPath(string member, string keyText) => member + \"[\" + EscapeKeySegment(keyText) + \"]\";"
            );
        }

        code.AppendLineAt(1, "}");
        code.AppendLineAt(0, "}");
        code.AppendLine();
        return code.ToString();
    }
}
