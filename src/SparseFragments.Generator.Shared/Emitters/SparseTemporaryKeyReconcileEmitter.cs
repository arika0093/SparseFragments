namespace SparseFragments.Generator.Shared;

/// <summary>Temporary-identity reconcile helpers for fragment models.</summary>
/// <remarks>
/// Builds the temporary-to-permanent correlation between a submitted model
/// and its authoritative persisted counterpart, then assigns mapped keys into
/// the live model. Values are never guessed: reconstruction replays recorded
/// post-submit edits onto persisted elements, and mismatches fail explicitly.
/// </remarks>
internal static class SparseTemporaryKeyReconcileEmitter
{
    /// <summary>Emits reconcile helpers for one model when ChangeSets are enabled.</summary>
    internal static void AppendReconcileHelpers(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType = true
    )
    {
        AppendBuildTempMap(code, modelType, members);
        AppendCollectTempMap(code, modelType, members);
        AppendRetargetTemps(code, modelType, runtimeNamespace, members, modelIsReferenceType);
    }

    private static void AppendBuildTempMap(
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
            "internal static (global::System.Collections.Generic.Dictionary<global::System.Guid, object?>? Map, string? Error) __SparseBuildTempMap("
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
            "var __map = new global::System.Collections.Generic.Dictionary<global::System.Guid, object?>();"
        );
        code.AppendLineAt(2, "var __error = __SparseCollectTempMap(submitted, persisted, __map);");
        code.AppendLineAt(2, "if (__error is not null) return (null, __error);");
        code.AppendLineAt(2, "return (__map, null);");
        code.AppendLineAt(1, "}");
    }

    private static void AppendCollectTempMap(
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
                + " persisted, global::System.Collections.Generic.Dictionary<global::System.Guid, object?> map)"
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
                "if (map.ContainsKey(__st.Value)) return \"Duplicate temporary key in keyed collection.\";"
            );
            code.AppendLineAt(4, "map[__st.Value] = (object?)" + foundUse + ";");
            code.AppendLineAt(
                4,
                "var __childError = "
                    + elementType
                    + ".__SparseCollectTempMap(__s, "
                    + foundUse
                    + ", map);"
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
                "var __nestedError = "
                    + elementType
                    + ".__SparseCollectTempMap(__s, "
                    + matchUse
                    + ", map);"
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
                "var __nestedError = " + childType + ".__SparseCollectTempMap(__sub, __per, map);"
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
                "var __nestedError = "
                    + childType
                    + ".__SparseCollectTempMap(__sub.Value, __per.Value, map);"
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
                "var __nestedError = "
                    + childType
                    + ".__SparseCollectTempMap(submitted."
                    + prop
                    + ", persisted."
                    + prop
                    + ", map);"
            );
            code.AppendLineAt(3, "if (__nestedError is not null) return __nestedError;");
            code.AppendLineAt(2, "}");
        }
    }

    private static void AppendRetargetTemps(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType
    )
    {
        code.AppendLineAt(
            1,
            "/// <summary>Assigns mapped temporary identities into the live model.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>Replays recorded post-submit edits onto persisted elements; unmapped temporary identities stay pending. Returns an error without partial writes when the live state is incompatible.</remarks>"
        );
        code.AppendLineAt(
            1,
            "internal static string? __SparseRetargetTemps("
                + modelType
                + " live, "
                + modelType
                + "? submittedAfter, "
                + modelType
                + "? persisted, global::System.Collections.Generic.Dictionary<global::System.Guid, object?> map, "
                + modelType
                + ".ChangeSet? pending)"
        );
        code.AppendLineAt(1, "{");
        code.AppendLineAt(
            2,
            "if ((object?)live is null) throw new global::System.ArgumentNullException(nameof(live));"
        );
        code.AppendLineAt(
            2,
            "if (map is null) throw new global::System.ArgumentNullException(nameof(map));"
        );
        foreach (var member in members)
        {
            if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                && member.Collection.ElementType.IsFragmentModel
            )
            {
                AppendKeyedRetarget(code, runtimeNamespace, member, modelIsReferenceType);
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
            )
            {
                AppendNestedRetarget(code, member, runtimeNamespace, modelIsReferenceType);
            }
        }
        code.AppendLineAt(2, "return null;");
        code.AppendLineAt(1, "}");
    }

    private static void AppendKeyedRetarget(
        SharedIndentedBuilder code,
        string runtimeNamespace,
        SparseMemberModel member,
        bool modelIsReferenceType
    )
    {
        var elementType = member.Collection.ElementType.NonNullableName;
        var keyType = member.Collection.KeyTypeName ?? "object?";
        var keyProp =
            member.Collection.KeyPropertyNames.Length == 1
                ? SparseNaming.EscapeIdentifier(member.Collection.KeyPropertyNames[0])
                : null;
        var tempProp = member.Collection.TemporaryKeyPropertyName is null
            ? null
            : SparseNaming.EscapeIdentifier(member.Collection.TemporaryKeyPropertyName);
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var unassigned = member.Collection.UnassignedKeyExpression;
        var optionalFragment = runtimeNamespace + "Optional<" + elementType + ".Fragment?>";
        // Nullable element annotations ride the working list so indexable
        // casts never fail on variance; element operations use the
        // non-nullable name with promotion guards.
        var elementName = member.Collection.ElementType.Name;
        var elementRef2 = member.Collection.ElementType.IsReferenceType;
        var elementNullableStruct2 =
            !elementRef2
            && (elementName.TrimEnd().EndsWith("?") || elementName.Contains("Nullable<"));
        code.AppendLineAt(2, "{");
        code.AppendLineAt(2, "var __liveList = live." + prop + ";");
        code.AppendLineAt(2, "var __subList = submittedAfter?." + prop + ";");
        code.AppendLineAt(2, "var __perList = persisted?." + prop + ";");
        code.AppendLineAt(2, "if (__liveList is not null)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var __transition = pending?." + prop + ";");
        // A clean member fast-forwards to the server state; local activity
        // keeps live field values while the server contributes identity.
        code.AppendLineAt(3, "var __clean = __transition is null || __transition.IsEmpty;");
        // Indexable shapes operate in place; other shapes work on a materialized
        // copy that is written back only when replacements occurred.
        code.AppendLineAt(
            3,
            "var __backing = __liveList as global::System.Collections.Generic.IList<"
                + elementName
                + ">;"
        );
        code.AppendLineAt(
            3,
            "var __working = __backing ?? new global::System.Collections.Generic.List<"
                + elementName
                + ">(__liveList);"
        );
        code.AppendLineAt(3, "var __dirty = false;");
        code.AppendLineAt(3, "for (var __i = 0; __i < __working.Count; __i++)");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(4, "var __e = __working[__i];");
        // Normalized element variable: references promote, nullable structs
        // unwrap, plain structs flow through.
        var eUse = "__e";
        if (elementRef2)
        {
            code.AppendLineAt(4, "if (__e is null) continue;");
        }
        else if (elementNullableStruct2)
        {
            code.AppendLineAt(4, "if (!__e.HasValue) continue;");
            code.AppendLineAt(4, "var __en = __e.Value;");
            eUse = "__en";
        }
        if (keyProp is not null)
        {
            code.AppendLineAt(4, "var __k = " + eUse + "." + keyProp + ";");
            // Keys without an unassigned state are always assigned; the
            // temporary branch below only exists for unassigned-capable keys.
            var hasUnassignedState = unassigned is not null;
            if (hasUnassignedState)
            {
                var isUnassignedCheck =
                    "global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__k, "
                    + unassigned
                    + ")";
                code.AppendLineAt(4, "if (" + isUnassignedCheck + ")");
                code.AppendLineAt(4, "{");
                if (tempProp is not null)
                {
                    code.AppendLineAt(4, "var __t = " + eUse + "." + tempProp + ";");
                    code.AppendLineAt(
                        5,
                        "if (!__t.HasValue || __t.Value == global::System.Guid.Empty) return \"The live model contains an unassigned element without a temporary identity.\";"
                    );
                    code.AppendLineAt(
                        5,
                        "if (!map.TryGetValue(__t.Value, out var __persisted)) continue;"
                    );
                    code.AppendLineAt(
                        5,
                        "var __edit = __transition?.GetTemporaryChange(__t.Value)?.Edit;"
                    );
                    code.AppendLineAt(5, elementType + " __merged;");
                    code.AppendLineAt(
                        5,
                        "if (__edit is null || __edit.IsEmpty) __merged = __clean ? "
                            + elementType
                            + ".Fragment.From(("
                            + elementType
                            + ")__persisted!).ToModel() : "
                            + eUse
                            + ";"
                    );
                    code.AppendLineAt(5, "else");
                    code.AppendLineAt(5, "{");
                    // The edit sits on the submitted temporary state, so applying
                    // it to the assigned persisted state goes stale. Patches are
                    // baseline-free and temp-correlated: local field values win
                    // while the persisted element contributes its identity.
                    code.AppendLineAt(
                        6,
                        "var __persistedElement = (" + elementType + ")__persisted!;"
                    );
                    code.AppendLineAt(
                        6,
                        "var __applied = __edit.ToPatch().Apply("
                            + elementType
                            + ".Fragment.From(__persistedElement));"
                    );
                    code.AppendLineAt(
                        6,
                        "if (!__applied.IsPresent || __applied.Value is null) return \"Reconciling produced an invalid element.\";"
                    );
                    code.AppendLineAt(6, "__merged = __applied.Value.ToModel();");
                    code.AppendLineAt(5, "}");
                    code.AppendLineAt(5, "__working[__i] = __merged; __dirty = true;");
                }
                else
                {
                    code.AppendLineAt(
                        5,
                        "return \"The live model contains an unassigned element without temporary support.\";"
                    );
                }
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "else");
                code.AppendLineAt(4, "{");
                AppendKeyedAssignedElement(
                    code,
                    runtimeNamespace,
                    member,
                    elementType,
                    keyType,
                    keyProp,
                    optionalFragment,
                    eUse,
                    elementRef2,
                    elementNullableStruct2
                );
                code.AppendLineAt(4, "}");
            }
            else
            {
                // No unassigned state: every element processes as assigned.
                AppendKeyedAssignedElement(
                    code,
                    runtimeNamespace,
                    member,
                    elementType,
                    keyType,
                    keyProp,
                    optionalFragment,
                    eUse,
                    elementRef2,
                    elementNullableStruct2
                );
            }
        }
        else
        {
            // Keyless keyed members cannot correlate; nested recursion still applies.
            code.AppendLineAt(
                4,
                "var __recurseError = "
                    + elementType
                    + ".__SparseRetargetTemps("
                    + eUse
                    + ", null, null, map, null);"
            );
            code.AppendLineAt(4, "if (__recurseError is not null) return __recurseError;");
        }
        code.AppendLineAt(3, "}");
        AppendRetargetWriteback(code, member, modelIsReferenceType);
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "}");
    }

    private static void AppendKeyedAssignedElement(
        SharedIndentedBuilder code,
        string runtimeNamespace,
        SparseMemberModel member,
        string elementType,
        string keyType,
        string keyProp,
        string optionalFragment,
        string eUse,
        bool elementRef,
        bool elementNullableStruct
    )
    {
        // Submitted-missing elements are post-submit additions and stay;
        // submitted elements missing from the persisted model were dropped
        // and leave; the rest replay post-submit edits onto persisted values
        // (or adopt them when untouched).
        code.AppendLineAt(5, elementType + " __subFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasSub = false;");
        code.AppendLineAt(5, "if (__subList is not null)");
        AppendFindByKey(
            code,
            "__subList",
            "__subFound",
            "__hasSub",
            elementType,
            keyType,
            keyProp,
            "__k",
            elementRef,
            elementNullableStruct,
            5
        );
        code.AppendLineAt(5, "if (!__hasSub) continue;");
        code.AppendLineAt(5, elementType + " __perFound = default" + (elementRef ? "!" : "") + ";");
        code.AppendLineAt(5, "bool __hasPer = false;");
        code.AppendLineAt(5, "if (__perList is not null)");
        AppendFindByKey(
            code,
            "__perList",
            "__perFound",
            "__hasPer",
            elementType,
            keyType,
            keyProp,
            "__k",
            elementRef,
            elementNullableStruct,
            5
        );
        code.AppendLineAt(5, "if (!__hasPer)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(6, "__working.RemoveAt(__i); __i--; __dirty = true; continue;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "var __edit = __transition?.GetChange(__k!)?.Edit;");
        code.AppendLineAt(5, "if (__edit is null || __edit.IsEmpty)");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (__clean && !"
                + elementType
                + ".Fragment.__SparseAreEqual("
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From("
                + eUse
                + ")), "
                + optionalFragment
                + ".Present("
                + elementType
                + ".Fragment.From(__perFound)))) { __working[__i] = "
                + elementType
                + ".Fragment.From(__perFound).ToModel(); __dirty = true; }"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        // The edit sits on the submitted state while adoption targets the
        // persisted state; the baseline-free temp-correlated patch carries
        // local field values onto the persisted identity.
        code.AppendLineAt(
            6,
            "var __applied = __edit.ToPatch().Apply(" + elementType + ".Fragment.From(__perFound));"
        );
        code.AppendLineAt(
            6,
            "if (!__applied.IsPresent || __applied.Value is null) return \"Reconciling produced an invalid element.\";"
        );
        code.AppendLineAt(6, "__working[__i] = __applied.Value.ToModel(); __dirty = true;");
        code.AppendLineAt(5, "}");
        _ = runtimeNamespace;
        _ = member;
    }

    private static void AppendFindByKey(
        SharedIndentedBuilder code,
        string list,
        string foundVar,
        string flagVar,
        string elementType,
        string keyType,
        string keyProp,
        string keyExpr,
        bool elementRef,
        bool elementNullableStruct,
        int indent
    )
    {
        code.AppendLineAt(indent, "{");
        if (elementRef)
        {
            code.AppendLineAt(indent + 1, "foreach (var __cand in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(indent + 2, "if (__cand is null) continue;");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        else if (elementNullableStruct)
        {
            code.AppendLineAt(indent + 1, "foreach (var __candRaw in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(indent + 2, "if (!__candRaw.HasValue) continue;");
            code.AppendLineAt(indent + 2, "var __cand = __candRaw.Value;");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        else
        {
            code.AppendLineAt(indent + 1, "foreach (var __cand in " + list + ")");
            code.AppendLineAt(indent + 1, "{");
            code.AppendLineAt(
                indent + 2,
                "if (global::System.Collections.Generic.EqualityComparer<"
                    + keyType
                    + ">.Default.Equals(__cand."
                    + keyProp
                    + ", "
                    + keyExpr
                    + ")) { "
                    + foundVar
                    + " = __cand; "
                    + flagVar
                    + " = true; break; }"
            );
            code.AppendLineAt(indent + 1, "}");
        }
        code.AppendLineAt(indent, "}");
        _ = elementType;
    }

    private static void AppendRetargetWriteback(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        bool modelIsReferenceType
    )
    {
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var writeable =
            modelIsReferenceType
            && !member.Property.IsReadOnly
            && !member.Property.IsInitOnly
            && (
                member.Collection.NamedTypeDefinition is null
                || member.Collection.NamedTypeDefinition.StartsWith(
                    "System.",
                    System.StringComparison.Ordinal
                )
            );
        if (writeable)
        {
            // Arrays are indexable and never reach here; BCL read interfaces
            // and List shapes accept the materialized list directly, while
            // array-typed properties convert back explicitly.
            var elementName = member.Collection.ElementType.Name;
            var conversion = member.Property.Type.Name.TrimEnd().EndsWith("[]")
                ? "new global::System.Collections.Generic.List<"
                    + elementName
                    + ">(__working).ToArray()"
                : "new global::System.Collections.Generic.List<" + elementName + ">(__working)";
            code.AppendLineAt(
                3,
                "if (__dirty && __backing is null) live." + prop + " = " + conversion + ";"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "if (__dirty) return \"The live collection shape cannot be reconciled in place.\";"
            );
        }
    }

    private static void AppendNestedRetarget(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string runtimeNamespace,
        bool modelIsReferenceType
    )
    {
        var childType = member.ChildModel!.Value.NonNullableName;
        var childFrag = childType + ".Fragment";
        var childOptionalFrag = runtimeNamespace + "Optional<" + childType + ".Fragment?>";
        var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
        var childRef = member.ChildModel.Value.IsReferenceType;
        var childNullableValue = !childRef && member.ChildModel.Value.Name.TrimEnd().EndsWith("?");
        var settable = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
        var defaultExpr = (childRef || childNullableValue) ? "default!" : "default";
        // The ?. accessors above always yield nullable locals. Bang
        // unwraps reference types, but Nullable<T> structs still need
        // .Value because neither ! nor is-null narrows them.
        var perAccess = childRef ? "__childPer!" : "__childPer.Value";
        // Child live value with nullability normalized; server-side drops with
        // local edits fail deterministically instead of losing edits silently.
        code.AppendLineAt(2, "{");
        code.AppendLineAt(2, "var __childLive = live." + prop + ";");
        if (childRef)
        {
            code.AppendLineAt(2, "if (__childLive is null)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(2, "}");
            code.AppendLineAt(2, "else");
            code.AppendLineAt(2, "{");
        }
        else if (childNullableValue)
        {
            code.AppendLineAt(2, "if (!__childLive.HasValue)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(2, "}");
            code.AppendLineAt(2, "else");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(2, "var __childValue = __childLive.Value;");
        }
        // Plain struct children flow through directly.
        string childLive = childNullableValue ? "__childValue" : "__childLive";
        code.AppendLineAt(2, "var __childPending = pending?." + prop + ";");
        code.AppendLineAt(2, "var __childSub = submittedAfter?." + prop + ";");
        code.AppendLineAt(2, "var __childPer = persisted?." + prop + ";");
        code.AppendLineAt(2, "if (__childPer is null)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (__childPending is not null && !__childPending.IsEmpty) return \"The server dropped a value with pending local edits.\";"
        );
        if (modelIsReferenceType && settable)
        {
            code.AppendLineAt(3, "live." + prop + " = " + defaultExpr + ";");
        }
        else
        {
            code.AppendLineAt(
                3,
                "var __dropError = "
                    + childType
                    + ".__SparseRetargetTemps("
                    + childLive
                    + ", __childSub, __childPer, map, __childPending);"
            );
            code.AppendLineAt(3, "if (__dropError is not null) return __dropError;");
        }
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "else if (__childSub is null)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "var __addError = "
                + childType
                + ".__SparseRetargetTemps("
                + childLive
                + ", __childSub, __childPer, map, __childPending);"
        );
        code.AppendLineAt(3, "if (__addError is not null) return __addError;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "else");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType && settable)
        {
            code.AppendLineAt(
                3,
                "var __mergedChildBase = " + childFrag + ".From(" + perAccess + ");"
            );
            // Pending edits sit on the submitted state while the base moved
            // to the persisted state; the baseline-free patch carries local
            // field values onto the persisted child instead.
            code.AppendLineAt(
                3,
                "var __mergedChildAdvanced = " + childOptionalFrag + ".Present(__mergedChildBase);"
            );
            code.AppendLineAt(3, "if (__childPending is not null && !__childPending.IsEmpty)");
            code.AppendLineAt(3, "{");
            code.AppendLineAt(
                4,
                "var __mergedChildApplied = __childPending.ToPatch().Apply(__mergedChildBase);"
            );
            code.AppendLineAt(
                4,
                "if (!__mergedChildApplied.IsPresent || __mergedChildApplied.Value is null) return \"Reconciling produced an invalid element.\";"
            );
            code.AppendLineAt(4, "__mergedChildAdvanced = __mergedChildApplied;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "if (!__mergedChildAdvanced.IsPresent || __mergedChildAdvanced.Value is null) return \"Reconciling produced an invalid element.\";"
            );
            code.AppendLineAt(3, "live." + prop + " = __mergedChildAdvanced.Value.ToModel();");
        }
        else
        {
            code.AppendLineAt(
                3,
                "var __childError = "
                    + childType
                    + ".__SparseRetargetTemps("
                    + childLive
                    + ", __childSub, __childPer, map, __childPending);"
            );
            code.AppendLineAt(3, "if (__childError is not null) return __childError;");
        }
        code.AppendLineAt(2, "}");
        if (childRef || childNullableValue)
        {
            code.AppendLineAt(2, "}");
        }
        code.AppendLineAt(2, "}");
    }
}
