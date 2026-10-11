namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity correlation builders for fragment models.</summary>
/// <remarks>
/// Owns the path-scoped submitted-to-persisted map surface so sibling
/// collections may reuse a Guid while within-collection duplicates fail.
/// </remarks>
internal static class SparseTemporaryKeyCollectEmitter
{
    internal static void AppendBuildTempMap(
        SharedIndentedBuilder code,
        string modelType,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        _ = members;
        code.AppendLineAt(
            1,
            "/// <summary>Correlates submitted temporary identities with persisted assigned keys.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>Returns the opaque correlation and a null error on success, or a null map with the first mismatch. Never mutates either model.</remarks>"
        );
        code.AppendLineAt(
            1,
            "internal static (global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?>? Map, string? Error) __SparseBuildTempMap("
                + modelType
                + " submitted, "
                + modelType
                + " persisted)"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "if ((object?)submitted is null) throw new global::System.ArgumentNullException(nameof(submitted));"
        );
        code.AppendLineAt(
            2,
            "if ((object?)persisted is null) throw new global::System.ArgumentNullException(nameof(persisted));"
        );
        code.AppendLineAt(
            2,
            "var __map = new global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?>();"
        );
        code.AppendLineAt(
            2,
            "var __error = __SparseCollectTempMap(submitted, persisted, __map, \"\");"
        );
        code.AppendLineAt(2, "if (__error is not null) return (null, __error);");
        code.AppendLineAt(2, "return (__map, null);");
        code.AppendLineAt(1, "}");
    }

