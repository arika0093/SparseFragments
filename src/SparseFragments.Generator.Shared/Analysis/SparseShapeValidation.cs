using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseFragments.Generator.Shared;

/// <summary>Product-neutral root-model shape outcome.</summary>
internal enum SparseRootShapeProblem
{
    MustBePartial,
    UnsupportedModel,
    MissingConstructor,
    RefLikeModel,
    FileLocalModel,
}

/// <summary>Product-neutral unsupported member-shape outcome.</summary>
/// <remarks>
/// Covers generated member shapes that can never appear in emitted fragment code
/// (they have no stable display name, cannot be generic arguments, or are scoped
/// to the stack). Detected recursively through nullable, array and collection
/// wrappers.
/// </remarks>
internal enum SparseMemberShapeProblem
{
    Dynamic,
    Pointer,
    FunctionPointer,
    TypeParameter,
    Error,
    RefLike,
}

/// <summary>
/// Shared product-neutral validation for fragment-generator inputs.
/// </summary>
/// <remarks>
/// Exposes validation <em>results</em>; diagnostics themselves keep
/// generator-specific IDs/messages and are owned by each generator.
/// </remarks>
internal static class SparseShapeValidation
{
    /// <summary>Validates the root model declaration shape.</summary>
    /// <remarks>
    /// Mirrors the historical SparseFragments root checks (partial, top-level,
    /// non-generic, non-abstract class-or-struct, supported constructor) plus the
    /// ref-like and file-local rejections shared with downstream generators.
    /// Returns <c>null</c> when the shape is supported.
    /// </remarks>
    public static SparseRootShapeProblem? ValidateRootShape(
        INamedTypeSymbol model,
        TypeDeclarationSyntax? declaration,
        SparseGeneratorConfig? config,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (declaration is null || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return SparseRootShapeProblem.MustBePartial;
        }

        if (model.IsRefLikeType)
        {
            return SparseRootShapeProblem.RefLikeModel;
        }

        if (IsFileLocal(model, declaration))
        {
            return SparseRootShapeProblem.FileLocalModel;
        }

        if (
            model.ContainingType is not null
            || model.Arity != 0
            || (model.TypeKind != TypeKind.Class && model.TypeKind != TypeKind.Struct)
            || model.IsAbstract
        )
        {
            return SparseRootShapeProblem.UnsupportedModel;
        }

        if (
            model.TypeKind == TypeKind.Class
            && (
                config is null
                    ? ModelConstructorBinding.AnalyzeRoot(model, cancellationToken)
                    : ModelConstructorBinding.AnalyzeRoot(model, config, cancellationToken)
            ) is null
        )
        {
            return SparseRootShapeProblem.MissingConstructor;
        }

        return null;
    }

    public static SparseRootShapeProblem? ValidateRootShape(
        INamedTypeSymbol model,
        TypeDeclarationSyntax? declaration,
        CancellationToken cancellationToken
    ) => ValidateRootShape(model, declaration, config: null, cancellationToken);

    private static bool IsFileLocal(INamedTypeSymbol model, TypeDeclarationSyntax declaration)
    {
        // `file` accessibility is declaration-site metadata; any file-local
        // declaration disqualifies the model because generated partials cannot
        // name the type across source files. Compared by token text because the
        // `file` contextual keyword postdates the referenced Roslyn version.
        foreach (var reference in model.DeclaringSyntaxReferences)
        {
            if (
                reference.GetSyntax() is TypeDeclarationSyntax candidate
                && HasFileModifier(candidate)
            )
            {
                return true;
            }
        }

        return HasFileModifier(declaration);
    }

    private static bool HasFileModifier(TypeDeclarationSyntax declaration) =>
        declaration.Modifiers.Any(static modifier =>
            string.Equals(modifier.Text, "file", StringComparison.Ordinal)
        );

