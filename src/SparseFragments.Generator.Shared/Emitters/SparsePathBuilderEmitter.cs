using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the model-aware fluent <c>SparsePath</c> navigation surface.</summary>
/// <remarks>
/// Each model gains a thin <c>T.SparsePath</c> entrypoint plus a generic
/// <c>SparsePaths&lt;TRoot&gt;</c> root builder and per-member collection
/// builders. Builders carry only navigation (no storage or helpers): leaves
/// are compile-time typed paths, so wrong key, value, or root types fail at
/// compile time on typed APIs. Navigation composes across models through the
/// root-preserving generic parameter, which also keeps recursive models finite.
/// </remarks>
internal static class SparsePathBuilderEmitter
{
    internal static void Append(
        SharedIndentedBuilder code,
        string modelType,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        var rootBuilder = "SparsePaths<" + modelType + ">";
        code.AppendLineAt(
            1,
            "/// <summary>Typed path navigation for '" + modelType + "'.</summary>"
        );
        code.AppendLineAt(
            1,
            "/// <remarks>For example <c>Order.SparsePath.Items.Key(id).Price</c>. Converts implicitly to the model-agnostic path for storage and lookup.</remarks>"
        );
        code.AppendLineAt(
            1,
            "public static "
                + rootBuilder
                + " SparsePath => new "
                + rootBuilder
                + "("
                + pathType
                + ".Root(typeof("
                + modelType
                + ")));"
        );
        code.AppendLineAt(1, "/// <summary>Root path builder for typed navigation.</summary>");
        code.AppendLineAt(
            1,
            "/// <typeparam name=\"TRoot\">The root model the path navigates from.</typeparam>"
        );
        code.AppendLineAt(1, "public sealed class SparsePaths<TRoot>");
        code.AppendLineAt(1, "{");
        // Public-first order: conversion and member navigation precede the
        // internal constructor, and the private backing field trails both.
        code.AppendLineAt(
            2,
            "/// <summary>Converts this builder to its model-agnostic path.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static implicit operator "
                + pathType
                + "(SparsePaths<TRoot> paths) => paths._path;"
        );
        var navigations = new SharedIndentedBuilder(code.CancellationToken)
        {
            IndentOffset = code.IndentOffset,
        };
        foreach (var member in members)
        {
            AppendMemberNavigation(navigations, member, pathType, dialect);
        }

        code.Append(navigations.ToString());
        code.AppendLineAt(2, "internal SparsePaths(" + pathType + " path) => _path = path;");
        code.AppendLineAt(2, "private readonly " + pathType + " _path;");
        code.AppendLineAt(1, "}");
    }

    private static void AppendMemberNavigation(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string pathType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
            member.Property.Name,
            true
        );
        var access = "_path.Member(" + literal + ")";
        if (SparseChangeSetBasicsEmitter.IsNested(member))
        {
            var child = member.ChildModel!.Value;
            if (child.IsFragmentModel && !member.ChildIsStructural)
            {
                var builder = child.NonNullableName + ".SparsePaths<TRoot>";
                code.AppendLineAt(
                    2,
                    "/// <summary>Gets the path builder for '" + name + "'.</summary>"
                );
                code.AppendLineAt(
                    2,
                    "public " + builder + " " + name + " => new " + builder + "(" + access + ");"
                );
                return;
            }

            AppendLeaf(code, name, access, member.Property.Type.Name, dialect);
            return;
        }

        if (
            SparseChangeSetBasicsEmitter.IsKeyed(member)
            || SparseChangeSetBasicsEmitter.IsDict(member)
            || SparseChangeSetBasicsEmitter.IsSet(member)
        )
        {
            AppendCollectionNavigation(code, member, pathType, dialect);
            return;
        }