    internal static void AppendCollectTempMap(
        SharedIndentedBuilder code,
        string modelType,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(
            1,
            "/// <summary>Collects temporary correlations for this model into a shared map.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>Recurses through keyed and nested members; server-side drops absorb while omitted, duplicated, or unassigned temporary identities fail.</remarks>"
        );
        code.AppendLineAt(
            1,
            "internal static string? __SparseCollectTempMap("
                + modelType
                + " submitted, "
                + modelType
                + " persisted, global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?> map, string __scope)"
        );
        code.AppendLineAt(1, "{");
        foreach (var member in members)
        {
            if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                && member.Collection.ElementType.IsFragmentModel
            )
            {
                AppendKeyedCollect(code, member);
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
            )
            {
                AppendNestedCollect(code, member);
            }
        }
        code.AppendLineAt(2, "return null;");
        code.AppendLineAt(1, "}");
    }

    private static void AppendKeyedCollect(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var elementType = member.Collection.ElementType.NonNullableName;
        var keyType = member.Collection.KeyTypeName ?? "object?";
        // Element shape drives null handling: references narrow with
        // `is null`, nullable structs unwrap with HasValue/Value (the
        // toolchain does not narrow `is null` on Nullable<T>), and plain
        // structs skip checks entirely.
        var elementRef = member.Collection.ElementType.IsReferenceType;
        var elementName = member.Collection.ElementType.Name.TrimEnd();
        var elementNullableStruct =
            !elementRef && (elementName.EndsWith("?") || elementName.Contains("Nullable<"));
        var keyProp =
            member.Collection.KeyPropertyNames.Length == 1
                ? SparseNaming.EscapeIdentifier(member.Collection.KeyPropertyNames[0])
                : null;
        var tempProp = member.Collection.TemporaryKeyPropertyName is null
            ? null
            : SparseNaming.EscapeIdentifier(member.Collection.TemporaryKeyPropertyName);
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var unassigned = member.Collection.UnassignedKeyExpression;
        var isUnassigned = unassigned is null
            ? "false"
            : "global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__sk, "
                + unassigned
                + ")";
        var persistedIsUnassigned = unassigned is null
            ? "false"
            : "global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default.Equals(__pk, "
                + unassigned
                + ")";
        code.AppendLineAt(
            2,
            "if (submitted." + prop + " is not null && persisted." + prop + " is not null)"
        );
        code.AppendLineAt(2, "{");
        // Collection scope keeps temporary correlation path-local: sibling
        // collections may reuse a Guid while duplicates within one
        // collection still fail. Assigned parent keys extend the scope so
        // repeated nested instances stay distinct.
        code.AppendLineAt(
            3,
            "var __collectionScope"
                + member.Id
                + " = string.IsNullOrEmpty(__scope) ? \""
                + prop
                + "\" : __scope + \"."
                + prop
                + "\";"
        );
        if (tempProp is not null && keyProp is not null)
        {
            // Submitted temporary elements correlate with persisted assigned
            // elements by Guid; anything else fails instead of guessing.
            AppendElementLoopHead(
                code,
                "submitted." + prop,
                "__s",
                elementRef,
                elementNullableStruct,
                3
            );
            code.AppendLineAt(4, "var __sk = __s." + keyProp + ";");
            code.AppendLineAt(4, "if (!(" + isUnassigned + ")) continue;");
            code.AppendLineAt(4, "var __st = __s." + tempProp + ";");
            code.AppendLineAt(
                4,
                "if (!__st.HasValue) return \"The submitted element is missing its temporary identity.\";"
            );
            code.AppendLineAt(
                4,
                "if (__st.Value == global::System.Guid.Empty) return \"The submitted element has a reserved temporary identity.\";"
            );
            code.AppendLineAt(4, elementType + "? __found = null;");
            AppendElementLoopHead(
                code,
                "persisted." + prop,
                "__p",
                elementRef,
                elementNullableStruct,
                4
            );
            code.AppendLineAt(5, "if (__p." + tempProp + " != __st.Value) continue;");
            if (elementRef)
            {
                code.AppendLineAt(
                    5,
                    "if (__found is not null) return \"The server response duplicates a temporary identity.\";"
                );
            }
            else
            {
                code.AppendLineAt(
                    5,
                    "if (__found.HasValue) return \"The server response duplicates a temporary identity.\";"
                );
            }
            code.AppendLineAt(5, "__found = __p;");
            code.AppendLineAt(4, "}");
            var foundUse = elementRef ? "__found" : "__found.Value";
            var foundMissing = elementRef ? "if (__found is null)" : "if (!__found.HasValue)";
            code.AppendLineAt(
                4,
                foundMissing + " return \"The server response omits a temporary identity.\";"
            );
            code.AppendLineAt(4, "var __pk = " + foundUse + "." + keyProp + ";");
            code.AppendLineAt(
                4,
                "if ("
                    + persistedIsUnassigned
                    + ") return \"The server response leaves a temporary identity unassigned.\";"
            );
            code.AppendLineAt(
                4,
                "if (map.ContainsKey((__collectionScope"
                    + member.Id
                    + ", __st.Value))) return \"Duplicate temporary key in keyed collection.\";"
            );
            code.AppendLineAt(
                4,
                "map[(__collectionScope" + member.Id + ", __st.Value)] = (object?)" + foundUse + ";"
            );
            code.AppendLineAt(
                4,
                "var __elemScope"
                    + member.Id
                    + " = __collectionScope"
                    + member.Id
                    + " + \"[\" + __st.Value.ToString(\"D\") + \"]\";"
            );
            code.AppendLineAt(
                4,
                "var __childError = "
                    + elementType
                    + ".__SparseCollectTempMap(__s, "
                    + foundUse
                    + ", map, __elemScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(4, "if (__childError is not null) return __childError;");
            code.AppendLineAt(3, "}");
        }
        if (keyProp is not null)
        {
            // Assigned pairs recurse so nested temporary identities under
            // stable parents still correlate; drops absorb silently.
            // Keys without an unassigned state never skip here.
            AppendElementLoopHead(
                code,
                "submitted." + prop,
                "__s",
                elementRef,
                elementNullableStruct,
                3
            );
            code.AppendLineAt(4, "var __sk = __s." + keyProp + ";");
            if (unassigned is not null)
            {
                code.AppendLineAt(4, "if (" + isUnassigned + ") continue;");
            }
            code.AppendLineAt(4, elementType + "? __match = null;");
            AppendElementLoopHead(
                code,
                "persisted." + prop,
                "__p",
                elementRef,
                elementNullableStruct,
                4
            );
            code.AppendLineAt(
                5,
                "if (!global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__p."
                    + keyProp
                    + ", __sk)) continue;"
            );
            code.AppendLineAt(5, "__match = __p; break;");
            code.AppendLineAt(4, "}");
            var matchUse = elementRef ? "__match" : "__match.Value";
            var matchMissing = elementRef ? "if (__match is null)" : "if (!__match.HasValue)";
            code.AppendLineAt(4, matchMissing + " continue;");
            code.AppendLineAt(
                4,
                "var __parentKey"
                    + member.Id
                    + " = global::System.Convert.ToString((object?)__sk, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;"
            );
            code.AppendLineAt(
                4,
                "var __assignedScope"
                    + member.Id
                    + " = __collectionScope"
                    + member.Id
                    + " + \"[\" + __parentKey"
                    + member.Id
                    + ".Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"]\", \"\\\\]\") + \"]\";"
            );
            code.AppendLineAt(
                4,
                "var __nestedError = "
                    + elementType
                    + ".__SparseCollectTempMap(__s, "
                    + matchUse
                    + ", map, __assignedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(4, "if (__nestedError is not null) return __nestedError;");
            code.AppendLineAt(3, "}");
        }
        code.AppendLineAt(2, "}");
    }

    /// <summary>Emits a null-safe element loop head with a normalized non-null variable.</summary>
    /// <remarks>
    /// References narrow with <c>is null</c>; nullable structs unwrap with
    /// <c>HasValue</c>/<c>Value</c> into a normalized alias; plain structs
    /// iterate directly. The normalized variable is always usable as non-null.
    /// </remarks>
    private static void AppendElementLoopHead(
        SharedIndentedBuilder code,
        string list,
        string normalized,
        bool elementRef,
        bool elementNullableStruct,
        int indent
    )
    {
        if (elementRef)
        {
            code.AppendLineAt(indent, "foreach (var " + normalized + " in " + list + ")");
            code.AppendLineAt(indent, "{");
            code.AppendLineAt(indent + 1, "if (" + normalized + " is null) continue;");
        }
        else if (elementNullableStruct)
        {
            var raw = normalized + "Raw";
            code.AppendLineAt(indent, "foreach (var " + raw + " in " + list + ")");
            code.AppendLineAt(indent, "{");
            code.AppendLineAt(indent + 1, "if (!" + raw + ".HasValue) continue;");
            code.AppendLineAt(indent + 1, "var " + normalized + " = " + raw + ".Value;");
        }
        else
        {
            code.AppendLineAt(indent, "foreach (var " + normalized + " in " + list + ")");
            code.AppendLineAt(indent, "{");
        }
    }

    private static void AppendNestedCollect(SharedIndentedBuilder code, SparseMemberModel member)
    {
        var childType = member.ChildModel!.Value.NonNullableName;
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var nullableValue =
            !member.ChildModel.Value.IsReferenceType
            && member.ChildModel.Value.Name.TrimEnd().EndsWith("?");
        if (member.ChildModel.Value.IsReferenceType)
        {
            // Locals promote nullability; property chains do not.
            code.AppendLineAt(2, "{");
            code.AppendLineAt(2, "var __sub = submitted." + prop + ";");
            code.AppendLineAt(2, "var __per = persisted." + prop + ";");
            code.AppendLineAt(2, "if (__sub is not null && __per is not null)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "var __nestedScope"
                    + member.Id
                    + " = string.IsNullOrEmpty(__scope) ? \""
                    + prop
                    + "\" : __scope + \"."
                    + prop
                    + "\";"
            );
            code.AppendLineAt(
                3,
                "var __nestedError = "
                    + childType
                    + ".__SparseCollectTempMap(__sub, __per, map, __nestedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(3, "if (__nestedError is not null) return __nestedError;");
            code.AppendLineAt(2, "}");
            code.AppendLineAt(2, "}");
        }
        else if (nullableValue)
        {
            code.AppendLineAt(2, "{");
            code.AppendLineAt(2, "var __sub = submitted." + prop + ";");
            code.AppendLineAt(2, "var __per = persisted." + prop + ";");
            code.AppendLineAt(2, "if (__sub.HasValue && __per.HasValue)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "var __nestedScope"
                    + member.Id
                    + " = string.IsNullOrEmpty(__scope) ? \""
                    + prop
                    + "\" : __scope + \"."
                    + prop
                    + "\";"
            );
            code.AppendLineAt(
                3,
                "var __nestedError = "
                    + childType
                    + ".__SparseCollectTempMap(__sub.Value, __per.Value, map, __nestedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(3, "if (__nestedError is not null) return __nestedError;");
            code.AppendLineAt(2, "}");
            code.AppendLineAt(2, "}");
        }
        else
        {
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "var __nestedScope"
                    + member.Id
                    + " = string.IsNullOrEmpty(__scope) ? \""
                    + prop
                    + "\" : __scope + \"."
                    + prop
                    + "\";"
            );
            code.AppendLineAt(
                3,
                "var __nestedError = "
                    + childType
                    + ".__SparseCollectTempMap(submitted."
                    + prop
                    + ", persisted."
                    + prop
                    + ", map, __nestedScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(3, "if (__nestedError is not null) return __nestedError;");
            code.AppendLineAt(2, "}");
        }
    }
}
