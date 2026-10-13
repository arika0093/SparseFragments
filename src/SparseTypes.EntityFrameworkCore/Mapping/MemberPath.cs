using System.Linq.Expressions;

namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Parses member-access lambdas into CLR member chains.
/// </summary>
internal sealed class MemberPath
{
    private MemberPath(string[] names)
    {
        Names = names;
    }

    /// <summary>Gets the member names from the lambda parameter outward.</summary>
    public string[] Names { get; }

    /// <summary>Gets the dotted path text.</summary>
    public string PathText => string.Join(".", Names);

    /// <summary>Parses a plain member-access lambda.</summary>
    public static MemberPath Parse<T, TValue>(Expression<Func<T, TValue?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var names = new List<string>();
        var current = selector.Body;
        while (true)
        {
            if (
                current is UnaryExpression unary
                && (
                    unary.NodeType == ExpressionType.Convert
                    || unary.NodeType == ExpressionType.ConvertChecked
                )
            )
            {
                current = unary.Operand;
                continue;
            }

            if (current is MemberExpression member)
            {
                names.Add(member.Member.Name);
                current = member.Expression;
                continue;
            }

            if (current is ParameterExpression)
            {
                break;
            }

            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The mapping selector '{selector}' must be plain member access (for example x => x.Address.City)."
            );
        }

        names.Reverse();
        if (names.Count == 0)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                null,
                $"The mapping selector '{selector}' selects no member."
            );
        }

        return new MemberPath(names.ToArray());
    }

    /// <summary>Returns the single member name, requiring a one-level selector.</summary>
    /// <param name="role">Whether the selector addresses a DTO or an entity member.</param>
    public string SingleName(string role)
    {
        if (Names.Length != 1)
        {
            throw new SparseEntityMappingException(
                SparseEntityMappingFailure.MetadataMismatch,
                PathText,
                $"The {role} selector must address a single member, not '{PathText}'."
            );
        }

        return Names[0];
    }
}
