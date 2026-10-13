using SparseFragments.Playground.Models;

namespace SparseFragments.Tests.Generation;

public sealed class PlaygroundLineIdentityTests
{
    private static PlaygroundOrderLineDto Existing(int id, string product = "tea") =>
        new()
        {
            Id = id,
            Product = product,
            Qty = 1,
        };

    private static PlaygroundOrderLineDto Added(Guid temp, string product = "cake") =>
        new()
        {
            Product = product,
            Qty = 2,
            TemporaryId = temp,
        };

    private static PlaygroundOrderDto Draft(params PlaygroundOrderLineDto[] lines) =>
        new() { Customer = "cafe", Lines = lines.ToList() };

    [Test]
    public void ReorderedPersistedRowsCorrelateByTemporaryIdentity()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var submitted = Draft(Added(tempA, "alpha"), Existing(1), Added(tempB, "beta"));
        // Persisted order differs from submit order; IDs are database-assigned.
        var persisted = Draft(
            Existing(1),
            new PlaygroundOrderLineDto
            {
                Id = 20,
                TemporaryId = tempB,
                Product = "beta",
                Qty = 2,
            },
            new PlaygroundOrderLineDto
            {
                Id = 10,
                TemporaryId = tempA,
                Product = "alpha",
                Qty = 2,
            }
        );
        var local = Draft(Added(tempA, "alpha"), Existing(1), Added(tempB, "beta"));
        local.Lines[0].Product = "alpha edited after submit";

        PlaygroundLineIdentity.RetargetNewLineIds(submitted, persisted, local);

        local.Lines[0].Id.ShouldBe(10);
        local.Lines[0].TemporaryId.ShouldBeNull();
        local.Lines[0].Product.ShouldBe("alpha edited after submit");
        local.Lines[1].Id.ShouldBe(1);
        local.Lines[2].Id.ShouldBe(20);
        local.Lines[2].TemporaryId.ShouldBeNull();
    }

    [Test]
    public void CountMismatchSkipsUnmatchedRowsWithoutThrowing()
    {
        var tempA = Guid.NewGuid();
        var tempGone = Guid.NewGuid();
        var submitted = Draft(Added(tempA), Added(tempGone), Existing(1));
        // An extra pre-existing row plus a concurrently deleted row: counts differ.
        var persisted = Draft(
            Existing(7),
            Existing(1),
            new PlaygroundOrderLineDto
            {
                Id = 10,
                TemporaryId = tempA,
                Product = "cake",
                Qty = 2,
            }
        );
        var local = Draft(Added(tempA), Added(tempGone), Existing(1));

        PlaygroundLineIdentity.RetargetNewLineIds(submitted, persisted, local);

        local.Lines[0].Id.ShouldBe(10);
        local.Lines[0].TemporaryId.ShouldBeNull();
        // The deleted row keeps its temporary identity for a later retry.
        local.Lines[1].Id.ShouldBeNull();
        local.Lines[1].TemporaryId.ShouldBe(tempGone);
        local.Lines[2].Id.ShouldBe(1);
    }

    [Test]
    public void MissingDatabaseIdentityFailsWithoutPartialRewrite()
    {
        var tempA = Guid.NewGuid();
        var tempB = Guid.NewGuid();
        var submitted = Draft(Added(tempA), Added(tempB));
        var persisted = Draft(
            new PlaygroundOrderLineDto
            {
                Id = 10,
                TemporaryId = tempA,
                Product = "cake",
                Qty = 2,
            },
            new PlaygroundOrderLineDto
            {
                TemporaryId = tempB,
                Product = "cake",
                Qty = 2,
            }
        );
        var local = Draft(Added(tempA), Added(tempB));

        var failure = Should.Throw<InvalidOperationException>(() =>
            PlaygroundLineIdentity.RetargetNewLineIds(submitted, persisted, local)
        );
        failure.Message.ShouldContain("database identity");

        // Two-phase writeback: nothing was rewritten before the failure.
        local.Lines[0].Id.ShouldBeNull();
        local.Lines[0].TemporaryId.ShouldBe(tempA);
        local.Lines[1].Id.ShouldBeNull();
        local.Lines[1].TemporaryId.ShouldBe(tempB);
    }

    [Test]
    public void DuplicatePersistedTemporaryIdentityFails()
    {
        var temp = Guid.NewGuid();
        var submitted = Draft(Added(temp));
        var persisted = Draft(Added(temp), Added(temp));
        var local = Draft(Added(temp));

        Should.Throw<InvalidOperationException>(() =>
            PlaygroundLineIdentity.RetargetNewLineIds(submitted, persisted, local)
        );

        local.Lines[0].Id.ShouldBeNull();
        local.Lines[0].TemporaryId.ShouldBe(temp);
    }

    [Test]
    public void PostSubmitRowsAreLeftAlone()
    {
        var tempA = Guid.NewGuid();
        var tempLate = Guid.NewGuid();
        var submitted = Draft(Added(tempA), Existing(1));
        var persisted = Draft(
            Existing(1),
            new PlaygroundOrderLineDto
            {
                Id = 10,
                TemporaryId = tempA,
                Product = "cake",
                Qty = 2,
            }
        );
        // tempLate was added after submit, so the submission never saw it.
        var local = Draft(Added(tempA), Existing(1), Added(tempLate));

        PlaygroundLineIdentity.RetargetNewLineIds(submitted, persisted, local);

        local.Lines[0].Id.ShouldBe(10);
        local.Lines[0].TemporaryId.ShouldBeNull();
        local.Lines[2].Id.ShouldBeNull();
        local.Lines[2].TemporaryId.ShouldBe(tempLate);
    }
}
