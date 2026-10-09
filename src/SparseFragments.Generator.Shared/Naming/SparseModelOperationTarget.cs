using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Relocation target for per-model Patch and ChangeSet operations.</summary>
/// <remarks>
/// <para>
/// Issue #194 moves model-specific Patch (apply, compose, invert, rebase and
/// nested/keyed/dictionary operations) and ChangeSet (Between, Compose,
/// Invert, Rebase, Apply, EnumerateChanges, projection and restoration)
/// algorithms out of the model-nested facades into external per-model
/// operation containers. Emitters receive this target alongside the facade
/// builder: a null target keeps the legacy single-file emission for
/// downstream dialects without an implementation namespace, while a non-null
/// target writes a thin delegating stub to the facade and the algorithm body
/// to the operation container.
/// </para>
/// <para>
/// Facade state never moves: Patch keeps its typed <c>ref</c> member access
/// and lazy nested identity, and ChangeSet keeps its canonical sparse
/// storage. Operation bodies therefore take the facade as an explicit
/// <c>self</c> parameter and reach state through <c>internal</c> bridges.
/// Bare <c>Patch</c>/<c>ChangeSet</c>/<c>Fragment</c> references inside moved
/// bodies resolve through file-level <c>using</c> aliases emitted by
/// <see cref="SparseModelOperationAliases"/>, so emitter bodies stay
/// product-neutral with no hard-coded product names.
/// </para>
/// </remarks>
internal sealed class SparseOperationTarget
{
    public SparseOperationTarget(
        SharedIndentedBuilder patchOperations,
        SharedIndentedBuilder changeSetOperations,
        string patchOperationsType,
        string changeSetOperationsType
    )
    {
        PatchOperations = patchOperations;
        ChangeSetOperations = changeSetOperations;
        PatchOperationsType = patchOperationsType;
        ChangeSetOperationsType = changeSetOperationsType;
    }

    /// <summary>Builder scoped inside the per-model patch operation container.</summary>
    public SharedIndentedBuilder PatchOperations { get; }

    /// <summary>Builder scoped inside the per-model change-set operation container.</summary>
    public SharedIndentedBuilder ChangeSetOperations { get; }

    /// <summary>Qualified per-model patch operation container type.</summary>
    public string PatchOperationsType { get; }

    /// <summary>Qualified per-model change-set operation container type.</summary>
    public string ChangeSetOperationsType { get; }

    private readonly HashSet<string> _openMembers = new(System.StringComparer.Ordinal);

    /// <summary>Opens the per-member operation class for one collection patch.</summary>
    /// <remarks>
    /// One member's surface, apply, algebra and rebase emitters run
    /// consecutively, so the class is closed by
    /// <see cref="CloseMemberOperations"/> right after that member's
    /// emission finishes. Emission is single-threaded and structured, so the
    /// member bodies write directly into the patch container builder.
    /// </remarks>
    /// <param name="key">Stable per-member key (the member id).</param>
    /// <param name="className">Nested operation class simple name.</param>
    /// <returns>The builder scoped for the member operation bodies.</returns>
    public SharedIndentedBuilder OpenMemberOperations(string key, string className)
    {
        if (!_openMembers.Add(key))
        {
            return PatchOperations;
        }

        PatchOperations.AppendLineAt(
            2,
            "/// <summary>Collection operations for one member patch.</summary>"
        );
        PatchOperations.AppendLineAt(2, "internal static class " + className);
        PatchOperations.AppendLineAt(2, "{");
        return PatchOperations;
    }

    /// <summary>Closes one pending member operation class.</summary>
    /// <param name="key">Stable per-member key (the member id).</param>
    public void CloseMemberOperations(string key)
    {
        if (!_openMembers.Remove(key))
        {
            return;
        }

        PatchOperations.AppendLineAt(2, "}");
    }

    /// <summary>Qualified per-member operation container type for facade stubs.</summary>
    /// <param name="patchOperationsType">Qualified patch operation container type.</param>
    /// <param name="member">Collection member.</param>
    /// <returns>The qualified member operation class.</returns>
    public static string MemberOperationsType(
        string patchOperationsType,
        SparseMemberModel member
    ) =>
        patchOperationsType
        + "."
        + SparseNaming.EscapeIdentifier(member.Property.Name)
        + "PatchOperations";
}

/// <summary>Split emission context for one nested collection patch.</summary>
/// <remarks>
/// A null split keeps legacy single-file emission into one builder. A
/// non-null split writes the thin nested facade (storage, lazy identity and
/// delegating stubs) to <see cref="Shell"/> and the mutator/apply/algebra
/// bodies to <see cref="Operations"/>, reached through <see cref="Receiver"/>
/// (<c>self.</c>). Bodies keep bare sibling references; facade type names
/// resolve through the implementation-file aliases.
/// </remarks>
internal sealed record SparseMemberOperationSplit(
    SharedIndentedBuilder Shell,
    SharedIndentedBuilder Operations,
    string OperationsType,
    string Receiver
);

