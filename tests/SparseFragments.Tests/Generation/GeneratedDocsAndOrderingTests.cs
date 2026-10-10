using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SparseFragments.Generator;

namespace SparseFragments.Tests.Generation;

// Guards the generated-code readability contract: every generated public
// member carries XML docs (so GenerateDocumentationFile builds stay warning
// free) and members are ordered public -> internal -> private. The probe
// covers one model per emission shape so every emitter family runs.
public sealed class GeneratedDocsAndOrderingTests
{
    private const string ProbeSource = """
        using System.Collections.Generic;
        using SparseFragments;
        namespace GeneratedContract.Probe
        {
            /// <summary>Scalar probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeScalar
            {
                public string Name { get; set; } = string.Empty;
                public int Count { get; set; }
                public string? Note { get; set; }
                public int? Retry { get; set; }
                public ISet<string> Labels { get; set; } = new HashSet<string>();
            }
            /// <summary>Nested probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeNested
            {
                public ProbeScalar Child { get; set; } = new();
                [SparseMerge(MergeMode.Replace)]
                public ProbePoco? Metadata { get; set; }
                [SparseMerge(MergeMode.Replace)]
                public List<ProbePoco?> MetadataItems { get; set; } = new();
                [SparseMerge(MergeMode.Replace)]
                public Dictionary<ProbePoco, string> MetadataByKey { get; set; } = new();
                [SparseMerge(MergeMode.Replace)]
                public List<ProbeScalar> Children { get; set; } = new();
                public ProbeScalar? Maybe { get; set; }
            }
            /// <summary>Poco.</summary>
            public sealed class ProbePoco
            {
                public string Label { get; set; } = string.Empty;
                public ProbePoco? Nested { get; set; }
            }
            /// <summary>Keyed probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeKeyedItem
            {
                [SparseKey]
                public string Id { get; set; } = string.Empty;
                public string Title { get; set; } = string.Empty;
                public int Count { get; set; }
            }
            /// <summary>Roster probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeKeyedRoster
            {
                public List<ProbeKeyedItem> Items { get; set; } = new();
            }
            /// <summary>Dictionary probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeDictionaries
            {
                public Dictionary<string, int> Scores { get; set; } = new();
                public Dictionary<string, ProbeScalar> Details { get; set; } = new();
            }
            /// <summary>Immutable probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeImmutable
            {
                public ProbeImmutable(int count, string name = "base", int[]? tags = null)
                {
                    Count = count;
                    Name = name;
                    Tags = tags;
                }
                public int Count { get; }
                public string Name { get; init; }
                public int[]? Tags { get; init; }
            }
            /// <summary>Reserved probe.</summary>
            [SparseFragmentModel]
            public partial class ProbeReservedEnumeration
            {
                public string EnumerateChanges { get; set; } = string.Empty;
                public string EnumerateChangedPaths { get; set; } = string.Empty;
                public string ChangeInfo { get; set; } = string.Empty;
                public string ChangeKind { get; set; } = string.Empty;
            }
        }
        """;

    private static readonly string[] XmlDocIds =
    [
        "CS1570",
        "CS1572",
        "CS1573",
        "CS1574",
        "CS1584",
        "CS1591",
        "CS1712",
    ];

    private static readonly Lazy<Compilation> Probe = new(Generate);

