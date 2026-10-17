using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using BenchmarkDotNet.Attributes;
using SparseTypes;

[SparseModel]
public partial class BenchJsonIndex003
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
}

[SparseModel]
public partial class BenchJsonIndex004
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
    public int P003 { get; set; }
}

[SparseModel]
public partial class BenchJsonIndex008
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
    public int P003 { get; set; }
    public int P004 { get; set; }
    public int P005 { get; set; }
    public int P006 { get; set; }
    public int P007 { get; set; }
}

[SparseModel]
public partial class BenchJsonIndex016
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
    public int P003 { get; set; }
    public int P004 { get; set; }
    public int P005 { get; set; }
    public int P006 { get; set; }
    public int P007 { get; set; }
    public int P008 { get; set; }
    public int P009 { get; set; }
    public int P010 { get; set; }
    public int P011 { get; set; }
    public int P012 { get; set; }
    public int P013 { get; set; }
    public int P014 { get; set; }
    public int P015 { get; set; }
}

[SparseModel]
public partial class BenchJsonIndex064
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
    public int P003 { get; set; }
    public int P004 { get; set; }
    public int P005 { get; set; }
    public int P006 { get; set; }
    public int P007 { get; set; }
    public int P008 { get; set; }
    public int P009 { get; set; }
    public int P010 { get; set; }
    public int P011 { get; set; }
    public int P012 { get; set; }
    public int P013 { get; set; }
    public int P014 { get; set; }
    public int P015 { get; set; }
    public int P016 { get; set; }
    public int P017 { get; set; }
    public int P018 { get; set; }
    public int P019 { get; set; }
    public int P020 { get; set; }
    public int P021 { get; set; }
    public int P022 { get; set; }
    public int P023 { get; set; }
    public int P024 { get; set; }
    public int P025 { get; set; }
    public int P026 { get; set; }
    public int P027 { get; set; }
    public int P028 { get; set; }
    public int P029 { get; set; }
    public int P030 { get; set; }
    public int P031 { get; set; }
    public int P032 { get; set; }
    public int P033 { get; set; }
    public int P034 { get; set; }
    public int P035 { get; set; }
    public int P036 { get; set; }
    public int P037 { get; set; }
    public int P038 { get; set; }
    public int P039 { get; set; }
    public int P040 { get; set; }
    public int P041 { get; set; }
    public int P042 { get; set; }
    public int P043 { get; set; }
    public int P044 { get; set; }
    public int P045 { get; set; }
    public int P046 { get; set; }
    public int P047 { get; set; }
    public int P048 { get; set; }
    public int P049 { get; set; }
    public int P050 { get; set; }
    public int P051 { get; set; }
    public int P052 { get; set; }
    public int P053 { get; set; }
    public int P054 { get; set; }
    public int P055 { get; set; }
    public int P056 { get; set; }
    public int P057 { get; set; }
    public int P058 { get; set; }
    public int P059 { get; set; }
    public int P060 { get; set; }
    public int P061 { get; set; }
    public int P062 { get; set; }
    public int P063 { get; set; }
}

