using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Compares model members that need no projection or cloning.</summary>
internal static class SparseChangeSetModelBetweenEmitter
{
    internal static bool CanCompareDirectly(ImmutableArray<SparseMemberModel> members) =>
        members.All(static member =>
            IsScalar(member)
            && !IsSet(member)
            && !SparseChangeSetTransitionEmitter.NeedsSnapshot(member)
            && member.Property.Type.PocoCloneHelperName is null
        );

    internal static void AppendBody(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType,
        string runtime,
        string optionalFragment
    )
    {
        // Preserve From's null validation without boxing value-type models.
        code.AppendLineAt(
            3,
            "if (!typeof("
                + modelType
                + ").IsValueType && (global::System.Object.ReferenceEquals(before, null) || global::System.Object.ReferenceEquals(after, null)))"
        );
        code.AppendLineAt(4, "throw new global::System.ArgumentNullException(\"value\");");
        foreach (var side in new[] { "before", "after" })
        {
            foreach (var member in members)
            {
                code.AppendLineAt(
                    3,
                    "var __"
                        + side
                        + member.Id
                        + " = "
                        + runtime
                        + "Optional<"
                        + FragmentValueType(member)
                        + ">.Present("
                        + side
                        + "."
                        + SparseNaming.EscapeIdentifier(member.Property.Name)
                        + ");"
                );
            }
        }
        var conditions = new List<string>();
        var args = new List<string> { "false", "default", "default" };
        foreach (var id in members.Select(static member => member.Id))
        {
            code.AppendLineAt(
                3,
                "bool __h"
                    + id
                    + " = !Fragment.__SparseEqual_"
                    + id
                    + "(__before"
                    + id
                    + ", __after"
                    + id
                    + ");"
            );
            conditions.Add("!__h" + id);
            args.Add("__h" + id + " ? __before" + id + " : default");
            args.Add("__h" + id + " ? __after" + id + " : default");
            args.Add("__h" + id);
        }
        if (conditions.Count > 0)
            code.AppendLineAt(3, "if (" + string.Join(" && ", conditions) + ")");
        code.AppendLineAt(
            conditions.Count > 0 ? 4 : 3,
            "return Between(default(" + optionalFragment + "), default(" + optionalFragment + "));"
        );
        if (conditions.Count > 0)
            code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", args) + ");");
    }

    internal static void AppendProjectedReturn(
        SharedIndentedBuilder code,
        string optionalFragment,
        int indent
    ) =>
        code.AppendLineAt(
            indent,
            "return Between("
                + optionalFragment
                + ".Present(Fragment.From(before)), "
                + optionalFragment
                + ".Present(Fragment.From(after)));"
        );
}
