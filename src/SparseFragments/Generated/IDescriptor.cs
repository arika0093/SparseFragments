using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments.Generated;

/// <summary>Describes one model property exposed through a generated observable.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IDescriptor
{
    /// <summary>Gets the CLR property name.</summary>
    string Name { get; }

    /// <summary>Gets the dotted property path from the descriptor root.</summary>
    string Path { get; }

    /// <summary>Gets the property's CLR type.</summary>
    /// <remarks>
    /// Declared model type. The runtime value returned by <see cref="GetValue"/>
    /// may be a generated observable/view proxy for nested generated models;
    /// see <see cref="ViewType"/> for the runtime type.
    /// </remarks>
    Type Type { get; }

    /// <summary>Gets the runtime observable/view type returned by current reads.</summary>
    /// <remarks>
    /// Equals <see cref="Type"/> for scalars and unproxied shapes; for nested
    /// generated-model references it names the generated observable proxy type,
    /// and for proxied collections the element/value view type differs.
    /// </remarks>
    Type ViewType { get; }

    /// <summary>Gets whether the property accepts null.</summary>
    bool IsNullable { get; }

    /// <summary>Gets whether the property is a C# required member.</summary>
    /// <remarks>
    /// Independent of <see cref="IsNullable"/> (a required member may be
    /// nullable) and of validation attributes such as <c>RequiredAttribute</c>
    /// (see <see cref="Attributes"/>): it reports the <c>required</c> keyword
    /// for creation forms. Editing existing instances is unaffected.
    /// </remarks>
    bool IsRequired { get; }

    /// <summary>Gets whether the property itself can be replaced.</summary>
    bool IsEditable { get; }

    /// <summary>Gets whether the property itself cannot be replaced.</summary>
    bool IsReadOnly { get; }

    /// <summary>Gets the property's declared attributes.</summary>
    IReadOnlyList<Attribute> Attributes { get; }

    /// <summary>Gets the current value through the generated observable.</summary>
    object? GetValue();

    /// <summary>Attempts to replace the value through the generated observable.</summary>
    bool TrySetValue(object? value);

    /// <summary>Gets descriptors for a nested generated model, when available.</summary>
    /// <remarks>
    /// Instance-bound: the set is captured against the current nested instance.
    /// After the parent property is replaced or nulled, retained descriptors
    /// keep their creation-time <see cref="Path"/> but writes fail safely
    /// (false/null) instead of mutating the orphan. Re-resolve for the current
    /// instance.
    /// </remarks>
    IDescriptorSet? Child { get; }

    /// <summary>Gets sequence operations for this property, when supported.</summary>
    IArrayDescriptor? Array { get; }

    /// <summary>Gets dictionary operations for this property, when supported.</summary>
    IDictDescriptor? Dictionary { get; }

    /// <summary>Gets set operations for this property, when supported.</summary>
    /// <remarks>
    /// Set members expose membership over live model values; unlike sequences
    /// there are no positional semantics and no per-item nested descriptors.
    /// Edit an element's members by removing and re-adding the element.
    /// </remarks>
    ISetDescriptor? Set { get; }

    /// <summary>Gets static declared shape information for this property.</summary>
    /// <remarks>
    /// Unlike the live <see cref="Child"/>, <see cref="Array"/>,
    /// <see cref="Dictionary"/> and <see cref="Set"/> accessors (which return
    /// null when the current value is null), the shape is always available and
    /// describes what <em>could</em> be constructed for a null member.
    /// </remarks>
    SparseDescriptorShape Shape { get; }
}

/// <summary>Provides the descriptors for one generated model instance.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IDescriptorSet
{
    /// <summary>Gets the model's property descriptors in declaration order.</summary>
    IReadOnlyList<IDescriptor> Members { get; }

    /// <summary>Looks up a property descriptor by its CLR name.</summary>
    bool TryGet(string name, out IDescriptor descriptor);
}

