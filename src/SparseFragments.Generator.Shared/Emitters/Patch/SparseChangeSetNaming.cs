using System.Collections.Generic;
using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Central reserved-name resolver for ChangeSet public surfaces.</summary>
/// <remarks>
/// Typed transitions, enumeration helpers, and path helpers must share one name
/// map. Separate reserved sets let a source member such as
/// <c>EnumerateChanges</c> collide with a generated method on one surface but
/// not the other, producing uncompilable output.
/// </remarks>
internal static class SparseChangeSetNaming
{
    /// <summary>Names reserved on the generated <c>ChangeSet</c> public surface.</summary>
    public static HashSet<string> CreateReservedSet() =>
        new(System.StringComparer.Ordinal)
        {
            "IsEmpty",
            "Between",
            "FromPatch",
            "ToPatch",
            "Invert",
            "Compose",
            "RebaseOnto",
            "ApplyTo",
            "TryApplyTo",
            "ApplyToBaseline",
            "ChangeInfo",
            "ChangeKind",
            "EnumerateChanges",
            "EnumerateChangedPaths",
            "__SparseBox",
            "__SparseCreateChangeInfo",
            "__SparseKeyPath",
        };

    /// <summary>Computes stable public property and transition type names.</summary>
    /// <param name="members">Model members in emission order.</param>
    /// <param name="propNames">Public property name per member id.</param>
    /// <param name="transNames">Transition type name per non-nested member id.</param>
    public static void ComputePublicNames(
        ImmutableArray<SparseMemberModel> members,
        out Dictionary<int, string> propNames,
        out Dictionary<int, string> transNames
    )
    {
        var usedProps = CreateReservedSet();
        propNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            var prefix = new System.Text.StringBuilder();
            while (usedProps.Contains(prefix.ToString() + member.Property.Name))
            {
                prefix.Append("Sparse");
            }

            var candidate = prefix.ToString() + member.Property.Name;
            usedProps.Add(candidate);
            propNames[member.Id] = candidate;
        }

        var usedTypes = new HashSet<string>(usedProps, System.StringComparer.Ordinal);
        transNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            if (SparseChangeSetBasicsEmitter.IsNested(member))
            {
                continue;
            }

            var prefix = new System.Text.StringBuilder();
            while (usedTypes.Contains(prefix.ToString() + propNames[member.Id] + "Transition"))
            {
                prefix.Append("Sparse");
            }

            var candidate = prefix.ToString() + propNames[member.Id] + "Transition";
            usedTypes.Add(candidate);
            transNames[member.Id] = candidate;
        }
    }
}
