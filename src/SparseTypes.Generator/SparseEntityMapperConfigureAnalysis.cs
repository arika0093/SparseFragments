using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator;

// Supported declarative Configure subset for SPF033 suppression: direct
// typed builder calls whose DTO selector is a single member access, with
// Object/Collection/Element lambdas tracked for scope. Anything else
// (helpers, control flow, dynamic calls) makes the whole Configure
// unanalyzable, in which case nothing is provable and diagnostics stay
// silent with runtime plan validation as the backstop.
internal static class SparseEntityMapperConfigureAnalysis
{
    internal static HashSet<(string Scope, string Member)>? TryCollectCoveredMembers(
        INamedTypeSymbol mapper
    )
    {
        var configure = mapper
            .GetMembers("Configure")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method => method.Parameters.Length == 1);
        if (configure is null)
        {
            return null;
        }

        var declaration =
            configure.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (declaration?.Body is not { } body)
        {
            return null;
        }

        var covered = new HashSet<(string Scope, string Member)>();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [configure.Parameters[0].Name] = string.Empty,
        };
        return ProcessConfigureBlock(body, vars, covered) ? covered : null;
    }

    private static bool ProcessConfigureBlock(
        BlockSyntax block,
        Dictionary<string, string> vars,
        HashSet<(string Scope, string Member)> covered
    )
    {
        foreach (var statement in block.Statements)
        {
            if (
                statement
                    is not ExpressionStatementSyntax
                    {
                        Expression: InvocationExpressionSyntax invocation
                    }
                || !ProcessConfigureInvocation(invocation, vars, covered)
            )
            {
                return false;
            }
        }

        return true;
    }

    private static bool ProcessConfigureInvocation(
        InvocationExpressionSyntax invocation,
        Dictionary<string, string> vars,
        HashSet<(string Scope, string Member)> covered
    )
    {
        if (
            invocation.Expression
                is not MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax receiver,
                    Name: IdentifierNameSyntax method
                }
            || !vars.TryGetValue(receiver.Identifier.Text, out var scope)
        )
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        switch (method.Identifier.Text)
        {
            case "Member":
                // Only a converter-bearing Member resolves a member: a
                // two-argument or policy-only Member cannot supply a conversion.
                if (
                    arguments.Count > 0
                    && HasConverterArgument(arguments)
                    && TrySingleMember(arguments[0].Expression, out var member)
                )
                {
                    covered.Add((scope, member));
                }

                return true;
            case "Write":
            case "Ignore":
                if (
                    arguments.Count > 0
                    && TrySingleMember(arguments[0].Expression, out var written)
                )
                {
                    covered.Add((scope, written));
                }

                return true;
            case "OnMissing":
            case "AllowWrite":
                // Policies alone resolve nothing; still supported syntax.
                return true;
            case "Object":
            case "Collection":
                if (
                    arguments.Count == 0
                    || !TrySingleMember(arguments[0].Expression, out var childMember)
                )
                {
                    return true;
                }

                covered.Add((scope, childMember));
                if (
                    arguments.Count < 3
                    || !TryLambdaStatements(
                        arguments[2].Expression,
                        out var parameter,
                        out var statements
                    )
                )
                {
                    return arguments.Count < 3;
                }

                var childScope = scope.Length == 0 ? childMember : scope + "." + childMember;
                var nested = new Dictionary<string, string>(vars, StringComparer.Ordinal)
                {
                    [parameter] = childScope,
                };
                return ProcessConfigureStatements(statements, nested, covered);
            case "Element":
                if (
                    arguments.Count == 0
                    || !TryLambdaStatements(
                        arguments[0].Expression,
                        out var elementParameter,
                        out var elementStatements
                    )
                )
                {
                    return false;
                }

                var scoped = new Dictionary<string, string>(vars, StringComparer.Ordinal)
                {
                    [elementParameter] = scope,
                };
                return ProcessConfigureStatements(elementStatements, scoped, covered);
            case "MissingDefault":
            case "DeclareCollection":
            case "DeclareCollectionElement":
            case "Key":
            case "Create":
            case "Remove":
            case "Delete":
            case "Detach":
            case "SoftDelete":
            case "Order":
            case "UnassignedKey":
                return true;
            default:
                return false;
        }
    }

    private static bool ProcessConfigureStatements(
        IReadOnlyList<StatementSyntax> statements,
        Dictionary<string, string> vars,
        HashSet<(string Scope, string Member)> covered
    )
    {
        foreach (var statement in statements)
        {
            if (
                statement
                    is not ExpressionStatementSyntax
                    {
                        Expression: InvocationExpressionSyntax invocation
                    }
                || !ProcessConfigureInvocation(invocation, vars, covered)
            )
            {
                return false;
            }
        }

        return true;
    }

    // A converter lives past the DTO/entity selector pair: the third or fourth
    // argument as a lambda (a policy enum member access is not one).
    private static bool HasConverterArgument(SeparatedSyntaxList<ArgumentSyntax> arguments)
    {
        for (var index = 2; index < arguments.Count; index++)
        {
            if (
                arguments[index].Expression
                is SimpleLambdaExpressionSyntax
                    or ParenthesizedLambdaExpressionSyntax
            )
            {
                return true;
            }
        }

        return false;
    }

    private static bool TrySingleMember(ExpressionSyntax expression, out string member)
    {
        member = string.Empty;
        if (
            expression
                is SimpleLambdaExpressionSyntax
                {
                    Parameter: var token,
                    Body: MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax receiver,
                        Name: IdentifierNameSyntax name
                    }
                }
            && receiver.Identifier.Text == token.Identifier.Text
        )
        {
            member = name.Identifier.Text;
            return true;
        }

        if (
            expression
                is ParenthesizedLambdaExpressionSyntax
                {
                    ParameterList.Parameters.Count: 1
                } parenthesized
            && parenthesized.Body
                is MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax parenthesizedReceiver,
                    Name: IdentifierNameSyntax parenthesizedName
                }
            && parenthesizedReceiver.Identifier.Text
                == parenthesized.ParameterList.Parameters[0].Identifier.Text
        )
        {
            member = parenthesizedName.Identifier.Text;
            return true;
        }

        return false;
    }

    private static bool TryLambdaStatements(
        ExpressionSyntax expression,
        out string parameter,
        out IReadOnlyList<StatementSyntax> statements
    )
    {
        parameter = string.Empty;
        statements = [];
        if (expression is SimpleLambdaExpressionSyntax simple)
        {
            parameter = simple.Parameter.Identifier.Text;
            if (simple.Body is BlockSyntax block)
            {
                statements = block.Statements;
                return true;
            }

            if (simple.Body is ExpressionSyntax body)
            {
                statements = [SyntaxFactory.ExpressionStatement(body)];
                return true;
            }

            return false;
        }

        if (
            expression is ParenthesizedLambdaExpressionSyntax
            {
                ParameterList.Parameters.Count: 1
            } parenthesized
        )
        {
            parameter = parenthesized.ParameterList.Parameters[0].Identifier.Text;
            if (parenthesized.Body is BlockSyntax parenthesizedBlock)
            {
                statements = parenthesizedBlock.Statements;
                return true;
            }

            if (parenthesized.Body is ExpressionSyntax parenthesizedBody)
            {
                statements = [SyntaxFactory.ExpressionStatement(parenthesizedBody)];
                return true;
            }
        }

        return false;
    }
}
