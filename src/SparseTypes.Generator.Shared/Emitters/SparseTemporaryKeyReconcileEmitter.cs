namespace SparseTypes.Generator.Shared;

/// <summary>Temporary-identity reconcile helpers for fragment models.</summary>
/// <remarks>Builds the temporary-to-permanent correlation between a submitted model and its authoritative persisted counterpart.</remarks>
internal static class SparseTemporaryKeyReconcileEmitter
{
    internal static void AppendReconcileHelpers(
        SharedIndentedBuilder code,
        string modelType,
        string runtimeNamespace,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType = true
    )
    {
        SparseTemporaryKeyCollectEmitter.AppendBuildTempMap(code, modelType, members);
        SparseTemporaryKeyCollectEmitter.AppendCollectTempMap(code, modelType, members);
        AppendRetargetTemps(code, modelType, runtimeNamespace, members, modelIsReferenceType);
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
            "/// <remarks>Replays recorded post-submit edits onto persisted elements; rows without their own edits adopt the persisted element, and unmapped temporary identities stay pending. Returns an error without partial writes when the live state is incompatible.</remarks>"
        );
        code.AppendLineAt(
            1,
            "internal static string? __SparseRetargetTemps("
                + modelType
                + " live, "
                + modelType
                + "? submittedAfter, "
                + modelType
                + "? persisted, global::System.Collections.Generic.Dictionary<(string Scope, global::System.Guid Temp), object?> map, "
                + modelType
                + ".ChangeSet? pending, string __scope)"
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
        code.AppendLineAt(
            2,
            "if (__scope is null) throw new global::System.ArgumentNullException(nameof(__scope));"
        );
        // Models without keyed fragment members or fragment children emit no
        // failing paths.
        var needsFailHelper = false;
        foreach (var member in members)
        {
            if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                && member.Collection.ElementType.IsFragmentModel
            )
            {
                needsFailHelper = true;
                break;
            }

            if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
            )
            {
                needsFailHelper = true;
                break;
            }
        }

        if (needsFailHelper)
        {
            AppendRetargetBackups(code, members);
            // Fail helper restores earlier commits so a late failure never
            // leaves a sibling collection changed.
            code.AppendLineAt(2, "string? __Fail(string __err)");
            code.AppendLineAt(2, "{");
            AppendRetargetRestoreStatements(code, members);
            code.AppendLineAt(3, "return __err;");
            code.AppendLineAt(2, "}");
        }
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

    private static void AppendRetargetBackups(
        SharedIndentedBuilder code,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        // Shallow backups preserve original row references for rollback.
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                && member.Collection.ElementType.IsFragmentModel
            )
            {
                var elementName = member.Collection.ElementType.Name;
                code.AppendLineAt(2, "var __origRef" + member.Id + " = live." + prop + ";");
                code.AppendLineAt(
                    2,
                    "var __backup"
                        + member.Id
                        + " = __origRef"
                        + member.Id
                        + " is null ? null : new global::System.Collections.Generic.List<"
                        + elementName
                        + ">(__origRef"
                        + member.Id
                        + ");"
                );
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
                && !member.Property.IsReadOnly
                && !member.Property.IsInitOnly
            )
            {
                code.AppendLineAt(2, "var __origChild" + member.Id + " = live." + prop + ";");
            }
        }
    }

    private static void AppendRetargetRestoreStatements(
        SharedIndentedBuilder code,
        System.Collections.Immutable.ImmutableArray<SparseMemberModel> members
    )
    {
        // Inline in __Fail so original references stay captured.
        foreach (var member in members)
        {
            var prop = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (
                SparseChangeSetBasicsEmitter.IsKeyed(member)
                && member.Collection.ElementType.IsFragmentModel
            )
            {
                var elementName = member.Collection.ElementType.Name;
                var settable = !member.Property.IsReadOnly && !member.Property.IsInitOnly;
                code.AppendLineAt(3, "{");
                code.AppendLineAt(3, "var __cur = live." + prop + ";");
                code.AppendLineAt(3, "var __orig = __origRef" + member.Id + ";");
                code.AppendLineAt(3, "var __bak = __backup" + member.Id + ";");
                code.AppendLineAt(3, "if (!object.ReferenceEquals(__cur, __orig))");
                code.AppendLineAt(3, "{");
                if (settable)
                {
                    code.AppendLineAt(4, "live." + prop + " = __orig!;");
                }
                else
                {
                    code.AppendLineAt(
                        4,
                        "if (__cur is global::System.Collections.Generic.IList<"
                            + elementName
                            + "> __curList && __bak is not null && !__curList.IsReadOnly) { __curList.Clear(); foreach (var __rb in __bak) __curList.Add(__rb); }"
                    );
                }
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else if (__cur is global::System.Collections.Generic.IList<"
                        + elementName
                        + "> __sameList && __bak is not null && !__sameList.IsReadOnly)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(
                    4,
                    "if (__sameList.Count != __bak.Count) { __sameList.Clear(); foreach (var __rb in __bak) __sameList.Add(__rb); }"
                );
                code.AppendLineAt(
                    4,
                    "else { var __diff = false; for (var __bi = 0; __bi < __bak.Count; __bi++) if (!object.ReferenceEquals(__sameList[__bi], __bak[__bi])) { __diff = true; break; } if (__diff) { __sameList.Clear(); foreach (var __rb in __bak) __sameList.Add(__rb); } }"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(3, "}");
            }
            else if (
                member.ChildModel is not null
                && !SparseFragmentPatchEmitter.IsCollectionPatch(member)
                && member.ChildModel.Value.IsFragmentModel
                && !member.Property.IsReadOnly
                && !member.Property.IsInitOnly
            )
            {
                code.AppendLineAt(3, "live." + prop + " = __origChild" + member.Id + "!;");
            }
        }
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
        // Nullable element annotations ride the working list so indexable casts
        // never fail on variance.
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
        // Collection scope matches the correlation scope so sibling
        // collections never cross-target a reused Guid.
        code.AppendLineAt(
            3,
            "var __collectionScope = string.IsNullOrEmpty(__scope) ? \""
                + prop
                + "\" : __scope + \"."
                + prop
                + "\";"
        );
        // Detached staging: the live list is never aliased, so late
        // shape failures return before any mutation.
        code.AppendLineAt(
            3,
            "var __backing = __liveList as global::System.Collections.Generic.IList<"
                + elementName
                + ">;"
        );
        code.AppendLineAt(
            3,
            "var __working = new global::System.Collections.Generic.List<"
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
                        "if (!__t.HasValue || __t.Value == global::System.Guid.Empty) return __Fail(\"The live model contains an unassigned element without a temporary identity.\");"
                    );
                    code.AppendLineAt(
                        5,
                        "if (!map.TryGetValue((__collectionScope, __t.Value), out var __persisted)) continue;"
                    );
                    code.AppendLineAt(
                        5,
                        "var __edit = __transition?.GetTemporaryChange(__t.Value)?.Edit;"
                    );
                    code.AppendLineAt(5, elementType + " __merged;");
                    // The live row carries no divergent local state, so the
                    // server state (identity plus values it computed) decides.
                    code.AppendLineAt(
                        5,
                        "if (__edit is null || __edit.IsEmpty) __merged = "
                            + elementType
                            + ".Fragment.From(("
                            + elementType
                            + ")__persisted!).ToModel();"
                    );
                    code.AppendLineAt(5, "else");
                    code.AppendLineAt(5, "{");
                    // The edit sits on the submitted temporary state, so
                    // applying it to the assigned persisted state goes stale.
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
                        "if (!__applied.IsPresent || __applied.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
                    );
                    code.AppendLineAt(6, "__merged = __applied.Value.ToModel();");
                    code.AppendLineAt(5, "}");
                    code.AppendLineAt(5, "__working[__i] = __merged; __dirty = true;");
                }
                else
                {
                    code.AppendLineAt(
                        5,
                        "return __Fail(\"The live model contains an unassigned element without temporary support.\");"
                    );
                }
                code.AppendLineAt(4, "}");
                code.AppendLineAt(4, "else");
                code.AppendLineAt(4, "{");
                SparseTemporaryKeyElementEmitter.AppendKeyedAssignedElement(
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
                SparseTemporaryKeyElementEmitter.AppendKeyedAssignedElement(
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
            // Keyless keyed members cannot correlate; nested recursion still
            // applies.
            code.AppendLineAt(
                4,
                "var __elemScope = __collectionScope + \"[\" + __i.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \"]\";"
            );
            code.AppendLineAt(
                4,
                "var __recurseError = "
                    + elementType
                    + ".__SparseRetargetTemps("
                    + eUse
                    + ", null, null, map, null, __elemScope);"
            );
            code.AppendLineAt(4, "if (__recurseError is not null) return __Fail(__recurseError);");
        }
        code.AppendLineAt(3, "}");
        SparseTemporaryKeyElementEmitter.AppendKeyedMembershipReconcile(
            code,
            member,
            elementType,
            keyType,
            keyProp,
            tempProp,
            unassigned,
            optionalFragment,
            elementRef2,
            elementNullableStruct2
        );
        AppendRetargetWriteback(code, member, modelIsReferenceType);
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "}");
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
        // Atomic commit: preflight every shape constraint before touching the
        // live list, then publish the detached staging in one step.
        var elementName = member.Collection.ElementType.Name;
        var conversion = member.Property.Type.Name.TrimEnd().EndsWith("[]")
            ? "new global::System.Collections.Generic.List<"
                + elementName
                + ">(__working).ToArray()"
            : "new global::System.Collections.Generic.List<" + elementName + ">(__working)";
        code.AppendLineAt(3, "if (__dirty)");
        code.AppendLineAt(3, "{");
        if (!writeable)
        {
            code.AppendLineAt(4, "_ = __backing;");
            code.AppendLineAt(
                4,
                "return __Fail(\"The live collection shape cannot be reconciled in place.\");"
            );
        }
        else
        {
            code.AppendLineAt(4, "if (__backing is not null)");
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__backing.IsReadOnly) return __Fail(\"The live collection shape cannot be reconciled in place.\");"
            );
            code.AppendLineAt(5, "__backing.Clear();");
            code.AppendLineAt(5, "foreach (var __commit in __working) __backing.Add(__commit);");
            code.AppendLineAt(4, "}");
            // Arrays never reach the backing branch as writable lists.
            code.AppendLineAt(4, "else live." + prop + " = " + conversion + ";");
        }
        code.AppendLineAt(3, "}");
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
        // The ?. accessors above always yield nullable locals.
        var perAccess = childRef ? "__childPer!" : "__childPer.Value";
        // Child live value with nullability normalized; server-side drops with
        // local edits fail deterministically instead of losing edits silently.
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            2,
            "var __childScope"
                + member.Id
                + " = string.IsNullOrEmpty(__scope) ? \""
                + prop
                + "\" : __scope + \"."
                + prop
                + "\";"
        );
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
            "if (__childPending is not null && !__childPending.IsEmpty) return __Fail(\"The server dropped a value with pending local edits.\");"
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
                    + ", __childSub, __childPer, map, __childPending, __childScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(3, "if (__dropError is not null) return __Fail(__dropError);");
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
                + ", __childSub, __childPer, map, __childPending, __childScope"
                + member.Id
                + ");"
        );
        code.AppendLineAt(3, "if (__addError is not null) return __Fail(__addError);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "else");
        code.AppendLineAt(2, "{");
        if (modelIsReferenceType && settable)
        {
            code.AppendLineAt(
                3,
                "var __mergedChildBase = " + childFrag + ".From(" + perAccess + ");"
            );
            // Pending edits sit on the submitted state while the base moved to
            // the persisted state.
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
                "if (!__mergedChildApplied.IsPresent || __mergedChildApplied.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
            );
            code.AppendLineAt(4, "__mergedChildAdvanced = __mergedChildApplied;");
            code.AppendLineAt(3, "}");
            code.AppendLineAt(
                3,
                "if (!__mergedChildAdvanced.IsPresent || __mergedChildAdvanced.Value is null) return __Fail(\"Reconciling produced an invalid element.\");"
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
                    + ", __childSub, __childPer, map, __childPending, __childScope"
                    + member.Id
                    + ");"
            );
            code.AppendLineAt(3, "if (__childError is not null) return __Fail(__childError);");
        }
        code.AppendLineAt(2, "}");
        if (childRef || childNullableValue)
        {
            code.AppendLineAt(2, "}");
        }
        code.AppendLineAt(2, "}");
    }
}
