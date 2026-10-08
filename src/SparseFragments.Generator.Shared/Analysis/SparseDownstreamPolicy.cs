using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SparseFragments.Generator.Shared;

/// <summary>Transport disclosure for one member.</summary>
/// <remarks>
/// Redaction is a transport policy: in-memory <c>ChangeSet</c> values stay
/// complete and baseline-aware, while the payload omits the undisclosed
/// before-state. A redacted payload never reconstructs a <c>ChangeSet</c>.
/// </remarks>
internal enum SparseMemberTransport
{
    /// <summary>Before and after states travel together.</summary>
    Full = 0,

    /// <summary>The before-state is undisclosed in transport; after is required.</summary>
    RedactedBefore = 1,

    /// <summary>Command-only member, undisclosed before and skipped in read projections.</summary>
    WriteOnly = 2,
}

/// <summary>Generator-time transport choice for one member, matched by member name.</summary>
internal sealed record SparseMemberPolicy
{
    public SparseMemberPolicy(
        string MemberName,
        SparseMemberTransport Transport = SparseMemberTransport.Full
    )
    {
        this.MemberName = MemberName;
        this.Transport = Transport;
    }

    /// <summary>Source member name the policy applies to (ordinal match).</summary>
    public string MemberName { get; init; }

    /// <summary>Disclosure for the member.</summary>
    public SparseMemberTransport Transport { get; init; }
}

/// <summary>How a redacted-before operation projects to baseline-free operations.</summary>
/// <remarks>
/// Passthrough applies the requested after-state without historical comparison,
/// as for an explicit patch set. Strict failure refuses instead. This hook is
/// intentionally narrow so the rebase-policy follow-up can extend it.
/// </remarks>
internal enum SparseRedactedBeforeBehavior
{
    /// <summary>Project the requested after-state without historical comparison.</summary>
    Passthrough = 0,

    /// <summary>Refuse to project a redacted-before operation.</summary>
    StrictFail = 1,
}

/// <summary>Generator-time rebase behavior for redacted-before operations.</summary>
internal sealed record SparseRebasePolicy
{
    public SparseRebasePolicy(
        SparseRedactedBeforeBehavior RedactedBefore = SparseRedactedBeforeBehavior.Passthrough
    )
    {
        this.RedactedBefore = RedactedBefore;
    }

    /// <summary>Projection behavior for redacted-before operations.</summary>
    public SparseRedactedBeforeBehavior RedactedBefore { get; init; }

    /// <summary>Default policy: redacted-before operations pass through.</summary>
    public static SparseRebasePolicy Passthrough { get; } = new SparseRebasePolicy();
}

/// <summary>Name-level mapping from one read member to one write-command member.</summary>
/// <remarks>
/// Only names are mapped; value conversion and domain mapping stay downstream.
/// </remarks>
internal sealed record SparseWriteMember
{
    public SparseWriteMember(string ReadMemberName, string WriteMemberName)
    {
        this.ReadMemberName = ReadMemberName;
        this.WriteMemberName = WriteMemberName;
    }

    /// <summary>Source member name on the read model.</summary>
    public string ReadMemberName { get; init; }

    /// <summary>Target member name on the write model.</summary>
    public string WriteMemberName { get; init; }
}

/// <summary>Write command targeted separately from the read projection.</summary>
/// <remarks>
/// Shared never assumes the read and write shapes are the same CLR model.
/// It projects sparse state into an existing write-command instance by mapped
/// member names; construction and domain mapping stay downstream.
/// </remarks>
internal sealed record SparseWriteContract
{
    public SparseWriteContract(
        string WriteModelType,
        ImmutableArray<SparseWriteMember> Members = default
    )
    {
        this.WriteModelType = WriteModelType;
        this.Members = Members;
    }

    /// <summary>Fully qualified write-command type name.</summary>
    public string WriteModelType { get; init; }

    /// <summary>Read-to-write member mappings; unmapped members keep their name.</summary>
    public ImmutableArray<SparseWriteMember> Members { get; init; }

    /// <summary>Mapped members, or empty when no overrides are configured.</summary>
    public ImmutableArray<SparseWriteMember> EffectiveMembers =>
        Members.IsDefault ? ImmutableArray<SparseWriteMember>.Empty : Members;

    /// <summary>Write member name for one read member.</summary>
    public string GetWriteMemberName(string readMemberName) =>
        EffectiveMembers
            .Where(mapping =>
                string.Equals(mapping.ReadMemberName, readMemberName, StringComparison.Ordinal)
            )
            .Select(static mapping => mapping.WriteMemberName)
            .FirstOrDefault()
        ?? readMemberName;
}

/// <summary>Validation for the downstream emission plan.</summary>
/// <remarks>
/// Exposes plan <em>results</em>; diagnostics keep generator-specific IDs and
/// are owned by each generator through <see cref="SparseGeneratorConfig"/>.
/// </remarks>
internal static class SparseDownstreamPolicy
{
    /// <summary>Whether a member can carry a non-full transport policy.</summary>
    /// <remarks>
    /// Only scalar members qualify: nested members recurse through the child
    /// model's own member names, and keyed or dictionary members keep full
    /// disclosure until the mixed-operation follow-up defines their shape.
    /// </remarks>
    public static bool IsScalarMember(SparseMemberModel member) =>
        member.ChildModel is null && !SparseFragmentPatchEmitter.IsCollectionPatch(member);