/// <summary>Describes and edits a sequence through its generated observable view.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IArrayDescriptor
{
    /// <summary>Gets the declared item type.</summary>
    /// <remarks>Declared model element type; see <see cref="ItemViewType"/> for the runtime view type.</remarks>
    Type ItemType { get; }

    /// <summary>Gets the runtime item view type returned by current reads.</summary>
    Type ItemViewType { get; }

    /// <summary>Gets whether sequence items accept null.</summary>
    bool IsItemNullable { get; }

    /// <summary>Gets the current number of items.</summary>
    int Count { get; }

    /// <summary>Gets whether items can be appended.</summary>
    bool CanAdd { get; }

    /// <summary>Gets whether existing items can be replaced.</summary>
    bool CanSetItem { get; }

    /// <summary>Gets whether items can be inserted.</summary>
    bool CanInsert { get; }

    /// <summary>Gets whether items can be removed.</summary>
    bool CanRemove { get; }

    /// <summary>Gets whether items can be moved.</summary>
    bool CanMove { get; }

    /// <summary>Gets an item through the generated observable view.</summary>
    object? GetItem(int index);

    /// <summary>Gets nested descriptors for an item that is a generated model.</summary>
    IDescriptorSet? GetItemDescriptors(int index);

    /// <summary>Attempts to replace an item through the generated observable view.</summary>
    bool TrySetItem(int index, object? value);

    /// <summary>Attempts to append an item through the generated observable view.</summary>
    bool TryAdd(object? value);

    /// <summary>Attempts to insert an item through the generated observable view.</summary>
    bool TryInsert(int index, object? value);

    /// <summary>Attempts to remove an item through the generated observable view.</summary>
    bool TryRemoveAt(int index);

    /// <summary>Attempts to move an item through the generated observable view.</summary>
    bool TryMove(int oldIndex, int newIndex);

    /// <summary>Gets whether the sequence carries stable keyed identity.</summary>
    /// <remarks>
    /// Sourced from the generator's key analysis; generic editors must not
    /// reverse-engineer <c>SparseKey</c> conventions themselves. Positional APIs
    /// stay positional: use <see cref="GetItemKey"/> and <see cref="IndexOfKey"/>
    /// to track identity through reorder.
    /// </remarks>
    bool IsKeyed { get; }

    /// <summary>Gets the key type, or null for unkeyed sequences.</summary>
    Type? KeyType { get; }

    /// <summary>Gets the key property names in key order.</summary>
    /// <remarks>Empty for unkeyed sequences and interface-computed keys.</remarks>
    IReadOnlyList<string> KeyPropertyNames { get; }

    /// <summary>Gets the assigned key of the item at an index.</summary>
    /// <remarks>Null when unkeyed or the item has no key.</remarks>
    object? GetItemKey(int index);

    /// <summary>Finds the first index with an equal assigned key.</summary>
    /// <remarks>
    /// Pure key equality: unassigned sentinels match like any other value, so
    /// callers exempting them must check <see cref="IsUnassignedKey"/> first.
    /// Returns -1 when unkeyed, unconvertible, or absent.
    /// </remarks>
    int IndexOfKey(object? key);

    /// <summary>Determines whether a key is the configured unassigned marker.</summary>
    /// <remarks>Always false when the sequence configures no marker.</remarks>
    bool IsUnassignedKey(object? key);
}

