namespace SparseTypes.EntityFrameworkCore.Mapping;

/// <summary>
/// Holds temporary-key writes until the complete DTO tree has been validated.
/// </summary>
internal sealed class TemporaryKeyAssignmentPlan
{
    private readonly List<Action> _assignments = [];

    /// <summary>Adds one validated temporary-key write.</summary>
    internal void Add(Action assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);
        _assignments.Add(assignment);
    }

    /// <summary>Executes the validated writes in traversal order.</summary>
    internal void Apply()
    {
        foreach (var assignment in _assignments)
        {
            assignment();
        }
    }
}
