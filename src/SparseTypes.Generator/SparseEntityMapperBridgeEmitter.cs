using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator;

// Emit the mapper bridge and synchronous Configure scope.
// Lock each instance during Create and clear the scope in finally.
internal static class SparseEntityMapperBridgeEmitter
{
    internal static void AppendBridge(
        StringBuilder code,
        int indent,
        INamedTypeSymbol mapper,
        INamedTypeSymbol entityType,
        INamedTypeSymbol dtoType,
        string companionName,
        bool hasConfigure
    )
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = mapper; current is not null; current = current.ContainingType)
        {
            chain.Add(current);
        }

        chain.Reverse();
        var levels = new List<TypeDeclarationSyntax>();
        foreach (var level in chain)
        {
            TypeDeclarationSyntax? declaration = null;
            foreach (var reference in level.DeclaringSyntaxReferences)
            {
                if (
                    reference.GetSyntax() is TypeDeclarationSyntax candidate
                    && candidate.Modifiers.Any(SyntaxKind.PartialKeyword)
                )
                {
                    declaration = candidate;
                    break;
                }
            }

            // Validated partial above; skip emission if somehow missing.
            if (declaration is null)
            {
                return;
            }

            levels.Add(declaration);
        }

        var entityName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dtoName = dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var mappingContract =
            "global::SparseTypes.EntityFrameworkCore.IEntityModelMapping<"
            + entityName
            + ", "
            + dtoName
            + ">";
        var builderType =
            "global::SparseTypes.EntityFrameworkCore.SparseEntityMapBuilder<"
            + entityName
            + ", "
            + dtoName
            + ">";
        var mapperFullName = mapper.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        for (var index = 0; index < levels.Count; index++)
        {
            var declaration = levels[index];
            var isInnermost = index == levels.Count - 1;
            AppendIndent(code, indent + index)
                .Append(ModifiersWithoutPartial(declaration))
                .Append("partial ")
                .Append(declaration.Keyword.ValueText)
                .Append(' ')
                .Append(
                    SparseEntityMapperEmitter.EscapeIdentifier(declaration.Identifier.ValueText)
                );
            if (isInnermost)
            {
                code.Append(" : ").Append(mappingContract);
            }

            code.AppendLine();
            AppendIndent(code, indent + index).AppendLine("{");
        }

        var body = indent + levels.Count;
        AppendIndent(code, body)
            .AppendLine(
                "/// <summary>Synchronizes concurrent plan builds on one mapping instance.</summary>"
            );
        AppendIndent(code, body).AppendLine("private readonly object __sparse_map_lock = new();");
        AppendIndent(code, body)
            .Append("private ")
            .Append(builderType)
            .AppendLine("? __sparse_map_scope = null;");
        AppendIndent(code, body)
            .AppendLine(
                "/// <summary>Gets the builder active for the current plan build.</summary>"
            );
        AppendIndent(code, body)
            .AppendLine(
                "/// <remarks>Usable only during synchronous <c>Configure</c>; never retained.</remarks>"
            );
        AppendIndent(code, body).Append("private ").Append(builderType).AppendLine(" Map");
        AppendIndent(code, body).AppendLine("{");
        AppendIndent(code, body + 1).AppendLine("get");
        AppendIndent(code, body + 1).AppendLine("{");
        AppendIndent(code, body + 2).AppendLine("var scope = __sparse_map_scope;");
        AppendIndent(code, body + 2)
            .Append(
                "if (scope is null) throw new global::System.InvalidOperationException(\"Sparse mapper '"
            )
            .Append(mapperFullName.Replace("\"", "\"\""))
            .AppendLine(
                " exposes Map only during Configure. Do not retain or access it outside the synchronous Configure call.\");"
            );
        AppendIndent(code, body + 2).AppendLine("return scope;");
        AppendIndent(code, body + 1).AppendLine("}");
        AppendIndent(code, body).AppendLine("}");
        AppendIndent(code, body)
            .AppendLine(
                "[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All, typeof("
                    + companionName
                    + "))]"
            );
        AppendIndent(code, body)
            .Append("void ")
            .Append(mappingContract)
            .Append(".Configure(")
            .Append(builderType)
            .AppendLine(" builder)");
        AppendIndent(code, body).AppendLine("{");
        if (!hasConfigure)
        {
            AppendIndent(code, body + 1).AppendLine("lock (__sparse_map_lock)");
            AppendIndent(code, body + 1).AppendLine("{");
            AppendIndent(code, body + 2).AppendLine("_ = builder;");
            AppendIndent(code, body + 1).AppendLine("}");
        }
        else
        {
            AppendIndent(code, body + 1).AppendLine("lock (__sparse_map_lock)");
            AppendIndent(code, body + 1).AppendLine("{");
            AppendIndent(code, body + 2)
                .Append(
                    "if (__sparse_map_scope is not null) throw new global::System.InvalidOperationException(\"Sparse mapper '"
                )
                .Append(mapperFullName.Replace("\"", "\"\""))
                .AppendLine(" does not support reentrant Configure calls.\");");
            AppendIndent(code, body + 2).AppendLine("__sparse_map_scope = builder;");
            AppendIndent(code, body + 2).AppendLine("try");
            AppendIndent(code, body + 2).AppendLine("{");
            AppendIndent(code, body + 3).AppendLine("Configure();");
            AppendIndent(code, body + 2).AppendLine("}");
            AppendIndent(code, body + 2).AppendLine("finally");
            AppendIndent(code, body + 2).AppendLine("{");
            AppendIndent(code, body + 3).AppendLine("__sparse_map_scope = null;");
            AppendIndent(code, body + 2).AppendLine("}");
            AppendIndent(code, body + 1).AppendLine("}");
        }

        AppendIndent(code, body).AppendLine("}");

        for (var index = levels.Count - 1; index >= 0; index--)
        {
            AppendIndent(code, indent + index).AppendLine("}");
        }
    }

    private static string ModifiersWithoutPartial(TypeDeclarationSyntax declaration)
    {
        var kept = declaration
            .Modifiers.Where(static modifier => !modifier.IsKind(SyntaxKind.PartialKeyword))
            .Select(static modifier => modifier.ValueText)
            .ToArray();
        return kept.Length == 0 ? string.Empty : string.Join(" ", kept) + " ";
    }

    private static StringBuilder AppendIndent(StringBuilder code, int indent)
    {
        for (var index = 0; index < indent; index++)
        {
            code.Append("    ");
        }

        return code;
    }
}
