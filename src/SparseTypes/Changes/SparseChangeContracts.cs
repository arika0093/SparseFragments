using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using SparseTypes.Generated;

namespace SparseTypes;

/// <summary>Read-only scalar (atomic) member transition between two states.</summary>
/// <remarks>
/// Implemented by generated per-member transition types (scalar members and set
/// members). <c>Before</c> and <c>After</c> preserve presence exactly:
/// <c>Missing</c> means the member was absent, <c>Present(null)</c> means it was
/// present with a null value, and <c>Present(value)</c> carries the value.
/// </remarks>
/// <typeparam name="T">The member value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseValueChange<T>
{
    /// <summary>Gets whether the member changed between the endpoints.</summary>
    bool IsChanged { get; }

    /// <summary>Gets the presence-aware value before the transition.</summary>
    Optional<T> Before { get; }

    /// <summary>Gets the presence-aware value after the transition.</summary>
    Optional<T> After { get; }
}

/// <summary>Read-only baseline-aware change set shared across generated models.</summary>
/// <remarks>
/// Implemented by every generated <c>ChangeSet</c> type. External consumers
/// (for example persistence mappers) traverse scalar, nested, keyed-sequence,
/// and dictionary members through the <c>TryGet*</c> entry points using member
/// CLR names and value/key CLR types, without referencing generated nested CLR
/// type names. Flattened inspection reuses the same projection as the typed
/// <c>EnumerateChanges()</c> surface: <see cref="EnumerateChangeRecords"/>
/// carries the same paths (including typed key segments, temporary-key
/// segments, and order entries) with model-independent values.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChangeSet
{
    /// <summary>Gets whether this change set contains no semantic changes.</summary>
    bool IsEmpty { get; }

    /// <summary>Gets whether this change set contains any semantic changes.</summary>
    bool IsChanged { get; }

    /// <summary>Enumerates flattened change records in deterministic order.</summary>
    /// <returns>The flattened transitions reusing the typed enumeration projection.</returns>
    IReadOnlyList<SparseChangeRecord> EnumerateChangeRecords();

    /// <summary>Gets the nested change set for a nested-model member.</summary>
    /// <param name="memberName">The member CLR name.</param>
    /// <param name="child">The nested change set when the member is a nested model.</param>
    /// <returns>True when the member is a nested model; otherwise false.</returns>
    bool TryGetNestedChange(string memberName, [NotNullWhen(true)] out ISparseChangeSet? child);

    /// <summary>Gets the scalar transition for an atomic member.</summary>
    /// <param name="memberName">The member CLR name.</param>
    /// <param name="change">The transition when the member is atomic with value type <typeparamref name="T"/>.</param>
    /// <returns>True on an exact member and value-type match; otherwise false.</returns>
    /// <typeparam name="T">The expected member value type.</typeparam>
    bool TryGetValueChange<T>(
        string memberName,
        [NotNullWhen(true)] out ISparseValueChange<T>? change
    );

    /// <summary>Gets the keyed-sequence transition for a keyed collection member.</summary>
    /// <param name="memberName">The member CLR name.</param>
    /// <param name="change">The transition on an exact key, element, and element-change match.</param>
    /// <returns>True on an exact member and type match; otherwise false.</returns>
    /// <typeparam name="TKey">The expected stable key type.</typeparam>
    /// <typeparam name="TElement">The expected element type.</typeparam>
    /// <typeparam name="TElementChange">The expected element change-set type.</typeparam>
    bool TryGetKeyedChange<TKey, TElement, TElementChange>(
        string memberName,
        [NotNullWhen(true)] out ISparseKeyedChange<TKey, TElement, TElementChange>? change
    );

    /// <summary>Gets the dictionary transition for a dictionary member.</summary>
    /// <param name="memberName">The member CLR name.</param>
    /// <param name="change">The transition on an exact key, value, and edited-projection match.</param>
    /// <returns>True on an exact member and type match; otherwise false.</returns>
    /// <typeparam name="TKey">The expected dictionary key type.</typeparam>
    /// <typeparam name="TValue">The expected dictionary value type.</typeparam>
    /// <typeparam name="TEdited">The expected edited-projection type (the value type, or the value change-set type for structural values).</typeparam>
    bool TryGetDictionaryChange<TKey, TValue, TEdited>(
        string memberName,
        [NotNullWhen(true)] out ISparseDictionaryChange<TKey, TValue, TEdited>? change
    );
}

