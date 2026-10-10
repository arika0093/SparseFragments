using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Orders split-mode Fragment surface public-first.</summary>
/// <remarks>
/// Extracted from <c>SparseFragmentEmitter</c> to keep that file under the
/// size guideline; it owns only the public-first assembly of the Fragment
/// facade (public tail, internal bridges, private constructor and JSON
/// shell) with no semantic changes.
/// </remarks>
internal static class SparseFragmentSurfaceOrderingEmitter
{
    /// <summary>Emits the split-mode public fragment tail after Diff.</summary>
    /// <param name="code">Surface target builder.</param>
    /// <param name="modelType">Model type name.</param>
    /// <param name="members">Analyzed members.</param>
    /// <param name="modelIsReferenceType">Whether the model is a reference type.</param>
    /// <param name="runtime">Runtime dialect.</param>
    /// <param name="patchDialect">Patch dialect.</param>
    /// <param name="features">Emission features.</param>
    /// <param name="implementationBuilder">Implementation target.</param>
    /// <param name="jsonConverterQualifiedName">Qualified converter name.</param>
    /// <param name="jsonConverterSimpleName">Simple converter name.</param>
    /// <param name="generatedAccessibility">Fragment accessibility.</param>
    internal static void AppendFragmentPublicTail(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseEmissionFeatures features,
        SharedIndentedBuilder? implementationBuilder,
        string? jsonConverterQualifiedName,
        string? jsonConverterSimpleName,
        string generatedAccessibility
    )
    {
        var writableMembers = members
            .Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly)
            .ToImmutableArray();
        var canWriteInPlace = modelIsReferenceType && writableMembers.Length == members.Length;
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        code.AppendLineAt(
            2,
            "/// <summary>Creates a mutable builder seeded from this fragment.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The seeded builder.</returns>");
        code.AppendLineAt(2, "public FragmentBuilder ToBuilder() => new(this);");
        if (features.EmitJsonConverters)
        {
            if (
                implementationBuilder is not null
                && jsonConverterQualifiedName is not null
                && jsonConverterSimpleName is not null
            )
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonFacade(
                    code,
                    jsonConverterQualifiedName
                );
                SparseFragmentJsonEmitter.AppendConverter(
                    implementationBuilder,
                    members,
                    runtime.OptionalType,
                    isStandalone: true,
                    converterClassName: jsonConverterSimpleName,
                    converterAccessibility: generatedAccessibility,
                    sealedConverter: false
                );
                implementationBuilder.AppendLine();
            }
            else
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(
                    code,
                    members,
                    runtime.OptionalType
                );
            }
        }
    }

    /// <summary>Emits split-mode Fragment surface in public-first order.</summary>
    /// <remarks>
    /// Facade overloads keep public members ahead of internal bridges; member
    /// caches, equality helpers and the private projection constructor trail
    /// the public tail, with the private JSON shell last.
    /// </remarks>
    internal static void AppendFragmentSplitSurface(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        bool modelIsReferenceType,
        bool usesPocoCloning,
        SparseFragmentCoreEmitter core,
        SparseFragmentExpressions expressions,
        SparseRuntimeDialect runtime,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        SparseEmissionFeatures features,
        SharedIndentedBuilder? implementationBuilder,
        string? jsonConverterQualifiedName,
        string? jsonConverterSimpleName,
        string generatedAccessibility,
        string operationsType,
        bool canApplyPatchInPlace
    )
    {
        core.AppendDiff(code, modelType, members, modelIsReferenceType, operationsType);
        core.AppendFragmentClonePublic(code, members, usesPocoCloning);
        var writableMembers = members
            .Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly)
            .ToImmutableArray();
        var canWriteInPlace = modelIsReferenceType && writableMembers.Length == members.Length;
        SparseFragmentPatchEmitter.AppendFragmentMethods(
            code,
            modelType,
            runtime.Namespace,
            members,
            canWriteInPlace,
            patchDialect.WriteContract,
            features
        );
        code.AppendLineAt(
            2,
            "/// <summary>Creates a mutable builder seeded from this fragment.</summary>"
        );
        code.AppendLineAt(2, "/// <returns>The seeded builder.</returns>");
        code.AppendLineAt(2, "public FragmentBuilder ToBuilder() => new(this);");
        if (features.EmitJsonConverters)
        {
            if (
                implementationBuilder is not null
                && jsonConverterQualifiedName is not null
                && jsonConverterSimpleName is not null
            )
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonAccessor(code);
                SparseFragmentJsonEmitter.AppendConverter(
                    implementationBuilder,
                    members,
                    runtime.OptionalType,
                    isStandalone: true,
                    converterClassName: jsonConverterSimpleName,
                    converterAccessibility: generatedAccessibility,
                    sealedConverter: false
                );
                implementationBuilder.AppendLine();
            }
            else
            {
                SparseFragmentJsonEmitter.AppendStandaloneFragmentJson(
                    code,
                    members,
                    runtime.OptionalType
                );
            }
        }

        // Internal group trails the public tail.
        core.AppendMemberCaches(
            code,
            members,
            runtime.MergeStrategyType,
            SparseFragmentPatchEmitter.GetRebasePolicyType(patchDialect),
            patchDialect.RebasePolicyField
        );
        SparseFragmentCoreEmitter.AppendFromModelInternalFacade(code, modelType, operationsType);
        SparseFragmentCoreEmitter.AppendDiffInternalFacade(code, modelType, operationsType);
        core.AppendFragmentCloneInternalMethod(code);
        AppendFragmentEquality(code, members, runtime.OptionalType, expressions, core);
        var writableMembersSplit = members
            .Where(static member => !member.Property.IsReadOnly && !member.Property.IsInitOnly)
            .ToImmutableArray();
        var canWriteInPlaceSplit =
            modelIsReferenceType && writableMembersSplit.Length == members.Length;
        if (canWriteInPlaceSplit || (features.EmitPatch && canApplyPatchInPlace))
        {
            AppendWritableMemberWriter(code, modelType, writableMembersSplit);
        }

        // Private group trails internal helpers: projection constructor then JSON shell.
        core.AppendFragmentClonePrivateCtor(code, members);
        if (
            features.EmitJsonConverters
            && implementationBuilder is not null
            && jsonConverterQualifiedName is not null
        )
        {
            SparseFragmentJsonEmitter.AppendStandaloneFragmentJsonShell(
                code,
                jsonConverterQualifiedName
            );
        }
    }

    internal static void AppendWritableMemberWriter(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members
    )
    {
        code.AppendLineAt(2, "internal void __SparseWriteWritableTo(" + modelType + " model)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
        );
        if (!members.IsEmpty)
        {
            code.AppendLineAt(3, "var __sparse_updated = ToModel();");
        }

        foreach (var member in members)
        {
            var property = SparseNaming.EscapeIdentifier(member.Property.Name);
            if (
                member.Collection.Kind == SparseCollectionKind.List
                && member.Collection.CloneKind == SparseCloneCollectionKind.List
            )
            {
                var listType =
                    "global::System.Collections.Generic.List<"
                    + member.Collection.ElementType.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + property
                        + " is "
                        + listType
                        + " __sparse_list"
                        + member.Id
                        + " && __sparse_updated."
                        + property
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_list" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "__sparse_list" + member.Id + ".AddRange(__sparse_updated." + property + ");"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + property + " = __sparse_updated." + property + "!;"
                );
            }
            else if (member.Collection.IsDictionary)
            {
                var dictionaryType =
                    "global::System.Collections.Generic.Dictionary<"
                    + member.Collection.ElementType.Name
                    + ", "
                    + member.Collection.ValueType!.Value.Name
                    + ">";
                code.AppendLineAt(
                    3,
                    "if (model."
                        + property
                        + " is "
                        + dictionaryType
                        + " __sparse_dict"
                        + member.Id
                        + " && __sparse_updated."
                        + property
                        + " is not null)"
                );
                code.AppendLineAt(3, "{");
                code.AppendLineAt(4, "__sparse_dict" + member.Id + ".Clear();");
                code.AppendLineAt(
                    4,
                    "foreach (var __sparse_pair"
                        + member.Id
                        + " in __sparse_updated."
                        + property
                        + ") __sparse_dict"
                        + member.Id
                        + ".Add(__sparse_pair"
                        + member.Id
                        + ".Key, __sparse_pair"
                        + member.Id
                        + ".Value);"
                );
                code.AppendLineAt(3, "}");
                code.AppendLineAt(
                    3,
                    "else model." + property + " = __sparse_updated." + property + "!;"
                );
            }
            else
            {
                code.AppendLineAt(
                    3,
                    "model." + property + " = __sparse_updated." + property + "!;"
                );
            }
        }

        code.AppendLineAt(2, "}");
    }

    internal static void AppendFragmentEquality(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        string optionalType,
        SparseFragmentExpressions expressions,
        SparseFragmentCoreEmitter core
    )
    {
        code.CancellationToken.ThrowIfCancellationRequested();
        code.AppendLineAt(
            2,
            "internal static bool __SparseAreEqual("
                + optionalType
                + "<Fragment?> left, "
                + optionalType
                + "<Fragment?> right)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "if (!left.IsPresent) return !right.IsPresent;");
        code.AppendLineAt(3, "if (!right.IsPresent) return false;");
        code.AppendLineAt(
            3,
            "if (global::System.Object.ReferenceEquals(left.Value, right.Value)) return true;"
        );
        code.AppendLineAt(3, "if (left.Value is null || right.Value is null) return false;");
        if (members.IsEmpty)
        {
            code.AppendLineAt(3, "return true;");
        }
        else
        {
            code.AppendIndent(3).Append("return ");
            for (var index = 0; index < members.Length; index++)
            {
                if (index > 0)
                {
                    code.Append(" && ");
                }

                var name = SparseNaming.EscapeIdentifier(members[index].Property.Name);
                code.Append("__SparseEqual_")
                    .Append(members[index].Id)
                    .Append("(left.Value.")
                    .Append(name)
                    .Append(", right.Value.")
                    .Append(name)
                    .Append(")");
            }

            code.AppendLine(";");
        }

        code.AppendLineAt(2, "}");
        foreach (var member in members)
        {
            var valueType = SparseFragmentEmitHelpers.FragmentValueType(member);
            code.AppendIndent(2)
                .Append("internal static bool __SparseEqual_")
                .Append(member.Id)
                .Append("(")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .Append("> left, ")
                .Append(optionalType)
                .Append("<")
                .Append(valueType)
                .AppendLine("> right)");
            code.AppendLineAt(2, "{");
            code.AppendLineAt(3, "if (!left.IsPresent) return !right.IsPresent;");
            code.AppendLineAt(3, "if (!right.IsPresent) return false;");
            string equality;
            if (member.ChildModel is not null)
            {
                equality = member.ChildFragmentType + ".__SparseAreEqual(left, right)";
            }
            else if (member.MergeStrategyType is not null)
            {
                equality = core.MergeStrategyField(member) + ".AreEqual(left.Value, right.Value)";
            }
            else if (member.ComparisonComparerType is not null)
            {
                equality =
                    SparseFragmentEmitHelpers.ComparisonComparerField(member)
                    + ".Equals(left.Value, right.Value)";
            }
            else
            {
                equality = expressions.ValueEqualityExpression(member, "left.Value", "right.Value");
            }

            code.AppendLineAt(3, "return " + equality + ";");
            code.AppendLineAt(2, "}");
        }

        code.AppendLine();
    }
}
