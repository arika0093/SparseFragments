namespace SparseFragments.Blazor.Tests;

public sealed class DescriptorPathTests
{
    // Raw live-model access now hides behind ISparseEditSession<TModel>.
    private static T Raw<T>(SparseFragments.ISparseEditSession<T> session)
        where T : class => session.Model;

    [Test]
    public void FieldResolvesDescriptorCanonicalDictionaryPaths()
    {
        const string trickyKey = "we]ird\"key\\x";
        var session = new OrderDto
        {
            Contacts = new Dictionary<string, OrderCustomer>
            {
                [trickyKey] = new OrderCustomer { Name = "Grace" },
            },
        }.CreateEditSession();

        session.Descriptors.TryGet(nameof(OrderDto.Contacts), out var contacts).ShouldBeTrue();
        var dict = contacts.Dictionary.ShouldNotBeNull();
        var set = dict!.GetValueDescriptors(trickyKey).ShouldNotBeNull();
        set!.TryGet(nameof(OrderCustomer.Name), out var name).ShouldBeTrue();
        name.Path.ShouldBe("Contacts[\"we]ird\\\"key\\\\x\"].Name");

        var field = session.Field(name.Path);
        ReferenceEquals(field.Model, Raw(session).Contacts[trickyKey]).ShouldBeTrue();
        field.FieldName.ShouldBe(nameof(OrderCustomer.Name));
    }
}
