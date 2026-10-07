using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace SparseFragments.Generator.Shared;

/// <summary>Product-neutral source-location snapshot suitable for incremental values.</summary>
/// <remarks><see cref="Location"/> lacks value equality, so equality-sensitive paths use this snapshot.</remarks>
internal readonly record struct SparseLocationSnapshot(string FilePath, int Start, int Length)
{
    public static SparseLocationSnapshot? Capture(Location? location)
    {
        if (location is null)
        {
            return null;
        }

        if (!location.IsInSource)
        {
            return new SparseLocationSnapshot(string.Empty, 0, 0);
        }

        return new SparseLocationSnapshot(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan.Start,
            location.SourceSpan.Length
        );
    }

    public override string ToString() => FilePath + "(" + Start + "," + Length + ")";
}

/// <summary>Product-neutral diagnostic payload keyed by diagnostic ID plus format arguments.</summary>
internal readonly record struct SparseDiagnosticPayload(
    string DescriptorId,
    SparseLocationSnapshot? Location,
    ImmutableArray<string?> Arguments
)
{
    public static SparseDiagnosticPayload FromDiagnostic(SparseGeneratorDiagnostic diagnostic) =>
        new(
            diagnostic.DescriptorId,
            SparseLocationSnapshot.Capture(diagnostic.Location),
            diagnostic.Arguments.IsDefault ? ImmutableArray<string?>.Empty : diagnostic.Arguments
        );

    public string? Argument(int index) =>
        !Arguments.IsDefault && (uint)index < (uint)Arguments.Length ? Arguments[index] : null;

    // ImmutableArray equality is reference-based; compare via SparseSequence.
    public bool Equals(SparseDiagnosticPayload other) =>
        string.Equals(DescriptorId, other.DescriptorId, StringComparison.Ordinal)
        && Location == other.Location
        && SparseSequence.Equal(Arguments, other.Arguments);

    public override int GetHashCode() =>
        unchecked(
            (StringComparer.Ordinal.GetHashCode(DescriptorId) * 31 + (Location?.GetHashCode() ?? 0))
                * 31
            + SparseSequence.Hash(Arguments)
        );
}

/// <summary>Reusable incremental-generator emission plumbing.</summary>
internal static class SparseExternalInit
{
    internal const string IsExternalInitMetadataName =
        "System.Runtime.CompilerServices.IsExternalInit";

    /// <summary>
    /// Determines whether the generator must emit its own <c>IsExternalInit</c>
    /// marker: emission is enabled by configuration and the compilation does not
    /// already reference an accessible marker type.
    /// </summary>
    public static bool ShouldEmitIsExternalInit(Compilation compilation, bool configured)
    {
        if (!configured)
        {
            return false;
        }

        var marker = compilation.GetTypeByMetadataName(IsExternalInitMetadataName);
        if (marker is null)
        {
            return true;
        }

        if (SymbolEqualityComparer.Default.Equals(marker.ContainingAssembly, compilation.Assembly))
        {
            return false;
        }

        return marker.DeclaredAccessibility != Accessibility.Public;
    }
}
