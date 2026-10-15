using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

// Validates the [SparseFragmentMapper{TEntity, TDto}] declaration shape.
internal static class SparseEntityMapperDeclaration
{
    internal sealed record Error(Location? Location, string Message);

    internal static bool TryResolveMappingPair(
        INamedTypeSymbol mapper,
        out INamedTypeSymbol? entityType,
        out INamedTypeSymbol? dtoType,
        out string? error
    )
    {
        entityType = null;
        dtoType = null;
        error = null;
        AttributeData? marker = null;
        var markerCount = 0;
        foreach (var attribute in mapper.GetAttributes())
        {
            if (
                attribute.AttributeClass?.OriginalDefinition is not { } definition
                || definition.MetadataName != "SparseFragmentMapperAttribute`2"
                || definition.ContainingNamespace.ToDisplayString()
                    != "SparseTypes.EntityFrameworkCore"
            )
            {
                continue;
            }

            markerCount++;
            marker ??= attribute;
        }

        if (markerCount != 1 || marker?.AttributeClass is null)
        {
            error =
                markerCount == 0
                    ? $"Entity mapper '{mapper.Name}' must declare exactly one [SparseFragmentMapper<TEntity, TDto>] attribute"
                    : $"Entity mapper '{mapper.Name}' declares {markerCount} [SparseFragmentMapper<TEntity, TDto>] attributes; declare exactly one";
            return false;
        }

        var arguments = marker.AttributeClass.TypeArguments;
        if (
            arguments.Length != 2
            || arguments[0] is not INamedTypeSymbol entity
            || arguments[1] is not INamedTypeSymbol dto
            || entity.TypeKind == TypeKind.Error
            || dto.TypeKind == TypeKind.Error
        )
        {
            error =
                $"Entity mapper '{mapper.Name}' declares invalid [SparseFragmentMapper<TEntity, TDto>] type arguments; use resolvable class types";
            return false;
        }

        entityType = entity;
        dtoType = dto;
        return true;
    }

    internal static ImmutableArray<Error> Validate(
        INamedTypeSymbol mapper,
        INamedTypeSymbol entityType,
        INamedTypeSymbol dtoType
    )
    {
        var errors = ImmutableArray.CreateBuilder<Error>();
        var location = mapper.Locations.FirstOrDefault();
        var mapperName = mapper.Name;
        void Fail(Location? at, string message) => errors.Add(new Error(at, message));

        if (mapper.TypeKind != TypeKind.Class || mapper.IsRecord || mapper.IsStatic)
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' must be a non-static, non-record class; declare '[SparseFragmentMapper<{entityType.Name}, {dtoType.Name}>] public sealed partial class {mapperName}'"
            );
            return errors.ToImmutable();
        }

        if (mapper.IsAbstract)
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' must be a non-abstract class so the generator can instantiate it"
            );
        }

        if (mapper.IsGenericType)
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' must not be generic; generic mapper shapes are not supported"
            );
        }

        for (
            var enclosing = mapper.ContainingType;
            enclosing is not null;
            enclosing = enclosing.ContainingType
        )
        {
            if (enclosing.IsGenericType)
            {
                Fail(
                    enclosing.Locations.FirstOrDefault() ?? location,
                    $"Entity mapper '{mapperName}' must not be nested in generic type '{enclosing.Name}'; generic enclosing shapes are not supported"
                );
            }

            if (enclosing.TypeKind != TypeKind.Class || enclosing.IsRecord)
            {
                Fail(
                    enclosing.Locations.FirstOrDefault() ?? location,
                    $"Entity mapper '{mapperName}' must not be nested in non-class type '{enclosing.Name}'"
                );
            }
        }

        if (mapper.BaseType is not null && mapper.BaseType.SpecialType != SpecialType.System_Object)
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' must not declare base type '{mapper.BaseType.Name}'; the generated bridge owns the mapper contract"
            );
        }

        var manual = mapper.AllInterfaces.FirstOrDefault(static candidate =>
            candidate.OriginalDefinition.MetadataName == "IEntityModelMapping`2"
            && candidate.OriginalDefinition.ContainingNamespace.ToDisplayString()
                == "SparseTypes.EntityFrameworkCore"
        );
        if (manual is not null)
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' must not implement IEntityModelMapping manually; the generator implements it from [SparseFragmentMapper<{entityType.Name}, {dtoType.Name}>]"
            );
        }

        if (!IsPartialChain(mapper))
        {
            Fail(
                location,
                $"Entity mapper '{mapperName}' and every enclosing type must be declared partial"
            );
        }

        var collision = mapper
            .GetMembers("Map")
            .FirstOrDefault(member =>
                SymbolEqualityComparer.Default.Equals(member.ContainingType, mapper)
            );
        if (collision is not null)
        {
            Fail(
                collision.Locations.FirstOrDefault() ?? location,
                $"Entity mapper '{mapperName}' declares member 'Map' that collides with the generated Map property; rename the member"
            );
        }

        var configures = mapper
            .GetMembers("Configure")
            .OfType<IMethodSymbol>()
            .Where(method => SymbolEqualityComparer.Default.Equals(method.ContainingType, mapper))
            .ToArray();
        if (configures.Length > 1)
        {
            var at = configures[1].Locations.FirstOrDefault() ?? location;
            Fail(
                at,
                $"Entity mapper '{mapperName}' declares {configures.Length} Configure methods; declare at most one parameterless 'private void Configure()' using Map, or omit it"
            );
        }
        else if (configures.Length == 1)
        {
            var configure = configures[0];
            var at = configure.Locations.FirstOrDefault() ?? location;
            if (
                configure.IsStatic
                || configure.Parameters.Length != 0
                || configure.TypeParameters.Length != 0
                || !configure.ReturnsVoid
            )
            {
                Fail(
                    at,
                    $"Entity mapper '{mapperName}' declares an unsupported Configure signature; declare 'private void Configure()' using the generated Map property, or omit it"
                );
            }
        }

        return errors.ToImmutable();
    }

    private static bool IsPartialChain(INamedTypeSymbol mapper)
    {
        for (var current = mapper; current is not null; current = current.ContainingType)
        {
            var found = current.DeclaringSyntaxReferences.Any(static reference =>
                reference.GetSyntax() is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
            );
            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
