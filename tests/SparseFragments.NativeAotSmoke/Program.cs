using System.Text;
using System.Text.Json;
using SparseFragments.NativeAotSmoke;

var failures = 0;

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS: {name}");
        return;
    }

    Console.WriteLine($"FAIL: {name}");
    failures++;
}

static JsonSerializerOptions AotOptions()
{
    var options = new JsonSerializerOptions { TypeInfoResolver = AotSerializerContext.Default };
    options.Converters.Add(new AotWidget.Fragment.FragmentJsonConverter());
    return options;
}

static byte[] Utf8(string json) => Encoding.UTF8.GetBytes(json);

// Serializes through the generated converter directly (the same path the
// JSON Patch bridge uses), which stays trim/NativeAOT clean unlike the
// reflection-dispatched JsonSerializer.Serialize overloads.
static string ToCanonicalJson(AotWidget.Fragment fragment, JsonSerializerOptions options)
{
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
        new AotWidget.Fragment.FragmentJsonConverter().Write(writer, fragment, options);
    }
    return Encoding.UTF8.GetString(stream.ToArray());
}

// 1. Optional distinguishes missing, present null, and present values.
var missing = Optional<string?>.Missing;
Check(!missing.IsPresent, "Optional.Missing is not present");

var presentNull = Optional<string?>.Present(null);
Check(presentNull.IsPresent && presentNull.Value is null, "Optional.Present(null) is present null");

Optional<string?> implicitValue = "hello";
Check(
    implicitValue.IsPresent && implicitValue.Value == "hello",
    "Optional implicit value is present"
);

// 2. Merge: explicit null wins, missing falls through.
var defaults = AotWidget.Fragment.From(
    new AotWidget
    {
        Name = "fallback",
        Nested = new AotNested { Host = "db.local" },
    }
);
var clearsName = new AotWidget.Fragment { Name = (string?)null };
Check(
    defaults.Merge(clearsName).ToModel().Name is null,
    "Merge keeps explicit null over lower layer"
);

var saysNothing = new AotWidget.Fragment();
Check(
    defaults.Merge(saysNothing).ToModel().Name == "fallback",
    "Merge falls through to lower layer when missing"
);

// 3. Nested fragments merge member by member; Append concatenates collections.
var lower = new AotWidget.Fragment
{
    Nested = Optional<AotNested.Fragment?>.Present(
        new AotNested.Fragment { Host = Optional<string>.Present("db.local") }
    ),
    Plugins = Optional<IReadOnlyList<string>>.Present(["base-plugin"]),
};
var higher = new AotWidget.Fragment
{
    Nested = new AotNested.Fragment { Port = 9 },
    Plugins = Optional<IReadOnlyList<string>>.Present(["extra-plugin"]),
};
var merged = lower.Merge(higher).ToModel();
Check(merged.Nested!.Host == "db.local", "Merge preserves lower nested member");
Check(merged.Nested.Port == 9, "Merge overrides with higher nested member");
Check(merged.Plugins.SequenceEqual(["base-plugin", "extra-plugin"]), "Merge appends collections");

// 4. Diff captures the delta; ApplyChanges replays it.
var before = new AotWidget { Name = "before", Count = 1 };
var after = new AotWidget { Name = "after", Count = 1 };
var diff = AotWidget.Fragment.Diff(before, after);
var replayed = AotWidget.Fragment.From(before).ApplyChanges(diff);
Check(replayed.Name.Value == "after", "Diff/ApplyChanges replays changed member");
Check(replayed.Count.Value == 1, "Diff/ApplyChanges keeps unchanged member");

// 5. Typed patch: Set/Unset/SetNull without mutating the original.
var original = AotWidget.Fragment.From(
    new AotWidget
    {
        Name = "original",
        Nested = new AotNested { Host = "keep", Port = 7 },
    }
);
var patch = new AotWidget.Patch { Name = (string?)null };
patch.Nested.Port = 9;
Check(!patch.IsEmpty, "Patch with edits is not empty");
Check(new AotWidget.Patch().IsEmpty, "Empty patch reports IsEmpty");

