namespace SparseTypes.EntityFrameworkCore;

/// <summary>
/// Decides how a changed-to-<c>Missing</c> after-state is applied to an entity.
/// </summary>
/// <remarks>The default <see cref="Throw" /> keeps the entity untouched and reports the path.</remarks>
public enum SparseEntityMissingPolicy
{
    /// <summary>
    /// Fails with <see cref="SparseEntityMappingFailure.MissingNotHandled"/>.
    /// </summary>
    Throw = 0,

    /// <summary>
    /// Leave the current entity value in place.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Write the default value of the entity member type.
    /// </summary>
    SetDefault = 2,

    /// <summary>
    /// Write null. The entity member must accept null.
    /// </summary>
    SetNull = 3,
}
