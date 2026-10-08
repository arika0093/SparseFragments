using SparseFragments;

namespace SparseFragments.Tests;

public sealed class OptionalObservableExtensionTests
{
    [Test]
    public void ToObservablePreservesOptionalModelState()
    {
        var missing = Optional<ObservableHolder?>.Missing.ToObservable();
        missing.IsPresent.ShouldBeFalse();

        var presentNull = Optional<ObservableHolder?>.Present(null).ToObservable();
        presentNull.IsPresent.ShouldBeTrue();
        presentNull.Value.ShouldBeNull();

        var model = new ObservableHolder { Title = "initial" };
        var notifications = 0;
        var presentValue = Optional<ObservableHolder?>
            .Present(model)
            .ToObservable(() => notifications++);

        presentValue.IsPresent.ShouldBeTrue();
        presentValue.Value!.Title.ShouldBe("initial");
        presentValue.Value.Title = "updated";
        model.Title.ShouldBe("updated");
        notifications.ShouldBe(1);
    }
}
