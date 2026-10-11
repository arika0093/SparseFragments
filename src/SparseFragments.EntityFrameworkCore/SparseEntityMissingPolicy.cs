namespace SparseFragments.EntityFrameworkCore;

/// <summary>
/// Decides how a changed-to-<c>Missing</c> after-state is applied to an entity.
/// </summary>
/// <remarks>
/// <c>Missing</c> is never silently interpreted as null: the default
/// <see cref="Throw" /> keeps the entity untouched and reports the path.
/// </remarks>
public enum SparseEntityMissingPolicy
{
    /// <summary>
    /// Fail with <see cref="SparseEntityMappingFailure.MissingNotHandled" /> and leave the entity untouched.
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
