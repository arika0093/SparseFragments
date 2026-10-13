using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator;

/// <summary>
/// Emits the <c>Create(IModel)</c> shorthand for <c>[SparseFragmentMapper{TEntity, TDto}]</c> types.
/// </summary>
/// <remarks>
/// Product-specific surface, not Shared: Shared never hard-codes
/// SparseTypes runtime or mapper names. For partial mapper classes with a
/// parameterless instance constructor, a partial declaration contributes
/// <c>public static SparseEntityMapper{TEntity, TDto} Create(IModel model)</c>,
/// so call sites read <c>OrderMapper.Create(db.Model)</c> instead of the
/// fully-qualified generic factory. Mappers without a parameterless
/// constructor (for example constructor-injected mappings) keep the generic
/// factory only.
/// </remarks>
internal static class SparseEntityMapperFactoryEmitter
{
    internal static void AppendFactory(
        StringBuilder code,
        int indent,
        INamedTypeSymbol mapper,
        INamedTypeSymbol entityType,
        INamedTypeSymbol dtoType
    )
    {
        if (mapper.IsAbstract || !HasParameterlessConstructor(mapper))
        {
            return;
        }

        if (!TryCollectLevels(mapper, out var levels) || levels.Count == 0)
        {
            return;
        }

        var entityName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var dtoName = dtoType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        for (var index = 0; index < levels.Count; index++)
        {
            var declaration = levels[index];
            AppendIndent(code, indent + index)
                .Append(ModifiersWithoutPartial(declaration))
                .Append("partial ")
                .Append(declaration.Keyword.ValueText)
                .Append(' ')
                .Append(
                    SparseEntityMapperEmitter.EscapeIdentifier(declaration.Identifier.ValueText)
                )
                .Append(declaration.TypeParameterList?.ToString() ?? string.Empty)
                .AppendLine();
            AppendIndent(code, indent + index).AppendLine("{");
        }

        var methodIndent = indent + levels.Count;
        AppendIndent(code, methodIndent)
            .AppendLine(
                "/// <summary>Creates the mapper for this mapping plus EF model.</summary>"
            );
        AppendIndent(code, methodIndent)
            .AppendLine(
                "/// <param name=\"model\">The EF model the plan is validated against.</param>"
            );
        AppendIndent(code, methodIndent)
            .AppendLine("/// <returns>The validated mapper for this model and mapping.</returns>");
        AppendIndent(code, methodIndent)
            .Append("public static global::SparseTypes.EntityFrameworkCore.SparseEntityMapper<")
            .Append(entityName)
            .Append(", ")
            .Append(dtoName)
            .AppendLine("> Create(global::Microsoft.EntityFrameworkCore.Metadata.IModel model)");
        AppendIndent(code, methodIndent).AppendLine("{");
        AppendIndent(code, methodIndent + 1)
            .AppendLine(
                "if (model is null) throw new global::System.ArgumentNullException(nameof(model));"
            );
        AppendIndent(code, methodIndent + 1)
            .Append("var mapping = new ")
            .Append(SparseEntityMapperEmitter.EscapeIdentifier(levels[^1].Identifier.ValueText))
            .AppendLine("();");
        AppendIndent(code, methodIndent + 1)
            .Append("return global::SparseTypes.EntityFrameworkCore.SparseEntityMapper<")
            .Append(entityName)
            .Append(", ")
            .Append(dtoName)
            .AppendLine(">.Create(mapping, model);");
        AppendIndent(code, methodIndent).AppendLine("}");

        for (var index = levels.Count - 1; index >= 0; index--)
        {
            AppendIndent(code, indent + index).AppendLine("}");
        }
    }

    private static bool HasParameterlessConstructor(INamedTypeSymbol mapper) =>
        mapper.InstanceConstructors.Any(static constructor => constructor.Parameters.Length == 0);

    private static bool TryCollectLevels(
        INamedTypeSymbol mapper,
        out System.Collections.Generic.List<TypeDeclarationSyntax> levels
    )
    {
        levels = new System.Collections.Generic.List<TypeDeclarationSyntax>();
        var chain = new System.Collections.Generic.List<INamedTypeSymbol>();
        for (var current = mapper; current is not null; current = current.ContainingType)
        {
            chain.Add(current);
        }

        chain.Reverse();
        foreach (var level in chain)
        {
            TypeDeclarationSyntax? declaration = null;
            foreach (var reference in level.DeclaringSyntaxReferences)
            {
                if (
                    reference.GetSyntax() is TypeDeclarationSyntax candidate
                    && candidate.Keyword.IsKind(SyntaxKind.ClassKeyword)
                    && candidate.Modifiers.Any(SyntaxKind.PartialKeyword)
                )
                {
                    declaration = candidate;
                    break;
                }
            }

            if (declaration is null)
            {
                levels.Clear();
                return false;
            }

            levels.Add(declaration);
        }

        return true;
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
