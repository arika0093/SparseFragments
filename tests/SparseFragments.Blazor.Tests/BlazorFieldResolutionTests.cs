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

    private static ReadOnlyContactBook ContactBook() =>
        new()
        {
            ByName = new ReadOnlyDictionaryStub<string, OrderCustomer>(
                new Dictionary<string, OrderCustomer>
                {
                    ["billing"] = new OrderCustomer { Name = "Ada" },
                }
            ),
            ByNumber = new ReadOnlyDictionaryStub<int, OrderCustomer>(
                new Dictionary<int, OrderCustomer> { [7] = new OrderCustomer { Name = "Grace" } }
            ),
        };

    [Test]
    public void PureReadOnlyStringDictionaryResolves()
    {
        var session = ContactBook().CreateEditSession();
        (session.Model.ByName is System.Collections.IDictionary).ShouldBeFalse();

        var field = session.Field("ByName[\"billing\"].Name");
        ReferenceEquals(field.Model, session.Model.ByName["billing"]).ShouldBeTrue();
        field.FieldName.ShouldBe(nameof(OrderCustomer.Name));
    }

    [Test]
    public void PureReadOnlyNonStringDictionaryKeysResolve()
    {
        var session = ContactBook().CreateEditSession();
        (session.Model.ByNumber is System.Collections.IDictionary).ShouldBeFalse();

        var quoted = session.Field("ByNumber[\"7\"].Name");
        ReferenceEquals(quoted.Model, session.Model.ByNumber[7]).ShouldBeTrue();

        var unquoted = session.Field("ByNumber[7].Name");
        ReferenceEquals(unquoted.Model, session.Model.ByNumber[7]).ShouldBeTrue();
    }

    [Test]
    public void PureReadOnlyDictionaryRejectsAbsentAndIncompatibleKeys()
    {
        var session = ContactBook().CreateEditSession();

        Should.Throw<ArgumentException>(() => session.Field("ByName[\"missing\"].Name"));
        Should.Throw<ArgumentException>(() => session.Field("ByNumber[\"not-a-number\"].Name"));
        Should.Throw<ArgumentException>(() => session.Field("ByNumber[8].Name"));
    }

    [Test]
    public void PureReadOnlyDictionaryValuesAcceptValidationErrors()
    {
        var session = ContactBook().CreateEditSession();
        var editContext = session.CreateEditContext();
        var store = session.CreateValidationStore(editContext);

        session.AddValidationError(store, session.Field("ByName[\"billing\"].Name"), "Required.");

        editContext
            .GetValidationMessages(session.Field("ByName[\"billing\"].Name"))
            .ShouldContain("Required.");
    }
}