[SparseModel]
public partial class BenchJsonIndex256
{
    public int P000 { get; set; }
    public int P001 { get; set; }
    public int P002 { get; set; }
    public int P003 { get; set; }
    public int P004 { get; set; }
    public int P005 { get; set; }
    public int P006 { get; set; }
    public int P007 { get; set; }
    public int P008 { get; set; }
    public int P009 { get; set; }
    public int P010 { get; set; }
    public int P011 { get; set; }
    public int P012 { get; set; }
    public int P013 { get; set; }
    public int P014 { get; set; }
    public int P015 { get; set; }
    public int P016 { get; set; }
    public int P017 { get; set; }
    public int P018 { get; set; }
    public int P019 { get; set; }
    public int P020 { get; set; }
    public int P021 { get; set; }
    public int P022 { get; set; }
    public int P023 { get; set; }
    public int P024 { get; set; }
    public int P025 { get; set; }
    public int P026 { get; set; }
    public int P027 { get; set; }
    public int P028 { get; set; }
    public int P029 { get; set; }
    public int P030 { get; set; }
    public int P031 { get; set; }
    public int P032 { get; set; }
    public int P033 { get; set; }
    public int P034 { get; set; }
    public int P035 { get; set; }
    public int P036 { get; set; }
    public int P037 { get; set; }
    public int P038 { get; set; }
    public int P039 { get; set; }
    public int P040 { get; set; }
    public int P041 { get; set; }
    public int P042 { get; set; }
    public int P043 { get; set; }
    public int P044 { get; set; }
    public int P045 { get; set; }
    public int P046 { get; set; }
    public int P047 { get; set; }
    public int P048 { get; set; }
    public int P049 { get; set; }
    public int P050 { get; set; }
    public int P051 { get; set; }
    public int P052 { get; set; }
    public int P053 { get; set; }
    public int P054 { get; set; }
    public int P055 { get; set; }
    public int P056 { get; set; }
    public int P057 { get; set; }
    public int P058 { get; set; }
    public int P059 { get; set; }
    public int P060 { get; set; }
    public int P061 { get; set; }
    public int P062 { get; set; }
    public int P063 { get; set; }
    public int P064 { get; set; }
    public int P065 { get; set; }
    public int P066 { get; set; }
    public int P067 { get; set; }
    public int P068 { get; set; }
    public int P069 { get; set; }
    public int P070 { get; set; }
    public int P071 { get; set; }
    public int P072 { get; set; }
    public int P073 { get; set; }
    public int P074 { get; set; }
    public int P075 { get; set; }
    public int P076 { get; set; }
    public int P077 { get; set; }
    public int P078 { get; set; }
    public int P079 { get; set; }
    public int P080 { get; set; }
    public int P081 { get; set; }
    public int P082 { get; set; }
    public int P083 { get; set; }
    public int P084 { get; set; }
    public int P085 { get; set; }
    public int P086 { get; set; }
    public int P087 { get; set; }
    public int P088 { get; set; }
    public int P089 { get; set; }
    public int P090 { get; set; }
    public int P091 { get; set; }
    public int P092 { get; set; }
    public int P093 { get; set; }
    public int P094 { get; set; }
    public int P095 { get; set; }
    public int P096 { get; set; }
    public int P097 { get; set; }
    public int P098 { get; set; }
    public int P099 { get; set; }
    public int P100 { get; set; }
    public int P101 { get; set; }
    public int P102 { get; set; }
    public int P103 { get; set; }
    public int P104 { get; set; }
    public int P105 { get; set; }
    public int P106 { get; set; }
    public int P107 { get; set; }
    public int P108 { get; set; }
    public int P109 { get; set; }
    public int P110 { get; set; }
    public int P111 { get; set; }
    public int P112 { get; set; }
    public int P113 { get; set; }
    public int P114 { get; set; }
    public int P115 { get; set; }
    public int P116 { get; set; }
    public int P117 { get; set; }
    public int P118 { get; set; }
    public int P119 { get; set; }
    public int P120 { get; set; }
    public int P121 { get; set; }
    public int P122 { get; set; }
    public int P123 { get; set; }
    public int P124 { get; set; }
    public int P125 { get; set; }
    public int P126 { get; set; }
    public int P127 { get; set; }
    public int P128 { get; set; }
    public int P129 { get; set; }
    public int P130 { get; set; }
    public int P131 { get; set; }
    public int P132 { get; set; }
    public int P133 { get; set; }
    public int P134 { get; set; }
    public int P135 { get; set; }
    public int P136 { get; set; }
    public int P137 { get; set; }
    public int P138 { get; set; }
    public int P139 { get; set; }
    public int P140 { get; set; }
    public int P141 { get; set; }
    public int P142 { get; set; }
    public int P143 { get; set; }
    public int P144 { get; set; }
    public int P145 { get; set; }
    public int P146 { get; set; }
    public int P147 { get; set; }
    public int P148 { get; set; }
    public int P149 { get; set; }
    public int P150 { get; set; }
    public int P151 { get; set; }
    public int P152 { get; set; }
    public int P153 { get; set; }
    public int P154 { get; set; }
    public int P155 { get; set; }
    public int P156 { get; set; }
    public int P157 { get; set; }
    public int P158 { get; set; }
    public int P159 { get; set; }
    public int P160 { get; set; }
    public int P161 { get; set; }
    public int P162 { get; set; }
    public int P163 { get; set; }
    public int P164 { get; set; }
    public int P165 { get; set; }
    public int P166 { get; set; }
    public int P167 { get; set; }
    public int P168 { get; set; }
    public int P169 { get; set; }
    public int P170 { get; set; }
    public int P171 { get; set; }
    public int P172 { get; set; }
    public int P173 { get; set; }
    public int P174 { get; set; }
    public int P175 { get; set; }
    public int P176 { get; set; }
    public int P177 { get; set; }
    public int P178 { get; set; }
    public int P179 { get; set; }
    public int P180 { get; set; }
    public int P181 { get; set; }
    public int P182 { get; set; }
    public int P183 { get; set; }
    public int P184 { get; set; }
    public int P185 { get; set; }
    public int P186 { get; set; }
    public int P187 { get; set; }
    public int P188 { get; set; }
    public int P189 { get; set; }
    public int P190 { get; set; }
    public int P191 { get; set; }
    public int P192 { get; set; }
    public int P193 { get; set; }
    public int P194 { get; set; }
    public int P195 { get; set; }
    public int P196 { get; set; }
    public int P197 { get; set; }
    public int P198 { get; set; }
    public int P199 { get; set; }
    public int P200 { get; set; }
    public int P201 { get; set; }
    public int P202 { get; set; }
    public int P203 { get; set; }
    public int P204 { get; set; }
    public int P205 { get; set; }
    public int P206 { get; set; }
    public int P207 { get; set; }
    public int P208 { get; set; }
    public int P209 { get; set; }
    public int P210 { get; set; }
    public int P211 { get; set; }
    public int P212 { get; set; }
    public int P213 { get; set; }
    public int P214 { get; set; }
    public int P215 { get; set; }
    public int P216 { get; set; }
    public int P217 { get; set; }
    public int P218 { get; set; }
    public int P219 { get; set; }
    public int P220 { get; set; }
    public int P221 { get; set; }
    public int P222 { get; set; }
    public int P223 { get; set; }
    public int P224 { get; set; }
    public int P225 { get; set; }
    public int P226 { get; set; }
    public int P227 { get; set; }
    public int P228 { get; set; }
    public int P229 { get; set; }
    public int P230 { get; set; }
    public int P231 { get; set; }
    public int P232 { get; set; }
    public int P233 { get; set; }
    public int P234 { get; set; }
    public int P235 { get; set; }
    public int P236 { get; set; }
    public int P237 { get; set; }
    public int P238 { get; set; }
    public int P239 { get; set; }
    public int P240 { get; set; }
    public int P241 { get; set; }
    public int P242 { get; set; }
    public int P243 { get; set; }
    public int P244 { get; set; }
    public int P245 { get; set; }
    public int P246 { get; set; }
    public int P247 { get; set; }
    public int P248 { get; set; }
    public int P249 { get; set; }
    public int P250 { get; set; }
    public int P251 { get; set; }
    public int P252 { get; set; }
    public int P253 { get; set; }
    public int P254 { get; set; }
    public int P255 { get; set; }
}

