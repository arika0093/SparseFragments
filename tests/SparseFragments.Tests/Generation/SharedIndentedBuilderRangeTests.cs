using SparseFragments.Generator.Shared;

namespace SparseFragments.Tests.Generation;

public sealed class SharedIndentedBuilderRangeTests
{
    [Test]
    public void RangeAddsIndentationAndKeepsTheFollowingAppendOnItsCurrentLine()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None) { IndentOffset = 1 };
        code.Append("xxa\nbYY", 2, 3).AppendLine("c");
        code.ToString().ShouldBe("    a\n    bc\n");
    }

    [Test]
    public void NewlineOutsideTheRangeDoesNotChangeLineState()
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        code.Append("xxabc\nzz", 2, 3).Append('d').Append('\n');
        code.Append("x\n", 1, 0);
        code.ToString().ShouldBe("abcd\n");
    }

    [Test]
    [Arguments(-1, 1)]
    [Arguments(0, -1)]
    [Arguments(4, 0)]
    [Arguments(3, 1)]
    [Arguments(1, int.MaxValue)]
    public void InvalidRangesAreRejectedBeforeMutation(int start, int length)
    {
        var code = new SharedIndentedBuilder(CancellationToken.None);
        code.Append("prefix");
        Should.Throw<ArgumentOutOfRangeException>(() => code.Append("abc", start, length));
        code.ToString().ShouldBe("prefix");
    }

    [Test]
    public void RangeAppendObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var code = new SharedIndentedBuilder(cancellation.Token);
        cancellation.Cancel();
        Should.Throw<OperationCanceledException>(() => code.Append("abc", 1, 1));
    }
}