    private static Compilation Generate()
    {
        var tree = CSharpSyntaxTree.ParseText(ProbeSource);
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseFragmentModelAttribute).Assembly.Location)
        );
        var compilation = CSharpCompilation.Create(
            "GeneratedContractProbe",
            [tree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseFragmentsGenerator());
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);
        return updated;
    }

    private static IEnumerable<SyntaxTree> GeneratedTrees(Compilation compilation) =>
        compilation.SyntaxTrees.Where(static tree => tree.FilePath.EndsWith(".g.cs"));

    // Re-parse with doc-diagnose options: the in-memory equivalent of
    // GenerateDocumentationFile=true used by the fixture project.
    private static Compilation WithDocDiagnostics(Compilation compilation)
    {
        var options = new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose);
        return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(
            compilation.SyntaxTrees.Select(tree =>
                tree.FilePath.EndsWith(".g.cs")
                    ? CSharpSyntaxTree.ParseText(tree.GetText(), options, path: tree.FilePath)
                    : tree
            )
        );
    }

    private static string Report(IReadOnlyList<string> violations) =>
        violations.Count + " violation(s):\n" + string.Join("\n", violations.Take(10));

    // Explicit interface implementations carry public API without modifiers.
    private static bool IsExplicitImplementation(MemberDeclarationSyntax member) =>
        member switch
        {
            MethodDeclarationSyntax method => method.ExplicitInterfaceSpecifier is not null,
            PropertyDeclarationSyntax property =>
                property.ExplicitInterfaceSpecifier is not null,
            _ => false,
        };

    private static int AccessibilityRank(MemberDeclarationSyntax member)
    {
        if (IsExplicitImplementation(member))
        {
            return 0;
        }

        if (member.Modifiers.Any(SyntaxKind.PublicKeyword))
        {
            return 0;
        }

        if (member.Modifiers.Any(SyntaxKind.InternalKeyword))
        {
            return 1;
        }

        if (member.Modifiers.Any(SyntaxKind.ProtectedKeyword))
        {
            return 1;
        }

        return 2;
    }

    private static bool IsEffectivelyPublic(MemberDeclarationSyntax member)
    {
        SyntaxNode? node = member;
        while (node is MemberDeclarationSyntax current)
        {
            if (IsExplicitImplementation(current))
            {
                return false;
            }

            if (!current.Modifiers.Any(SyntaxKind.PublicKeyword))
            {
                return false;
            }

            node = node.Parent;
        }

        return true;
    }

    private static string MemberLabel(MemberDeclarationSyntax member)
    {
        var name = member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            ConstructorDeclarationSyntax ctor => ctor.Identifier.Text,
            DestructorDeclarationSyntax dtor => dtor.Identifier.Text,
            OperatorDeclarationSyntax op => op.OperatorToken.Text,
            ConversionOperatorDeclarationSyntax => "conversion",
            PropertyDeclarationSyntax property => property.Identifier.Text,
            IndexerDeclarationSyntax => "this",
            EventDeclarationSyntax evt => evt.Identifier.Text,
            BaseFieldDeclarationSyntax field =>
                string.Join(
                    ",",
                    field.Declaration.Variables.Select(static v => v.Identifier.Text)
                ),
            TypeDeclarationSyntax type => type.Identifier.Text,
            DelegateDeclarationSyntax del => del.Identifier.Text,
            _ => member.Kind().ToString(),
        };
        return (member.Modifiers.ToString() + " " + member.Kind() + " " + name).Trim();
    }

    private static string LocationOf(SyntaxTree tree, MemberDeclarationSyntax member)
    {
        var file = Path.GetFileName(tree.FilePath);
        var line = member.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        return file + ":" + line;
    }

    [Test]
    public void GeneratedProbeCompilesWithoutErrors()
    {
        Probe
            .Value.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(static diagnostic => diagnostic.ToString())
            .ToList()
            .ShouldBeEmpty();
    }

    [Test]
    public void GeneratedCodeHasNoXmlDocWarnings()
    {
        WithDocDiagnostics(Probe.Value)
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error
            )
            .Where(diagnostic => XmlDocIds.Contains(diagnostic.Id))
            .Select(static diagnostic => diagnostic.ToString())
            .ToList()
            .ShouldBeEmpty();
    }

    [Test]
    public void GeneratedPublicMembersHaveXmlDocs()
    {
        var missing = new List<string>();
        var paramMismatches = new List<string>();
        foreach (var tree in GeneratedTrees(Probe.Value))
        {
            foreach (
                var member in tree
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .SelectMany(static type => type.Members)
            )
            {
                if (!IsEffectivelyPublic(member))
                {
                    continue;
                }

                var docs = member.GetLeadingTrivia().ToString();
                if (!docs.Contains("/// <summary", StringComparison.Ordinal))
                {
                    missing.Add(LocationOf(tree, member) + " " + MemberLabel(member));
                }

                if (member is BaseMethodDeclarationSyntax method)
                {
                    var declared = method
                        .ParameterList.Parameters.Select(static p => p.Identifier.Text)
                        .ToHashSet(StringComparer.Ordinal);
                    var documented = Regex
                        .Matches(docs, "<param name=\"([^\"]+)\"")
                        .Select(static match => match.Groups[1].Value)
                        .ToHashSet(StringComparer.Ordinal);
                    if (!declared.SetEquals(documented))
                    {
                        paramMismatches.Add(
                            LocationOf(tree, member)
                                + " "
                                + MemberLabel(member)
                                + " declared=["
                                + string.Join(",", declared)
                                + "] documented=["
                                + string.Join(",", documented)
                                + "]"
                        );
                    }
                }
            }
        }

        missing.ShouldBeEmpty(Report(missing));
        paramMismatches.ShouldBeEmpty(Report(paramMismatches));
    }

    [Test]
    public void GeneratedMembersAreOrderedPublicFirst()
    {
        var violations = new List<string>();
        foreach (var tree in GeneratedTrees(Probe.Value))
        {
            foreach (
                var type in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>()
            )
            {
                var members = type.Members.ToArray();
                for (var i = 1; i < members.Length; i++)
                {
                    var before = AccessibilityRank(members[i - 1]);
                    var after = AccessibilityRank(members[i]);
                    if (after < before)
                    {
                        violations.Add(
                            LocationOf(tree, members[i])
                                + " "
                                + type.Identifier.Text
                                + ": "
                                + MemberLabel(members[i - 1])
                                + " ("
                                + before
                                + ") -> "
                                + MemberLabel(members[i])
                                + " ("
                                + after
                                + ")"
                        );
                    }
                }
            }
        }

        violations.ShouldBeEmpty(Report(violations));
    }
}
