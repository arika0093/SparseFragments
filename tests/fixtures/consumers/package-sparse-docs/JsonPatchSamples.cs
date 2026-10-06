using System.Text;
using SparseFragments;

// Canonical compile-checked mirror of docs/json-patch.md (#45).
// Covers the RFC 6902 bridge both ways (FromJsonPatch import, ToJsonPatch
// export with semantic round-tripping) and the typed JsonPatchException
// failure surface.
public static class JsonPatchSamples
{
    public static void Run()
    {
        ImportAndApply();
        ExportRoundTripsSemantically();
        FailuresAreTyped();
    }

    private static void ImportAndApply()
    {
        var baseline = new JsonDocsSettings.Fragment { Label = "base" };
        var baselineOpt = Optional<JsonDocsSettings.Fragment?>.Present(baseline);
        var document = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/Label","value":"patched"}]""");

        var patch = JsonDocsSettings.Patch.FromJsonPatch(baselineOpt, document);
        var updated = baseline.Apply(patch);
        DocsCheck.Require(updated.Label.Value == "patched", "JSON Patch import applies");
    }

    private static void ExportRoundTripsSemantically()
    {
        var baseline = new JsonDocsSettings.Fragment { Label = "base" };
        var baselineOpt = Optional<JsonDocsSettings.Fragment?>.Present(baseline);
        var document = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/Label","value":"patched"}]""");
        var patch = JsonDocsSettings.Patch.FromJsonPatch(baselineOpt, document);

        // Export is semantic: re-importing the export onto the same baseline
        // produces the same fragment as the original typed patch.
        var exported = patch.ToJsonPatch(baselineOpt);
        var exportedText = Encoding.UTF8.GetString(exported.ToArray());
        DocsCheck.Require(exportedText.Contains("/Label"), "JSON Patch export names the member");
        var reimported = JsonDocsSettings.Patch.FromJsonPatch(
            baselineOpt,
            Encoding.UTF8.GetBytes(exportedText));
        DocsCheck.Require(
            baseline.Apply(reimported).Label.Value == baseline.Apply(patch).Label.Value,
            "JSON Patch export round-trips semantically");
    }

    private static void FailuresAreTyped()
    {
        // Mirrors the docs' MissingTarget example: replace on a path that does
        // not exist in the baseline (Label is mapped but absent here).
        var emptyBaseline = Optional<JsonDocsSettings.Fragment?>.Present(new JsonDocsSettings.Fragment());
        var replaceMissing = Encoding.UTF8.GetBytes(
            """[{"op":"replace","path":"/Label","value":"x"}]""");

        var caught = false;
        try
        {
            JsonDocsSettings.Patch.FromJsonPatch(emptyBaseline, replaceMissing);
        }
        catch (JsonPatchException ex) when (ex.Kind == JsonPatchErrorKind.MissingTarget)
        {
            caught = true;
        }

        DocsCheck.Require(caught, "JSON Patch replace of a missing target throws MissingTarget");
    }
}

[SparseFragmentModel]
public partial class JsonDocsSettings
{
    public string? Label { get; set; }

    public int Count { get; set; }
}