    /// <summary>Transport configured for one member name.</summary>
    public static SparseMemberTransport GetTransport(
        ImmutableArray<SparseMemberPolicy> policies,
        string memberName
    ) =>
        policies
            .Where(policy => string.Equals(policy.MemberName, memberName, StringComparison.Ordinal))
            .Select(static policy => policy.Transport)
            .FirstOrDefault();

    /// <summary>Whether the dialect carries any non-full member policy.</summary>
    public static bool HasAnyNonFullPolicy(SparseFragmentPatchEmitter.SparsePatchDialect dialect) =>
        dialect.EffectiveMemberPolicies.Any(static policy =>
            policy.Transport != SparseMemberTransport.Full
        );

    /// <summary>Whether this model's members carry any non-full policy.</summary>
    public static bool HasOwnNonFullPolicy(
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var policies = dialect.EffectiveMemberPolicies;
        if (policies.IsEmpty)
            return false;
        return members.Any(member =>
            GetTransport(policies, member.Property.Name) != SparseMemberTransport.Full
        );
    }

    /// <summary>Policy and mapping names matching no analyzed member.</summary>
    public static ImmutableArray<string> FindUnknownMemberNames(
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var known = new HashSet<string>(
            members.Select(static member => member.Property.Name),
            StringComparer.Ordinal
        );
        var candidates = dialect.EffectiveMemberPolicies.Select(static policy => policy.MemberName);
        var contract = dialect.WriteContract;
        if (contract is not null)
            candidates = candidates.Concat(
                contract.EffectiveMembers.Select(static mapping => mapping.ReadMemberName)
            );
        return candidates
            .Where(candidate => !known.Contains(candidate))
            .Distinct()
            .ToImmutableArray();
    }

    /// <summary>Non-full policies on members that cannot carry them.</summary>
    public static ImmutableArray<string> FindNonScalarPolicyMembers(
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var policies = dialect.EffectiveMemberPolicies;
        if (policies.IsEmpty)
            return ImmutableArray<string>.Empty;
        return members
            .Where(member =>
                !IsScalarMember(member)
                && GetTransport(policies, member.Property.Name) != SparseMemberTransport.Full
            )
            .Select(static member => member.Property.Name)
            .ToImmutableArray();
    }

    /// <summary>Fails fast on plan errors for direct emitter calls.</summary>
    public static void ThrowOnInvalidTransport(
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var unknown = FindUnknownMemberNames(members, dialect);
        if (!unknown.IsDefaultOrEmpty && unknown.Length > 0)
            throw new ArgumentException(
                "A product policy references unknown member '" + unknown[0] + "'.",
                nameof(dialect)
            );
        var offenders = FindNonScalarPolicyMembers(members, dialect);
        if (!offenders.IsDefaultOrEmpty && offenders.Length > 0)
            throw new ArgumentException(
                "Member '" + offenders[0] + "' cannot carry a redacted or write-only policy.",
                nameof(dialect)
            );
    }

    /// <summary>Validates the emission plan during analysis.</summary>
    /// <remarks>
    /// Checks feature dependencies, unknown policy names, non-scalar policies,
    /// and generated-name collisions covering emitted families and declared
    /// product names. Standalone defaults pass without extra configuration.
    /// </remarks>
    public static void ValidateEmissionPlan(
        ImmutableArray<SparseMemberModel> members,
        SparseGeneratorConfig config,
        ImmutableArray<SparseGeneratorDiagnostic>.Builder diagnostics,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = config.EffectiveDiagnosticIds;
        foreach (var error in config.EffectiveEmissionFeatures.ValidateDependencies())
        {
            cancellationToken.ThrowIfCancellationRequested();
            diagnostics.Add(new SparseGeneratorDiagnostic(ids.InvalidEmissionPlan, null, error));
        }

        var dialect = config.PatchDialect;
        if (dialect.HasValue)
        {
            foreach (var name in FindUnknownMemberNames(members, dialect.Value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(ids.UnknownProductMember, null, name)
                );
            }

            foreach (var name in FindNonScalarPolicyMembers(members, dialect.Value))
            {
                cancellationToken.ThrowIfCancellationRequested();
                diagnostics.Add(
                    new SparseGeneratorDiagnostic(
                        ids.InvalidEmissionPlan,
                        null,
                        "Member '" + name + "' cannot carry a redacted or write-only policy."
                    )
                );
            }
        }

        // Member collisions keep the historical reserved set and stay in the
        // analyzer: names such as Observable are renamed gracefully during
        // emission. Only product declarations are checked here, against the
        // reserved names plus the emitted families they would shadow.
        var productNames = config.EffectiveProductExtensionNames;
        var productCollision = productNames
            .Where(product => IsGeneratedName(product, config))
            .FirstOrDefault();
        if (productCollision is not null)
        {
            diagnostics.Add(
                new SparseGeneratorDiagnostic(ids.GeneratedNameCollision, null, productCollision)
            );
        }
    }

    private static bool IsGeneratedName(string name, SparseGeneratorConfig config) =>
        config
            .EffectiveEmissionFeatures.GetEmittedTypeNames()
            .Any(emitted => string.Equals(emitted, name, StringComparison.Ordinal))
        || config.EffectiveReservedGeneratedNames.Any(reserved =>
            string.Equals(reserved, name, StringComparison.Ordinal)
        );
}
