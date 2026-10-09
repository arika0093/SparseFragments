using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseFragments.Generator.Shared;

internal static class ComparisonRuleMetadataProbe
{
    internal static void Validate(
        SparseGeneratorConfig config,
        IEnumerable<MetadataReference> references
    )
    {
        const string source = """
            using System;
            using System.Collections.Generic;
            namespace RuleMetadataProbe
            {
                public sealed class ModelAttribute : Attribute { }
                [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
                public class RuleAttribute : Attribute
                {
                    public RuleAttribute(Type value, Type comparer) { }
                }
                public sealed class GenericRuleAttribute<T> : RuleAttribute
                {
                    public GenericRuleAttribute(Type value, Type comparer) : base(value, comparer) { }
                }
                public sealed class 比較属性 : RuleAttribute
                {
                    public 比較属性(Type value, Type comparer) : base(value, comparer) { }
                }
                public sealed class RuleComparer : IEqualityComparer<string>
                {
                    public bool Equals(string? left, string? right) => left == right;
                    public int GetHashCode(string value) => value.GetHashCode();
                }
                public class Target { }
                [Model]
                [Rule(typeof(string), typeof(RuleComparer))]
                [GenericRule<int>(typeof(string), typeof(RuleComparer))]
                [比較属性(typeof(string), typeof(RuleComparer))]
                public class Root
                {
                    public Target Child { get; } = new();
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "ComparisonRuleMetadataProbe",
            new[]
            {
                CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview)),
            },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        var errors = compilation
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error);
        if (errors.Any())
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        var target = compilation.GetTypeByMetadataName("RuleMetadataProbe.Target")!;
        var valueType = compilation.GetSpecialType(SpecialType.System_String);
        foreach (
            var name in new[]
            {
                "RuleMetadataProbe.RuleAttribute",
                "RuleMetadataProbe.GenericRuleAttribute<int>",
                "RuleMetadataProbe.比較属性",
                "Absent.RuleAttribute",
            }
        )
        {
            var probeConfig = config with
            {
                ModelAttributeMetadataName = "RuleMetadataProbe.ModelAttribute",
                ComparisonAttributeMetadataName = name,
            };
            var rules = SparseComparisonRules.CreateRuleSet(
                target,
                probeConfig,
                CancellationToken.None
            );
            var found = rules.TryGetComparerType(valueType, out var comparer);
            var expected = !name.StartsWith("Absent.", StringComparison.Ordinal);
            if (found != expected || (expected && comparer?.Name != "RuleComparer"))
            {
                throw new InvalidOperationException(
                    "Configured attribute names must preserve exact matching."
                );
            }
        }
    }
}
