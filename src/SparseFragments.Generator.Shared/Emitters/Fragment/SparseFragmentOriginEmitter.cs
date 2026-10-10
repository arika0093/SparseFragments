using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits optional fragment origin attribution: construction state, merge propagation and effective-value queries.</summary>
/// <remarks>
/// Origins are explanatory metadata, not semantic state. The emitter stays
/// dormant unless the owning generator opts into <c>EmitFragmentOrigins</c>;
/// downstream runtimes without provenance support therefore never see these
/// references. All runtime type names resolve through the configured runtime
/// namespace, the configured collection-merger facade and the configured
/// path type, never a fixed product reference.
/// </remarks>
internal sealed class SparseFragmentOriginEmitter(
    string optional,
    string runtimeNamespace,
    string collectionMerger,
    string pathType
)
{
    private string Optional { get; } = optional;
    private string RuntimeNamespace { get; } = runtimeNamespace;
    private string CollectionMerger { get; } = collectionMerger;
    private string PathType { get; } = pathType;

    private string SparsePathType => PathType;

    private string OriginEntryType => RuntimeNamespace + "FragmentOriginEntry";

    private string OriginGroupType => RuntimeNamespace + "FragmentOriginGroup<Fragment>";

    /// <summary>Creates the origin emitter when fragment origins are enabled.</summary>
    /// <remarks>
    /// Origins are product-opt-in: only generators whose runtime carries the
    /// provenance contracts enable them, so the references never leak into
    /// downstream dialects. The path type resolves through the patch dialect
    /// so typed paths bind without per-model holders.
    /// </remarks>
    /// <param name="features">The selected emission families.</param>
    /// <param name="runtime">The configured runtime names.</param>
    /// <param name="patchDialect">The configured patch names.</param>
    public static SparseFragmentOriginEmitter? TryCreate(
        SparseEmissionFeatures features,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect
    ) =>
        features.EmitFragmentOrigins
            ? new SparseFragmentOriginEmitter(
                runtime.OptionalType,
                runtime.Namespace,
                runtime.CollectionMerger,
                SparseFragmentPatchEmitter.GetPathType(patchDialect)
            )
            : null;

    /// <summary>Whether a member merges through a custom strategy (origin falls back to Unknown).</summary>
    internal static bool IsCustomMerge(SparseMemberModel member) =>
        member.MergeStrategyType is not null;

    /// <summary>Whether a member deep-merges nested values.</summary>
    internal static bool IsDeepMerge(SparseMemberModel member) =>
        SparseMergeModes.IsDeepMerge(member.MergeMode, member.ChildModel is not null);

    /// <summary>Whether a member merges collections with append or set-union semantics.</summary>
    internal static bool IsCollectionMerge(SparseMemberModel member) =>
        !IsCustomMerge(member)
        && member.MergeMode is SparseMergeModes.Append or SparseMergeModes.SetUnion;

    /// <summary>Whether origin queries recurse into a nested fragment value.</summary>
    internal static bool IsFragmentDeep(SparseMemberModel member) =>
        IsDeepMerge(member) && !member.ChildIsStructural && member.ChildFragmentType is not null;

    /// <summary>Whether a member tracks per-element origins (append/set-union sequences and sets).</summary>
    /// <remarks>Keyed sequences, dictionaries and whole-value replacements attribute the whole value.</remarks>
    internal static bool HasElementOrigins(SparseMemberModel member) =>
        IsCollectionMerge(member)
        && member.Collection.Kind
            is not SparseCollectionKind.Unsupported
                and not SparseCollectionKind.MutableList
        && !member.Collection.IsDictionary
        && !member.Collection.IsKeyedSequence;

    /// <summary>Emits the fragment-wide origin property and the origin constructor.</summary>
    /// <remarks>Storage follows separately via <c>AppendOriginStorage</c> so the
    /// public surface stays ahead of internal members.</remarks>
    public static void AppendOriginState(SharedIndentedBuilder code)
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "/// <summary>The fragment-wide default attribution. Null means Unknown.</summary>"
        );
        code.AppendLineAt(2, "public string? Origin { get; }");
        code.AppendLine();
        code.AppendLineAt(
            2,
            "/// <summary>Creates a fragment attributing its values to an origin. Null means Unknown.</summary>"
        );
        code.AppendLineAt(2, "public Fragment(string? origin)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "Origin = origin;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    /// <summary>Emits the internal per-member and per-element attribution storage.</summary>
    /// <remarks>Emitted with the internal caches so no public member trails it.</remarks>
    public static void AppendOriginStorage(SharedIndentedBuilder code)
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(2, "internal string?[]? __SparseMemberOrigins { get; init; }");
        code.AppendLineAt(2, "internal string?[]?[]? __SparseElementOrigins { get; init; }");
        code.AppendLine();
    }

    /// <summary>Emits the origin query surface: facades over the operations class, or inline bodies without one.</summary>
    public void AppendOriginSurface(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType,
        string? operationsType = null
    )
    {
        if (operationsType is not null)
        {
            AppendQueryFacades(code, operationsType);
            return;
        }

        AppendQueryBodies(code, members, modelType, isStatic: false, receiver: "this.");
    }

    /// <summary>Emits the origin query implementations for the per-model operations class.</summary>
    public void AppendOriginOperations(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType
    ) => AppendQueryBodies(code, members, modelType, isStatic: true, receiver: "self.");

    /// <summary>Builds the merged member-origin expression for <c>Merge</c>.</summary>
    /// <remarks>Higher-priority presence wins; a value merged from both sides stays member-level Unknown.
    /// Fallbacks carry the enclosing fragment default into nested merges; explicit
    /// attributions always win over them.</remarks>
    public static string BuildMemberOrigin(
        SparseMemberModel member,
        string lowerSide,
        string higherSide,
        int position,
        string? lowerFallback,
        string? higherFallback
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var lowerAttribution = MemberAttribution(lowerSide, position, lowerFallback);
        var higherAttribution = MemberAttribution(higherSide, position, higherFallback);
        if (IsCustomMerge(member))
        {
            // Custom strategies carry no provenance contract.
            return "null";
        }

        if (IsDeepMerge(member) || IsCollectionMerge(member))
        {
            return "(!"
                + higherSide
                + name
                + ".IsPresent ? "
                + lowerAttribution
                + " : (!"
                + lowerSide
                + name
                + ".IsPresent ? "
                + higherAttribution
                + " : ((object?)"
                + higherSide
                + name
                + ".Value is null ? "
                + higherAttribution
                + " : ((object?)"
                + lowerSide
                + name
                + ".Value is null ? "
                + higherAttribution
                + " : null))))";
        }

        return "("
            + higherSide
            + name
            + ".IsPresent ? "
            + higherAttribution
            + " : "
            + lowerAttribution
            + ")";
    }

    /// <summary>Builds the merged element-origin expression for an append/set-union member.</summary>
    public string BuildElementOrigins(
        SparseMemberModel member,
        string lowerSide,
        string higherSide,
        int position,
        string mergedValue,
        string? lowerFallback,
        string? higherFallback
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var lowerValue = lowerSide + name + ".Value!";
        var higherValue = higherSide + name + ".Value!";
        var lowerAttribution = MemberAttribution(lowerSide, position, lowerFallback);
        var higherAttribution = MemberAttribution(higherSide, position, higherFallback);
        var lowerElements = lowerSide + "__SparseElementOrigins?[" + position + "]";
        var higherElements = higherSide + "__SparseElementOrigins?[" + position + "]";
        var element = member.Collection.ElementType.Name;
        string call;
        if (member.Collection.Kind == SparseCollectionKind.Set)
        {
            call =
                CollectionMerger
                + ".MergeSetOrigins<"
                + element
                + ">("
                + lowerValue
                + ", "
                + lowerElements
                + ", "
                + lowerAttribution
                + ", "
                + higherValue
                + ", "
                + higherElements
                + ", "
                + higherAttribution
                + ", "
                + mergedValue
                + ")";
        }
        else if (member.MergeMode == SparseMergeModes.SetUnion)
        {
            call =
                CollectionMerger
                + ".MergeSetUnionOrigins<"
                + element
                + ">("
                + lowerValue
                + ", "
                + lowerElements
                + ", "
                + lowerAttribution
                + ", "
                + higherValue
                + ", "
                + higherElements
                + ", "
                + higherAttribution
                + ", null)";
        }
        else
        {
            call =
                CollectionMerger
                + ".MergeAppendOrigins<"
                + element
                + ">("
                + lowerValue
                + ", "
                + lowerElements
                + ", "
                + lowerAttribution
                + ", "
                + higherValue
                + ", "
                + higherElements
                + ", "
                + higherAttribution
                + ")";
        }

        return "("
            + lowerSide
            + name
            + ".IsPresent && (object?)"
            + lowerValue
            + " is not null && "
            + higherSide
            + name
            + ".IsPresent && (object?)"
            + higherValue
            + " is not null ? "
            + call
            + " : null)";
    }

    /// <summary>Builds the member-origin expression for <c>ApplyChanges</c>.</summary>
    public static string BuildApplyOrigin(
        SparseMemberModel member,
        string selfSide,
        string changesSide,
        int position,
        string? selfFallback,
        string? changesFallback
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var selfAttribution = MemberAttribution(selfSide, position, selfFallback);
        var changesAttribution = MemberAttribution(changesSide, position, changesFallback);
        if (member.ChildModel is null)
        {
            return "("
                + changesSide
                + name
                + ".IsPresent ? "
                + changesAttribution
                + " : "
                + selfAttribution
                + ")";
        }

        return "(!"
            + changesSide
            + name
            + ".IsPresent ? "
            + selfAttribution
            + " : (!"
            + selfSide
            + name
            + ".IsPresent ? "
            + changesAttribution
            + " : ((object?)"
            + changesSide
            + name
            + ".Value is null ? "
            + changesAttribution
            + " : ((object?)"
            + selfSide
            + name
            + ".Value is null ? "
            + changesAttribution
            + " : null))))";
    }

    /// <summary>Builds the element-origin passthrough expression for <c>ApplyChanges</c>.</summary>
    public static string BuildApplyElements(
        SparseMemberModel member,
        string selfSide,
        string changesSide,
        int position
    )
    {
        if (member.ChildModel is not null)
        {
            return "null";
        }

        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        return "("
            + changesSide
            + name
            + ".IsPresent ? "
            + changesSide
            + "__SparseElementOrigins?["
            + position
            + "] : "
            + selfSide
            + "__SparseElementOrigins?["
            + position
            + "])";
    }

    /// <summary>Emits the fragment-builder origin capture fields.</summary>
    public static void AppendBuilderFields(SharedIndentedBuilder code)
    {
        code.AppendLineAt(2, "private readonly string? __sparse_origin;");
        code.AppendLineAt(2, "private string?[]? __sparse_member_origins;");
        code.AppendLineAt(2, "private string?[]?[]? __sparse_element_origins;");
    }

    /// <summary>Emits the fragment-builder origin capture from its source fragment.</summary>
    public static void AppendBuilderCapture(SharedIndentedBuilder code)
    {
        code.AppendLineAt(3, "__sparse_origin = fragment.Origin;");
        code.AppendLineAt(3, "__sparse_member_origins = fragment.__SparseMemberOrigins;");
        code.AppendLineAt(3, "__sparse_element_origins = fragment.__SparseElementOrigins;");
    }

    internal static string MemberAttribution(string side, int position, string? fallback)
    {
        // Explicit attributions (present slots, including explicit Unknown) win;
        // the fallback only covers members with no attribution state at all.
        var sideDefault = fallback is null
            ? side + "Origin"
            : "(" + side + "Origin ?? " + fallback + ")";
        return "("
            + side
            + "__SparseMemberOrigins is null ? "
            + sideDefault
            + " : "
            + side
            + "__SparseMemberOrigins["
            + position
            + "])";
    }

    private static string Literal(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private void AppendQueryFacades(SharedIndentedBuilder code, string operationsType)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Reads the origin attributed to the effective value at a path. Null means Unknown or absent.</summary>"
        );
        code.AppendLineAt(
            2,
            "public string? GetOrigin("
                + SparsePathType
                + " path) => "
                + operationsType
                + ".GetOrigin(this, path);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Tries to read the origin attributed to the effective value at a path.</summary>"
        );
        code.AppendLineAt(
            2,
            "public bool TryGetOrigin("
                + SparsePathType
                + " path, out string? origin) => "
                + operationsType
                + ".TryGetOrigin(this, path, out origin);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Enumerates the effective values attributed to their origins for inspection.</summary>"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IEnumerable<"
                + OriginEntryType
                + "> EnumerateOrigins() => "
                + operationsType
                + ".EnumerateOrigins(this);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Projects the effective values attributed to one origin. Grouping is lossy and read-only.</summary>"
        );
        code.AppendLineAt(
            2,
            "public Fragment GetByOrigin(string? origin) => "
                + operationsType
                + ".GetByOrigin(this, origin);"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Groups the effective values by origin in first-seen order. Grouping is lossy and read-only.</summary>"
        );
        code.AppendLineAt(
            2,
            "public global::System.Collections.Generic.IReadOnlyList<"
                + OriginGroupType
                + "> SplitByOrigin() => "
                + operationsType
                + ".SplitByOrigin(this);"
        );
        code.AppendLine();
    }

    private void AppendQueryBodies(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType,
        bool isStatic,
        string receiver
    )
    {
        AppendGetOrigin(code, isStatic);
        AppendTryGetOrigin(code, isStatic);
        AppendEnumerateOrigins(code, members, modelType, isStatic, receiver);
        AppendGetByOrigin(code, members, isStatic, receiver);
        AppendSplitByOrigin(code, isStatic);
    }

    private void AppendGetOrigin(SharedIndentedBuilder code, bool isStatic)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Reads the origin attributed to the effective value at a path. Null means Unknown or absent.</summary>"
        );
        code.AppendIndent(2)
            .Append(
                isStatic
                    ? "public static string? GetOrigin(Fragment self, "
                    : "public string? GetOrigin("
            )
            .Append(SparsePathType)
            .AppendLine(" path)");
        code.AppendLineAt(2, "{");
        if (isStatic)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
        }

        code.AppendLineAt(
            3,
            "return "
                + (isStatic ? "TryGetOrigin(self, path" : "TryGetOrigin(path")
                + ", out var __sparse_origin) ? __sparse_origin : null;"
        );
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendTryGetOrigin(SharedIndentedBuilder code, bool isStatic)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Tries to read the origin attributed to the effective value at a path.</summary>"
        );
        code.AppendIndent(2)
            .Append(
                isStatic
                    ? "public static bool TryGetOrigin(Fragment self, "
                    : "public bool TryGetOrigin("
            )
            .Append(SparsePathType)
            .AppendLine(" path, out string? origin)");
        code.AppendLineAt(2, "{");
        if (isStatic)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
        }

        // One source of truth: a path resolves exactly when enumeration yields
        // it. Member-level paths over mixed collections or deep-merged children
        // report Unknown; read their elements or leaves for precise attribution.
        code.AppendLineAt(3, "origin = null;");
        code.AppendLineAt(
            3,
            "foreach (var __sparse_entry in "
                + (isStatic ? "EnumerateOrigins(self)" : "EnumerateOrigins()")
                + ")"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (__sparse_entry.Path == path) { origin = __sparse_entry.Origin; return true; }"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "return false;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendEnumerateOrigins(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string modelType,
        bool isStatic,
        string receiver
    )
    {
        var self = receiver.TrimEnd('.');
        code.AppendLineAt(
            2,
            "/// <summary>Enumerates the effective values attributed to their origins for inspection.</summary>"
        );
        code.AppendIndent(2)
            .Append(
                isStatic
                    ? "public static global::System.Collections.Generic.IEnumerable<"
                    : "public global::System.Collections.Generic.IEnumerable<"
            )
            .Append(OriginEntryType)
            .Append(isStatic ? "> EnumerateOrigins(Fragment self)" : "> EnumerateOrigins()")
            .AppendLine("");
        code.AppendLineAt(2, "{");
        if (isStatic)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
        }

        code.AppendLineAt(
            3,
            "var __sparse_entries = new global::System.Collections.Generic.List<"
                + OriginEntryType
                + ">();"
        );
        for (var position = 0; position < members.Length; position++)
        {
            AppendEnumerateMember(code, members[position], position, self, modelType);
        }

        code.AppendLineAt(3, "return __sparse_entries;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendEnumerateMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int position,
        string self,
        string modelType
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var prefix =
            SparsePathType
            + ".Root(typeof("
            + modelType
            + ")).Member("
            + Literal(member.Property.Name)
            + ")";
        var attribution = MemberAttribution(self + ".", position, null);
        code.AppendLineAt(3, "if (" + self + "." + name + ".IsPresent)");
        code.AppendLineAt(3, "{");
        if (IsFragmentDeep(member))
        {
            code.AppendLineAt(4, "if ((object?)" + self + "." + name + ".Value is null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = "
                    + prefix
                    + ", Origin = "
                    + attribution
                    + " });"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "foreach (var __sparse_child_"
                    + member.Id
                    + " in "
                    + self
                    + "."
                    + name
                    + ".Value!.EnumerateOrigins())"
            );
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = "
                    + prefix
                    + ".Append(__sparse_child_"
                    + member.Id
                    + ".Path), Origin = __sparse_child_"
                    + member.Id
                    + ".Origin });"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
        else if (HasElementOrigins(member))
        {
            code.AppendLineAt(4, "if ((object?)" + self + "." + name + ".Value is null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = "
                    + prefix
                    + ", Origin = "
                    + attribution
                    + " });"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __sparse_values_"
                    + member.Id
                    + " = global::System.Linq.Enumerable.ToArray("
                    + self
                    + "."
                    + name
                    + ".Value!);"
            );
            code.AppendLineAt(
                5,
                "var __sparse_elements_"
                    + member.Id
                    + " = "
                    + self
                    + ".__SparseElementOrigins is null ? null : "
                    + self
                    + ".__SparseElementOrigins["
                    + position
                    + "];"
            );
            code.AppendLineAt(5, "if (__sparse_elements_" + member.Id + " is null)");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(
                6,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = "
                    + prefix
                    + ", Origin = "
                    + attribution
                    + " });"
            );
            code.AppendLineAt(5, "}");
            code.AppendLineAt(5, "else");
            code.AppendLineAt(5, "{");
            code.AppendLineAt(6, "var __sparse_element_prefix_" + member.Id + " = " + prefix + ";");
            code.AppendLineAt(
                6,
                "for (var __sparse_position_"
                    + member.Id
                    + " = 0; __sparse_position_"
                    + member.Id
                    + " < __sparse_values_"
                    + member.Id
                    + ".Length; __sparse_position_"
                    + member.Id
                    + "++)"
            );
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "var __sparse_element_origin_"
                    + member.Id
                    + " = (__sparse_position_"
                    + member.Id
                    + " >= __sparse_elements_"
                    + member.Id
                    + ".Length) ? "
                    + attribution
                    + " : (__sparse_elements_"
                    + member.Id
                    + "[__sparse_position_"
                    + member.Id
                    + "] ?? "
                    + attribution
                    + ");"
            );
            code.AppendLineAt(
                7,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = __sparse_element_prefix_"
                    + member.Id
                    + ".At(__sparse_position_"
                    + member.Id
                    + "), Origin = __sparse_element_origin_"
                    + member.Id
                    + " });"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(5, "}");
            code.AppendLineAt(4, "}");
        }
        else
        {
            code.AppendLineAt(
                4,
                "__sparse_entries.Add(new "
                    + OriginEntryType
                    + " { Path = "
                    + prefix
                    + ", Origin = "
                    + attribution
                    + " });"
            );
        }

        code.AppendLineAt(3, "}");
    }

    private void AppendGetByOrigin(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        bool isStatic,
        string receiver
    )
    {
        var self = receiver.TrimEnd('.');
        code.AppendLineAt(
            2,
            "/// <summary>Projects the effective values attributed to one origin. Grouping is lossy and read-only.</summary>"
        );
        code.AppendIndent(2)
            .Append(
                isStatic
                    ? "public static Fragment GetByOrigin(Fragment self, string? origin)"
                    : "public Fragment GetByOrigin(string? origin)"
            )
            .AppendLine("");
        code.AppendLineAt(2, "{");
        if (isStatic)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
        }

        code.AppendLineAt(3, "var __sparse_builder = new Fragment(origin).ToBuilder();");
        for (var position = 0; position < members.Length; position++)
        {
            AppendGetByOriginMember(code, members[position], position, self);
        }

        code.AppendLineAt(3, "return __sparse_builder.Build();");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }

    private void AppendGetByOriginMember(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        int position,
        string self
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var match =
            "global::System.String.Equals("
            + MemberAttribution(self + ".", position, null)
            + ", origin, global::System.StringComparison.Ordinal)";
        if (IsFragmentDeep(member))
        {
            code.AppendLineAt(3, "if (" + self + "." + name + ".IsPresent)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(4, "if ((object?)" + self + "." + name + ".Value is null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (" + match + ") __sparse_builder." + name + " = " + self + "." + name + ";"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "else");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __sparse_grouped_"
                    + member.Id
                    + " = "
                    + self
                    + "."
                    + name
                    + ".Value!.GetByOrigin(origin);"
            );
            code.AppendLineAt(5, "if (!__sparse_grouped_" + member.Id + ".IsEmpty)");
            code.AppendLineAt(
                6,
                "__sparse_builder."
                    + name
                    + " = "
                    + Optional
                    + "<"
                    + SparseFragmentEmitHelpers.FragmentValueType(member)
                    + ">.Present(__sparse_grouped_"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(3, "}");
            return;
        }

        if (HasElementOrigins(member))
        {
            var element = member.Collection.ElementType.Name;
            code.AppendLineAt(
                3,
                "if ("
                    + self
                    + "."
                    + name
                    + ".IsPresent && (object?)"
                    + self
                    + "."
                    + name
                    + ".Value is null && "
                    + match
                    + ") __sparse_builder."
                    + name
                    + " = "
                    + self
                    + "."
                    + name
                    + ";"
            );
            code.AppendLineAt(
                3,
                "if ("
                    + self
                    + "."
                    + name
                    + ".IsPresent && (object?)"
                    + self
                    + "."
                    + name
                    + ".Value is not null)"
            );
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "var __sparse_values_"
                    + member.Id
                    + " = global::System.Linq.Enumerable.ToArray("
                    + self
                    + "."
                    + name
                    + ".Value!);"
            );
            code.AppendLineAt(
                4,
                "var __sparse_elements_"
                    + member.Id
                    + " = "
                    + self
                    + ".__SparseElementOrigins is null ? null : "
                    + self
                    + ".__SparseElementOrigins["
                    + position
                    + "];"
            );
            code.AppendLineAt(
                4,
                "var __sparse_match_"
                    + member.Id
                    + " = new global::System.Collections.Generic.List<"
                    + element
                    + ">();"
            );
            code.AppendLineAt(
                4,
                "for (var __sparse_position_"
                    + member.Id
                    + " = 0; __sparse_position_"
                    + member.Id
                    + " < __sparse_values_"
                    + member.Id
                    + ".Length; __sparse_position_"
                    + member.Id
                    + "++)"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "var __sparse_element_origin_"
                    + member.Id
                    + " = (__sparse_elements_"
                    + member.Id
                    + " is null || __sparse_position_"
                    + member.Id
                    + " >= __sparse_elements_"
                    + member.Id
                    + ".Length) ? "
                    + MemberAttribution(self + ".", position, null)
                    + " : (__sparse_elements_"
                    + member.Id
                    + "[__sparse_position_"
                    + member.Id
                    + "] ?? "
                    + MemberAttribution(self + ".", position, null)
                    + ");"
            );
            code.AppendLineAt(
                5,
                "if (global::System.String.Equals(__sparse_element_origin_"
                    + member.Id
                    + ", origin, global::System.StringComparison.Ordinal)) __sparse_match_"
                    + member.Id
                    + ".Add(__sparse_values_"
                    + member.Id
                    + "[__sparse_position_"
                    + member.Id
                    + "]);"
            );
            code.AppendLineAt(4, "}");
            code.AppendLineAt(4, "if (__sparse_match_" + member.Id + ".Count > 0)");
            code.AppendLineAt(
                5,
                "__sparse_builder."
                    + name
                    + " = "
                    + Optional
                    + "<"
                    + SparseFragmentEmitHelpers.FragmentValueType(member)
                    + ">.Present("
                    + SparseFragmentExpressions.MaterializeCollection(
                        member,
                        "__sparse_match_" + member.Id
                    )
                    + ");"
            );
            code.AppendLineAt(3, "}");
            return;
        }

        code.AppendLineAt(
            3,
            "if ("
                + self
                + "."
                + name
                + ".IsPresent && "
                + match
                + ") __sparse_builder."
                + name
                + " = "
                + self
                + "."
                + name
                + ";"
        );
    }

    private void AppendSplitByOrigin(SharedIndentedBuilder code, bool isStatic)
    {
        code.AppendLineAt(
            2,
            "/// <summary>Groups the effective values by origin in first-seen order. Grouping is lossy and read-only.</summary>"
        );
        code.AppendIndent(2)
            .Append(
                isStatic
                    ? "public static global::System.Collections.Generic.IReadOnlyList<"
                    : "public global::System.Collections.Generic.IReadOnlyList<"
            )
            .Append(OriginGroupType)
            .Append(isStatic ? "> SplitByOrigin(Fragment self)" : "> SplitByOrigin()")
            .AppendLine("");
        code.AppendLineAt(2, "{");
        if (isStatic)
        {
            SparseFragmentEmitHelpers.AppendNullGuard(code, 3, "self");
        }

        code.AppendLineAt(
            3,
            "var __sparse_order = new global::System.Collections.Generic.List<string?>();"
        );
        code.AppendLineAt(
            3,
            "foreach (var __sparse_entry in "
                + (isStatic ? "EnumerateOrigins(self)" : "EnumerateOrigins()")
                + ")"
        );
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (!__sparse_order.Contains(__sparse_entry.Origin)) __sparse_order.Add(__sparse_entry.Origin);"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "var __sparse_groups = new global::System.Collections.Generic.List<"
                + OriginGroupType
                + ">(__sparse_order.Count);"
        );
        code.AppendLineAt(3, "foreach (var __sparse_origin in __sparse_order)");
        code.AppendLineAt(
            4,
            "__sparse_groups.Add(new "
                + OriginGroupType
                + "(__sparse_origin, GetByOrigin("
                + (isStatic ? "self, " : string.Empty)
                + "__sparse_origin)));"
        );
        code.AppendLineAt(3, "return __sparse_groups;");
        code.AppendLineAt(2, "}");
        code.AppendLine();
    }
}
