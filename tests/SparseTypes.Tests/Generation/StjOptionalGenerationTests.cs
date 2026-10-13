using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

/// <summary>Certifies compilation-aware optional JSON support (issues #242/#243/#244).</summary>
/// <remarks>
/// The no-STJ probe compiles a model against framework references with every
/// System.Text.Json assembly removed, then asserts the generator emits only
/// serializer-independent APIs and the combined compilation still builds.
/// The STJ probe asserts the v1 bridge appears, and the pair asserts losing
/// the reference drops only JSON artifacts with stable hint names.
/// Representative older and partial API sets (issue #264) compile against
/// hand-built stub surfaces instead of the runner's reference assemblies:
/// each lane either emits compiling code with the available symbols or omits
/// JSON with an actionable SPF028 diagnostic, and reference upgrades restore
/// the bridge without stale sources or unrelated output churn.
/// </remarks>
public sealed class StjOptionalGenerationTests
{
    private const string ModelSource = """
        using SparseTypes;
        using System.Collections.Generic;

        [SparseModel]
        public partial class StjProbeModel
        {
            public string? Label { get; set; }

            public List<string> Tags { get; set; } = new();
        }
        """;

    private static CSharpCompilation CreateCompilation(bool includeStj)
    {
        var tree = CSharpSyntaxTree.ParseText(ModelSource, path: "StjProbeModel.cs");
        return CSharpCompilation.Create(
            "StjOptionalProbe",
            [tree],
            TrustedReferences(includeStj),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static List<MetadataReference> TrustedReferences(bool includeStj)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(
            Path.PathSeparator
        );
        var references = trusted
            .Where(path =>
                includeStj
                || !Path.GetFileName(path).StartsWith("System.Text.Json", StringComparison.Ordinal)
            )
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList();
        references.Add(
            MetadataReference.CreateFromFile(typeof(SparseModelAttribute).Assembly.Location)
        );
        return references;
    }

    private static CSharpCompilation CreateStubCompilation(string stubSource)
    {
        var modelTree = CSharpSyntaxTree.ParseText(ModelSource, path: "StjProbeModel.cs");
        var stubTree = CSharpSyntaxTree.ParseText(stubSource, path: "StjStubs.cs");
        return CSharpCompilation.Create(
            "StjStubProbe",
            [modelTree, stubTree],
            TrustedReferences(includeStj: false),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary
            ).WithNullableContextOptions(NullableContextOptions.Enable)
        );
    }

    private static Dictionary<string, string> Run(CSharpCompilation compilation)
    {
        var (sources, diagnostics) = RunWithDiagnostics(compilation);
        diagnostics
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
        return sources;
    }