/// <summary>File-level aliases letting relocated bodies reuse bare facade type names.</summary>
/// <remarks>
/// Relocated Patch/ChangeSet bodies name <c>Patch</c>, <c>ChangeSet</c>,
/// <c>Fragment</c>, nested collection patches, transition types and payload
/// envelopes without qualification. The implementation file aliases each of
/// those to the model-facing facade so emitter bodies move without
/// product-specific qualification.
/// </remarks>
internal static class SparseModelOperationAliases
{
    /// <summary>Collects the alias directives for one model's implementation file.</summary>
    /// <param name="modelType">Qualified model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="dialect">Product patch dialect.</param>
    /// <param name="plan">Effective emission features.</param>
    /// <param name="canApplyPatchInPlace">Whether the patch in-place result exists.</param>
    /// <returns>Alias pairs of simple name to qualified facade type.</returns>
    public static ImmutableArray<KeyValuePair<string, string>> Collect(
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseEmissionFeatures plan,
        bool canApplyPatchInPlace
    )
    {
        var aliases = new List<KeyValuePair<string, string>>
        {
            new("Patch", modelType + ".Patch"),
            new("ChangeSet", modelType + ".ChangeSet"),
            new("Fragment", modelType + ".Fragment"),
        };
        if (plan.EmitChangePayload)
        {
            aliases.Add(
                new KeyValuePair<string, string>("ChangePayload", modelType + ".ChangePayload")
            );
            var container = PayloadContainerName(dialect, modelType);
            if (container is not null)
            {
                aliases.Add(
                    new KeyValuePair<string, string>(container, modelType + "." + container)
                );
                // Nested and element payload envelopes are referenced bare by
                // relocated export and partition bodies.
                foreach (var owner in PayloadOwnerNames(members))
                {
                    if (string.Equals(owner, modelType, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var childContainer = PayloadContainerName(dialect, owner);
                    if (
                        childContainer is not null
                        && !string.Equals(
                            childContainer,
                            container,
                            System.StringComparison.Ordinal
                        )
                    )
                    {
                        aliases.Add(
                            new KeyValuePair<string, string>(
                                childContainer,
                                owner + "." + childContainer
                            )
                        );
                    }
                }
            }
        }
        if (canApplyPatchInPlace)
        {
            aliases.Add(
                new KeyValuePair<string, string>(
                    "ApplyInPlaceResult",
                    modelType + ".Patch.ApplyInPlaceResult"
                )
            );
        }
        if (plan.EmitChangeSet)
        {
            aliases.Add(
                new KeyValuePair<string, string>("ChangeKind", modelType + ".ChangeSet.ChangeKind")
            );
            aliases.Add(
                new KeyValuePair<string, string>("ChangeInfo", modelType + ".ChangeSet.ChangeInfo")
            );
        }
        foreach (var member in members.Where(SparseKeyedCollectionEmitter.IsCollectionPatch))
        {
            var patch = SparseKeyedCollectionEmitter.CollectionPatchName(member);
            aliases.Add(new KeyValuePair<string, string>(patch, modelType + ".Patch." + patch));
        }
        SparseChangeSetNaming.ComputePublicNames(members, out _, out var transNames);
        foreach (var name in transNames.Select(static pair => pair.Value))
        {
            aliases.Add(new KeyValuePair<string, string>(name, modelType + ".ChangeSet." + name));
        }
        return aliases.ToImmutableArray();
    }

    /// <summary>Emits the alias directives ahead of the implementation namespace.</summary>
    /// <param name="code">File builder.</param>
    /// <param name="aliases">Alias pairs from <see cref="Collect"/>.</param>
    public static void Append(
        SharedIndentedBuilder code,
        ImmutableArray<KeyValuePair<string, string>> aliases
    )
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var alias in aliases)
        {
            if (!seen.Add(alias.Key))
            {
                continue;
            }

            code.AppendLine("using " + alias.Key + " = global::" + TrimGlobal(alias.Value) + ";");
        }
    }

    private static string? PayloadContainerName(
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string? modelType
    )
    {
        if (modelType is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(dialect.PayloadImplementationContainerPrefix))
        {
            return null;
        }

        return SparseChangeSetPayloadEmitter.RequirePayloadContainerName(dialect, modelType);
    }

    private static ImmutableArray<string> PayloadOwnerNames(
        ImmutableArray<SparseMemberModel> members
    ) =>
        members
            .SelectMany(static member =>
                new[]
                {
                    member.ChildModel?.IsFragmentModel == true
                        ? member.ChildModel.Value.NonNullableName
                        : string.Empty,
                    member.Collection.ElementType.IsFragmentModel
                        ? member.Collection.ElementType.NonNullableName
                        : string.Empty,
                    member.Collection.ValueType?.IsFragmentModel == true
                        ? member.Collection.ValueType.Value.NonNullableName
                        : string.Empty,
                }
            )
            .Where(static owner => owner.Length > 0)
            .Distinct(System.StringComparer.Ordinal)
            .ToImmutableArray();

    private static string TrimGlobal(string qualified) =>
        qualified.StartsWith("global::", System.StringComparison.Ordinal)
            ? qualified.Substring("global::".Length)
            : qualified;
}
