using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits ChangeSet STJ read path.</summary>
internal static class SparseChangeSetStjReadEmitter
{
    internal static void AppendChangeSetRead(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect
    )
    {
        var runtime = SparseStjKeyHelpers.RuntimeFor(dialect);
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseReadBodyStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "__SparseValidateJsonNames(options);");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change set must be a JSON object.\");"
        );
        code.AppendLineAt(3, "bool __seenWhole = false;");
        code.AppendLineAt(3, runtime + "Optional<Fragment?> __wholeBefore = default;");
        code.AppendLineAt(3, runtime + "Optional<Fragment?> __wholeAfter = default;");
        code.AppendLineAt(3, "bool __hasWholeBefore = false; bool __hasWholeAfter = false;");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
            {
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    SparseStjKeyHelpers.ChangeSetChildChangeSet(member, dialect)
                        + "? __n_"
                        + member.Id
                        + " = null;"
                );
            }
            else if (
                SparseStjKeyHelpers.IsDict(member)
                || SparseKeyedCollectionEmitter.IsKeyedSequence(member)
            )
            {
                var vt = SparseStjKeyHelpers.ChangeSetValueType(member);
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(3, "bool __whole_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __wb_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __wa_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    "bool __hb_" + member.Id + " = false; bool __ha_" + member.Id + " = false;"
                );
                // Item/order accumulators are assigned in the member branch and
                // consumed by final ChangeSet construction below. Trans names use
                // the full member list (same collision scheme as ChangeSet).
                var __trans = SparseStjKeyHelpers.SparseTransName(members, member);
                code.AppendLineAt(
                    3,
                    "global::System.Collections.Generic.List<"
                        + __trans
                        + ".Item>? __items"
                        + member.Id
                        + " = null;"
                );
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                {
                    var __kt = SparseStjKeyHelpers.KeyTypeOf(member);
                    code.AppendLineAt(
                        3,
                        "global::System.Collections.Generic.List<"
                            + __kt
                            + ">? __bO"
                            + member.Id
                            + " = null; global::System.Collections.Generic.List<"
                            + __kt
                            + ">? __aO"
                            + member.Id
                            + " = null;"
                    );
                }
            }
            else
            {
                var vt = SparseStjKeyHelpers.ChangeSetValueType(member);
                code.AppendLineAt(3, "bool __seen_" + member.Id + " = false;");
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __b_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    runtime + "Optional<" + vt + "> __a_" + member.Id + " = default;"
                );
                code.AppendLineAt(
                    3,
                    "bool __hb_" + member.Id + " = false; bool __ha_" + member.Id + " = false;"
                );
            }
        }
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a change-set property name.\");"
        );
        code.AppendLineAt(4, "var __prop = reader.GetString();");
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of change set.\");"
        );
        code.AppendLineAt(4, "if (__prop == \"$whole\")");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Duplicate change-set property '$whole'.\");"
        );
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            code.AppendLineAt(
                5,
                "if (__seen_"
                    + member.Id
                    + ") throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
            );
        }
        code.AppendLineAt(5, "__seenWhole = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A whole transition must be a JSON object.\");"
        );
        code.AppendLineAt(5, "while (reader.Read())");
        code.AppendLineAt(5, "{");
        code.AppendLineAt(
            6,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            6,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a whole-transition property name.\");"
        );
        code.AppendLineAt(6, "var __isWb = reader.ValueTextEquals(\"before\");");
        code.AppendLineAt(6, "var __isWa = !__isWb && reader.ValueTextEquals(\"after\");");
        code.AppendLineAt(6, "var __wUnknown = __isWb || __isWa ? null : reader.GetString();");
        code.AppendLineAt(
            6,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of whole transition.\");"
        );
        code.AppendLineAt(6, "if (__isWb)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasWholeBefore) throw new global::System.Text.Json.JsonException(\"Duplicate whole before.\");"
        );
        code.AppendLineAt(7, "__hasWholeBefore = true;");
        code.AppendLineAt(7, "__wholeBefore = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(6, "else if (__isWa)");
        code.AppendLineAt(6, "{");
        code.AppendLineAt(
            7,
            "if (__hasWholeAfter) throw new global::System.Text.Json.JsonException(\"Duplicate whole after.\");"
        );
        code.AppendLineAt(7, "__hasWholeAfter = true;");
        code.AppendLineAt(7, "__wholeAfter = __SparseReadOptionalFragment(ref reader, options);");
        code.AppendLineAt(6, "}");
        code.AppendLineAt(
            6,
            "else throw new global::System.Text.Json.JsonException(\"Unknown whole property '\" + __wUnknown + \"'.\");"
        );
        code.AppendLineAt(5, "}");
        code.AppendLineAt(
            5,
            "if (!__hasWholeBefore) throw new global::System.Text.Json.JsonException(\"Missing whole before.\");"
        );
        code.AppendLineAt(
            5,
            "if (!__hasWholeAfter) throw new global::System.Text.Json.JsonException(\"Missing whole after.\");"
        );
        code.AppendLineAt(4, "}");
        foreach (var member in members.Where(static m => !m.Property.IsJsonIgnored))
        {
            var wire = SparseStjKeyHelpers.ChangeSetWireName(member);
            var lit = SparseStjKeyHelpers.Lit(wire);
            var usePolicy = member.Property.HasExplicitJsonPropertyName ? "false" : "true";
            code.AppendLineAt(
                4,
                "else if" + " (__SparseMatches(__prop, " + lit + ", " + usePolicy + ", options))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(
                5,
                "if (__seenWhole) throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
            );
            code.AppendLineAt(
                5,
                "if (__seen_"
                    + member.Id
                    + ") throw new global::System.Text.Json.JsonException(\"Duplicate change-set property.\");"
            );
            code.AppendLineAt(5, "__seen_" + member.Id + " = true;");
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
            {
                var child = SparseStjKeyHelpers.ChangeSetChildChangeSet(member, dialect);
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A nested change set must be a JSON object.\");"
                );
                code.AppendLineAt(
                    5,
                    "var __nn"
                        + member.Id
                        + " = "
                        + child
                        + ".__SparseReadBodyStj(ref reader, options);"
                );
                code.AppendLineAt(
                    5,
                    "if (__nn"
                        + member.Id
                        + ".IsEmpty) throw new global::System.Text.Json.JsonException(\"Empty nested change must not be serialized.\");"
                );
                code.AppendLineAt(5, "__n_" + member.Id + " = __nn" + member.Id + ";");
            }
            else if (
                SparseStjKeyHelpers.IsDict(member)
                || SparseKeyedCollectionEmitter.IsKeyedSequence(member)
            )
            {
                SparseChangeSetStjSparseMemberReadEmitter.AppendChangeSetSparseMemberRead(
                    code,
                    member,
                    members,
                    dialect
                );
            }
            else
            {
                code.AppendLineAt(
                    5,
                    "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A member transition must be a JSON object.\");"
                );
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
                    "var __mUnknown = __isB || __isA ? null : reader.GetString();"
                );
                code.AppendLineAt(
                    6,
                    "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of member transition.\");"
                );
                code.AppendLineAt(6, "if (__isB)");
                code.AppendLineAt(6, "{");
                code.AppendLineAt(
                    7,
                    "if (__hb_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Duplicate member before.\");"
                );
                code.AppendLineAt(7, "__hb_" + member.Id + " = true;");
                code.AppendLineAt(
                    7,
                    "__b_"
                        + member.Id
                        + " = __SparseReadOpt_"
                        + member.Id
                        + "(ref reader, options);"
                );
                code.AppendLineAt(6, "}");
                code.AppendLineAt(6, "else if (__isA)");
                code.AppendLineAt(6, "{");
                code.AppendLineAt(
                    7,
                    "if (__ha_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Duplicate member after.\");"
                );
                code.AppendLineAt(7, "__ha_" + member.Id + " = true;");
                code.AppendLineAt(
                    7,
                    "__a_"
                        + member.Id
                        + " = __SparseReadOpt_"
                        + member.Id
                        + "(ref reader, options);"
                );
                code.AppendLineAt(6, "}");
                code.AppendLineAt(
                    6,
                    "else throw new global::System.Text.Json.JsonException(\"Unknown member property '\" + __mUnknown + \"'.\");"
                );
                code.AppendLineAt(5, "}");
                code.AppendLineAt(
                    5,
                    "if (!__hb_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Missing member before.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (!__ha_"
                        + member.Id
                        + ") throw new global::System.Text.Json.JsonException(\"Missing member after.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (!__b_"
                        + member.Id
                        + ".IsPresent && !__a_"
                        + member.Id
                        + ".IsPresent) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
                );
                code.AppendLineAt(
                    5,
                    "if (Fragment.__SparseEqual_"
                        + member.Id
                        + "(__b_"
                        + member.Id
                        + ", __a_"
                        + member.Id
                        + ")) throw new global::System.Text.Json.JsonException(\"Empty member transition must not be serialized.\");"
                );
            }
            code.AppendLineAt(4, "}");
        }
        // Ignored members: skip.
        foreach (var member in members.Where(static m => m.Property.IsJsonIgnored))
        {
            var wire = SparseStjKeyHelpers.ChangeSetWireName(member);
            var lit = SparseStjKeyHelpers.Lit(wire);
            var usePolicy = member.Property.HasExplicitJsonPropertyName ? "false" : "true";
            code.AppendLineAt(
                4,
                "else if (__SparseMatches(__prop, " + lit + ", " + usePolicy + ", options))"
            );
            code.AppendLineAt(4, "{");
            code.AppendLineAt(5, "reader.Skip();");
            code.AppendLineAt(4, "}");
        }
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown change-set property '\" + __prop + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(3, "if (__seenWhole)");
        code.AppendLineAt(3, "{");
        var tail = new System.Text.StringBuilder();
        var sep = string.Empty;
        foreach (var member in members)
        {
            tail.Append(sep);
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
                tail.Append("null");
            else if (
                SparseStjKeyHelpers.IsDict(member)
                || SparseKeyedCollectionEmitter.IsKeyedSequence(member)
            )
            {
                tail.Append("false, false, default, default, null");
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                    tail.Append(", null, null");
            }
            else
                tail.Append("default, default, false");
            sep = ", ";
        }
        if (tail.Length == 0)
            code.AppendLineAt(4, "return Between(__wholeBefore, __wholeAfter);");
        else
            code.AppendLineAt(
                4,
                "if ("
                    + string.Join(
                        " || ",
                        members.Where(m => !m.Property.IsJsonIgnored).Select(m => "__seen_" + m.Id)
                    )
                    + (members.Any(m => !m.Property.IsJsonIgnored) ? ") " : string.Empty)
                    + (
                        members.Any(m => !m.Property.IsJsonIgnored)
                            ? "throw new global::System.Text.Json.JsonException(\"Whole and member transitions cannot coexist.\");"
                            : string.Empty
                    )
            );
        if (tail.Length != 0)
            code.AppendLineAt(
                4,
                "return new ChangeSet(true, __wholeBefore, __wholeAfter, " + tail.ToString() + ");"
            );
        code.AppendLineAt(3, "}");
        var cargs = new System.Collections.Generic.List<string> { "false", "default", "default" };
        foreach (var member in members)
        {
            var __isSparse =
                SparseStjKeyHelpers.IsDict(member)
                || SparseKeyedCollectionEmitter.IsKeyedSequence(member);
            if (member.Property.IsJsonIgnored)
            {
                if (SparseStjKeyHelpers.ChangeSetIsNested(member))
                    cargs.Add("null");
                else if (__isSparse)
                {
                    cargs.AddRange(new[] { "false", "false", "default", "default", "null" });
                    if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                        cargs.AddRange(new[] { "null", "null" });
                }
                else
                    cargs.AddRange(new[] { "default", "default", "false" });
                continue;
            }
            if (SparseStjKeyHelpers.ChangeSetIsNested(member))
                cargs.Add("__n_" + member.Id);
            else if (__isSparse)
            {
                cargs.AddRange(
                    new[]
                    {
                        "__seen_" + member.Id,
                        "__whole_" + member.Id,
                        "__wb_" + member.Id,
                        "__wa_" + member.Id,
                        "__items" + member.Id,
                    }
                );
                if (SparseKeyedCollectionEmitter.IsKeyedSequence(member))
                    cargs.AddRange(new[] { "__bO" + member.Id, "__aO" + member.Id });
            }
            else
                cargs.AddRange(
                    new[] { "__b_" + member.Id, "__a_" + member.Id, "__seen_" + member.Id }
                );
        }
        code.AppendLineAt(3, "return new ChangeSet(" + string.Join(", ", cargs) + ");");
        code.AppendLineAt(2, "}");
        // v1 document envelope reader (order-independent, strict).
        code.AppendLine();
        code.AppendLineAt(
            2,
            "internal static ChangeSet __SparseReadStj(ref global::System.Text.Json.Utf8JsonReader reader, global::System.Text.Json.JsonSerializerOptions options)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.StartObject) throw new global::System.Text.Json.JsonException(\"A change-set document must be a JSON object.\");"
        );
        code.AppendLineAt(3, "bool __seenVersion = false;");
        code.AppendLineAt(3, "bool __seenChanges = false;");
        code.AppendLineAt(3, "ChangeSet? __docBody = null;");
        code.AppendLineAt(3, "while (reader.Read())");
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            "if (reader.TokenType == global::System.Text.Json.JsonTokenType.EndObject) break;"
        );
        code.AppendLineAt(
            4,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.PropertyName) throw new global::System.Text.Json.JsonException(\"Expected a change-set document property name.\");"
        );
        code.AppendLineAt(4, "var __isVersion = reader.ValueTextEquals(\"version\");");
        code.AppendLineAt(
            4,
            "var __isChanges = !__isVersion && reader.ValueTextEquals(\"changes\");"
        );
        code.AppendLineAt(
            4,
            "var __docUnknown = __isVersion || __isChanges ? null : reader.GetString();"
        );
        code.AppendLineAt(
            4,
            "if (!reader.Read()) throw new global::System.Text.Json.JsonException(\"Unexpected end of change-set document.\");"
        );
        code.AppendLineAt(4, "if (__isVersion)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenVersion) throw new global::System.Text.Json.JsonException(\"Duplicate change-set version.\");"
        );
        code.AppendLineAt(5, "__seenVersion = true;");
        code.AppendLineAt(
            5,
            "if (reader.TokenType != global::System.Text.Json.JsonTokenType.Number || !reader.TryGetInt32(out var __v)) throw new global::System.Text.Json.JsonException(\"Change-set version must be the integer 1.\");"
        );
        code.AppendLineAt(
            5,
            "if (__v != 1) throw new global::System.Text.Json.JsonException(\"Unsupported change-set version '\" + __v + \"'. Expected version 1.\");"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else if (__isChanges)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "if (__seenChanges) throw new global::System.Text.Json.JsonException(\"Duplicate change-set changes.\");"
        );
        code.AppendLineAt(5, "__seenChanges = true;");
        code.AppendLineAt(5, "__docBody = __SparseReadBodyStj(ref reader, options);");
        code.AppendLineAt(4, "}");
        code.AppendLineAt(
            4,
            "else throw new global::System.Text.Json.JsonException(\"Unknown change-set document property '\" + __docUnknown + \"'.\");"
        );
        code.AppendLineAt(3, "}");
        code.AppendLineAt(
            3,
            "if (!__seenVersion) throw new global::System.Text.Json.JsonException(\"Missing change-set version.\");"
        );
        code.AppendLineAt(
            3,
            "if (!__seenChanges) throw new global::System.Text.Json.JsonException(\"Missing change-set changes.\");"
        );
        code.AppendLineAt(3, "return __docBody!;");
        code.AppendLineAt(2, "}");
    }
}
