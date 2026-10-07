using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits sparse keyed/dict member ChangeSet JSON read.</summary>
internal static class SparseChangeSetStjSparseMemberReadEmitter
{
    /// <summary>Emits sparse keyed/dict member ChangeSet JSON read.</summary>
    internal static void AppendChangeSetSparseMemberRead(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var id = member.Id;
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        var isKeyed = SparseKeyedCollectionEmitter.IsKeyedSequence(member);
        var keyType = SparseStjKeyHelpers.KeyTypeOf(member);
        var elementType = isKeyed
            ? SparseStjKeyHelpers.ElementTypeOf(member)
            : SparseStjKeyHelpers.ValueTypeOf(member);
        var trans = SparseStjKeyHelpers.SparseTransName(members, member);
        var comparer =
            "global::System.Collections.Generic.EqualityComparer<" + keyType + ">.Default";
        var hasPatch = isKeyed
            ? SparseStjKeyHelpers.HasElementPatch(member)
            : SparseStjKeyHelpers.HasValuePatch(member);
        string? elemCs;
        string? elemFrag;
        if (isKeyed)
        {
            elemCs = SparseStjKeyHelpers.SparseElementChangeSet(member);
            elemFrag = SparseStjKeyHelpers.SparseElementFragment(member);
        }
        else if (hasPatch)
        {
            elemCs = SparseStjKeyHelpers.SparseValueChangeSet(member);
            elemFrag = SparseStjKeyHelpers.SparseValueFragment(member);
        }
        else
        {
            elemCs = null;
            elemFrag = null;
        }
        var optElem = runtime + "Optional<" + elementType + ">";
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A member transition must be a JSON object.\");"
        );
        // Per-member sparse locals are predeclared at method scope
        // (__wb/__wa/__hb/__ha/__whole/__hasItems/__items[/__bO/__aO]).
        if (isKeyed)
        {
            code.AppendLineAt(5, "bool __hbO" + id + " = false; bool __haO" + id + " = false;");
        }
        code.AppendLineAt(5, "bool __hasIt" + id + " = false;");
        code.AppendLineAt(5, "while (reader.Read())");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a member-transition property name.\");"
        );
        code.AppendLineAt(6, "var __isB = reader.ValueTextEquals(\"before\");");
        code.AppendLineAt(6, "var __isA = !__isB && reader.ValueTextEquals(\"after\");");
        code.AppendLineAt(
            6,
            "var __isItems = !__isB && !__isA && reader.ValueTextEquals(\"items\");"
        );
        string orderDecl = isKeyed
            ? "var __isBO = !__isB && !__isA && !__isItems && reader.ValueTextEquals(\"beforeOrder\"); var __isAO = !__isB && !__isA && !__isItems && !__isBO && reader.ValueTextEquals(\"afterOrder\"); var __mUnknown = __isB || __isA || __isItems || __isBO || __isAO ? null : reader.GetString();"
            : "var __mUnknown = __isB || __isA || __isItems ? null : reader.GetString();";
        code.AppendLineAt(6, orderDecl);
        code.AppendLineAt(
            6,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of member transition.\");"
        );
        // before (whole).
        code.AppendLineAt(6, "if (__isB)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hb_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member before.\");"
        );
        code.AppendLineAt(7, "__hb_" + id + " = true;");
        code.AppendLineAt(7, "__wb_" + id + " = __SparseReadOpt_" + id + "(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isA)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__ha_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member after.\");"
        );
        code.AppendLineAt(7, "__ha_" + id + " = true;");
        code.AppendLineAt(7, "__wa_" + id + " = __SparseReadOpt_" + id + "(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isItems)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Duplicate member items.\");"
        );
        code.AppendLineAt(7, "__hasIt" + id + " = true;");
        code.AppendLineAt(
            7,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"Items must be an array.\");"
        );
        code.AppendLineAt(
            7,
            "__items" + id + " = new global::System.Collections.Generic.List<" + trans + ".Item>();"
        );
        code.AppendLineAt(
            7,
            "var __seenKeys"
                + id
                + " = new global::System.Collections.Generic.HashSet<"
                + keyType
                + ">("
                + comparer
                + ");"
        );
        code.AppendLineAt(7, "while (reader.Read())");
        code.AppendLineAt(7, "{");
        code.AppendLineAt(
            8,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break;"
        );
        code.AppendLineAt(
            8,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"An item must be a JSON object.\");"
        );
        // Item fields (indexes/reorder only for keyed sequences).
        if (isKeyed)
        {
            code.AppendLineAt(
                8,
                "bool __hK = false; bool __hKind = false; bool __hB = false; bool __hA = false; bool __hBi = false; bool __hAi = false; bool __hRe = false;"
            );
            code.AppendLineAt(8, keyType + " __k = default!;");
            code.AppendLineAt(8, "byte __kind = 0;");
            code.AppendLineAt(8, elementType + " __bv = default!;");
            code.AppendLineAt(8, elementType + " __av = default!;");
            code.AppendLineAt(8, "int __bi = -1; int __ai = -1; bool __re = false;");
        }
        else
        {
            code.AppendLineAt(
                8,
                "bool __hK = false; bool __hKind = false; bool __hB = false; bool __hA = false;"
            );
            code.AppendLineAt(8, keyType + " __k = default!;");
            code.AppendLineAt(8, "byte __kind = 0;");
            code.AppendLineAt(8, elementType + " __bv = default!;");
            code.AppendLineAt(8, elementType + " __av = default!;");
        }
        code.AppendLineAt(8, "while (reader.Read())");
        code.AppendLineAt(8, "{");
        code.AppendLineAt(
            9,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            9,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected an item property name.\");"
        );
        code.AppendLineAt(9, "var __isK = reader.ValueTextEquals(\"key\");");
        code.AppendLineAt(9, "var __isKind = !__isK && reader.ValueTextEquals(\"kind\");");
        code.AppendLineAt(
            9,
            "var __isBf = !__isK && !__isKind && reader.ValueTextEquals(\"before\");"
        );
        code.AppendLineAt(
            9,
            "var __isAf = !__isK && !__isKind && !__isBf && reader.ValueTextEquals(\"after\");"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                9,
                "var __isBi = !__isK && !__isKind && !__isBf && !__isAf && reader.ValueTextEquals(\"beforeIndex\");"
            );
            code.AppendLineAt(
                9,
                "var __isAi = !__isK && !__isKind && !__isBf && !__isAf && !__isBi && reader.ValueTextEquals(\"afterIndex\");"
            );
            code.AppendLineAt(
                9,
                "var __isRe = !__isK && !__isKind && !__isBf && !__isAf && !__isBi && !__isAi && reader.ValueTextEquals(\"reordered\");"
            );
            code.AppendLineAt(
                9,
                "var __iu = __isK || __isKind || __isBf || __isAf || __isBi || __isAi || __isRe ? null : reader.GetString();"
            );
        }
        else
        {
            code.AppendLineAt(
                9,
                "var __iu = __isK || __isKind || __isBf || __isAf ? null : reader.GetString();"
            );
        }
        code.AppendLineAt(
            9,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of item.\");"
        );
        code.AppendLineAt(
            9,
            "if (__isK) { if (__hK) throw new global::System.Text.Json.JsonException(\"Duplicate item key.\"); __hK = true; __k = __SparseReadKey_"
                + id
                + "(ref reader, options); }"
        );
        code.AppendLineAt(9, "else if (__isKind)");
        code.AppendLineAt(9, "{");
        code.AppendLineAt(
            10,
            "if (__hKind) throw new global::System.Text.Json.JsonException(\"Duplicate item kind.\"); __hKind = true;"
        );
        code.AppendLineAt(
            10,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.String) throw new global::System.Text.Json.JsonException(\"Item kind must be a string.\");"
        );
        code.AppendLineAt(
            10,
            "__kind = reader.ValueTextEquals(\"add\") ? (byte)1 : reader.ValueTextEquals(\"remove\") ? (byte)2 : reader.ValueTextEquals(\"edit\") ? (byte)3 : reader.ValueTextEquals(\"reorder\") ? (byte)4 : (byte)5;"
        );
        code.AppendLineAt(
            10,
            "if (__kind == 5) throw new global::System.Text.Json.JsonException(\"Unknown item kind.\");"
        );
        code.AppendLineAt(9, "}");
        code.AppendLineAt(
            9,
            "else if (__isBf) { if (__hB) throw new global::System.Text.Json.JsonException(\"Duplicate item before.\"); __hB = true; __bv = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + elementType
                + ">(options))!; }"
        );
        code.AppendLineAt(
            9,
            "else if (__isAf) { if (__hA) throw new global::System.Text.Json.JsonException(\"Duplicate item after.\"); __hA = true; __av = global::System.Text.Json.JsonSerializer.Deserialize(ref reader, GetMemberTypeInfo<"
                + elementType
                + ">(options))!; }"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                9,
                "else if (__isBi) { if (__hBi) throw new global::System.Text.Json.JsonException(\"Duplicate item beforeIndex.\"); __hBi = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out __bi)) throw new global::System.Text.Json.JsonException(\"Item beforeIndex must be an integer.\"); }"
            );
            code.AppendLineAt(
                9,
                "else if (__isAi) { if (__hAi) throw new global::System.Text.Json.JsonException(\"Duplicate item afterIndex.\"); __hAi = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out __ai)) throw new global::System.Text.Json.JsonException(\"Item afterIndex must be an integer.\"); }"
            );
            code.AppendLineAt(
                9,
                "else if (__isRe) { if (__hRe) throw new global::System.Text.Json.JsonException(\"Duplicate item reordered.\"); __hRe = true; if (reader.TokenType != global::System.Text.Json.JsonTokenType.True && reader.TokenType != global::System.Text.Json.JsonTokenType.False) throw new global::System.Text.Json.JsonException(\"Item reordered must be a boolean.\"); __re = reader.GetBoolean(); if (!__re) throw new global::System.Text.Json.JsonException(\"Item reordered must be true when present.\"); }"
            );
        }
        code.AppendLineAt(
            9,
            "else throw new global::System.Text.Json.JsonException(\"Unknown item property '\" + __iu + \"'.\");"
        );
        code.AppendLineAt(8, "}");
        // Validate item.
        code.AppendLineAt(
            8,
            "if (!__hK) throw new global::System.Text.Json.JsonException(\"Missing item key.\");"
        );
        code.AppendLineAt(
            8,
            "if (__kind == 0) throw new global::System.Text.Json.JsonException(\"Missing item kind.\");"
        );
        code.AppendLineAt(
            8,
            "if (!__seenKeys"
                + id
                + ".Add(__k)) throw new global::System.Text.Json.JsonException(\"Duplicate item key.\");"
        );
        if (isKeyed)
        {
            code.AppendLineAt(
                8,
                "if (!__hBi) throw new global::System.Text.Json.JsonException(\"Missing item beforeIndex.\");"
            );
            code.AppendLineAt(
                8,
                "if (!__hAi) throw new global::System.Text.Json.JsonException(\"Missing item afterIndex.\");"
            );
            // kind/endpoint/index consistency.
            code.AppendLineAt(
                8,
                "if (__kind == 1) { if (__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Added item must have after only.\"); if (__bi != -1 || __ai < 0) throw new global::System.Text.Json.JsonException(\"Added item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "else if (__kind == 2) { if (!__hB || __hA) throw new global::System.Text.Json.JsonException(\"Removed item must have before only.\"); if (__bi < 0 || __ai != -1) throw new global::System.Text.Json.JsonException(\"Removed item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "else { if (!__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Edited/reordered item must have before and after.\"); if (__bi < 0 || __ai < 0) throw new global::System.Text.Json.JsonException(\"Edited/reordered item has invalid indexes.\"); }"
            );
            code.AppendLineAt(
                8,
                "if (__kind == 4 && __re) throw new global::System.Text.Json.JsonException(\"Reordered item must not set reordered flag.\");"
            );
            code.AppendLineAt(
                8,
                "if ((__kind == 1 || __kind == 2) && __re) throw new global::System.Text.Json.JsonException(\"Added/removed item must not be reordered.\");"
            );
            // Reconstruct nested edit and validate.
            code.AppendLineAt(
                8,
                runtime
                    + "Optional<"
                    + elemFrag
                    + "?> __eb = __hB ? "
                    + runtime
                    + "Optional<"
                    + elemFrag
                    + "?>.Present("
                    + elemFrag
                    + ".From(__bv!)) : default;"
            );
            code.AppendLineAt(
                8,
                runtime
                    + "Optional<"
                    + elemFrag
                    + "?> __ea = __hA ? "
                    + runtime
                    + "Optional<"
                    + elemFrag
                    + "?>.Present("
                    + elemFrag
                    + ".From(__av!)) : default;"
            );
            code.AppendLineAt(8, "var __edit = " + elemCs + ".Between(__eb, __ea);");
            code.AppendLineAt(
                8,
                "if (__kind == 3 && __edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Edited item has empty edit.\");"
            );
            code.AppendLineAt(
                8,
                "if (__kind == 4 && !__edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Reordered item must have equal endpoints.\");"
            );
            code.AppendLineAt(
                8,
                "bool __isEd = __kind == 3; bool __isRe2 = __kind == 4 || (__kind == 3 && __re); bool __isAd = __kind == 1; bool __isRm = __kind == 2;"
            );
            code.AppendLineAt(
                8,
                optElem
                    + " __ob = __hB ? "
                    + runtime
                    + "Optional<"
                    + elementType
                    + ">.Present(__bv!) : default; "
                    + optElem
                    + " __oa = __hA ? "
                    + runtime
                    + "Optional<"
                    + elementType
                    + ">.Present(__av!) : default;"
            );
            code.AppendLineAt(
                8,
                "__items"
                    + id
                    + ".Add(new "
                    + trans
                    + ".Item(__k, __ob, __oa, __bi, __ai, __isAd, __isRm, __isEd, __isRe2, __edit, false));"
            );
        }
        else
        {
            // Dict: kind/endpoint consistency, no indexes.
            code.AppendLineAt(
                8,
                "if (__kind == 1) { if (__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Added entry must have after only.\"); }"
            );
            code.AppendLineAt(
                8,
                "else if (__kind == 2) { if (!__hB || __hA) throw new global::System.Text.Json.JsonException(\"Removed entry must have before only.\"); }"
            );
            code.AppendLineAt(
                8,
                "else { if (!__hB || !__hA) throw new global::System.Text.Json.JsonException(\"Edited entry must have before and after.\"); }"
            );
            if (hasPatch)
            {
                code.AppendLineAt(
                    8,
                    runtime
                        + "Optional<"
                        + elemFrag
                        + "?> __eb = __hB ? "
                        + runtime
                        + "Optional<"
                        + elemFrag
                        + "?>.Present("
                        + elemFrag
                        + ".From(__bv!)) : default;"
                );
                code.AppendLineAt(
                    8,
                    runtime
                        + "Optional<"
                        + elemFrag
                        + "?> __ea = __hA ? "
                        + runtime
                        + "Optional<"
                        + elemFrag
                        + "?>.Present("
                        + elemFrag
                        + ".From(__av!)) : default;"
                );
                code.AppendLineAt(8, "var __edit = " + elemCs + ".Between(__eb, __ea);");
                code.AppendLineAt(
                    8,
                    "if (__kind == 3 && __edit.IsEmpty) throw new global::System.Text.Json.JsonException(\"Edited entry has empty edit.\");"
                );
                code.AppendLineAt(8, "if ((__kind == 1 || __kind == 2) && !__edit.IsEmpty) { }");
                code.AppendLineAt(
                    8,
                    "bool __isAd = __kind == 1; bool __isRm = __kind == 2; bool __isEd = __kind == 3;"
                );
                code.AppendLineAt(
                    8,
                    optElem
                        + " __ob = __hB ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__bv!) : default; "
                        + optElem
                        + " __oa = __hA ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__av!) : default;"
                );
                code.AppendLineAt(
                    8,
                    "__items"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k, __ob, __oa, __isAd, __isRm, __isEd, __edit, false));"
                );
            }
            else
            {
                code.AppendLineAt(
                    8,
                    "if (__kind == 3 && global::System.Collections.Generic.EqualityComparer<"
                        + elementType
                        + ">.Default.Equals(__bv!, __av!)) throw new global::System.Text.Json.JsonException(\"Edited entry must change the value.\");"
                );
                code.AppendLineAt(
                    8,
                    "bool __isAd = __kind == 1; bool __isRm = __kind == 2; bool __isEd = __kind == 3;"
                );
                code.AppendLineAt(
                    8,
                    optElem
                        + " __ob = __hB ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__bv!) : default; "
                        + optElem
                        + " __oa = __hA ? "
                        + runtime
                        + "Optional<"
                        + elementType
                        + ">.Present(__av!) : default;"
                );
                code.AppendLineAt(
                    8,
                    "__items"
                        + id
                        + ".Add(new "
                        + trans
                        + ".Item(__k, __ob, __oa, __isAd, __isRm, __isEd, false));"
                );
            }
        }
        code.AppendLineAt(7, "}");
        code.AppendLineAt(
            7,
            "if (__items"
                + id
                + ".Count == 0) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(6, "}");
        if (isKeyed)
        {
            code.AppendLineAt(6, "else if (__isBO)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (__hbO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate member beforeOrder.\"); __hbO"
                    + id
                    + " = true;"
            );
            code.AppendLineAt(
                7,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"beforeOrder must be an array.\");"
            );
            code.AppendLineAt(
                7,
                "__bO" + id + " = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                7,
                "while (reader.Read()) { if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break; __bO"
                    + id
                    + ".Add(__SparseReadKey_"
                    + id
                    + "(ref reader, options)); }"
            );
            code.AppendLineAt(6, "}");
            code.AppendLineAt(6, "else if (__isAO)");
            code.AppendLineAt(6, "{");
            code.AppendLineAt(
                7,
                "if (__haO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate member afterOrder.\"); __haO"
                    + id
                    + " = true;"
            );
            code.AppendLineAt(
                7,
                "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartArray) throw new global::System.Text.Json.JsonException(\"afterOrder must be an array.\");"
            );
            code.AppendLineAt(
                7,
                "__aO" + id + " = new global::System.Collections.Generic.List<" + keyType + ">();"
            );
            code.AppendLineAt(
                7,
                "while (reader.Read()) { if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndArray) break; __aO"
                    + id
                    + ".Add(__SparseReadKey_"
                    + id
                    + "(ref reader, options)); }"
            );
            code.AppendLineAt(6, "}");
        }
        code.AppendLineAt(
            6,
            "else throw new global::System.Text.Json.JsonException(\"Unknown member property.\");"
        );
        code.AppendLineAt(5, "}");
        // Whole vs granular exclusivity and non-empty validation.
        code.AppendLineAt(5, "bool __isWhole" + id + " = __hb_" + id + " || __ha_" + id + ";");
        code.AppendLineAt(
            5,
            "if (__isWhole"
                + id
                + " && __hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Whole and granular transitions cannot coexist.\");"
        );
        if (isKeyed)
            code.AppendLineAt(
                5,
                "if (__isWhole"
                    + id
                    + " && (__hbO"
                    + id
                    + " || __haO"
                    + id
                    + ")) throw new global::System.Text.Json.JsonException(\"Whole and order transitions cannot coexist.\");"
            );
        code.AppendLineAt(5, "if (__isWhole" + id + ")");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            5,
            "if (!__hb_"
                + id
                + " || !__ha_"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Missing whole before/after.\");"
        );
        code.AppendLineAt(
            5,
            "if (!__wb_"
                + id
                + ".IsPresent && !__wa_"
                + id
                + ".IsPresent) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(
            5,
            "if (Fragment.__SparseEqual_"
                + id
                + "(__wb_"
                + id
                + ", __wa_"
                + id
                + ")) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
        );
        code.AppendLineAt(5, "__whole_" + id + " = true;");
        code.AppendLineAt(5, "}");
        code.AppendLineAt(5, "else");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            5,
            "if (!__hasIt"
                + id
                + ") throw new global::System.Text.Json.JsonException(\"Missing member items.\");"
        );
        if (isKeyed)
            code.AppendLineAt(
                5,
                "if (__hbO"
                    + id
                    + " != __haO"
                    + id
                    + ") throw new global::System.Text.Json.JsonException(\"beforeOrder and afterOrder must coexist.\");"
            );
        code.AppendLineAt(5, "}");
    }
}