/// <summary>Mutable naming policy modeling stateful custom policies.</summary>
public sealed class IndexStatefulNamingPolicy : JsonNamingPolicy
{
    /// <summary>Gets or sets the suffix appended to every converted name.</summary>
    public string Suffix { get; set; } = "_s";

    /// <inheritdoc />
    public override string ConvertName(string name) => name + Suffix;
}

/// <summary>Shared payload and options setup for the fragment index benchmarks.</summary>
internal static class FragmentJsonIndexSetup
{
    /// <summary>Creates serializer options for the named naming scenario.</summary>
    internal static JsonSerializerOptions CreateOptions(string naming)
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        switch (naming)
        {
            case "camel":
                options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                break;
            case "camelInsensitive":
                options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.PropertyNameCaseInsensitive = true;
                break;
            case "stateful":
                options.PropertyNamingPolicy = new IndexStatefulNamingPolicy();
                break;
        }
        return options;
    }

    /// <summary>Resolves the wire name used by a naming scenario.</summary>
    internal static string WireName(string naming, int index)
    {
        var declared = "P" + index.ToString("000");
        return naming switch
        {
            "camel" or "camelInsensitive" => JsonNamingPolicy.CamelCase.ConvertName(declared),
            "stateful" => declared + "_s",
            _ => declared,
        };
    }

    /// <summary>Builds a single-object payload for the given shape.</summary>
    internal static byte[] BuildPayload(int memberCount, string shape, string naming)
    {
        var builder = new StringBuilder();
        builder.Append('{');
        var first = true;
        void AppendProperty(string name, string value)
        {
            if (!first)
            {
                builder.Append(',');
            }
            first = false;
            builder.Append('"').Append(name).Append("\":").Append(value);
        }
        var last = memberCount - 1;
        if (shape == "dense")
        {
            for (var i = 0; i < memberCount; i++)
                AppendProperty(WireName(naming, i), i.ToString());
        }
        else
        {
            AppendProperty(WireName(naming, last), last.ToString());
            if (shape == "unknown")
            {
                for (var u = 0; u < 20; u++)
                    AppendProperty("u" + u.ToString("00"), """{"nested":[1,2]}""");
            }
        }
        builder.Append('}');
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}

