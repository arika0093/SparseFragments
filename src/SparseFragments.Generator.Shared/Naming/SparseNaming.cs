using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SparseFragments.Generator.Shared;

/// <summary>Shared identifier and type-name formatting used by both generators.</summary>
internal static class SparseNaming
{
    public const int InitialSchemaVersion = 1;

    public static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
                | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public static string TypeName(ITypeSymbol type) => type.ToDisplayString(TypeFormat);

    public static string NonNullableTypeName(ITypeSymbol type) =>
        type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);

    public static string EscapeIdentifier(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None
            ? "@" + identifier
            : identifier;

    public static string PatchApiPrefix(IEnumerable<string> memberNames)
    {
        var names = new HashSet<string>(memberNames);
        var prefix = new StringBuilder();
        while (
            new[] { "Between", "Compose", "Invert", "Rebase" }.Any(name =>
                names.Contains(prefix.ToString() + name)
            )
        )
            prefix.Append("Sparse");
        return prefix.ToString();
    }

    public static string WholeApiPrefix(IEnumerable<string> memberNames)
    {
        var names = new HashSet<string>(memberNames);
        var prefix = new StringBuilder();
        while (
            new[] { "Set", "SetNull", "Remove", "IsEmpty" }.Any(name =>
                names.Contains(prefix.ToString() + name)
            )
        )
            prefix.Append("Sparse");
        return prefix.ToString();
    }

    public static string Sanitize(string identifier, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(identifier.Length);
        foreach (var character in identifier)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    public static string GetStableTypeHash(string value, CancellationToken cancellationToken)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;
        var hash = offsetBasis;
        foreach (var character in value)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash = unchecked((hash ^ character) * prime);
        }

        return hash.ToString("X8", CultureInfo.InvariantCulture);
    }

    public static string MergeModeName(int mode) =>
        mode switch
        {
            SparseMergeModes.Default => "Default",
            SparseMergeModes.Deep => "Deep",
            SparseMergeModes.Append => "Append",
            SparseMergeModes.SetUnion => "SetUnion",
            SparseMergeModes.Custom => "Custom",
            _ => "Replace",
        };
}

/// <summary>Canonical bracket-key path suffixes shared by descriptors and change sets.</summary>
/// <remarks>
/// Every key renders double-quoted with backslash and quote escaping, using the
/// InvariantCulture text of the key. Numeric and Guid keys are not distinguished
/// from strings; consumers parse the quoted text back with the key type.
/// </remarks>
internal static class SparseCanonicalKeyPath
{
    /// <summary>Builds a path-suffix expression for converted key text.</summary>
    /// <param name="convertedTextExpression">Expression producing the key text.</param>
    internal static string AppendQuoted(string convertedTextExpression) =>
        " + \"[\\\"\" + "
        + convertedTextExpression
        + ".Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"\\\"\", \"\\\\\\\"\") + \"\\\"]\"";

    /// <summary>Builds a path-suffix expression for a boxed key value.</summary>
    /// <param name="keyExpression">Expression producing the key (any type).</param>
    internal static string AppendKey(string keyExpression) =>
        AppendQuoted(
            "(global::System.Convert.ToString("
                + keyExpression
                + ", global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)"
        );

    /// <summary>Builds a path-suffix expression for already-escaped key text.</summary>
    /// <param name="escapedTextExpression">Expression producing escaped key text.</param>
    /// <remarks>
    /// No further escaping is applied, so change-set paths composed as
    /// <c>__SparseEscapeKey(__SparseKeyText(key))</c> (issues #137/#138) keep
    /// their canonical <c>Name["key"]</c> grammar without double-escaping.
    /// Raw key text uses <see cref="AppendQuoted"/> instead.
    /// </remarks>
    internal static string AppendEscaped(string escapedTextExpression) =>
        " + \"[\\\"\" + " + escapedTextExpression + " + \"\\\"]\"";
}
