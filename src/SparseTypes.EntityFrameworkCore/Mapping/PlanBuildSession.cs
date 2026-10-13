using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Why an entity member refuses inferred or implicit writes.
/// </summary>
internal enum EntityProtection
{
    None,
    Shadow,
    Key,
    Concurrency,
    Generated,
}

/// <summary>
/// Shared per-plan-build state: the EF model plus global target claims.
/// </summary>
internal sealed class PlanBuildSession
{
    private readonly Dictionary<(IEntityType Type, string Property), string> _claims = new();

    private readonly Dictionary<(IEntityType Type, string Navigation), string> _navigationClaims =
        new();

    private readonly List<SparseEntityMappingError> _errors = [];
    private int _buildDepth;

    public PlanBuildSession(IModel model)
    {
        Model = model;
    }

    public IModel Model { get; }

    /// <summary>Captures an independent build check and retains its mapping errors.</summary>
    public bool TryCollect(Action check)
    {
        ArgumentNullException.ThrowIfNull(check);
        var propertyClaims = new Dictionary<(IEntityType Type, string Property), string>(_claims);
        var navigationClaims = new Dictionary<(IEntityType Type, string Navigation), string>(
            _navigationClaims
        );
        try
        {
            check();
            return true;
        }
        catch (SparseEntityMappingException exception)
        {
            _claims.Clear();
            foreach (var claim in propertyClaims)
            {
                _claims.Add(claim.Key, claim.Value);
            }

            _navigationClaims.Clear();
            foreach (var claim in navigationClaims)
            {
                _navigationClaims.Add(claim.Key, claim.Value);
            }

            _errors.AddRange(exception.Errors);
            return false;
        }
    }

    /// <summary>Begins a nested build scope and throws accumulated errors at the outermost exit.</summary>
    public IDisposable BeginBuild()
    {
        _buildDepth++;
        return new BuildScope(this);
    }

    /// <summary>Throws all mapping errors collected across this plan build.</summary>
    public void ThrowIfCollectedErrors()
    {
        if (_errors.Count > 0)
        {
            throw SparseEntityMappingException.FromErrors(_errors);
        }
    }

    /// <summary>Claims an entity property for a DTO path; throws when already claimed.</summary>
    public void Claim(IEntityType type, string property, string dtoPath)
    {
        var key = (type, property);
        if (_claims.TryGetValue(key, out var owner))
        {
            if (owner == dtoPath)
            {
                return;
            }

            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Ambiguous,
                dtoPath,
                $"The DTO paths '{owner}' and '{dtoPath}' target the same entity member '{type.ClrType.Name}.{property}'. Map each entity member once.",
                $"{type.ClrType.Name}.{property}"
            );
        }

        _claims[key] = dtoPath;
    }

    /// <summary>Claims a reference navigation for a DTO path; throws when already claimed.</summary>
    public void ClaimNavigation(IEntityType type, string navigation, string dtoPath)
    {
        var key = (type, navigation);
        if (_navigationClaims.TryGetValue(key, out var owner))
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.Ambiguous,
                dtoPath,
                $"The DTO paths '{owner}' and '{dtoPath}' target the same entity navigation '{type.ClrType.Name}.{navigation}'. Map each navigation once.",
                $"{type.ClrType.Name}.{navigation}"
            );
        }

        _navigationClaims[key] = dtoPath;
    }

    /// <summary>Reads the persistence protection for an entity property from EF metadata.</summary>
    public static (EntityProtection Protection, string Detail) GetProtection(
        IEntityType type,
        IProperty property
    )
    {
        if (property.IsShadowProperty())
        {
            return (EntityProtection.Shadow, "a shadow property with no CLR accessor");
        }

        var primary = type.FindPrimaryKey();
        if (primary is not null && primary.Properties.Contains(property))
        {
            return (EntityProtection.Key, "a primary-key member");
        }

        if (
            type.GetKeys()
                .Where(static key => !key.IsPrimaryKey())
                .SelectMany(static key => key.Properties)
                .Contains(property)
        )
        {
            return (EntityProtection.Key, "an alternate-key member");
        }

        if (property.IsConcurrencyToken)
        {
            return (EntityProtection.Concurrency, "a concurrency token");
        }

        if (property.ValueGenerated != ValueGenerated.Never)
        {
            return (
                EntityProtection.Generated,
                $"a database-generated member ({property.ValueGenerated})"
            );
        }

        return (EntityProtection.None, string.Empty);
    }

    /// <summary>Whether a CLR type is a scalar mappable value.</summary>
    public static bool IsScalarType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsPrimitive
            || underlying.IsEnum
            || underlying == typeof(string)
            || underlying == typeof(decimal)
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateOnly)
            || underlying == typeof(TimeOnly)
            || underlying == typeof(TimeSpan)
            || underlying == typeof(Guid)
            || underlying == typeof(byte[]);
    }

    /// <summary>Whether a CLR type is collection-shaped (string and byte[] are scalars).</summary>
    public static bool IsCollectionType(Type type)
    {
        return type != typeof(string)
            && type != typeof(byte[])
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(type);
    }

    private sealed class BuildScope(PlanBuildSession session) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            session._buildDepth--;
            if (session._buildDepth == 0)
            {
                session.ThrowIfCollectedErrors();
            }
        }
    }
}
