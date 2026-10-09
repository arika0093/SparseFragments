using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SparseFragments.Generator;

internal static class SparseEditSessionCoreEmitter
{
    private const string CoreHintName = "SparseFragments.GeneratedEditSessionCore.g.cs";
    private const string CurrentHintName = "SparseFragments.GeneratedEditSessionWithCurrent.g.cs";
    private const string CoreResourceName = "SparseFragments.Generator.SparseEditSessionCore.cs";
    private const string CurrentResourceName =
        "SparseFragments.Generator.SparseEditSessionWithCurrentCore.cs";

    internal static void Emit(SourceProductionContext context)
    {
        context.AddSource(
            CoreHintName,
            SourceText.From(ReadSource(CoreResourceName), Encoding.UTF8)
        );
        context.AddSource(
            CurrentHintName,
            SourceText.From(ReadSource(CurrentResourceName), Encoding.UTF8)
        );
    }

    private static string ReadSource(string resourceName)
    {
        using var stream =
            typeof(SparseEditSessionCoreEmitter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                "Embedded edit-session source template is missing: " + resourceName
            );
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
