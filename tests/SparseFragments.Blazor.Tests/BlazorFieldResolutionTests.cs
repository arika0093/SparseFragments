using Microsoft.AspNetCore.Components.Forms;

namespace SparseFragments.Blazor.Tests;

public sealed class BlazorFieldResolutionTests
{
    private static GuidDirectory Directory() =>
        new()
        {
            ById =
            {
                [Guid.Parse("3d6f3c1e-1a2b-4c5d-9e8f-0123456789ab")] = new GuidContact
                {
                    Id = Guid.Parse("3d6f3c1e-1a2b-4c5d-9e8f-0123456789ab"),
                    Name = "Ada",
                },
            },
        };

    [Test]
    public void GuidDictionaryKeysResolveFromQuotedAndUnquotedPaths()
    {
        var session = Directory().CreateEditSession();
        const string id = "3d6f3c1e-1a2b-4c5d-9e8f-0123456789ab";

        var quoted = session.Field($"ById[\"{id}\"].Name");
        ReferenceEquals(quoted.Model, session.Model.ById[Guid.Parse(id)]).ShouldBeTrue();
        quoted.FieldName.ShouldBe(nameof(GuidContact.Name));

        var unquoted = session.Field($"ById[{id}].Name");
        ReferenceEquals(unquoted.Model, session.Model.ById[Guid.Parse(id)]).ShouldBeTrue();
    }

    [Test]
    public void InvalidGuidDictionaryKeysFailAsInvalidPaths()
    {
        var session = Directory().CreateEditSession();

        Should.Throw<ArgumentException>(() => session.Field("ById[\"not-a-guid\"].Name"));
        Should.Throw<ArgumentException>(() => session.Field("ById[not-a-guid].Name"));
        Should.Throw<ArgumentException>(() =>
            session.Field("ById[\"00000000-0000-0000-0000-000000000000\"].Name")
        );
    }
}