    /// <summary>
    /// Recursively rejects member shapes that generated code cannot represent.
    /// </summary>
    /// <remarks>
    /// Unwraps nullable, array and supported-collection wrappers and rejects
    /// <c>dynamic</c>, pointer, function-pointer, type-parameter, error and
    /// ref-like leaves. Returns <c>null</c> when the shape is representable.
    /// </remarks>
    public static SparseMemberShapeProblem? GetUnsupportedMemberReason(ITypeSymbol type)
    {
        var pending = new Stack<ITypeSymbol>();
        var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        pending.Push(type);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            switch (current)
            {
                case IDynamicTypeSymbol:
                    return SparseMemberShapeProblem.Dynamic;
                case IPointerTypeSymbol:
                    return SparseMemberShapeProblem.Pointer;
                case IFunctionPointerTypeSymbol:
                    return SparseMemberShapeProblem.FunctionPointer;
                case ITypeParameterSymbol:
                    return SparseMemberShapeProblem.TypeParameter;
                case IErrorTypeSymbol:
                    return SparseMemberShapeProblem.Error;
            }

            if (current is IArrayTypeSymbol array)
            {
                pending.Push(array.ElementType);
                continue;
            }

            if (current is not INamedTypeSymbol named)
            {
                continue;
            }

            if (named.IsRefLikeType)
            {
                return SparseMemberShapeProblem.RefLike;
            }

            if (
                named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1
            )
            {
                pending.Push(named.TypeArguments[0]);
                continue;
            }

            var collection = SparseCollectionAnalyzer.GetCollectionInfo(named);
            if (collection.CloneKind != SparseCloneCollectionKind.Unsupported)
            {
                if (collection.ElementType is not null)
                {
                    pending.Push(collection.ElementType);
                }

                if (collection.ValueType is not null)
                {
                    pending.Push(collection.ValueType);
                }
            }
        }

        return null;
    }

    /// <summary>Wire name used for JSON comparisons of a generated member.</summary>
    public static string GetWireName(SparseMemberModel member) =>
        member.Property.JsonPropertyName ?? member.Property.Name;

    /// <summary>
    /// Finds JSON wire names claimed by more than one serializable member.
    /// </summary>
    /// <remarks>
    /// Ordinal comparison: members ignored by <c>JsonIgnore</c> never participate
    /// (the generated converter skips them). Case-insensitive runtime policy
    /// collisions remain runtime errors; only statically provable duplicates are
    /// reported. Returns the duplicated names in ordinal order.
    /// </remarks>
    public static ImmutableArray<string> FindDuplicateWireNames(
        ImmutableArray<SparseMemberModel> members
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new SortedSet<string>(StringComparer.Ordinal);
        foreach (
            var property in members
                .Select(static member => member.Property)
                .Where(static property => !property.IsJsonIgnored)
        )
        {
            var wire = property.JsonPropertyName ?? property.Name;
            if (!seen.Add(wire))
            {
                duplicates.Add(wire);
            }
        }

        return duplicates.ToImmutableArray();
    }

    /// <summary>Resolves the property symbol carrying a duplicated wire name.</summary>
    public static IPropertySymbol? FindWireNameOwner(
        ImmutableArray<SparseSymbolMemberModel> symbolMembers,
        string wireName,
        CancellationToken cancellationToken
    ) =>
        symbolMembers
            .Select(static member => member.Property)
            .FirstOrDefault(property => MatchesWireName(property, wireName, cancellationToken));

    private static bool MatchesWireName(
        IPropertySymbol property,
        string wireName,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            SparseJsonNaming.GetJsonIgnoreCondition(property, cancellationToken)
            == SparseJsonNaming.JsonIgnoreAlways
        )
        {
            return false;
        }

        var wire = SparseJsonNaming.GetJsonPropertyName(property, cancellationToken, out _);
        return string.Equals(wire, wireName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Finds member names colliding with generator-reserved names, in member order.
    /// </summary>
    public static ImmutableArray<string> FindReservedNameCollisions(
        IEnumerable<string> memberNames,
        ImmutableArray<string> reservedNames
    )
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        if (!reservedNames.IsDefault)
        {
            foreach (var name in reservedNames)
            {
                reserved.Add(name);
            }
        }

        var collisions = memberNames
            .Where(reserved.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
        return collisions;
    }

    /// <summary>
    /// First member colliding with this generator's reserved names, if any.
    /// </summary>
    /// <remarks>
    /// Preserves the historical single-diagnostic render-time behavior while the
    /// check itself runs on the shared primitive during analysis.
    /// </remarks>
    public static string? FindFirstReservedNameCollision(
        ImmutableArray<SparseMemberModel> members,
        ImmutableArray<string> reservedNames
    )
    {
        var collisions = FindReservedNameCollisions(
            members.Select(static member => member.Property.Name),
            reservedNames
        );
        return collisions.IsDefaultOrEmpty ? null : collisions[0];
    }
}