var updated = original.Apply(patch);
Check(updated.Name.IsPresent && updated.Name.Value is null, "Patch applies explicit null");
Check(
    updated.Nested.Value!.Host.Value == "keep" && updated.Nested.Value.Port.Value == 9,
    "Patch merges nested set"
);
Check(
    original.Nested.Value!.Port.Value == 7 && original.Name.Value == "original",
    "Apply does not mutate the original"
);

var remove = new AotWidget.Patch();
remove.Nested.Unset();
Check(!original.Apply(remove).Nested.IsPresent, "Patch Unset drops the contribution");

var toNull = new AotWidget.Patch();
toNull.Nested.SetNull();
var nulled = original.Apply(toNull);
Check(nulled.Nested.IsPresent && nulled.Nested.Value is null, "Patch SetNull is present null");

// 6. Builder copies with edits; DeepClone isolates the graph.
var edited = original.ToBuilder();
edited.Name = Optional<string?>.Missing;
var rebuilt = edited.Build();
Check(!rebuilt.Name.IsPresent && original.Name.IsPresent, "Builder drops the member");

var model = new AotWidget
{
    Name = "m",
    Nested = new AotNested { Host = "h" },
};
var clone = model.DeepClone();
clone.Nested!.Host = "changed";
Check(
    model.Nested.Host == "h" && clone.Nested.Host == "changed",
    "DeepClone isolates nested references"
);

// 7. JSON round trip through the generated converter with source-generated metadata.
var jsonOptions = AotOptions();
var jsonBaseline = new AotWidget.Fragment
{
    Name = Optional<string?>.Present("a"),
    Count = Optional<int>.Present(1),
    Nested = Optional<AotNested.Fragment?>.Present(
        new AotNested.Fragment { Host = Optional<string>.Present("h") }
    ),
};
var canonical = ToCanonicalJson(jsonBaseline, jsonOptions);
Check(canonical.Contains("\"Name\":\"a\""), "Fragment serializes present members");
Check(!canonical.Contains("Plugins"), "Fragment omits missing members");

var explicitNullFragment = new AotWidget.Fragment { Name = Optional<string?>.Present(null) };
Check(
    ToCanonicalJson(explicitNullFragment, jsonOptions).Contains("\"Name\":null"),
    "Fragment preserves explicit null in JSON"
);

var jsonPatch = AotWidget.Patch.FromJsonPatch(
    Optional<AotWidget.Fragment?>.Present(jsonBaseline),
    Utf8("""[{"op":"replace","path":"/Name","value":"b"}]"""),
    jsonOptions
);
Check(
    jsonBaseline.Apply(jsonPatch).Name.Value == "b",
    "FromJsonPatch applies with source-generated metadata"
);

var exported = Encoding.UTF8.GetString(
    jsonPatch
        .ToJsonPatch(Optional<AotWidget.Fragment?>.Present(jsonBaseline), jsonOptions)
        .ToArray()
);
Check(exported.Contains("/Name"), "ToJsonPatch exports the member path");

// 8. Without a resolver and with reflection disabled, the bridge fails fast.
var failedFast = false;
try
{
    _ = AotWidget.Patch.FromJsonPatch(
        Optional<AotWidget.Fragment?>.Present(jsonBaseline),
        Utf8("""[{"op":"replace","path":"/Name","value":"c"}]""")
    );
}
catch (InvalidOperationException)
{
    failedFast = true;
}
Check(failedFast, "JSON bridge fails fast without a resolver when reflection is disabled");

if (failures > 0)
{
    Console.WriteLine($"{failures} NATIVEAOT SMOKE CHECK(S) FAILED");
    return 1;
}

Console.WriteLine("ALL NATIVEAOT SMOKE CHECKS PASSED");
return 0;