/// <summary>Describes and edits a dictionary through its generated observable view.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IDictDescriptor
{
    /// <summary>Gets the declared key type.</summary>
    Type KeyType { get; }

    /// <summary>Gets the declared value type.</summary>
    /// <remarks>Declared model value type; see <see cref="ValueViewType"/> for the runtime view type.</remarks>
    Type ValueType { get; }

    /// <summary>Gets the runtime value view type returned by current reads.</summary>
    Type ValueViewType { get; }

    /// <summary>Gets whether dictionary values accept null.</summary>
    bool IsValueNullable { get; }

    /// <summary>Gets the current number of entries.</summary>
    int Count { get; }

    /// <summary>Gets whether entries can be added.</summary>
    bool CanAdd { get; }

    /// <summary>Gets whether entries can be removed.</summary>
    bool CanRemove { get; }

    /// <summary>Gets whether existing values can be replaced.</summary>
    bool CanSet { get; }

    /// <summary>Gets the keys currently present in the dictionary.</summary>
    IEnumerable<object?> Keys { get; }

    /// <summary>Attempts to get a value by key through the generated observable view.</summary>
    bool TryGetValue(object? key, out object? value);

    /// <summary>Gets nested descriptors for a value that is a generated model.</summary>
    /// <remarks>
    /// The nested set's <see cref="IDescriptor.Path"/> uses the canonical
    /// bracket-key form: the InvariantCulture key text, double-quoted with
    /// backslash and quote escaping (for example <c>Scores["a]b"].Value</c>).
    /// All key types render quoted without a type prefix, matching change-set
    /// paths and Blazor field paths.
    /// </remarks>
    IDescriptorSet? GetValueDescriptors(object? key);

    /// <summary>Attempts to add an entry through the generated observable view.</summary>
    bool TryAdd(object? key, object? value);

    /// <summary>Attempts to replace an entry through the generated observable view.</summary>
    bool TrySetValue(object? key, object? value);

    /// <summary>Attempts to remove an entry through the generated observable view.</summary>
    bool TryRemove(object? key);
}

/// <summary>Describes and edits a set through its live model values.</summary>
/// <remarks>
/// Membership uses the backing set's comparer. Mutation is supported only where
/// the backing set is mutable; each effective add or remove raises the parent
/// property notification. No-op adds/removes report false and raise nothing.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISetDescriptor
{
    /// <summary>Gets the declared item type.</summary>
    Type ItemType { get; }

    /// <summary>Gets whether set items accept null.</summary>
    bool IsItemNullable { get; }

    /// <summary>Gets the current number of items.</summary>
    int Count { get; }

    /// <summary>Gets whether items can be added.</summary>
    bool CanAdd { get; }

    /// <summary>Gets whether items can be removed.</summary>
    bool CanRemove { get; }

    /// <summary>Gets the live item values.</summary>
    IEnumerable<object?> Items { get; }

    /// <summary>Determines whether an item is a member of the set.</summary>
    bool Contains(object? item);

    /// <summary>Attempts to add an item to the set.</summary>
    bool TryAdd(object? value);

    /// <summary>Attempts to remove an item from the set.</summary>
    bool TryRemove(object? value);
}

/// <summary>Static declared shape information for one model property.</summary>
/// <remarks>
/// Shape answers "is this shape supported" without a live instance; the
/// <c>Child</c>/<c>Array</c>/<c>Dictionary</c>/<c>Set</c> accessors answer
/// "is there a live instance right now". A nullable member with no value has
/// shape but no live accessors until it is assigned.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDescriptorShape
{
    /// <summary>Gets whether the property is a nested generated-model shape.</summary>
    public bool HasChild { get; init; }

    /// <summary>Gets the nested model type, or null when not a child shape.</summary>
    public Type? ChildType { get; init; }

    /// <summary>Gets whether the property is a sequence shape.</summary>
    public bool HasArray { get; init; }

    /// <summary>Gets the sequence item type, or null when not a sequence shape.</summary>
    public Type? ArrayItemType { get; init; }

    /// <summary>Gets whether sequence items accept null.</summary>
    public bool ArrayItemNullable { get; init; }

    /// <summary>Gets whether the property is a dictionary shape.</summary>
    public bool HasDictionary { get; init; }

    /// <summary>Gets the dictionary key type, or null when not a dictionary shape.</summary>
    public Type? DictionaryKeyType { get; init; }

    /// <summary>Gets the dictionary value type, or null when not a dictionary shape.</summary>
    public Type? DictionaryValueType { get; init; }

    /// <summary>Gets whether dictionary values accept null.</summary>
    public bool DictionaryValueNullable { get; init; }

    /// <summary>Gets whether the property is a set shape.</summary>
    public bool HasSet { get; init; }

    /// <summary>Gets the set item type, or null when not a set shape.</summary>
    public Type? SetItemType { get; init; }

    /// <summary>Gets whether set items accept null.</summary>
    public bool SetItemNullable { get; init; }
}
