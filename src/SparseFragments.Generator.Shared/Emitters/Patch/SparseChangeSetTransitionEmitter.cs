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
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
    )
    {
        if (members.IsDefaultOrEmpty)
            return;
        var runtime = dialect.RuntimeNamespace;
        SparseChangeSetNaming.ComputePublicNames(members, out var propNames, out var transNames);
        // Canonical-state readers and snapshot helpers move into the
        // operation container when relocating; the typed shells below stay.
        var helpers = target?.ChangeSetOperations ?? code;
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
                var keyedBefore =
                    "__sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " && "
                    + KeyedWholeFlag(member)
                    + " ? "
                    + KeyedWholeBefore(member)
                    + " : default)";
                var keyedAfter =
                    "__sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " && "
                    + KeyedWholeFlag(member)
                    + " ? "
                    + KeyedWholeAfter(member)
                    + " : default)";
                if (target is not null)
                {
                    helpers.AppendLineAt(
                        2,
                        "internal static "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.AppendLineAt(2, "{");
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLineAt(3, "return " + SnapshotWrap(member, keyedBefore) + ";");
                    helpers.AppendLineAt(2, "}");
                    helpers.AppendLineAt(
                        2,
                        "internal static "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.AppendLineAt(2, "{");
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLineAt(3, "return " + SnapshotWrap(member, keyedAfter) + ";");
                    helpers.AppendLineAt(2, "}");
                }
                else
                {
                    helpers.AppendLineAt(
                        2,
                        "private "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, keyedBefore)
                            + ";"
                    );
                    helpers.AppendLineAt(
                        2,
                        "private "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, keyedAfter)
                            + ";"
                    );
                }
            }
            else
            {
                var scalarBefore =
                    "__sparse_hasWhole ? (__sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + BeforeField(member)
                    + " : default)";
                var scalarAfter =
                    "__sparse_hasWhole ? (__sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                    + esc
                    + " : default) : ("
                    + HasField(member)
                    + " ? "
                    + AfterField(member)
                    + " : default)";
                if (target is not null)
                {
                    helpers.AppendLineAt(
                        2,
                        "internal static "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.AppendLineAt(2, "{");
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLineAt(3, "return " + SnapshotWrap(member, scalarBefore) + ";");
                    helpers.AppendLineAt(2, "}");
                    helpers.AppendLineAt(
                        2,
                        "internal static "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "(ChangeSet self)"
                    );
                    helpers.AppendLineAt(2, "{");
                    SparseChangeSetBasicsEmitter.AppendMemberStateAliases(helpers, member);
                    helpers.AppendLineAt(3, "return " + SnapshotWrap(member, scalarAfter) + ";");
                    helpers.AppendLineAt(2, "}");
                }
                else
                {
                    helpers.AppendLineAt(
                        2,
                        "private "
                            + opt
                            + " __SparseBefore_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, scalarBefore)
                            + ";"
                    );
                    helpers.AppendLineAt(
                        2,
                        "private "
                            + opt
                            + " __SparseAfter_"
                            + member.Id
                            + "() => "
                            + SnapshotWrap(member, scalarAfter)
                            + ";"
                    );
                }
            }
            AppendSnapshotHelper(helpers, member, opt);
        }
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(propNames[member.Id]);
            if (IsSet(member))
                AppendSetTransition(
                    code,
                    member,
                    prop,
                    transNames[member.Id],
                    runtime,
                    dialect.HashSetImplementsReadOnlySet,
                    target
                );
            else if (IsScalar(member))
                AppendScalarTransition(code, member, prop, transNames[member.Id], runtime, target);
            else if (IsNested(member))
                AppendNestedProperty(code, member, prop, dialect, target);
            else if (IsKeyed(member))
                AppendKeyedTransition(
                    code,
                    member,
                    prop,
                    transNames[member.Id],
                    runtime,
                    dialect,
                    target
                );
            else if (IsDict(member))
                AppendDictTransition(
                    code,
                    member,
                    prop,
                    transNames[member.Id],
                    runtime,
                    dialect,
                    target
                );
        }
    }

    /// <summary>Whether member values need a defensive container snapshot.</summary>
    /// <remarks>
    /// Collection containers are caller-mutable aliases; plain scalars and
    /// nested change sets need no snapshot (issue #170).
    /// </remarks>
    internal static bool NeedsSnapshot(SparseMemberModel member) =>
        !IsNested(member) && member.Collection.CloneKind != SparseCloneCollectionKind.Unsupported;

    private static string SnapshotWrap(SparseMemberModel member, string expression) =>
        NeedsSnapshot(member)
            ? "__SparseSnapshot_" + member.Id + "(" + expression + ")"
            : expression;

    /// <summary>Emits the defensive container snapshot for one member.</summary>
    /// <remarks>
    /// Shallow and comparer-preserving: the container is copied so later
    /// caller-side mutation cannot alter retained history, while element
    /// values stay shared. Shapes without a known copy fall back to the
    /// borrowed reference.
    /// </remarks>
    internal static void AppendSnapshotHelper(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string opt
    )
    {
        if (!NeedsSnapshot(member))
        {
            return;
        }

        var valueType = FragmentValueType(member);
        // A branch is emitted only when its copy is provably assignable to the
        // declared member type: either the exact shape or a known interface it
        // implements. The (VTYPE)(object) cast then always compiles and never
        // fails at runtime; exotic shapes keep the borrowed reference.
        var bareValueType = valueType.TrimEnd('?');
        code.AppendLineAt(
            2,
            "/// <summary>Defensive container snapshot for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(
            2,
            "private static " + opt + " __SparseSnapshot_" + member.Id + "(" + opt + " value)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (!value.IsPresent || (object?)value.Value is null) return value;");
        code.AppendLineAt(3, "var __source" + member.Id + " = (object)value.Value!;");
        if (member.Collection.CloneKind == SparseCloneCollectionKind.Dictionary)
        {
            var keyType = KeyTypeOf(member);
            var elementValueType = ValueTypeOf(member);
            var dictInterfaces = new[]
            {
                "global::System.Collections.Generic.IDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "global::System.Collections.Generic.IReadOnlyDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
            };
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__dict" + member.Id,
                "new global::System.Collections.Generic.Dictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__dict"
                    + member.Id
                    + ", __dict"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.SortedDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__sorted" + member.Id,
                "new global::System.Collections.Generic.SortedDictionary<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__sorted"
                    + member.Id
                    + ", __sorted"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.SortedList<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">",
                "__sortedList" + member.Id,
                "new global::System.Collections.Generic.SortedList<"
                    + keyType
                    + ", "
                    + elementValueType
                    + ">(__sortedList"
                    + member.Id
                    + ", __sortedList"
                    + member.Id
                    + ".Comparer)",
                dictInterfaces
            );
        }
        else if (member.Collection.CloneKind == SparseCloneCollectionKind.Set)
        {
            var elementType = ElementTypeOf(member);
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.HashSet<" + elementType + ">",
                "__set" + member.Id,
                "new global::System.Collections.Generic.HashSet<"
                    + elementType
                    + ">(__set"
                    + member.Id
                    + ", __set"
                    + member.Id
                    + ".Comparer)",
                [
                    "global::System.Collections.Generic.ISet<" + elementType + ">",
                    "global::System.Collections.Generic.IReadOnlySet<" + elementType + ">",
                ]
            );
        }
        else
        {
            var elementType = ElementTypeOf(member);
            var listInterfaces = new[]
            {
                "global::System.Collections.Generic.IList<" + elementType + ">",
                "global::System.Collections.Generic.ICollection<" + elementType + ">",
                "global::System.Collections.Generic.IEnumerable<" + elementType + ">",
                "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">",
                "global::System.Collections.Generic.IReadOnlyCollection<" + elementType + ">",
            };
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.Generic.List<" + elementType + ">",
                "__list" + member.Id,
                "new global::System.Collections.Generic.List<"
                    + elementType
                    + ">(__list"
                    + member.Id
                    + ")",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                elementType + "[]",
                "__array" + member.Id,
                "(" + elementType + "[])__array" + member.Id + ".Clone()",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.ObjectModel.Collection<" + elementType + ">",
                "__collection" + member.Id,
                "new global::System.Collections.ObjectModel.Collection<"
                    + elementType
                    + ">(new global::System.Collections.Generic.List<"
                    + elementType
                    + ">(__collection"
                    + member.Id
                    + "))",
                listInterfaces
            );
            AppendSnapshotBranch(
                code,
                member,
                opt,
                valueType,
                bareValueType,
                "global::System.Collections.ObjectModel.ObservableCollection<" + elementType + ">",
                "__observable" + member.Id,
                "new global::System.Collections.ObjectModel.ObservableCollection<"
                    + elementType
                    + ">(__observable"
                    + member.Id
                    + ")",
                listInterfaces
            );
        }
        code.AppendLineAt(3, "return value;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendSnapshotBranch(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string opt,
        string valueType,
        string bareValueType,
        string sourceType,
        string local,
        string copyExpression,
        IReadOnlyList<string> implementedInterfaces
    )
    {
        if (
            !string.Equals(bareValueType, sourceType, System.StringComparison.Ordinal)
            && !implementedInterfaces.Any(candidate =>
                string.Equals(bareValueType, candidate, System.StringComparison.Ordinal)
            )
        )
        {
            return;
        }

        code.AppendLineAt(3, "if (__source" + member.Id + " is " + sourceType + " " + local + ")");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "return " + opt + ".Present((" + valueType + ")(object)" + copyExpression + ");"
        );
        code.AppendLineAt(3, "}");
    }

    internal static void AppendScalarTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        SparseOperationTarget? target = null
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        code.AppendLineAt(
            2,
            "/// <summary>Typed transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public readonly struct " + trans);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "/// <summary>Gets whether the member changed between the endpoints.</summary>"
        );
        code.AppendLineAt(3, "public bool IsChanged => _isChanged;");
        code.AppendLineAt(3, "/// <summary>Gets the value before the transition.</summary>");
        code.AppendLineAt(3, "public " + opt + " Before => _before;");
        code.AppendLineAt(3, "/// <summary>Gets the value after the transition.</summary>");
        code.AppendLineAt(3, "public " + opt + " After => _after;");
        code.AppendLineAt(
            3,
            "internal " + trans + "(" + opt + " before, " + opt + " after, bool isChanged)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "_before = before;");
        code.AppendLineAt(4, "_after = after;");
        code.AppendLineAt(4, "_isChanged = isChanged;");
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "private readonly " + opt + " _before;");
        code.AppendLineAt(3, "private readonly " + opt + " _after;");
        code.AppendLineAt(3, "private readonly bool _isChanged;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "public "
                    + trans
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLineAt(
                2,
                "/// <summary>Projects the typed transition for one member.</summary>"
            );
            ops.AppendLineAt(
                2,
                "internal static " + trans + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.AppendLineAt(2, "{");
            ops.AppendLineAt(3, "var __b = __SparseBefore_" + member.Id + "(self);");
            ops.AppendLineAt(3, "var __a = __SparseAfter_" + member.Id + "(self);");
            ops.AppendLineAt(
                3,
                "return new "
                    + trans
                    + "(__b, __a, !Fragment.__SparseEqual_"
                    + member.Id
                    + "(__b, __a));"
            );
            ops.AppendLineAt(2, "}");
            return;
        }
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

    internal static void AppendSetTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime
    ) => AppendSetTransition(code, member, prop, trans, runtime, supportsReadOnlySet: true);

    internal static void AppendSetTransition(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        string trans,
        string runtime,
        bool supportsReadOnlySet,
        SparseOperationTarget? target = null
    )
    {
        var opt = runtime + "Optional<" + FragmentValueType(member) + ">";
        var elementType = ElementTypeOf(member);
        var readOnlyList = "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
        code.AppendLineAt(
            2,
            "/// <summary>Typed set transition for member '" + member.Property.Name + "'.</summary>"
        );
        code.AppendLineAt(2, "public sealed class " + trans);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "/// <summary>Whether this set transition contains no semantic changes.</summary>"
        );
        code.AppendLineAt(3, "public bool IsEmpty { get; }");
        code.AppendLineAt(
            3,
            "/// <summary>Whether this set transition contains any semantic changes.</summary>"
        );
        code.AppendLineAt(3, "public bool IsChanged => !IsEmpty;");
        code.AppendLineAt(
            3,
            "/// <summary>The presence-aware set value before the transition.</summary>"
        );
        code.AppendLineAt(3, "public " + opt + " Before { get; }");
        code.AppendLineAt(
            3,
            "/// <summary>The presence-aware set value after the transition.</summary>"
        );
        code.AppendLineAt(3, "public " + opt + " After { get; }");
        code.AppendLineAt(3, "/// <summary>Values present only in the after set.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + " Added { get; }");
        code.AppendLineAt(3, "/// <summary>Values present only in the before set.</summary>");
        code.AppendLineAt(3, "public " + readOnlyList + " Removed { get; }");
        code.AppendLineAt(
            3,
            "internal "
                + trans
                + "("
                + opt
                + " before, "
                + opt
                + " after, bool isEmpty, global::System.Collections.Generic.List<"
                + elementType
                + "> added, global::System.Collections.Generic.List<"
                + elementType
                + "> removed)"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "Before = before;");
        code.AppendLineAt(4, "After = after;");
        code.AppendLineAt(4, "IsEmpty = isEmpty;");
        code.AppendLineAt(4, "Added = added.AsReadOnly();");
        code.AppendLineAt(4, "Removed = removed.AsReadOnly();");
        code.AppendLineAt(3, "}");
        // IReadOnlySet<T> only exists on netstandard2.1 and later. Compilations
        // without the type (netstandard2.0, net48) cannot declare it, and naming
        // it would fail compilation, so fall back to comparer-agnostic search.
        var contains = supportsReadOnlySet
            ? "((object?)values.Value is global::System.Collections.Generic.ISet<"
                + elementType
                + "> set ? set.Contains(value) : (object?)values.Value is global::System.Collections.Generic.IReadOnlySet<"
                + elementType
                + "> readOnly ? readOnly.Contains(value) : global::System.Linq.Enumerable.Contains(values.Value!, value))"
            : "((object?)values.Value is global::System.Collections.Generic.ISet<"
                + elementType
                + "> set ? set.Contains(value) : global::System.Linq.Enumerable.Contains(values.Value!, value))";
        code.AppendLineAt(
            3,
            "internal static bool __Contains("
                + opt
                + " values, "
                + elementType
                + " value) => values.IsPresent && (object?)values.Value is not null && "
                + contains
                + ";"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the typed set transition for member '"
                + member.Property.Name
                + "'.</summary>"
        );
        code.AppendLineAt(2, "[global::System.Text.Json.Serialization.JsonIgnore]");
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "public "
                    + trans
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLineAt(
                2,
                "/// <summary>Projects the typed set transition for one member.</summary>"
            );
            ops.AppendLineAt(
                2,
                "internal static " + trans + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.AppendLineAt(2, "{");
            ops.AppendLineAt(3, "var __b = __SparseBefore_" + member.Id + "(self);");
            ops.AppendLineAt(3, "var __a = __SparseAfter_" + member.Id + "(self);");
            AppendSetProjectionBody(ops, member, trans, elementType);
            ops.AppendLineAt(2, "}");
            return;
        }
        code.AppendLineAt(2, "public " + trans + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __b = __SparseBefore_" + member.Id + "();");
        code.AppendLineAt(4, "var __a = __SparseAfter_" + member.Id + "();");
        AppendSetProjectionBody(code, member, trans, elementType, indent: 4);
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendSetProjectionBody(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string trans,
        string elementType,
        int indent = 3
    )
    {
        code.AppendLineAt(
            indent,
            "var __isEmpty = Fragment.__SparseEqual_" + member.Id + "(__b, __a);"
        );
        code.AppendLineAt(
            indent,
            "var __added = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            indent,
            "if (!__isEmpty && __a.IsPresent && (object?)__a.Value is not null) foreach (var __item in __a.Value!) if (!"
                + trans
                + ".__Contains(__b, __item)) __added.Add(__item);"
        );
        code.AppendLineAt(
            indent,
            "var __removed = new global::System.Collections.Generic.List<" + elementType + ">();"
        );
        code.AppendLineAt(
            indent,
            "if (!__isEmpty && __b.IsPresent && (object?)__b.Value is not null) foreach (var __item in __b.Value!) if (!"
                + trans
                + ".__Contains(__a, __item)) __removed.Add(__item);"
        );
        code.AppendLineAt(
            indent,
            "return new " + trans + "(__b, __a, __isEmpty, __added, __removed);"
        );
    }

    internal static void AppendNestedProperty(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string prop,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null
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
        if (target is not null)
        {
            code.AppendLineAt(
                2,
                "public "
                    + childCs
                    + " "
                    + prop
                    + " => "
                    + target.ChangeSetOperationsType
                    + ".__SparseGet_"
                    + member.Id
                    + "(this);"
            );
            var ops = target.ChangeSetOperations;
            ops.AppendLineAt(
                2,
                "/// <summary>Projects the nested typed change set for one member.</summary>"
            );
            ops.AppendLineAt(
                2,
                "internal static " + childCs + " __SparseGet_" + member.Id + "(ChangeSet self)"
            );
            ops.AppendLineAt(2, "{");
            SparseChangeSetBasicsEmitter.AppendMemberStateAliases(ops, member);
            AppendNestedProjectionBody(ops, member, esc, childCs, runtime, indent: 3);
            ops.AppendLineAt(2, "}");
            return;
        }
        code.AppendLineAt(2, "public " + childCs + " " + prop);
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "get");
        code.AppendLineAt(3, "{");
        AppendNestedProjectionBody(code, member, esc, childCs, runtime, indent: 4);
        code.AppendLineAt(3, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendNestedProjectionBody(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string esc,
        string childCs,
        string runtime,
        int indent
    )
    {
        code.AppendLineAt(indent, "if (__sparse_hasWhole)");
        code.AppendLineAt(indent, "{");
        code.AppendLineAt(
            indent + 1,
            "var __wb = __sparse_wholeBefore.IsPresent && __sparse_wholeBefore.Value is not null ? __sparse_wholeBefore.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(
            indent + 1,
            "var __wa = __sparse_wholeAfter.IsPresent && __sparse_wholeAfter.Value is not null ? __sparse_wholeAfter.Value."
                + esc
                + " : default;"
        );
        code.AppendLineAt(indent + 1, "return " + childCs + ".Between(__wb, __wa);");
        code.AppendLineAt(indent, "}");
        code.AppendLineAt(
            indent,
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
    }
}