        AppendLeaf(code, name, access, member.Property.Type.Name, dialect);
    }

    private static void AppendLeaf(
        SharedIndentedBuilder code,
        string name,
        string access,
        string valueType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var typed = SparseFragmentPatchEmitter.GetTypedPathType(dialect, "TRoot", valueType);
        code.AppendLineAt(2, "/// <summary>Gets the path to '" + name + "'.</summary>");
        code.AppendLineAt(
            2,
            "public " + typed + " " + name + " => new " + typed + "(" + access + ");"
        );
    }

    private static void AppendCollectionNavigation(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        string pathType,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var name = SparseNaming.EscapeIdentifier(member.Property.Name);
        var literal = Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(
            member.Property.Name,
            true
        );
        var builderName = "Sparse" + name + "Paths";
        code.AppendLineAt(2, "/// <summary>Gets the path builder for '" + name + "'.</summary>");
        code.AppendLineAt(
            2,
            "public "
                + builderName
                + " "
                + name
                + " => new "
                + builderName
                + "(_path.Member("
                + literal
                + "));"
        );
        code.AppendLineAt(2, "/// <summary>Collection path builder for '" + name + "'.</summary>");
        code.AppendLineAt(2, "public sealed class " + builderName);
        code.AppendLineAt(2, "{");
        // Public-first order, matching the root builder above.
        code.AppendLineAt(
            3,
            "/// <summary>Converts this builder to its model-agnostic path.</summary>"
        );
        code.AppendLineAt(
            3,
            "public static implicit operator "
                + pathType
                + "("
                + builderName
                + " paths) => paths._path;"
        );
        var entries = new SharedIndentedBuilder(code.CancellationToken)
        {
            IndentOffset = code.IndentOffset,
        };
        if (SparseChangeSetBasicsEmitter.IsSet(member))
        {
            var element = member.Collection.ElementType.Name;
            var typed = SparseFragmentPatchEmitter.GetTypedPathType(dialect, "TRoot", element);
            entries.AppendLineAt(3, "/// <summary>Gets the path to a set element.</summary>");
            entries.AppendLineAt(
                3,
                "public "
                    + typed
                    + " Element("
                    + element
                    + " item) => new "
                    + typed
                    + "(_path.Key(item));"
            );
        }
        else if (SparseChangeSetBasicsEmitter.IsDict(member))
        {
            AppendKeyMethod(
                entries,
                member.Collection.ElementType.Name,
                ElementNavigation(member.Collection.ValueType!.Value, dialect)
            );
        }
        else
        {
            // Only keyed sequences reach here: dictionaries and sets return
            // above, and other shapes stay scalar leaves.
            var navigation = ElementNavigation(member.Collection.ElementType, dialect);
            AppendKeyMethod(entries, member.Collection.KeyTypeName ?? "object?", navigation);
            if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
            {
                AppendTemporaryKeyMethod(entries, navigation);
            }
            AppendAtMethod(entries, navigation);
        }

        code.Append(entries.ToString());
        code.AppendLineAt(
            3,
            "internal " + builderName + "(" + pathType + " path) => _path = path;"
        );
        code.AppendLineAt(3, "private readonly " + pathType + " _path;");
        code.AppendLineAt(2, "}");
    }

    private static void AppendKeyMethod(
        SharedIndentedBuilder code,
        string keyType,
        string navigation
    )
    {
        code.AppendLineAt(
            3,
            "/// <summary>Gets the path to the entry with the given key.</summary>"
        );
        code.AppendLineAt(
            3,
            "public "
                + navigation
                + " Key("
                + keyType
                + " key) => new "
                + navigation
                + "(_path.Key(key));"
        );
    }

    private static void AppendTemporaryKeyMethod(SharedIndentedBuilder code, string navigation)
    {
        code.AppendLineAt(
            3,
            "/// <summary>Gets the path to the unassigned entry with the given temporary identity.</summary>"
        );
        code.AppendLineAt(
            3,
            "/// <remarks>Temporary segments never equal key segments, even when the permanent key type is <c>Guid</c>.</remarks>"
        );
        code.AppendLineAt(
            3,
            "public "
                + navigation
                + " TemporaryKey(global::System.Guid temporaryKey) => new "
                + navigation
                + "(_path.TemporaryKey(temporaryKey));"
        );
    }

    private static void AppendAtMethod(SharedIndentedBuilder code, string navigation)
    {
        code.AppendLineAt(
            3,
            "/// <summary>Gets the path to the entry at the given position.</summary>"
        );
        code.AppendLineAt(
            3,
            "public " + navigation + " At(int index) => new " + navigation + "(_path.At(index));"
        );
    }

    private static string ElementNavigation(
        SparseTypeModel element,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        if (element.IsFragmentModel)
        {
            return element.NonNullableName + ".SparsePaths<TRoot>";
        }

        return SparseFragmentPatchEmitter.GetTypedPathType(dialect, "TRoot", element.Name);
    }
}