/// <summary>Read-only baseline-aware change set associated with a model type.</summary>
/// <typeparam name="TModel">The model type whose changes this change set describes.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
[SuppressMessage(
    "Major Code Smell",
    "S2326:Unused type parameters should be removed",
    Justification = "TModel is a compile-time model identity for typed change sets."
)]
public interface ISparseChangeSet<TModel> : ISparseChangeSet { }

/// <summary>Read-only keyed-sequence transition with typed permanent keys.</summary>
/// <remarks>
/// Implemented by generated per-member keyed transition types. Additions,
/// removals, and edits follow the semantic before/after states (never the
/// patch operation shape); order observation follows surviving-key relative
/// rank. Members whose element type opts into temporary identity implement
/// <see cref="ISparseTemporaryKeyedChange{TKey, TElement, TElementChange}"/>
/// instead, which adds collection-scoped temporary lookup.
/// <para>
/// Item enumeration is a named method (not <c>IEnumerable&lt;T&gt;</c>) so the
/// existing typed <c>IEnumerable&lt;Item&gt;</c> keeps unambiguous LINQ
/// inference.
/// </para>
/// </remarks>
/// <typeparam name="TKey">The stable key type.</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <typeparam name="TElementChange">The element change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseKeyedChange<TKey, TElement, TElementChange>
{
    /// <summary>Gets whether this transition contains no changes.</summary>
    bool IsEmpty { get; }

    /// <summary>Gets whether this transition contains any changes.</summary>
    bool IsChanged { get; }

    /// <summary>Gets whether the key order changed.</summary>
    bool OrderChanged { get; }

    /// <summary>Gets the key order before the transition.</summary>
    IReadOnlyList<TKey> BeforeOrder { get; }

    /// <summary>Gets the key order after the transition.</summary>
    IReadOnlyList<TKey> AfterOrder { get; }

    /// <summary>Gets the elements added by the transition.</summary>
    IReadOnlyList<TElement> Added { get; }

    /// <summary>Gets the elements removed by the transition.</summary>
    IReadOnlyList<TElement> Removed { get; }

    /// <summary>Gets the entries edited by the transition.</summary>
    IReadOnlyDictionary<TKey, TElementChange> Edited { get; }

    /// <summary>Looks up the typed change for a stable key; never returns null.</summary>
    /// <param name="key">The stable key to look up.</param>
    /// <returns>The typed change for the key.</returns>
    ISparseKeyedItemChange<TKey, TElement, TElementChange> GetChange(TKey key);

    /// <summary>Enumerates the changed items without naming the generated item type.</summary>
    /// <returns>The same instances produced by typed enumeration and lookup.</returns>
    IEnumerable<ISparseKeyedItemChange<TKey, TElement, TElementChange>> EnumerateItems();
}

/// <summary>A single typed keyed item change.</summary>
/// <remarks>
/// Implemented by generated per-member keyed item types. <c>BeforeIndex</c> and
/// <c>AfterIndex</c> are absolute collection indexes; <c>IsReordered</c>
/// observes surviving-key relative rank, so membership-only shifts leave it
/// false. The unassigned sentinel is never a stable lookup: unknown keys
/// produce the shared empty item.
/// </remarks>
/// <typeparam name="TKey">The stable key type.</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <typeparam name="TElementChange">The element change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseKeyedItemChange<TKey, TElement, TElementChange>
{
    /// <summary>Gets the entry key.</summary>
    TKey Key { get; }

    /// <summary>Gets the temporary identity for unassigned entries.</summary>
    /// <remarks>
    /// Non-null only when the entry key is unassigned and the element type opts
    /// into temporary identity; assigned keys always take precedence. The value
    /// is scoped to its keyed collection, never a root-global identity.
    /// </remarks>
    Guid? TemporaryKey { get; }

    /// <summary>Gets the value before the transition.</summary>
    Optional<TElement> Before { get; }

    /// <summary>Gets the value after the transition.</summary>
    Optional<TElement> After { get; }

    /// <summary>Gets the index before the transition.</summary>
    int BeforeIndex { get; }

    /// <summary>Gets the index after the transition.</summary>
    int AfterIndex { get; }

    /// <summary>Gets whether the entry was added.</summary>
    bool IsAdded { get; }

    /// <summary>Gets whether the entry was removed.</summary>
    bool IsRemoved { get; }

    /// <summary>Gets whether the entry was edited.</summary>
    bool IsEdited { get; }

    /// <summary>Gets whether the entry was reordered.</summary>
    bool IsReordered { get; }

    /// <summary>Whether this item carries no semantic change for the requested key.</summary>
    bool IsEmpty { get; }

    /// <summary>Gets whether this item contains any changes.</summary>
    bool IsChanged { get; }

    /// <summary>Gets the nested transition for the entry.</summary>
    TElementChange Edit { get; }
}