/// <summary>Compares per-read index construction against cached index reuse.</summary>
[MemoryDiagnoser]
public class FragmentJsonIndexBenchmarks
{
    private static readonly int[] MemberCounts = [3, 4, 8, 16, 64, 256];
    private static readonly string[] Shapes = ["dense", "sparse", "unknown"];
    private static readonly string[] Namings = ["default", "camel", "camelInsensitive", "stateful"];

    /// <summary>Gets or sets the model width. Three members use linear dispatch; wider models use the effective-name index.</summary>
    [Params(3, 4, 8, 16, 64, 256)]
    public int MemberCount { get; set; }

    /// <summary>Gets or sets the payload shape.</summary>
    [Params("dense", "sparse", "unknown")]
    public string Shape { get; set; } = "dense";

    /// <summary>Gets or sets the naming scenario. Only stateless inputs reuse the cached index.</summary>
    [Params("default", "camel", "camelInsensitive", "stateful")]
    public string Naming { get; set; } = "default";

    private Dictionary<string, JsonSerializerOptions> _options = null!;
    private Dictionary<(int MemberCount, string Shape, string Naming), byte[]> _payloads = null!;

    /// <summary>Builds every payload and validates each combination once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _options = new Dictionary<string, JsonSerializerOptions>(StringComparer.Ordinal);
        foreach (var naming in Namings)
            _options[naming] = FragmentJsonIndexSetup.CreateOptions(naming);
        _payloads = new Dictionary<(int, string, string), byte[]>();
        foreach (var size in MemberCounts)
        foreach (var shape in Shapes)
        foreach (var naming in Namings)
        {
            var payload = FragmentJsonIndexSetup.BuildPayload(size, shape, naming);
            if (!IsLastPresent(size, payload, _options[naming]))
                throw new InvalidOperationException("Fragment index payload validation failed.");
            _payloads[(size, shape, naming)] = payload;
        }
    }

    private static bool IsLastPresent(int size, byte[] payload, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return size switch
        {
            3 => BenchJsonIndex003
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex003.Fragment),
                    options
                )!
                .P002.IsPresent,
            4 => BenchJsonIndex004
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex004.Fragment),
                    options
                )!
                .P003.IsPresent,
            8 => BenchJsonIndex008
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex008.Fragment),
                    options
                )!
                .P007.IsPresent,
            16 => BenchJsonIndex016
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex016.Fragment),
                    options
                )!
                .P015.IsPresent,
            64 => BenchJsonIndex064
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex064.Fragment),
                    options
                )!
                .P063.IsPresent,
            256 => BenchJsonIndex256
                .Fragment.JsonConverter.Read(
                    ref reader,
                    typeof(BenchJsonIndex256.Fragment),
                    options
                )!
                .P255.IsPresent,
            _ => throw new InvalidOperationException("Unexpected member count."),
        };
    }

    /// <summary>Reads one fragment through the generated converter.</summary>
    [Benchmark]
    public object ReadSingle()
    {
        var payload = _payloads[(MemberCount, Shape, Naming)];
        var options = _options[Naming];
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        return MemberCount switch
        {
            3 => BenchJsonIndex003.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex003.Fragment),
                options
            )!,
            4 => BenchJsonIndex004.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex004.Fragment),
                options
            )!,
            8 => BenchJsonIndex008.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex008.Fragment),
                options
            )!,
            16 => BenchJsonIndex016.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex016.Fragment),
                options
            )!,
            64 => BenchJsonIndex064.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex064.Fragment),
                options
            )!,
            256 => BenchJsonIndex256.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex256.Fragment),
                options
            )!,
            _ => throw new InvalidOperationException("Unexpected member count."),
        };
    }
}

