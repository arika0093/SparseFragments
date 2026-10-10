using System.Collections.Immutable;
using static SparseFragments.Generator.Shared.SparseChangeSetBasicsEmitter;

namespace SparseFragments.Generator.Shared;

/// <summary>Collection branches of the resolution fragment applier (issue #203).</summary>
/// <remarks>Keyed sequences, dictionaries, and set/whole-collection members. Scalar, nested, dispatch, and slot-check helpers stay on <see cref="SparseResolutionApplyEmitter"/>.</remarks>
internal static class SparseResolutionCollectionEmitter
{
    internal static void AppendKeyHelpers(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members
    )
    {
        foreach (var member in members)
        {
            if (!IsKeyed(member))
            {
                continue;
            }

            var keyType = KeyTypeOf(member);
            var elementType = ElementTypeOf(member);
            var keys = member.Collection.KeyPropertyNames;
            string extraction;
            if (member.Collection.KeyKind == SparseKeyKind.Interface)
            {
                extraction =
                    "element.SparseKey"
                    + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : string.Empty)
                    + ";";
            }
            else if (!keys.IsDefault && keys.Length == 1)
            {
                extraction =
                    "element."
                    + SparseNaming.EscapeIdentifier(keys[0])
                    + (SparseKeyedCollectionEmitter.HasUnassignedKey(member) ? "!" : string.Empty)
                    + ";";
            }
            else
            {
                var components = keys.IsDefault ? ImmutableArray<string>.Empty : keys;
                extraction =
                    "("
                    + string.Join(
                        ", ",
                        components.Select(static key =>
                            "element." + SparseNaming.EscapeIdentifier(key)
                        )
                    )
                    + ");";
            }

            body.AppendLineAt(
                2,
                "/// <summary>Extracts the stable key of one element (resolution infrastructure).</summary>"
            );
            body.AppendLineAt(
                2,
                "private static "
                    + keyType
                    + " __SparseResolutionKeyOf_"
                    + member.Id
                    + "("
                    + elementType
                    + " element)"
            );
            body.AppendLineAt(2, "{");
            body.AppendLineAt(
                3,
                "if ((object?)element is null) throw new global::System.InvalidOperationException(\"Null elements have no stable key.\");"
            );
            body.AppendLineAt(3, "return " + extraction);
            body.AppendLineAt(2, "}");
        }
    }

    internal static void AppendKeyedBranch(
        SharedIndentedBuilder body,
        SparseMemberModel member,
        string esc,
        string name,
        string runtime
    )
    {
        var valueType = FragmentValueType(member);
        var optional = runtime + "Optional<" + valueType + ">";
        var keyType = KeyTypeOf(member);
        var elementType = ElementTypeOf(member);
        var elementFragment = ElementFragmentOf(member);
        var elementChangeSet = ElementChangeSetOf(member);
        var listType = "global::System.Collections.Generic.List<" + elementType + ">";
        var readOnlyListType =
            "global::System.Collections.Generic.IReadOnlyList<" + elementType + ">";
        var orderListType = "global::System.Collections.Generic.List<" + keyType + ">";
        var segmentKind = runtime + "SparsePathSegmentKind";
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var id = member.Id;
        var slotIsArray = valueType.EndsWith("[]", System.StringComparison.Ordinal);
        var slotValue = slotIsArray ? "list" + id + ".ToArray()" : "list" + id;
        var keyOf = "__SparseResolutionKeyOf_" + id;
        var elementIsRef = member.Collection.ElementType.IsReferenceType;

        // Member-level paths address either the whole collection or, for keyed
        // sequences, the order transition: both share the canonical member path
        // (#202), so the desired runtime type selects the branch. Order carries
        // a key list while wholesale carries the collection value.
        body.AppendLineAt(4, "if (offset + 1 == path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            4,
            "if (desired.IsPresent && desired.Value is "
                + orderListType
                + " orderDesired"
                + id
                + ")"
        );
        body.AppendLineAt(4, "{");
        AppendKeyedOrder(
            body,
            esc,
            name,
            runtime,
            optional,
            valueType,
            keyType,
            elementType,
            listType,
            readOnlyListType,
            slotValue,
            keyOf,
            elementIsRef,
            id,
            member.Collection.IsCompositeKey,
            useDesiredVariable: true
        );
        body.AppendLineAt(4, "}");
        SparseResolutionApplyEmitter.AppendCheckedSlot(
            body,
            "builder." + esc,
            optional,
            valueType,
            name,
            id
        );
        body.AppendLineAt(4, "updated = builder.Build();");
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "if (offset + 2 > path.Depth)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The resolution path is too short for member '" + name + "'.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var keySegment" + id + " = path.Segments[offset + 1];");
        body.AppendLineAt(4, "var rawKey" + id + " = keySegment" + id + ".Key;");
        body.AppendLineAt(
            4,
            "if (keySegment"
                + id
                + ".Kind != "
                + segmentKind
                + ".Key || rawKey"
                + id
                + " is null || !typeof("
                + keyType
                + ").IsInstanceOfType(rawKey"
                + id
                + "))"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"The key segment for member '" + name + "' has an unexpected type.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var key" + id + " = (" + keyType + ")rawKey" + id + ";");
        if (!member.Collection.ElementType.IsFragmentModel)
        {
            body.AppendLineAt(4, "if (offset + 2 != path.Depth)");
            body.AppendLineAt(4, "{");
            body.AppendLineAt(
                5,
                "error = \"Member '" + name + "' has no resolvable element leaves.\";"
            );
            body.AppendLineAt(5, "return false;");
            body.AppendLineAt(4, "}");
        }

        body.AppendLineAt(
            4,
            "if (!builder." + esc + ".IsPresent || builder." + esc + ".Value is null)"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (!desired.IsPresent)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "updated = builder.Build();");
        body.AppendLineAt(6, "return true;");
        body.AppendLineAt(5, "}");
        AppendKeyedAbsentAdd(
            body,
            esc,
            optional,
            elementType,
            elementFragment,
            id,
            name,
            slotIsArray,
            member.Collection.ElementType.IsFragmentModel
        );
        body.AppendLineAt(4, "}");
        body.AppendLineAt(
            4,
            "if (builder." + esc + ".Value is not " + readOnlyListType + " source" + id + ")"
        );
        body.AppendLineAt(4, "{");
        body.AppendLineAt(
            5,
            "error = \"Member '" + name + "' has an unsupported collection shape.\";"
        );
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "var list" + id + " = new " + listType + "(source" + id + ");");
        body.AppendLineAt(4, "var index" + id + " = -1;");
        body.AppendLineAt(
            4,
            "for (var scan" + id + " = 0; scan" + id + " < list" + id + ".Count; scan" + id + "++)"
        );
        body.AppendLineAt(4, "{");
        if (elementIsRef)
        {
            body.AppendLineAt(5, "var candidate" + id + " = list" + id + "[scan" + id + "];");
            body.AppendLineAt(5, "if (candidate" + id + " is null) continue;");
        }
        else
        {
            body.AppendLineAt(5, "var candidate" + id + " = list" + id + "[scan" + id + "];");
        }

        body.AppendLineAt(
            5,
            "if ("
                + comparer
                + ".Equals("
                + keyOf
                + "(candidate"
                + id
                + "), key"
                + id
                + ")) { index"
                + id
                + " = scan"
                + id
                + "; break; }"
        );
        body.AppendLineAt(4, "}");
        if (member.Collection.ElementType.IsFragmentModel)
        {
            body.AppendLineAt(4, "if (offset + 2 == path.Depth)");
            body.AppendLineAt(4, "{");
            AppendKeyedElementWholesale(
                body,
                esc,
                optional,
                name,
                elementType,
                elementFragment,
                id,
                slotValue,
                elementIsModel: true
            );
            body.AppendLineAt(4, "}");
            body.AppendLineAt(4, "if (index" + id + " < 0)");
            body.AppendLineAt(4, "{");
            body.AppendLineAt(
                5,
                "error = \"No element with the key was found in member '" + name + "'.\";"
            );
            body.AppendLineAt(5, "return false;");
            body.AppendLineAt(4, "}");
            if (elementIsRef)
            {
                body.AppendLineAt(4, "if (list" + id + "[index" + id + "] is null)");
                body.AppendLineAt(4, "{");
                body.AppendLineAt(
                    5,
                    "error = \"The element for member '" + name + "' is missing.\";"
                );
                body.AppendLineAt(5, "return false;");
                body.AppendLineAt(4, "}");
            }

            body.AppendLineAt(
                4,
                "var elementFragment"
                    + id
                    + " = "
                    + elementFragment
                    + ".From(list"
                    + id
                    + "[index"
                    + id
                    + "]!);"
            );
            body.AppendLineAt(
                4,
                "if (!"
                    + elementChangeSet
                    + ".__SparseTryApplyResolution(elementFragment"
                    + id
                    + ", path, offset + 2, desired, out var updatedElement"
                    + id
                    + ", out error)) return false;"
            );
            body.AppendLineAt(4, "if (updatedElement" + id + " is null)");
            body.AppendLineAt(4, "{");
            body.AppendLineAt(5, "error = \"The element resolution produced no fragment.\";");
            body.AppendLineAt(5, "return false;");
            body.AppendLineAt(4, "}");
            body.AppendLineAt(
                4,
                "list" + id + "[index" + id + "] = updatedElement" + id + ".ToModel();"
            );
            body.AppendLineAt(
                4,
                "builder." + esc + " = " + optional + ".Present(" + slotValue + ");"
            );
            body.AppendLineAt(4, "updated = builder.Build();");
            body.AppendLineAt(4, "return true;");
        }
        else
        {
            body.AppendLineAt(4, "{");
            AppendKeyedElementWholesale(
                body,
                esc,
                optional,
                name,
                elementType,
                elementFragment,
                id,
                slotValue,
                elementIsModel: false
            );
            body.AppendLineAt(4, "}");
        }
    }

    private static void AppendKeyedAbsentAdd(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string elementType,
        string elementFragment,
        int id,
        string name,
        bool slotIsArray,
        bool elementIsModel
    )
    {
        var checkElement = SparseResolutionApplyEmitter.NonNullable(elementType);
        var seedSuffix = slotIsArray ? ".ToArray()" : string.Empty;
        body.AppendLineAt(5, "if (desired.Value is " + checkElement + " fresh" + id + ")");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "builder."
                + esc
                + " = "
                + optional
                + ".Present(new global::System.Collections.Generic.List<"
                + elementType
                + "> { fresh"
                + id
                + " }"
                + seedSuffix
                + ");"
        );
        body.AppendLineAt(6, "updated = builder.Build();");
        body.AppendLineAt(6, "return true;");
        body.AppendLineAt(5, "}");
        if (elementIsModel)
        {
            body.AppendLineAt(
                5,
                "if (desired.Value is " + elementFragment + " freshFragment" + id + ")"
            );
            body.AppendLineAt(5, "{");
            body.AppendLineAt(
                6,
                "builder."
                    + esc
                    + " = "
                    + optional
                    + ".Present(new global::System.Collections.Generic.List<"
                    + elementType
                    + "> { freshFragment"
                    + id
                    + ".ToModel() }"
                    + seedSuffix
                    + ");"
            );
            body.AppendLineAt(6, "updated = builder.Build();");
            body.AppendLineAt(6, "return true;");
            body.AppendLineAt(5, "}");
        }
        body.AppendLineAt(
            5,
            "error = \"Member '" + name + "' is absent and the resolution value cannot seed it.\";"
        );
        body.AppendLineAt(5, "return false;");
    }

    private static void AppendKeyedElementWholesale(
        SharedIndentedBuilder body,
        string esc,
        string optional,
        string name,
        string elementType,
        string elementFragment,
        int id,
        string slotValue,
        bool elementIsModel
    )
    {
        var checkElement = SparseResolutionApplyEmitter.NonNullable(elementType);
        body.AppendLineAt(5, "if (!desired.IsPresent)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "if (index" + id + " >= 0) list" + id + ".RemoveAt(index" + id + ");");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else if (desired.Value is null)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"Null elements are not supported for member '" + name + "'.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "else if (desired.Value is " + checkElement + " element" + id + ")");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "if (index" + id + " >= 0) list" + id + "[index" + id + "] = element" + id + ";"
        );
        body.AppendLineAt(6, "else list" + id + ".Add(element" + id + ");");
        body.AppendLineAt(5, "}");
        if (elementIsModel)
        {
            body.AppendLineAt(
                5,
                "else if (desired.Value is " + elementFragment + " wholeFragment" + id + ")"
            );
            body.AppendLineAt(5, "{");
            body.AppendLineAt(
                6,
                "var wholeModel"
                    + id
                    + " = wholeFragment"
                    + id
                    + ".ToModel(); if (index"
                    + id
                    + " >= 0) list"
                    + id
                    + "[index"
                    + id
                    + "] = wholeModel"
                    + id
                    + "; else list"
                    + id
                    + ".Add(wholeModel"
                    + id
                    + ");"
            );
            body.AppendLineAt(5, "}");
        }
        body.AppendLineAt(5, "else");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"The resolution value for member '" + name + "' has an unsupported type.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "builder." + esc + " = " + optional + ".Present(" + slotValue + ");");
        body.AppendLineAt(5, "updated = builder.Build();");
        body.AppendLineAt(5, "return true;");
    }

    private static void AppendKeyedOrder(
        SharedIndentedBuilder body,
        string esc,
        string name,
        string runtime,
        string optional,
        string valueType,
        string keyType,
        string elementType,
        string listType,
        string readOnlyListType,
        string slotValue,
        string keyOf,
        bool elementIsRef,
        int id,
        bool compositeKey,
        bool useDesiredVariable = false
    )
    {
        _ = runtime;
        _ = valueType;
        if (compositeKey)
        {
            body.AppendLineAt(
                5,
                "error = \"Member '"
                    + name
                    + "' has composite keys; order resolves at the member level.\";"
            );
            body.AppendLineAt(5, "return false;");
            return;
        }

        // Order resolutions carry the desired key list. When reached through the
        // member-level branch the list is already matched as orderDesired; the
        // legacy whole-member order path below is retained for direct calls.
        var orderValue = useDesiredVariable ? "orderDesired" + id : "order" + id;
        if (!useDesiredVariable)
        {
            body.AppendLineAt(
                5,
                "if (desired.Value is not global::System.Collections.Generic.List<"
                    + keyType
                    + "> order"
                    + id
                    + ")"
            );
            body.AppendLineAt(5, "{");
            body.AppendLineAt(6, "error = \"The order resolution value must be a key list.\";");
            body.AppendLineAt(6, "return false;");
            body.AppendLineAt(5, "}");
        }

        body.AppendLineAt(
            5,
            "if (!builder." + esc + ".IsPresent || builder." + esc + ".Value is null)"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "error = \"Member '" + name + "' is absent and has no order.\";");
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "if (builder." + esc + ".Value is not " + readOnlyListType + " orderSource" + id + ")"
        );
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "error = \"Member '" + name + "' has an unsupported collection shape.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "var orderComparer"
                + id
                + " = global::System.Collections.Generic.EqualityComparer<"
                + keyType
                + ">.Default;"
        );
        body.AppendLineAt(
            5,
            "var reordered" + id + " = new " + listType + "(orderSource" + id + ".Count);"
        );
        body.AppendLineAt(5, "foreach (var orderKey" + id + " in " + orderValue + ")");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(6, "var orderMatches" + id + " = 0;");
        body.AppendLineAt(6, "var orderMatch" + id + " = default(" + elementType + ");");
        body.AppendLineAt(6, "foreach (var orderElement" + id + " in orderSource" + id + ")");
        body.AppendLineAt(6, "{");
        if (elementIsRef)
        {
            body.AppendLineAt(7, "if (orderElement" + id + " is null) continue;");
        }

        body.AppendLineAt(
            7,
            "if (orderComparer"
                + id
                + ".Equals("
                + keyOf
                + "(orderElement"
                + id
                + "), orderKey"
                + id
                + ")) { orderMatch"
                + id
                + " = orderElement"
                + id
                + "; orderMatches"
                + id
                + "++; }"
        );
        body.AppendLineAt(6, "}");
        body.AppendLineAt(6, "if (orderMatches" + id + " == 0)");
        body.AppendLineAt(6, "{");
        body.AppendLineAt(7, "error = \"The order references an unknown key.\";");
        body.AppendLineAt(7, "return false;");
        body.AppendLineAt(6, "}");
        body.AppendLineAt(6, "if (orderMatches" + id + " > 1)");
        body.AppendLineAt(6, "{");
        body.AppendLineAt(7, "error = \"The collection holds duplicate keys.\";");
        body.AppendLineAt(7, "return false;");
        body.AppendLineAt(6, "}");
        body.AppendLineAt(6, "reordered" + id + ".Add(orderMatch" + id + "!);");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(5, "foreach (var orderElement" + id + " in orderSource" + id + ")");
        body.AppendLineAt(5, "{");
        if (elementIsRef)
        {
            body.AppendLineAt(6, "if (orderElement" + id + " is null) continue;");
        }

        body.AppendLineAt(6, "var orderKnown" + id + " = false;");
        body.AppendLineAt(6, "foreach (var orderKey" + id + " in " + orderValue + ")");
        body.AppendLineAt(6, "{");
        body.AppendLineAt(
            7,
            "if (orderComparer"
                + id
                + ".Equals("
                + keyOf
                + "(orderElement"
                + id
                + "), orderKey"
                + id
                + ")) { orderKnown"
                + id
                + " = true; break; }"
        );
        body.AppendLineAt(6, "}");
        body.AppendLineAt(
            6,
            "if (!orderKnown" + id + ") reordered" + id + ".Add(orderElement" + id + ");"
        );
        body.AppendLineAt(5, "}");
        body.AppendLineAt(
            5,
            "builder."
                + esc
                + " = "
                + optional
                + ".Present("
                + slotValue.Replace("list" + id, "reordered" + id)
                + ");"
        );
        body.AppendLineAt(5, "updated = builder.Build();");
        body.AppendLineAt(5, "return true;");
    }
}
