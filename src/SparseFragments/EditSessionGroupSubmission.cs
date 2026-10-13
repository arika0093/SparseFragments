using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SparseFragments;

/// <summary>Metadata and typed change sets captured by one group preparation.</summary>
public sealed class EditSessionGroupSubmission
{
    private readonly object _owner;
    private readonly IReadOnlyDictionary<
        IEditSessionGroupSection,
        EditSessionSubmittedSection
    > _bySection;

    internal EditSessionGroupSubmission(
        object owner,
        IReadOnlyList<EditSessionSubmittedSection> sections
    )
    {
        _owner = owner;
        Sections = sections;
        var bySection = new Dictionary<IEditSessionGroupSection, EditSessionSubmittedSection>();
        var paths = new List<EditSessionGroupChangedPath>();
        foreach (var section in sections)
        {
            bySection.Add(section.Section, section);
            foreach (var path in section.ChangedPaths)
            {
                paths.Add(
                    new EditSessionGroupChangedPath(section.SectionId, path, path.ToString())
                );
            }
        }

        _bySection = new ReadOnlyDictionary<IEditSessionGroupSection, EditSessionSubmittedSection>(
            bySection
        );
        ChangedPaths = Array.AsReadOnly(paths.ToArray());
    }

    /// <summary>Gets the changed sections included in this immutable submission.</summary>
    public IReadOnlyList<EditSessionSubmittedSection> Sections { get; }

    /// <summary>Gets changed paths from all submitted sections.</summary>
    public IReadOnlyList<EditSessionGroupChangedPath> ChangedPaths { get; }

    /// <summary>Gets the captured change set for a registered section.</summary>
    /// <param name="handle">The typed handle returned by the same group.</param>
    /// <returns>The section's immutable captured change set and path snapshot.</returns>
    /// <exception cref="ArgumentException">The handle belongs to another group.</exception>
    /// <exception cref="KeyNotFoundException">The section had no changes in this submission.</exception>
    public EditSessionSubmissionEntry<TChangeSet> Get<TModel, TChangeSet>(
        EditSessionSectionHandle<TModel, TChangeSet> handle
    )
        where TModel : class
        where TChangeSet : class
    {
        if (handle is null)
            throw new ArgumentNullException(nameof(handle));
        if (!ReferenceEquals(handle.Owner, _owner))
            throw new ArgumentException(
                "The handle belongs to another edit-session group.",
                nameof(handle)
            );
        if (!_bySection.TryGetValue(handle.Section, out var section))
            throw new KeyNotFoundException(
                "The section has no changes in this submission or its registration was replaced."
            );
        if (section is not EditSessionSubmissionEntry<TChangeSet> typed)
            throw new ArgumentException(
                "The handle does not match the submitted section type.",
                nameof(handle)
            );
        return typed;
    }

    internal bool TryGet(
        IEditSessionGroupSection section,
        out EditSessionSubmittedSection? entry
    ) => _bySection.TryGetValue(section, out entry);

    internal bool BelongsTo(object owner) => ReferenceEquals(_owner, owner);
}

/// <summary>Immutable metadata for a submitted section.</summary>
public class EditSessionSubmittedSection
{
    internal EditSessionSubmittedSection(
        IEditSessionGroupSection section,
        string sectionId,
        long baselineRevision,
        IReadOnlyList<SparsePath> changedPaths
    )
    {
        Section = section;
        SectionId = sectionId;
        BaselineRevision = baselineRevision;
        ChangedPaths = changedPaths;
    }

    /// <summary>Gets the stable section identifier.</summary>
    public string SectionId { get; }

    /// <summary>Gets the group's baseline revision at preparation time.</summary>
    public long BaselineRevision { get; }

    /// <summary>Gets a read-only snapshot of paths changed by the submitted transition.</summary>
    public IReadOnlyList<SparsePath> ChangedPaths { get; }

    internal IEditSessionGroupSection Section { get; }
}

/// <summary>A submitted change set retained with its preparation revision and paths.</summary>
/// <typeparam name="TChangeSet">The generated immutable change-set type.</typeparam>
public sealed class EditSessionSubmissionEntry<TChangeSet> : EditSessionSubmittedSection
    where TChangeSet : class
{
    internal EditSessionSubmissionEntry(
        IEditSessionGroupSection section,
        string sectionId,
        long baselineRevision,
        TChangeSet changes,
        IReadOnlyList<SparsePath> changedPaths
    )
        : base(section, sectionId, baselineRevision, changedPaths)
    {
        Changes = changes;
    }

    /// <summary>Gets the immutable generated change set captured at preparation time.</summary>
    public TChangeSet Changes { get; }
}

/// <summary>A changed path identified by its section and generated sparse path.</summary>
public sealed class EditSessionGroupChangedPath
{
    internal EditSessionGroupChangedPath(string sectionId, SparsePath path, string pathText)
    {
        SectionId = sectionId;
        Path = path;
        PathText = pathText;
    }

    /// <summary>Gets the stable section identifier.</summary>
    public string SectionId { get; }

    /// <summary>Gets the typed sparse path.</summary>
    public SparsePath Path { get; }

    /// <summary>Gets the display form of <see cref="Path"/>.</summary>
    public string PathText { get; }
}

/// <summary>A validation problem that prevented an edit-session group submission.</summary>
public sealed class EditSessionGroupValidationIssue
{
    internal EditSessionGroupValidationIssue(string sectionId, string message)
    {
        SectionId = sectionId;
        Message = message;
    }

    /// <summary>Gets the section that failed validation.</summary>
    public string SectionId { get; }

    /// <summary>Gets the validation failure.</summary>
    public string Message { get; }
}

/// <summary>Reports one or more sections that prevented submission preparation.</summary>
public sealed class EditSessionGroupValidationException : InvalidOperationException
{
    internal EditSessionGroupValidationException(
        IReadOnlyList<EditSessionGroupValidationIssue> issues
    )
        : base("The edit-session group is not ready to prepare a submission.")
    {
        Issues = issues;
    }

    /// <summary>Gets the sections and reasons that failed validation.</summary>
    public IReadOnlyList<EditSessionGroupValidationIssue> Issues { get; }
}

/// <summary>Reports an unrelated, replaced, or stale submission acknowledgement.</summary>
public sealed class EditSessionStaleSubmissionException : InvalidOperationException
{
    internal EditSessionStaleSubmissionException(string message)
        : base(message) { }

    internal EditSessionStaleSubmissionException(string message, Exception innerException)
        : base(message, innerException) { }
}
