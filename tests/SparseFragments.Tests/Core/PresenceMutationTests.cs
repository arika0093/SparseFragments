using SparseFragments;

namespace SparseFragments.Tests;

/// <summary>
/// Guards the presence-preserving mutation helpers (issue #32):
/// <see cref="OptionalExtensions"/> and <see cref="FragmentOperationExtensions"/>
/// mutate <c>ref</c> storage without collapsing Missing vs. Present(null)/default,
/// and converting copies invoke the converter only for present/set inputs.
/// </summary>
public sealed class PresenceMutationTests
{
    [Test]
    public void OptionalSetKeepsExplicitNullAndDefaultPresent()
    {
        Optional<int> valueType = Optional<int>.Missing;
        valueType.Set(0);
        valueType.IsPresent.ShouldBeTrue();
        valueType.Value.ShouldBe(0);

        Optional<string?> referenceType = Optional<string?>.Missing;
        referenceType.Set(null);
        referenceType.IsPresent.ShouldBeTrue();
        referenceType.Value.ShouldBeNull();

        referenceType.Set("hello");
        referenceType.ShouldBe(Optional<string?>.Present("hello"));
    }

    [Test]
    public void OptionalUnsetMakesMissing()
    {
        var present = Optional<int>.Present(42);
        present.Unset();
        present.IsPresent.ShouldBeFalse();
        present.ShouldBe(Optional<int>.Missing);

        var presentNull = Optional<string?>.Present(null);
        presentNull.Unset();
        presentNull.ShouldBe(Optional<string?>.Missing);
    }

    [Test]
    public void OptionalCopyFromSameTypePreservesPresence()
    {
        Optional<int> target = Optional<int>.Present(1);
        target.CopyFrom(Optional<int>.Missing);
        target.ShouldBe(Optional<int>.Missing);

        target.CopyFrom(Optional<int>.Present(0));
        target.ShouldBe(Optional<int>.Present(0));

        Optional<string?> referenceTarget = Optional<string?>.Missing;
        referenceTarget.CopyFrom(Optional<string?>.Present(null));
        referenceTarget.IsPresent.ShouldBeTrue();
        referenceTarget.Value.ShouldBeNull();
    }

    [Test]
    public void OptionalCopyFromConvertingPreservesMissingWithoutInvokingConverter()
    {
        var invocations = 0;
        Optional<string> target = Optional<string>.Present("seed");
        target.CopyFrom(Optional<int>.Missing, static value =>
        {
            throw new InvalidOperationException("Converter must not run for missing input.");
        });
        target.ShouldBe(Optional<string>.Missing);

        var missingInt = Optional<int>.Missing;
        var calls = 0;
        Optional<string> converted = Optional<string>.Missing;
        converted.CopyFrom(missingInt, value =>
        {
            calls++;
            return value.ToString();
        });
        calls.ShouldBe(0);
        converted.IsPresent.ShouldBeFalse();
        invocations.ShouldBe(0);
    }

    [Test]
    public void OptionalCopyFromConvertingKeepsPresentNullAndDefault()
    {
        Optional<int?> sourceDefault = Optional<int?>.Present(null);
        Optional<string?> target = Optional<string?>.Missing;
        target.CopyFrom(sourceDefault, static value => value?.ToString());
        target.IsPresent.ShouldBeTrue();
        target.Value.ShouldBeNull();

        Optional<int> zero = Optional<int>.Present(0);
        Optional<string> text = Optional<string>.Missing;
        text.CopyFrom(zero, static value => value.ToString());
        text.ShouldBe(Optional<string>.Present("0"));

        var calls = 0;
        Optional<int> number = Optional<int>.Present(7);
        Optional<int> doubled = Optional<int>.Missing;
        doubled.CopyFrom(number, value =>
        {
            calls++;
            return value * 2;
        });
        calls.ShouldBe(1);
        doubled.ShouldBe(Optional<int>.Present(14));
    }

    [Test]
    public void OptionalCopyFromConvertingRejectsNullConverter()
    {
        Optional<int> target = Optional<int>.Missing;
        Should.Throw<ArgumentNullException>(() =>
            target.CopyFrom(
                Optional<int>.Present(1),
                (Func<int, int>)null!
            )
        );
        Should.Throw<ArgumentNullException>(() =>
            target.CopyFrom(Optional<int>.Missing, (Func<int, int>)null!)
        );
    }

    [Test]
    public void OptionalHelpersWorkThroughGeneratedRefReturnBuilder()
    {
        var builder = new BuilderParent.Fragment().ToBuilder();

        builder.Label.Set("built");
        builder.Build().Label.ShouldBe(Optional<string?>.Present("built"));

        builder.Label.Set(null);
        var presentNull = builder.Build();
        presentNull.Label.IsPresent.ShouldBeTrue();
        presentNull.Label.Value.ShouldBeNull();

        builder.Label.Unset();
        builder.Build().Label.IsPresent.ShouldBeFalse();

        builder.Label.CopyFrom(Optional<string?>.Present("copied"));
        builder.Build().Label.ShouldBe(Optional<string?>.Present("copied"));

        builder.Label.CopyFrom(Optional<int>.Missing, static value => value.ToString());
        builder.Build().Label.IsPresent.ShouldBeFalse();
    }