/// <summary>Read-only keyed-sequence transition with collection-scoped temporary identity.</summary>
/// <remarks>
/// Implemented by generated keyed transitions whose element type declares a
/// temporary-identity property. Temporary lookup is scoped to its keyed
/// collection: the same identifier reused under a different collection path
/// resolves independently there. A valid assigned permanent key always wins,
/// so assigned entries are never returned from temporary lookup even when
/// they retain a temporary value.
/// </remarks>
/// <typeparam name="TKey">The stable key type.</typeparam>
/// <typeparam name="TElement">The element type.</typeparam>
/// <typeparam name="TElementChange">The element change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseTemporaryKeyedChange<TKey, TElement, TElementChange>
    : ISparseKeyedChange<TKey, TElement, TElementChange>
{
    /// <summary>Looks up the typed change for a temporary identity; never returns null.</summary>
    /// <param name="temporaryKey">The temporary identity to look up.</param>
    /// <returns>The typed change for the identity.</returns>
    ISparseKeyedItemChange<TKey, TElement, TElementChange> GetTemporaryChange(Guid temporaryKey);
}

/// <summary>Read-only dictionary transition with typed keys and values.</summary>
/// <remarks>
/// Implemented by generated per-member dictionary transition types.
/// <typeparamref name="TEdited"/> is the value type for scalar values and the
/// value change-set type for structural values. Entry enumeration is a named
/// method (not <c>IEnumerable&lt;T&gt;</c>) so the existing typed
/// <c>IEnumerable&lt;Item&gt;</c> keeps unambiguous LINQ inference.
/// </remarks>
/// <typeparam name="TKey">The dictionary key type.</typeparam>
/// <typeparam name="TValue">The dictionary value type.</typeparam>
/// <typeparam name="TEdited">The edited-projection type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseDictionaryChange<TKey, TValue, TEdited>
{
    /// <summary>Gets whether this transition contains no changes.</summary>
    bool IsEmpty { get; }

    /// <summary>Gets whether this transition contains any changes.</summary>
    bool IsChanged { get; }

    /// <summary>Gets the entries added by the transition.</summary>
    IReadOnlyDictionary<TKey, TValue> Added { get; }

    /// <summary>Gets the entries removed by the transition.</summary>
    IReadOnlyDictionary<TKey, TValue> Removed { get; }

    /// <summary>Gets the entries edited by the transition.</summary>
    IReadOnlyDictionary<TKey, TEdited> Edited { get; }

    /// <summary>Looks up the typed change for a dictionary key; never returns null.</summary>
    /// <param name="key">The dictionary key to look up.</param>
    /// <returns>The typed change for the key.</returns>
    ISparseDictionaryItemChange<TKey, TValue> GetChange(TKey key);

    /// <summary>Enumerates the changed entries without naming the generated item type.</summary>
    /// <returns>The same instances produced by typed enumeration and lookup.</returns>
    IEnumerable<ISparseDictionaryItemChange<TKey, TValue>> EnumerateEntries();
}

/// <summary>A single typed dictionary entry change.</summary>
/// <typeparam name="TKey">The dictionary key type.</typeparam>
/// <typeparam name="TValue">The dictionary value type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseDictionaryItemChange<TKey, TValue>
{
    /// <summary>Gets the entry key.</summary>
    TKey Key { get; }

    /// <summary>Gets the value before the transition.</summary>
    Optional<TValue> Before { get; }

    /// <summary>Gets the value after the transition.</summary>
    Optional<TValue> After { get; }

    /// <summary>Gets whether the entry was added.</summary>
    bool IsAdded { get; }

    /// <summary>Gets whether the entry was removed.</summary>
    bool IsRemoved { get; }

    /// <summary>Gets whether the entry was edited.</summary>
    bool IsEdited { get; }

    /// <summary>Whether this entry carries no semantic change for the requested key.</summary>
    bool IsEmpty { get; }

    /// <summary>Gets whether this entry contains any changes.</summary>
    bool IsChanged { get; }
}

/// <summary>A single typed dictionary entry change with a nested value transition.</summary>
/// <remarks>Implemented by generated dictionary items whose value is structural.</remarks>
/// <typeparam name="TKey">The dictionary key type.</typeparam>
/// <typeparam name="TValue">The dictionary value type.</typeparam>
/// <typeparam name="TEdit">The value change-set type.</typeparam>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseStructuralDictionaryItemChange<TKey, TValue, TEdit>
    : ISparseDictionaryItemChange<TKey, TValue>
{
    /// <summary>Gets the nested transition for the edited entry.</summary>
    TEdit Edit { get; }
}
