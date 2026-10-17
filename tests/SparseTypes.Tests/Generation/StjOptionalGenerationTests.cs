using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SparseTypes.Generator;
using SparseTypes.Generator.Shared;

namespace SparseTypes.Tests.Generation;

/// <summary>Certifies compilation-aware optional JSON support.</summary>
/// <remarks>The no-STJ probe compiles a model against framework references with every System.Text.Json assembly removed.</remarks>
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

    private static (
        Dictionary<string, string> Sources,
        ImmutableArray<Diagnostic> Diagnostics
    ) RunIncremental(ref GeneratorDriver driver, CSharpCompilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out _
        );
        outputCompilation
            .GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ShouldBeEmpty();

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

    // Model older or partial System.Text.Json references so each test isolates
    // one capability gap.
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
        stub.AppendLine("    public sealed class JsonNamingPolicy");
        stub.AppendLine("    {");
        stub.AppendLine(
            "        public static JsonNamingPolicy CamelCase { get; } = new JsonNamingPolicy();"
        );
        stub.AppendLine(
            "        public string ConvertName(string name) => throw new System.NotImplementedException();"
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
        stub.AppendLine("        public JsonNamingPolicy? PropertyNamingPolicy { get; set; }");
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
        stub.AppendLine("}");
        stub.AppendLine("namespace System.Text.Json.Serialization.Metadata");
        stub.AppendLine("{");
        stub.AppendLine("    public class JsonTypeInfo { }");
        stub.AppendLine("    public sealed class JsonTypeInfo<T> : JsonTypeInfo { }");
        stub.AppendLine("}");
        // Normalize so the tests' newline-sensitive Replace calls behave the
        // same on Windows and Unix hosts.
        return stub.ToString().Replace("\r\n", "\n");
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

    private static void UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
        CSharpCompilation compilation,
        string missingCapability
    )
    {
        var capabilities = SparseStjCapabilityDetector.Probe(compilation);
        capabilities.SupportsV1Wire.ShouldBeFalse();
        capabilities.DescribeMissing().ShouldContain(missingCapability);

        var (sources, diagnostics) = RunWithDiagnostics(compilation);
        diagnostics
            .Where(static diagnostic => diagnostic.Id == "SPF028")
            .ShouldHaveSingleItem()
            .GetMessage()
            .ShouldContain(missingCapability);
        sources.Keys.ShouldContain(static hint => hint.Contains("StjProbeModel"));
        var generated = string.Concat(sources.Values);
        generated.ShouldContain("Fragment");
        generated.ShouldContain("ChangePayload");
        foreach (var source in sources.Values)
            source.ShouldNotContain("System.Text.Json");

        CombinedCompilationHasNoErrors(compilation, sources);
    }

    private static void HasSpf028(
        ImmutableArray<Diagnostic> diagnostics,
        bool expected,
        string? expectedMessage = null
    )
    {
        var spf028 = diagnostics.Where(static diagnostic => diagnostic.Id == "SPF028").ToList();
        if (expected)
        {
            spf028.ShouldHaveSingleItem();
            if (expectedMessage is not null)
                spf028[0].GetMessage().ShouldContain(expectedMessage);
        }
        else
        {
            spf028.ShouldBeEmpty();
        }
    }

    private static void HasNoJsonArtifacts(Dictionary<string, string> sources)
    {
        foreach (var source in sources.Values)
            source.ShouldNotContain("System.Text.Json");
    }

    private static void HasStableHints(
        Dictionary<string, string> expected,
        Dictionary<string, string> actual
    ) => actual.Keys.ShouldBe(expected.Keys, ignoreOrder: true);

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
    public void FullStubSurface_EmitsBridgeWithoutDiagnostic()
    {
        // The hand-built surface is complete, so the detector must accept it
        // exactly like a current reference assembly.
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
        // lookup.
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
            .ShouldBe("JsonPropertyOrderAttribute, JsonTypeInfo/GetTypeInfo/serializer overloads");

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
    public void WrongSerializerReturnType_OmitsJsonAndReportsSpf028()
    {
        var stub = StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
            .Replace(
                "public static T? Deserialize<T>(ref Utf8JsonReader reader, Serialization.Metadata.JsonTypeInfo<T> typeInfo) => throw new System.NotImplementedException();",
                "public static string? Deserialize<T>(ref Utf8JsonReader reader, Serialization.Metadata.JsonTypeInfo<T> typeInfo) => throw new System.NotImplementedException();",
                StringComparison.Ordinal
            );

        UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
            CreateStubCompilation(stub),
            "serializer overloads"
        );
    }

    [Test]
    public void SerializerArgumentMismatch_OmitsJsonAndReportsSpf028()
    {
        var stub = StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
            .Replace(
                "public static T? Deserialize<T>(ref Utf8JsonReader reader, Serialization.Metadata.JsonTypeInfo<T> typeInfo)",
                "public static T? Deserialize<T>(ref Utf8JsonReader reader, object typeInfo)",
                StringComparison.Ordinal
            );

        UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
            CreateStubCompilation(stub),
            "serializer overloads"
        );
    }

    [Test]
    public void MissingReaderMethod_OmitsJsonAndReportsSpf028()
    {
        var stub = StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
            .Replace(
                "        public void Skip() => throw new System.NotImplementedException();\n",
                string.Empty,
                StringComparison.Ordinal
            );

        UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
            CreateStubCompilation(stub),
            "Utf8JsonReader/Writer"
        );
    }

    [Test]
    public void ConverterRefKindMismatch_OmitsJsonAndReportsSpf028()
    {
        var stub = StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
            .Replace(
                "public abstract T? Read(ref System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options);",
                "public abstract T? Read(System.Text.Json.Utf8JsonReader reader, System.Type typeToConvert, System.Text.Json.JsonSerializerOptions options);",
                StringComparison.Ordinal
            );

        UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
            CreateStubCompilation(stub),
            "JsonConverter<T>"
        );
    }

    [Test]
    public void UnusableAttributeSetter_OmitsJsonAndReportsSpf028()
    {
        var stub = StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            )
            .Replace(
                "public JsonIgnoreCondition Condition { get; set; }",
                "public JsonIgnoreCondition Condition { get; private set; }",
                StringComparison.Ordinal
            );

        UnsupportedJsonSurfaceOmitsJsonAndReportsSpf028(
            CreateStubCompilation(stub),
            "JsonIgnoreAttribute/Condition"
        );
    }

    [Test]
    public void StjReferenceAndApiChangesRegenerateOnSameIncrementalDriver()
    {
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SparseTypesGenerator());
        var withoutStjCompilation = CreateCompilation(includeStj: false);
        var withoutStj = RunIncremental(ref driver, withoutStjCompilation);

        HasSpf028(withoutStj.Diagnostics, expected: false);
        HasNoJsonArtifacts(withoutStj.Sources);

        var withStjCompilation = withoutStjCompilation.WithReferences(
            TrustedReferences(includeStj: true)
        );
        var withStj = RunIncremental(ref driver, withStjCompilation);

        HasSpf028(withStj.Diagnostics, expected: false);
        string.Concat(withStj.Sources.Values).ShouldContain("ChangePayloadEndpointJsonConverter");
        HasStableHints(withoutStj.Sources, withStj.Sources);
        JsonFreeSourcesAreIdentical(withStj.Sources, withoutStj.Sources);

        var withoutStjAgainCompilation = withStjCompilation.WithReferences(
            TrustedReferences(includeStj: false)
        );
        var withoutStjAgain = RunIncremental(ref driver, withoutStjAgainCompilation);

        HasSpf028(withoutStjAgain.Diagnostics, expected: false);
        HasNoJsonArtifacts(withoutStjAgain.Sources);
        HasStableHints(withStj.Sources, withoutStjAgain.Sources);
        JsonFreeSourcesAreIdentical(withStj.Sources, withoutStjAgain.Sources);

        var partialStubTree = CSharpSyntaxTree.ParseText(
            StjStubSource(
                includePropertyOrder: false,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: false
            ),
            path: "StjStubs.cs"
        );
        var partialStubCompilation = withoutStjAgainCompilation.AddSyntaxTrees(partialStubTree);
        var partialStub = RunIncremental(ref driver, partialStubCompilation);

        HasSpf028(
            partialStub.Diagnostics,
            expected: true,
            expectedMessage: "JsonPropertyOrderAttribute"
        );
        HasNoJsonArtifacts(partialStub.Sources);
        HasStableHints(withStj.Sources, partialStub.Sources);
        JsonFreeSourcesAreIdentical(withStj.Sources, partialStub.Sources);

        var upgradedStubTree = CSharpSyntaxTree.ParseText(
            StjStubSource(
                includePropertyOrder: true,
                includeUnmappedHandling: true,
                includeTypeInfoLookup: true
            ),
            path: "StjStubs.cs"
        );
        var upgradedStubCompilation = (CSharpCompilation)
            partialStubCompilation.ReplaceSyntaxTree(partialStubTree, upgradedStubTree);
        var upgradedStub = RunIncremental(ref driver, upgradedStubCompilation);

        HasSpf028(upgradedStub.Diagnostics, expected: false);
        string.Concat(upgradedStub.Sources.Values)
            .ShouldContain("ChangePayloadEndpointJsonConverter");
        HasStableHints(partialStub.Sources, upgradedStub.Sources);
        JsonFreeSourcesAreIdentical(upgradedStub.Sources, partialStub.Sources);
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