    [Test]
    public void FragmentOperationSetUnsetAndSetUnchanged()
    {
        FragmentOperation<int> valueOp = FragmentOperation<int>.Unchanged;
        valueOp.Set(0);
        valueOp.Kind.ShouldBe(FragmentOperationKind.Set);
        valueOp.Value.ShouldBe(0);

        FragmentOperation<string?> referenceOp = FragmentOperation<string?>.Unchanged;
        referenceOp.Set(null);
        referenceOp.Kind.ShouldBe(FragmentOperationKind.Set);
        referenceOp.Value.ShouldBeNull();

        valueOp.Unset();
        valueOp.Kind.ShouldBe(FragmentOperationKind.Unset);

        valueOp.SetUnchanged();
        valueOp.Kind.ShouldBe(FragmentOperationKind.Unchanged);
    }

    [Test]
    public void FragmentOperationCopyFromSameTypePreservesKindAndValue()
    {
        FragmentOperation<int> target = FragmentOperation<int>.Unchanged;
        target.CopyFrom(FragmentOperation<int>.Set(0));
        target.Kind.ShouldBe(FragmentOperationKind.Set);
        target.Value.ShouldBe(0);

        target.CopyFrom(FragmentOperation<int>.Unset);
        target.Kind.ShouldBe(FragmentOperationKind.Unset);

        target.CopyFrom(FragmentOperation<int>.Unchanged);
        target.Kind.ShouldBe(FragmentOperationKind.Unchanged);

        FragmentOperation<string?> referenceTarget = FragmentOperation<string?>.Unchanged;
        referenceTarget.CopyFrom(FragmentOperation<string?>.Set(null));
        referenceTarget.Kind.ShouldBe(FragmentOperationKind.Set);
        referenceTarget.Value.ShouldBeNull();
    }

    [Test]
    public void FragmentOperationCopyFromConvertingInvokesConverterOnlyForSet()
    {
        FragmentOperation<string> target = FragmentOperation<string>.Unchanged;
        target.CopyFrom(FragmentOperation<int>.Set(21), static value => (value * 2).ToString());
        target.Kind.ShouldBe(FragmentOperationKind.Set);
        target.Value.ShouldBe("42");

        FragmentOperation<string> fromUnset = FragmentOperation<string>.Set("seed");
        fromUnset.CopyFrom(
            FragmentOperation<int>.Unset,
            static _ => throw new InvalidOperationException("Converter must not run for Unset.")
        );
        fromUnset.Kind.ShouldBe(FragmentOperationKind.Unset);

        FragmentOperation<string> fromUnchanged = FragmentOperation<string>.Set("seed");
        var calls = 0;
        fromUnchanged.CopyFrom(
            FragmentOperation<int>.Unchanged,
            value =>
            {
                calls++;
                return value.ToString();
            }
        );
        calls.ShouldBe(0);
        fromUnchanged.Kind.ShouldBe(FragmentOperationKind.Unchanged);

        // A null/default conversion result stays an explicit Set.
        FragmentOperation<string?> nullResult = FragmentOperation<string?>.Unchanged;
        nullResult.CopyFrom(FragmentOperation<int>.Set(0), static _ => (string?)null);
        nullResult.Kind.ShouldBe(FragmentOperationKind.Set);
        nullResult.Value.ShouldBeNull();
    }

    [Test]
    public void FragmentOperationCopyFromConvertingRejectsNullConverter()
    {
        FragmentOperation<int> target = FragmentOperation<int>.Unchanged;
        Should.Throw<ArgumentNullException>(() =>
            target.CopyFrom(
                FragmentOperation<int>.Set(1),
                (Func<int, int>)null!
            )
        );
        Should.Throw<ArgumentNullException>(() =>
            target.CopyFrom(FragmentOperation<int>.Unset, (Func<int, int>)null!)
        );
    }

    [Test]
    public void FragmentOperationHelpersWorkThroughGeneratedRefReturnPatch()
    {
        var patch = new Settings.Patch();

        patch.Label.Set("patched");
        patch.Label.Kind.ShouldBe(FragmentOperationKind.Set);
        patch.Label.Value.ShouldBe("patched");

        patch.Label.Set(null);
        patch.Label.Kind.ShouldBe(FragmentOperationKind.Set);
        patch.Label.Value.ShouldBeNull();

        patch.Label.Unset();
        patch.Label.Kind.ShouldBe(FragmentOperationKind.Unset);

        patch.Label.SetUnchanged();
        patch.Label.Kind.ShouldBe(FragmentOperationKind.Unchanged);

        patch.RetryCount.CopyFrom(FragmentOperation<int>.Set(9));
        patch.RetryCount.Kind.ShouldBe(FragmentOperationKind.Set);
        patch.RetryCount.Value.ShouldBe(9);

        patch.RetryCount.CopyFrom(
            FragmentOperation<string>.Set("12"),
            static value => int.Parse(value!)
        );
        patch.RetryCount.Value.ShouldBe(12);
    }
}
