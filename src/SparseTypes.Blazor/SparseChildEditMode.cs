namespace SparseTypes.Blazor;

/// <summary>Specifies whether child editors use an independent draft or edit through to their parent.</summary>
public enum SparseChildEditMode
{
    /// <summary>Keep edits in an independent child session until they are accepted.</summary>
    Draft,

    /// <summary>Accept each child edit into the parent and report changes from the parent state.</summary>
    PassThrough,
}