/// <summary>Measures repeated converter reads across arrays of fragments.</summary>
[MemoryDiagnoser]
public class FragmentJsonIndexArrayBenchmarks
{
    /// <summary>Gets or sets the number of fragments per array.</summary>
    [Params(1, 100, 10000)]
    public int ArraySize { get; set; }

    /// <summary>Gets or sets the naming scenario.</summary>
    [Params("default", "camel", "camelInsensitive", "stateful")]
    public string Naming { get; set; } = "default";

    private Dictionary<string, JsonSerializerOptions> _options = null!;
    private Dictionary<(int ArraySize, string Naming), byte[]> _arrays = null!;

    /// <summary>Builds dense eight-member arrays and validates each combination once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _options = new Dictionary<string, JsonSerializerOptions>(StringComparer.Ordinal)
        {
            ["default"] = FragmentJsonIndexSetup.CreateOptions("default"),
            ["camel"] = FragmentJsonIndexSetup.CreateOptions("camel"),
            ["camelInsensitive"] = FragmentJsonIndexSetup.CreateOptions("camelInsensitive"),
            ["stateful"] = FragmentJsonIndexSetup.CreateOptions("stateful"),
        };
        _arrays = new Dictionary<(int, string), byte[]>();
        foreach (var size in new[] { 1, 100, 10000 })
        foreach (var naming in _options.Keys)
        {
            var element = Encoding.UTF8.GetString(
                FragmentJsonIndexSetup.BuildPayload(8, "dense", naming)
            );
            var payload = Encoding.UTF8.GetBytes(
                "[" + string.Join(",", Enumerable.Repeat(element, size)) + "]"
            );
            if (ReadArray(payload, _options[naming]) != size)
                throw new InvalidOperationException("Fragment index array validation failed.");
            _arrays[(size, naming)] = payload;
        }
    }

    /// <summary>Reads every fragment of an array through the generated converter.</summary>
    [Benchmark]
    public int ReadArrayBenchmark()
    {
        var count = ReadArray(_arrays[(ArraySize, Naming)], _options[Naming]);
        if (count != ArraySize)
            throw new InvalidOperationException("Array payload mismatch.");
        return count;
    }

    private static int ReadArray(byte[] payload, JsonSerializerOptions options)
    {
        var reader = new Utf8JsonReader(payload);
        reader.Read();
        var count = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            BenchJsonIndex008.Fragment.JsonConverter.Read(
                ref reader,
                typeof(BenchJsonIndex008.Fragment),
                options
            );
            count++;
        }
        return count;
    }
}