    private static (
        Dictionary<string, string> Sources,
        System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics
    ) RunWithDiagnostics(CSharpCompilation compilation)
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        var sources = result
            .Results.SelectMany(static result => result.GeneratedSources)
            .GroupBy(static source => source.HintName)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().SourceText.ToString(),
                StringComparer.Ordinal
            );
        return (sources, result.Diagnostics);
    }

    private static void CombinedCompilationHasNoErrors(
        CSharpCompilation compilation,
        Dictionary<string, string> sources
    )
    {
        var generated = sources.Select(pair =>
            CSharpSyntaxTree.ParseText(pair.Value, path: pair.Key)
        );
        var combined = compilation.AddSyntaxTrees(generated);
        combined
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();
    }

    // Representative System.Text.Json surface standing in for older or partial
    // references (issue #264). The detector probes these declarations exactly
    // like real reference assemblies: ordering attributes and metadata lookup
    // model an older set, while a handling enum without Disallow models a
    // partially available member signature. The remaining APIs always resolve
    // so each lane isolates one capability gap.
    private static string StjStubSource(
        bool includePropertyOrder,
        bool includeUnmappedHandling,
        bool includeTypeInfoLookup
    )
    {
        var stub = new System.Text.StringBuilder();
        stub.AppendLine("namespace System.Text.Json");
        stub.AppendLine("{");
        stub.AppendLine("    public sealed class JsonSerializer");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public static T? Deserialize<T>(ref Utf8JsonReader reader, Serialization.Metadata.JsonTypeInfo<T> typeInfo) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public static void Serialize<T>(Utf8JsonWriter writer, T? value, Serialization.Metadata.JsonTypeInfo<T> typeInfo) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public static object? Deserialize(ref Utf8JsonReader reader, Serialization.Metadata.JsonTypeInfo typeInfo) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public static void Serialize(Utf8JsonWriter writer, object? value, Serialization.Metadata.JsonTypeInfo typeInfo) => throw new System.NotImplementedException();"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public struct Utf8JsonReader");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public JsonTokenType TokenType => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public bool Read() => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public string? GetString() => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public bool ValueTextEquals(System.ReadOnlySpan<byte> utf8Text) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void Skip() => throw new System.NotImplementedException();"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class Utf8JsonWriter");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public void WriteStartObject() => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void WriteEndObject() => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void WritePropertyName(string name) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void WriteNullValue() => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void WriteString(string propertyName, string? value) => throw new System.NotImplementedException();"
        );
        stub.AppendLine(
            "        public void WriteStringValue(string? value) => throw new System.NotImplementedException();"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public enum JsonTokenType");
        stub.AppendLine("    {");
        stub.AppendLine("        Null,");
        stub.AppendLine("        StartObject,");
        stub.AppendLine("        EndObject,");
        stub.AppendLine("        PropertyName,");
        stub.AppendLine("        String,");
        stub.AppendLine("        Number,");
        stub.AppendLine("        True,");
        stub.AppendLine("        False,");
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonSerializerOptions");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public Serialization.JsonNamingPolicy? PropertyNamingPolicy { get; set; }"
        );
        stub.AppendLine("        public bool PropertyNameCaseInsensitive { get; set; }");
        stub.AppendLine(
            "        public Serialization.JsonUnmappedMemberHandling UnmappedMemberHandling { get; set; }"
        );
        if (includeTypeInfoLookup)
            stub.AppendLine(
                "        public Serialization.Metadata.JsonTypeInfo GetTypeInfo(System.Type type) => throw new System.NotImplementedException();"
            );
        stub.AppendLine("    }");
        stub.AppendLine("    public class JsonException : System.Exception");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonException() { }");
        stub.AppendLine("        public JsonException(string? message) : base(message) { }");
        stub.AppendLine("    }");
        stub.AppendLine("}");
        stub.AppendLine("namespace System.Text.Json.Serialization");
        stub.AppendLine("{");
        stub.AppendLine("    public abstract class JsonConverter<T>");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options);"
        );
        stub.AppendLine(
            "        public abstract void Write(System.Text.Json.Utf8JsonWriter writer, T? value, System.Text.Json.JsonSerializerOptions options);"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonPropertyNameAttribute : System.Attribute");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonPropertyNameAttribute(string name) { }");
        stub.AppendLine("    }");
        if (includePropertyOrder)
        {
            stub.AppendLine(
                "    public sealed class JsonPropertyOrderAttribute : System.Attribute"
            );
            stub.AppendLine("    {");
            stub.AppendLine("        public JsonPropertyOrderAttribute(int order) { }");
            stub.AppendLine("    }");
        }
        stub.AppendLine("    public enum JsonIgnoreCondition");
        stub.AppendLine("    {");
        stub.AppendLine("        Never,");
        stub.AppendLine("        Always,");
        stub.AppendLine("        WhenWritingDefault,");
        stub.AppendLine("        WhenWritingNull,");
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonIgnoreAttribute : System.Attribute");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonIgnoreAttribute() { }");
        stub.AppendLine("        public JsonIgnoreCondition Condition { get; set; }");
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonPolymorphicAttribute : System.Attribute");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonPolymorphicAttribute() { }");
        stub.AppendLine("        public string? TypeDiscriminatorPropertyName { get; set; }");
        stub.AppendLine("    }");
        stub.AppendLine(
            "    [System.AttributeUsage(System.AttributeTargets.Class, AllowMultiple = true)]"
        );
        stub.AppendLine("    public sealed class JsonDerivedTypeAttribute : System.Attribute");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonDerivedTypeAttribute(System.Type derivedType) { }");
        stub.AppendLine(
            "        public JsonDerivedTypeAttribute(System.Type derivedType, string typeDiscriminator) { }"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public enum JsonUnmappedMemberHandling");
        stub.AppendLine("    {");
        stub.AppendLine("        Skip,");
        if (includeUnmappedHandling)
            stub.AppendLine("        Disallow,");
        stub.AppendLine("    }");
        stub.AppendLine(
            "    public sealed class JsonUnmappedMemberHandlingAttribute : System.Attribute"
        );
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public JsonUnmappedMemberHandlingAttribute(JsonUnmappedMemberHandling handling) { }"
        );
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonConverterAttribute : System.Attribute");
        stub.AppendLine("    {");
        stub.AppendLine("        public JsonConverterAttribute(System.Type converterType) { }");
        stub.AppendLine("    }");
        stub.AppendLine("    public sealed class JsonNamingPolicy");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public static JsonNamingPolicy CamelCase { get; } = new JsonNamingPolicy();"
        );
        stub.AppendLine(
            "        public string ConvertName(string name) => throw new System.NotImplementedException();"
        );
        stub.AppendLine("    }");
        stub.AppendLine("}");
        stub.AppendLine("namespace System.Text.Json.Serialization.Metadata");
        stub.AppendLine("{");
        stub.AppendLine("    public class JsonTypeInfo { }");
        stub.AppendLine("    public sealed class JsonTypeInfo<T> : JsonTypeInfo { }");
        stub.AppendLine("}");
        return stub.ToString();
    }

    private static void JsonFreeSourcesAreIdentical(
        Dictionary<string, string> jsonRun,
        Dictionary<string, string> otherRun
    )
    {
        foreach (var pair in jsonRun)
        {
            if (pair.Value.Contains("System.Text.Json", StringComparison.Ordinal))
                continue;
            otherRun[pair.Key].ShouldBe(pair.Value);
        }
    }

    [Test]
    public void WithoutStj_EmitsSerializerIndependentCoreOnly()
    {
        var compilation = CreateCompilation(includeStj: false);
        var sources = Run(compilation);

        // Core families still emitted.
        sources.Keys.ShouldContain(static hint => hint.Contains("StjProbeModel"));
        var merged = string.Concat(sources.Values);
        merged.ShouldContain("ChangePayload");
        merged.ShouldContain("ToPayload");

        // No JSON surface anywhere.
        foreach (var pair in sources)
            pair.Value.ShouldNotContain("System.Text.Json");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void WithStj_EmitsVersion1Bridge()
    {
        var compilation = CreateCompilation(includeStj: true);
        var sources = Run(compilation);
        var merged = string.Concat(sources.Values);

        merged.ShouldContain("ChangePayloadEndpointJsonConverter");
        merged.ShouldContain("ChangePayloadItemKindJsonConverter");
        merged.ShouldContain("JsonPolymorphic");
        merged.ShouldContain("JsonUnmappedMemberHandling");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void LosingStjReferenceDropsOnlyJsonArtifacts()
    {
        var withStj = Run(CreateCompilation(includeStj: true));
        var withoutStj = Run(CreateCompilation(includeStj: false));

        // Deterministic regeneration: identical hint names, no stale sources.
        withoutStj.Keys.ShouldBe(withStj.Keys, ignoreOrder: true);
        foreach (var hint in withStj.Keys)
        {
            var hadJson = withStj[hint].Contains("System.Text.Json");
            var hasJson = withoutStj[hint].Contains("System.Text.Json");
            if (hadJson)
                hasJson.ShouldBeFalse(hint);
        }
        foreach (var source in withoutStj.Values)
            source.ShouldNotContain("System.Text.Json");
    }

    [Test]
    public void FullStubSurface_EmitsBridgeWithoutDiagnostic()
    {
        // The hand-built surface is complete, so the detector must accept it
        // exactly like a current reference assembly: bridge emitted, no
        // diagnostic, and the combined compilation still builds.
        var compilation = CreateStubCompilation(
            StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
        );
        var sources = Run(compilation);
        var merged = string.Concat(sources.Values);

        merged.ShouldContain("ChangePayloadEndpointJsonConverter");
        merged.ShouldContain("ChangePayloadItemKindJsonConverter");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void OlderSurface_OmitsJsonAndReportsSpf028()
    {
        // Representative older set: no ordering attribute and no metadata
        // lookup. Emission must omit JSON with SPF028 naming both gaps while
        // the serializer-independent core still compiles.
        var compilation = CreateStubCompilation(
            StjStubSource(
                includePropertyOrder: false,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: false
            )
        );
        var capabilities = SparseStjCapabilityDetector.Probe(compilation);
        capabilities.SupportsV1Wire.ShouldBeFalse();
        capabilities
            .DescribeMissing()
            .ShouldBe("JsonPropertyOrderAttribute, JsonTypeInfo/GetTypeInfo");

        var (sources, diagnostics) = RunWithDiagnostics(compilation);
        var spf028 = diagnostics.Where(static diagnostic => diagnostic.Id == "SPF028").ToList();
        spf028.ShouldHaveSingleItem();
        spf028[0].GetMessage().ShouldContain("JsonPropertyOrderAttribute");

        foreach (var pair in sources)
            pair.Value.ShouldNotContain("System.Text.Json");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void PartialSurface_OmitsJsonAndReportsSpf028()
    {
        // Partially available member signature: the handling enum exists but
        // Disallow is missing, so strict payloads cannot be expressed.
        var compilation = CreateStubCompilation(
            StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: false,
                includeTypeInfoLookup: true
            )
        );
        var capabilities = SparseStjCapabilityDetector.Probe(compilation);
        capabilities.SupportsV1Wire.ShouldBeFalse();
        capabilities.DescribeMissing().ShouldBe("JsonUnmappedMemberHandling.Disallow");

        var (sources, diagnostics) = RunWithDiagnostics(compilation);
        var spf028 = diagnostics.Where(static diagnostic => diagnostic.Id == "SPF028").ToList();
        spf028.ShouldHaveSingleItem();
        spf028[0].GetMessage().ShouldContain("Disallow");

        foreach (var pair in sources)
            pair.Value.ShouldNotContain("System.Text.Json");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    [Test]
    public void UpgradingPartialSurfaceToFull_RestoresBridgeWithStableHints()
    {
        var olderCompilation = CreateStubCompilation(
            StjStubSource(
                includePropertyOrder: false,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: false
            )
        );
        var (olderSources, olderDiagnostics) = RunWithDiagnostics(olderCompilation);
        olderDiagnostics.Where(static diagnostic => diagnostic.Id == "SPF028").Count().ShouldBe(1);

        var fullCompilation = CreateStubCompilation(
            StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
        );
        var fullSources = Run(fullCompilation);

        fullSources.Keys.ShouldBe(olderSources.Keys, ignoreOrder: true);
        foreach (var source in olderSources.Values)
            source.ShouldNotContain("System.Text.Json");
        string.Concat(fullSources.Values).ShouldContain("ChangePayloadEndpointJsonConverter");
        JsonFreeSourcesAreIdentical(fullSources, olderSources);
    }

    [Test]
    public void GainingStjReference_RestoresBridgeWithStableHints()
    {
        var withoutStj = Run(CreateCompilation(includeStj: false));
        var withStj = Run(CreateCompilation(includeStj: true));

        withStj.Keys.ShouldBe(withoutStj.Keys, ignoreOrder: true);
        string.Concat(withStj.Values).ShouldContain("ChangePayloadEndpointJsonConverter");
        foreach (var source in withoutStj.Values)
            source.ShouldNotContain("System.Text.Json");
        JsonFreeSourcesAreIdentical(withStj, withoutStj);
    }
}
