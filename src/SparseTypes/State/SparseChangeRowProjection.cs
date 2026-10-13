using System;
using System.Collections.Generic;
using System.ComponentModel;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>One UI-neutral change row projected from a flattened change record.</summary>
/// <remarks>
/// Rows keep the record's typed path and presence-aware endpoints next to their
/// formatted text, so a table renders <see cref="BeforeText"/> and
/// <see cref="AfterText"/> while tooltips or editors can still inspect
/// <see cref="Before"/> and <see cref="After"/> to tell <c>Missing</c> apart
/// from <c>Present(null)</c>. Plain data: no components, CSS, or dialogs.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record SparseChangeRow
{
    /// <summary>Gets the typed path of the change.</summary>
    public SparsePath Path { get; init; }

    /// <summary>Gets the wire-compatible path text.</summary>
    public string PathText { get; init; } = string.Empty;

    /// <summary>Gets the model-independent change kind.</summary>
    public SparseChangeKind Kind { get; init; }

    /// <summary>Gets the operation label from the projection options.</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>Gets the presence-aware value before the change.</summary>
    public Optional<object?> Before { get; init; }

    /// <summary>Gets the presence-aware value after the change.</summary>
    public Optional<object?> After { get; init; }

    /// <summary>Gets the formatted before text.</summary>
    public string BeforeText { get; init; } = string.Empty;

    /// <summary>Gets the formatted after text.</summary>
    public string AfterText { get; init; } = string.Empty;
}

/// <summary>Options controlling change-row projection.</summary>
/// <remarks>
/// Display strings stay under application control: every hook is optional and
/// the defaults are invariant tokens (kind names, <c>ToString</c> values), never
/// localized prose. Formatting hooks must be total for the values the model can
/// produce; a throwing hook degrades that cell to
/// <see cref="GetErrorText"/> instead of aborting the table.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed record SparseChangeRowOptions
{
    /// <summary>Gets the operation label factory, or null for the kind name.</summary>
    public Func<SparseChangeKind, string>? FormatOperation { get; init; }

    /// <summary>Gets the value formatter, or null for a <c>ToString</c> fallback.</summary>
    /// <remarks>
    /// The hook receives the unwrapped value, including null for
    /// <c>Present(null)</c>; <c>Missing</c> endpoints never reach it and render
    /// as <see cref="MissingText"/> instead.
    /// </remarks>
    public Func<object?, string>? FormatValue { get; init; }

    /// <summary>Gets the text for <c>Missing</c> endpoints.</summary>
    public string MissingText { get; init; } = "—";

    /// <summary>Gets the fallback factory for throwing formatters, or null for the default.</summary>
    public Func<Exception, string>? FormatError { get; init; }

    /// <summary>Builds the operation label for one change kind.</summary>
    /// <param name="kind">The change kind to label.</param>
    /// <returns>The hook result, or the invariant kind name by default.</returns>
    public string GetOperation(SparseChangeKind kind) =>
        FormatOperation is null ? kind.ToString() : FormatOperation(kind);

    /// <summary>Builds the display text for one presence-aware endpoint.</summary>
    /// <param name="value">The endpoint to format.</param>
    /// <returns>
    /// <see cref="MissingText"/> for <c>Missing</c>; otherwise the formatted value,
    /// or <see cref="GetErrorText"/> when the value hook throws.
    /// </returns>
    public string GetValueText(Optional<object?> value)
    {
        if (!value.IsPresent)
        {
            return MissingText ?? "—";
        }

        try
        {
            return FormatValue is null
                ? value.Value?.ToString() ?? "null"
                : FormatValue(value.Value);
        }
        catch (Exception exception)
        {
            return GetErrorText(exception);
        }
    }

    /// <summary>Builds the fallback text for a throwing formatter.</summary>
    /// <param name="exception">The formatter exception.</param>
    /// <returns>The hook result, or an invariant type-and-message marker.</returns>
    /// <remarks>The hook itself must not throw; there is no second fallback.</remarks>
    public string GetErrorText(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (FormatError is not null)
        {
            return FormatError(exception);
        }

        return $"Error: {exception.GetType().Name}: {exception.Message}";
    }
}

/// <summary>Projects flattened change records into UI-neutral table rows.</summary>
/// <remarks>
/// The projection reuses <see cref="ISparseChangeSet.EnumerateChangeRecords"/>,
/// so every generated model works without naming its CLR change types; typed
/// values stay on the row until the value hook formats them. Enumeration order
/// is preserved, keeping stable keyed entries and <c>Order</c> rows in the
/// deterministic order the change set produced.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseChangeRowProjection
{
    /// <summary>Projects a change set into UI-neutral rows.</summary>
    /// <param name="changes">The change set to inspect.</param>
    /// <param name="options">The operation, value, missing, and error policy; null keeps defaults.</param>
    /// <returns>One row per flattened record, in enumeration order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changes"/> is null.</exception>
    /// <exception cref="ArgumentException">A record in the change set is null.</exception>
    public static IReadOnlyList<SparseChangeRow> Project(
        ISparseChangeSet changes,
        SparseChangeRowOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(changes);
        return Project(changes.EnumerateChangeRecords(), options);
    }

    /// <summary>Projects pre-filtered change records into UI-neutral rows.</summary>
    /// <param name="records">The records to project, in the desired row order.</param>
    /// <param name="options">The operation, value, missing, and error policy; null keeps defaults.</param>
    /// <returns>One row per record, in list order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="records"/> is null.</exception>
    /// <exception cref="ArgumentException">A record in <paramref name="records"/> is null.</exception>
    public static IReadOnlyList<SparseChangeRow> Project(
        IReadOnlyList<SparseChangeRecord> records,
        SparseChangeRowOptions? options = null
    )
    {
        ArgumentNullException.ThrowIfNull(records);
        options ??= new SparseChangeRowOptions();
        var rows = new List<SparseChangeRow>(records.Count);
        foreach (var record in records)
        {
            if (record is null)
            {
                throw new ArgumentException(
                    "Change records must not contain null entries.",
                    nameof(records)
                );
            }

            rows.Add(
                new SparseChangeRow
                {
                    Path = record.Path,
                    PathText = record.PathText,
                    Kind = record.Kind,
                    Operation = SafeOperation(options, record.Kind),
                    Before = record.Before,
                    After = record.After,
                    BeforeText = options.GetValueText(record.Before),
                    AfterText = options.GetValueText(record.After),
                }
            );
        }

        return rows;
    }

    // An operation hook failure degrades that label instead of dropping the row.
    private static string SafeOperation(SparseChangeRowOptions options, SparseChangeKind kind)
    {
        try
        {
            return options.GetOperation(kind);
        }
        catch (Exception exception)
        {
            return options.GetErrorText(exception);
        }
    }
}
