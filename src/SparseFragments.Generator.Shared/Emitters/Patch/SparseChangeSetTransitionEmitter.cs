using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetDictTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedBetweenEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedComposeEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedRebaseEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetKeyedTransitionEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetMatchEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetPatchSyncEmitter;
using static SparseFragments.Generator.Shared.SparseChangeSetRebaseEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Typed surface (scalar/nested transitions).</summary>
internal static class SparseChangeSetTransitionEmitter
{
    internal static void AppendTypedSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (members.IsDefaultOrEmpty)
            return;
        var runtime = dialect.RuntimeNamespace;
        var reserved = new HashSet<string>(System.StringComparer.Ordinal)
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
        };
        var usedProps = new HashSet<string>(reserved, System.StringComparer.Ordinal);
        var propNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            var prefix = new System.Text.StringBuilder();
            while (usedProps.Contains(prefix.ToString() + member.Property.Name))
                prefix.Append("Sparse");
            var candidate = prefix.ToString() + member.Property.Name;
            usedProps.Add(candidate);
            propNames[member.Id] = candidate;
        }
        var usedTypes = new HashSet<string>(usedProps, System.StringComparer.Ordinal);
        var transNames = new Dictionary<int, string>();
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var prefix = new System.Text.StringBuilder();
            while (usedTypes.Contains(prefix.ToString() + propNames[member.Id] + "Transition"))
                prefix.Append("Sparse");
            var t = prefix.ToString() + propNames[member.Id] + "Transition";
            usedTypes.Add(t);
            transNames[member.Id] = t;
        }
        // Sparse before/after helpers read canonical sparse storage.
        // Whole-root transitions project member states from the retained root
        // fragments; memberwise transitions expose only retained changed paths.
        // Keyed/dict granular transitions retain no full member snapshots: the
        // member Before/After project whole endpoints only for whole presence
        // transitions, and Missing otherwise.
        foreach (var member in members)
        {
            if (IsNested(member))
                continue;
            var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
            var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (IsKeyed(member) || IsDict(member))
            {
                code.AppendLineAt(
                    2,
                    "private "
                        + opt
                        + " __SparseBefore_"
                        + member.Id
                        + "() => __sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                        + esc
                        + " : default) : ("
                        + HasField(member)
                        + " && "
                        + KeyedWholeFlag(member)
                        + " ? "
                        + KeyedWholeBefore(member)
                        + " : default);"
                );
                code.AppendLineAt(
                    2,
                    "private "
                        + opt
                        + " __SparseAfter_"
                        + member.Id
                        + "() => __sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                        + esc
                        + " : default) : ("
                        + HasField(member)
                        + " && "
                        + KeyedWholeFlag(member)
                        + " ? "
                        + KeyedWholeAfter(member)
                        + " : default);"
                );
            }
            else
            {
                code.AppendLineAt(
                    2,
                    "private "
                        + opt
                        + " __SparseBefore_"
                        + member.Id
                        + "() => __sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                        + esc
                        + " : default) : ("
                        + HasField(member)
                        + " ? "
                        + BeforeField(member)
                        + " : default);"
                );
                code.AppendLineAt(
                    2,
                    "private "
                        + opt
                        + " __SparseAfter_"
                        + member.Id
                        + "() => __sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                        + esc
                        + " : default) : ("
                        + HasField(member)
                        + " ? "
                        + AfterField(member)
                        + " : default);"
                );
            }
        }
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            if (IsScalar(member))
                AppendScalarTransition(code, member, prop, transNames[member.Id], runtime);
            else if (IsNested(member))
                AppendNestedProperty(code, member, prop, dialect);
            else if (IsKeyed(member))
                AppendKeyedTransition(code, member, prop, transNames[member.Id], runtime, dialect);
            else if (IsDict(member))
                AppendDictTransition(code, member, prop, transNames[member.Id], runtime, dialect);
        }
    }

    internal static void AppendScalarTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        code.AppendLineAt(
            2,
            "/// <summary>Typed transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public readonly struct " + trans);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "private readonly " + opt + " _before;");
        code.AppendLineAt(3, "private readonly " + opt + " _after;");
        code.AppendLineAt(3, "private readonly bool _isChanged;");
        code.AppendLineAt(
            3,
            "internal " + trans + "(" + opt + " before, " + opt + " after, bool isChanged)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_before = before;");
        code.AppendLineAt(4, "_after = after;");
        code.AppendLineAt(4, "_isChanged = isChanged;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "public bool IsChanged => _isChanged;");
        code.AppendLineAt(3, "public " + opt + " Before => _before;");
        code.AppendLineAt(3, "public " + opt + " After => _after;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(2, "public " + trans + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __b = __SparseBefore_" + member.Id + "();");
        code.AppendLineAt(4, "var __a = __SparseAfter_" + member.Id + "();");
        code.AppendLineAt(
            4,
            "return new "
                + trans
                + "(__b, __a, !Fragment.__SparseEqual_"
                + member.Id
                + "(__b, __a));"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    internal static void AppendNestedProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var childCs = ChildChangeSet(member, dialect);
        var runtime = dialect.RuntimeNamespace;
        var esc = SparseNaming.EscapeIdentifier(member.Property.Name);
        code.AppendLineAt(
            2,
            "/// <summary>Gets the nested typed change set for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        code.AppendLineAt(2, "public " + childCs + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "if (__sparse_hasWhole)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "var __wb = __sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(
            5,
            "var __wa = __sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(5, "return " + childCs + ".Between(__wb, __wa);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "return "
                + NestedField(member)
                + " ?? "
                + childCs
                + ".Between("
                + runtime
                + "Optional<"
                + member.ChildFragmentType
                + "?>.Missing, "
                + runtime
                + "Optional<"
                + member.ChildFragmentType
                + "?>.Missing);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }
}
