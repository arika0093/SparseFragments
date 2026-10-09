using System.ComponentModel.DataAnnotations;

namespace SparseFragments.Tests;

[SparseFragmentModel]
public partial class RequiredHolder
{
    public required string Name { get; set; } = string.Empty;

    public required string? Nickname { get; set; }

    public required int Count { get; set; }

    public required string Code { get; init; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    [Required]
    public string Validated { get; set; } = string.Empty;
}

public sealed class DescriptorRequiredTests
{
    private static RequiredHolder Model() =>
        new()
        {
            Name = "name",
            Nickname = null,
            Count = 1,
            Code = "code",
        };

    [Test]
    public void RequiredKeywordIsReportedIndependently()
    {
        var session = Model().CreateEditSession();

        session.Descriptors.TryGet(nameof(RequiredHolder.Name), out var name).ShouldBeTrue();
        name.IsRequired.ShouldBeTrue();
        name.IsNullable.ShouldBeFalse();
        name.IsEditable.ShouldBeTrue();

        session
            .Descriptors.TryGet(nameof(RequiredHolder.Nickname), out var nickname)
            .ShouldBeTrue();
        nickname.IsRequired.ShouldBeTrue();
        nickname.IsNullable.ShouldBeTrue();

        session.Descriptors.TryGet(nameof(RequiredHolder.Count), out var count).ShouldBeTrue();
        count.IsRequired.ShouldBeTrue();
        count.IsNullable.ShouldBeFalse();

        // Init-only still reports required, but is not editable.
        session.Descriptors.TryGet(nameof(RequiredHolder.Code), out var code).ShouldBeTrue();
        code.IsRequired.ShouldBeTrue();
        code.IsEditable.ShouldBeFalse();
        code.IsReadOnly.ShouldBeTrue();
    }

    [Test]
    public void RequiredDiffersFromValidationAttributes()
    {
        var session = Model().CreateEditSession();

        session
            .Descriptors.TryGet(nameof(RequiredHolder.Validated), out var validated)
            .ShouldBeTrue();
        validated.IsRequired.ShouldBeFalse();
        validated.Attributes.OfType<RequiredAttribute>().Count().ShouldBe(1);

        session.Descriptors.TryGet(nameof(RequiredHolder.Name), out var name).ShouldBeTrue();
        name.IsRequired.ShouldBeTrue();
        name.Attributes.OfType<RequiredAttribute>().ShouldBeEmpty();

        session.Descriptors.TryGet(nameof(RequiredHolder.Title), out var title).ShouldBeTrue();
        title.IsRequired.ShouldBeFalse();
    }

    [Test]
    public void RequiredMembersEditLikeAnyOtherMember()
    {
        var model = Model();
        var session = model.CreateEditSession();

        session.Descriptors.TryGet(nameof(RequiredHolder.Name), out var name).ShouldBeTrue();
        name.TrySetValue("edited").ShouldBeTrue();
        model.Name.ShouldBe("edited");
        session.HasChanges.ShouldBeTrue();
    }
}
