using SparseTypes.Generated;
using SparseTypes.NativeAotFixtures;

namespace SparseTypes.NativeAotSmoke;

/// <summary>Session capability contracts under NativeAOT.</summary>
/// <remarks>Proves generated sessions expose history, field and collection state, batch edit, and descriptors through typed contracts without runtime member lookup.</remarks>
public sealed class AotSessionCapabilityTests
{
    [Test]
    public async Task GeneratedSessionsImplementCapabilityContracts()
    {
        var session = new PayloadRoot { Label = "a" }.CreateEditSession();

        await Assert.That(session is ISparseEditSessionWithHistory).IsTrue();
        await Assert.That(session is ISparseEditSessionWithFieldState<PayloadRoot>).IsTrue();
        await Assert
            .That(session is ISparseEditSessionWithCollectionState)
            .IsTrue();
        await Assert.That(session is ISparseEditSessionWithBatchEdit).IsTrue();
        await Assert.That(session is ISparseEditSessionWithDescriptors).IsTrue();
    }

    [Test]
    public async Task CapabilityMembersWorkWithoutMemberLookup()
    {
        var session = new PayloadRoot { Label = "a" }.CreateEditSession();
        var historyOwner = (ISparseEditSessionWithHistory)session;
        var history = historyOwner.EnableHistory(null);
        await Assert.That(history.CanUndo).IsFalse();

        ((ISparseEditSessionWithBatchEdit)session).BatchEdit(
            () => session.Observable.Label = "b"
        );
        await Assert.That(history.CanUndo).IsTrue();

        var state = ((ISparseEditSessionWithFieldState<PayloadRoot>)session).FieldState(
            PayloadRoot.SparsePath.Label
        );
        await Assert.That(state.IsChanged).IsTrue();

        var descriptors = ((ISparseEditSessionWithDescriptors)session).Descriptors;
        await Assert.That(descriptors.TryGet(nameof(PayloadRoot.Label), out _)).IsTrue();

        historyOwner.DisableHistory();
        await Assert.That(historyOwner.History is null).IsTrue();
    }

    [Test]
    public async Task RuntimeKeyTypeKeepsKeyIdentityWithoutGenericLookup()
    {
        var segment = SparsePathSegment.KeyOf("id-1", typeof(string));

        await Assert.That(segment.Kind).IsEqualTo(SparsePathSegmentKind.Key);
        await Assert.That(segment.KeyType).IsEqualTo(typeof(string));
        await Assert.That(segment).IsEqualTo(SparsePathSegment.KeyOf("id-1"));
    }
}
