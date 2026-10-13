namespace SparseTypes.Tests;

public sealed class SparsePathFormattingTests
{
    [Test]
    public void FormattingCanReenterFromAKeyFormatter()
    {
        var nested = SparsePath.Root<object>().Member("Nested").Member("Value");
        var path = SparsePath
            .Root<object>()
            .Member("Items")
            .Key(new FormattingKey(nested.ToString))
            .Member("Name");
        for (var index = 0; index < 3; index++)
            path.ToString().ShouldBe("Items[\"Nested.Value\"].Name");
        nested.ToString().ShouldBe("Nested.Value");
    }

    [Test]
    public void FormattingFailureAndLargePathsDoNotLeakTextIntoLaterCalls()
    {
        var failing = SparsePath
            .Root<object>()
            .Member("Failed")
            .Key(new FormattingKey(() => throw new InvalidOperationException("format failed")));
        Should
            .Throw<InvalidOperationException>(() => failing.ToString())
            .Message.ShouldBe("format failed");
        var text = new string('x', 4096);
        SparsePath.Root<object>().Member(text).ToString().ShouldBe(text);
        SparsePath.Root<object>().Member("Short").ToString().ShouldBe("Short");
    }

    private sealed class FormattingKey : IFormattable
    {
        private readonly Func<string> _format;

        internal FormattingKey(Func<string> format) => _format = format;

        public string ToString(string? format, IFormatProvider? formatProvider) => _format();
    }
}
