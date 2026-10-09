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
    Type Type { get; }

    /// <summary>Gets whether the property accepts null.</summary>
    bool IsNullable { get; }

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
    IDescriptorSet? Child { get; }

    /// <summary>Gets sequence operations for this property, when supported.</summary>
    IArrayDescriptor? Array { get; }

    /// <summary>Gets dictionary operations for this property, when supported.</summary>
    IDictDescriptor? Dictionary { get; }
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
    Type ItemType { get; }

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
}

/// <summary>Describes and edits a dictionary through its generated observable view.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface IDictDescriptor
{
    /// <summary>Gets the declared key type.</summary>
    Type KeyType { get; }

    /// <summary>Gets the declared value type.</summary>
    Type ValueType { get; }

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
    IDescriptorSet? GetValueDescriptors(object? key);

    /// <summary>Attempts to add an entry through the generated observable view.</summary>
    bool TryAdd(object? key, object? value);

    /// <summary>Attempts to replace an entry through the generated observable view.</summary>
    bool TrySetValue(object? key, object? value);

    /// <summary>Attempts to remove an entry through the generated observable view.</summary>
    bool TryRemove(object? key);
}
