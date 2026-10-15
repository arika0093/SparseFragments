using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SparseTypes.Generator;

// SPF033 recognizes direct typed Map registrations with member selectors and
// scoped lambdas.
internal static class SparseEntityMapperConfigureAnalysis
{
    internal static bool TryFindParameterlessConfigure(
        INamedTypeSymbol mapper,
        out IMethodSymbol? configure
    )
    {
        configure = null;
        foreach (var candidate in mapper.GetMembers("Configure").OfType<IMethodSymbol>())
        {
            if (
                !candidate.IsStatic
                && candidate.Parameters.Length == 0
                && candidate.TypeParameters.Length == 0
                && candidate.ReturnsVoid
            )
            {
                if (configure is not null)
                {
                    return false;
                }

                configure = candidate;
            }
        }

        return configure is not null;
    }

    internal static HashSet<(string Scope, string Member)>? TryCollectCoveredMembers(
        INamedTypeSymbol mapper
    )
    {
        var supported = mapper
            .GetMembers("Configure")
            .OfType<IMethodSymbol>()
            .Where(static method => !method.IsStatic && method.Parameters.Length == 0)
            .ToArray();
        if (supported.Length == 0)
        {
            return [];
        }

        if (
            supported.Length != 1
            || supported[0].TypeParameters.Length != 0
            || !supported[0].ReturnsVoid
        )
        {
            return null;
        }

        var configure = supported[0];
        var declaration =
            configure.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
            as MethodDeclarationSyntax;
        if (declaration is null)
        {
            return null;
        }

        IReadOnlyList<StatementSyntax>? statements = null;
        if (declaration.Body is { } body)
        {
            statements = body.Statements;
        }
        else if (declaration.ExpressionBody?.Expression is ExpressionSyntax arrow)
        {
            statements = [SyntaxFactory.ExpressionStatement(arrow)];
        }

        if (statements is null)
        {
            return null;
        }

        var covered = new HashSet<(string Scope, string Member)>();
        var vars = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Map"] = string.Empty,
        };
        return ProcessConfigureStatements(statements, vars, covered) ? covered : null;
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

    private static bool ProcessConfigureInvocation(
        InvocationExpressionSyntax invocation,
        Dictionary<string, string> vars,
        HashSet<(string Scope, string Member)> covered
    )
    {
        if (!TryGetReceiver(invocation, vars, out var scope, out var method))
        {
            return false;
        }

        var arguments = invocation.ArgumentList.Arguments;
        switch (method)
        {
            case "Member":
                // Only a converter-bearing Member resolves a member: a
                // two-argument or policy-only Member cannot supply a
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
            case "Read":
                return true;
            default:
                return false;
        }
    }

    // Receivers are the generated Map property (Map. or this.Map.) or a nested
    // scope lambda parameter (lines., child.).
    private static bool TryGetReceiver(
        InvocationExpressionSyntax invocation,
        Dictionary<string, string> vars,
        out string scope,
        out string method
    )
    {
        scope = string.Empty;
        method = string.Empty;
        if (invocation.Expression is not MemberAccessExpressionSyntax access)
        {
            return false;
        }

        if (access.Name is not IdentifierNameSyntax methodName)
        {
            return false;
        }

        method = methodName.Identifier.Text;
        if (access.Expression is IdentifierNameSyntax receiver)
        {
            return vars.TryGetValue(receiver.Identifier.Text, out scope!);
        }

        if (
            access.Expression is MemberAccessExpressionSyntax qualified
            && qualified.Expression is ThisExpressionSyntax
            && qualified.Name is IdentifierNameSyntax qualifiedName
            && vars.TryGetValue(qualifiedName.Identifier.Text, out scope!)
        )
        {
            return true;
        }

        return false;
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
