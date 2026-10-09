using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments.Generated;

/// <summary>Provides lookup over a fixed set of generated property descriptors.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDescriptorSet : IDescriptorSet
{
    private readonly IReadOnlyList<IDescriptor> _members;
    private readonly Dictionary<string, IDescriptor> _byName;

    /// <summary>Creates a descriptor set from generated property descriptors.</summary>
    public SparseDescriptorSet(IReadOnlyList<IDescriptor> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        var copy = new IDescriptor[members.Count];
        _byName = new Dictionary<string, IDescriptor>(members.Count, StringComparer.Ordinal);
        for (var index = 0; index < members.Count; index++)
        {
            var descriptor =
                members[index]
                ?? throw new ArgumentException(
                    "A descriptor set cannot contain null entries.",
                    nameof(members)
                );
            copy[index] = descriptor;
            _byName.Add(descriptor.Name, descriptor);
        }

        _members = Array.AsReadOnly(copy);
    }

    /// <summary>Gets the model's property descriptors in generated member order.</summary>
    public IReadOnlyList<IDescriptor> Members => _members;

    /// <inheritdoc />
    public bool TryGet(string name, out IDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out descriptor!);
    }
}

/// <summary>Implements one model property descriptor using generated observable accessors.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDescriptor : IDescriptor
{
    private readonly Func<object?> _getValue;
    private readonly Func<object?, bool>? _setValue;
    private readonly Func<IDescriptorSet?>? _getChild;
    private readonly Func<IArrayDescriptor?>? _getArray;
    private readonly Func<IDictDescriptor?>? _getDictionary;

    /// <summary>Creates a descriptor backed by generated property accessors.</summary>
    public SparseDescriptor(
        string name,
        string path,
        Type type,
        bool isNullable,
        bool isEditable,
        Attribute[] attributes,
        Func<object?> getValue,
        Func<object?, bool>? setValue = null,
        Func<IDescriptorSet?>? getChild = null,
        Func<IArrayDescriptor?>? getArray = null,
        Func<IDictDescriptor?>? getDictionary = null,
        Type? viewType = null
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(attributes);
        _getValue = getValue ?? throw new ArgumentNullException(nameof(getValue));
        _setValue = setValue;
        _getChild = getChild;
        _getArray = getArray;
        _getDictionary = getDictionary;
        Name = name;
        Path = path;
        Type = type;
        ViewType = viewType ?? type;
        IsNullable = isNullable;
        IsEditable = isEditable && setValue is not null;
        IsReadOnly = !IsEditable;
        Attributes = global::System.Array.AsReadOnly((Attribute[])attributes.Clone());
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public Type Type { get; }

    /// <inheritdoc />
    public Type ViewType { get; }

    /// <inheritdoc />
    public bool IsNullable { get; }

    /// <inheritdoc />
    public bool IsEditable { get; }

    /// <inheritdoc />
    public bool IsReadOnly { get; }

    /// <inheritdoc />
    public IReadOnlyList<Attribute> Attributes { get; }

    /// <inheritdoc />
    public object? GetValue() => _getValue();

    /// <inheritdoc />
    public bool TrySetValue(object? value) =>
        _setValue is not null && (value is not null || IsNullable) && _setValue(value);

    /// <inheritdoc />
    public IDescriptorSet? Child => _getChild?.Invoke();

    /// <inheritdoc />
    public IArrayDescriptor? Array => _getArray?.Invoke();

    /// <inheritdoc />
    public IDictDescriptor? Dictionary => _getDictionary?.Invoke();
}

/// <summary>Defines generated accessors for a sequence descriptor.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseArrayDescriptorAccess
{
    /// <summary>Gets or sets the current sequence count.</summary>
    public Func<int>? Count { get; set; }

    /// <summary>Gets or sets the item accessor.</summary>
    public Func<int, object?>? GetItem { get; set; }

    /// <summary>Gets or sets the item descriptor accessor.</summary>
    public Func<int, IDescriptorSet?>? GetItemDescriptors { get; set; }

    /// <summary>Gets or sets whether items can be replaced.</summary>
    public Func<bool>? CanSetItem { get; set; }

    /// <summary>Gets or sets whether items can be appended.</summary>
    public Func<bool>? CanAdd { get; set; }

    /// <summary>Gets or sets whether items can be inserted.</summary>
    public Func<bool>? CanInsert { get; set; }

    /// <summary>Gets or sets whether items can be removed.</summary>
    public Func<bool>? CanRemove { get; set; }

    /// <summary>Gets or sets whether items can be moved.</summary>
    public Func<bool>? CanMove { get; set; }

    /// <summary>Gets or sets the item replacement operation.</summary>
    public Func<int, object?, bool>? TrySetItem { get; set; }

    /// <summary>Gets or sets the append operation.</summary>
    public Func<object?, bool>? TryAdd { get; set; }

    /// <summary>Gets or sets the indexed insertion operation.</summary>
    public Func<int, object?, bool>? TryInsert { get; set; }

    /// <summary>Gets or sets the indexed removal operation.</summary>
    public Func<int, bool>? TryRemoveAt { get; set; }

    /// <summary>Gets or sets the indexed move operation.</summary>
    public Func<int, int, bool>? TryMove { get; set; }
}

/// <summary>Describes a sequence and delegates edits to its generated observable view.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseArrayDescriptor : IArrayDescriptor
{
    private readonly SparseArrayDescriptorAccess _access;

    /// <summary>Creates a sequence descriptor with its declared item type and accessors.</summary>
    public SparseArrayDescriptor(
        Type itemType,
        bool isItemNullable,
        SparseArrayDescriptorAccess access,
        Type? itemViewType = null
    )
    {
        ItemType = itemType ?? throw new ArgumentNullException(nameof(itemType));
        IsItemNullable = isItemNullable;
        ItemViewType = itemViewType ?? itemType;
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <inheritdoc />
    public Type ItemType { get; }

    /// <inheritdoc />
    public Type ItemViewType { get; }

    /// <inheritdoc />
    public bool IsItemNullable { get; }

    /// <inheritdoc />
    public int Count =>
        (
            _access.Count
            ?? throw new InvalidOperationException(
                "The generated sequence count is not configured."
            )
        )();

    /// <inheritdoc />
    public bool CanAdd => _access.CanAdd?.Invoke() == true;

    /// <inheritdoc />
    public bool CanSetItem => _access.CanSetItem?.Invoke() == true;

    /// <inheritdoc />
    public bool CanInsert => _access.CanInsert?.Invoke() == true;

    /// <inheritdoc />
    public bool CanRemove => _access.CanRemove?.Invoke() == true;

    /// <inheritdoc />
    public bool CanMove => _access.CanMove?.Invoke() == true;

    /// <inheritdoc />
    public object? GetItem(int index) =>
        (
            _access.GetItem
            ?? throw new InvalidOperationException(
                "The generated sequence item accessor is not configured."
            )
        )(index);

    /// <inheritdoc />
    public IDescriptorSet? GetItemDescriptors(int index) =>
        _access.GetItemDescriptors?.Invoke(index);

    /// <inheritdoc />
    public bool TrySetItem(int index, object? value) =>
        _access.CanSetItem?.Invoke() == true && _access.TrySetItem?.Invoke(index, value) == true;

    /// <inheritdoc />
    public bool TryAdd(object? value) => CanAdd && _access.TryAdd?.Invoke(value) == true;

    /// <inheritdoc />
    public bool TryInsert(int index, object? value) =>
        CanInsert && _access.TryInsert?.Invoke(index, value) == true;

    /// <inheritdoc />
    public bool TryRemoveAt(int index) => CanRemove && _access.TryRemoveAt?.Invoke(index) == true;

    /// <inheritdoc />
    public bool TryMove(int oldIndex, int newIndex) =>
        CanMove && _access.TryMove?.Invoke(oldIndex, newIndex) == true;
}

/// <summary>Defines generated accessors for a dictionary descriptor.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDictionaryDescriptorAccess
{
    /// <summary>Gets or sets the current dictionary count.</summary>
    public Func<int>? Count { get; set; }

    /// <summary>Gets or sets whether entries can be added.</summary>
    public Func<bool>? CanAdd { get; set; }

    /// <summary>Gets or sets whether entries can be removed.</summary>
    public Func<bool>? CanRemove { get; set; }

    /// <summary>Gets or sets whether existing values can be replaced.</summary>
    public Func<bool>? CanSet { get; set; }

    /// <summary>Gets or sets an enumerable of the current dictionary keys.</summary>
    public Func<IEnumerable<object?>>? Keys { get; set; }

    /// <summary>Gets or sets the value lookup operation.</summary>
    public Func<object?, (bool Found, object? Value)>? TryGetValue { get; set; }

    /// <summary>Gets or sets the nested value descriptor accessor.</summary>
    public Func<object?, IDescriptorSet?>? GetValueDescriptors { get; set; }

    /// <summary>Gets or sets the dictionary add operation.</summary>
    public Func<object?, object?, bool>? TryAdd { get; set; }

    /// <summary>Gets or sets the dictionary value replacement operation.</summary>
    public Func<object?, object?, bool>? TrySetValue { get; set; }

    /// <summary>Gets or sets the dictionary removal operation.</summary>
    public Func<object?, bool>? TryRemove { get; set; }
}

/// <summary>Describes a dictionary and delegates edits to its generated observable view.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseDictionaryDescriptor : IDictDescriptor
{
    private readonly SparseDictionaryDescriptorAccess _access;

    /// <summary>Creates a dictionary descriptor with its key and value types and accessors.</summary>
    public SparseDictionaryDescriptor(
        Type keyType,
        Type valueType,
        bool isValueNullable,
        SparseDictionaryDescriptorAccess access,
        Type? valueViewType = null
    )
    {
        KeyType = keyType ?? throw new ArgumentNullException(nameof(keyType));
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        IsValueNullable = isValueNullable;
        ValueViewType = valueViewType ?? valueType;
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <inheritdoc />
    public Type KeyType { get; }

    /// <inheritdoc />
    public Type ValueType { get; }

    /// <inheritdoc />
    public Type ValueViewType { get; }

    /// <inheritdoc />
    public bool IsValueNullable { get; }

    /// <inheritdoc />
    public int Count =>
        (
            _access.Count
            ?? throw new InvalidOperationException(
                "The generated dictionary count is not configured."
            )
        )();

    /// <inheritdoc />
    public bool CanAdd => _access.CanAdd?.Invoke() == true;

    /// <inheritdoc />
    public bool CanRemove => _access.CanRemove?.Invoke() == true;

    /// <inheritdoc />
    public bool CanSet => _access.CanSet?.Invoke() == true;

    /// <inheritdoc />
    public IEnumerable<object?> Keys =>
        (
            _access.Keys
            ?? throw new InvalidOperationException(
                "The generated dictionary keys are not configured."
            )
        )();

    /// <inheritdoc />
    public bool TryGetValue(object? key, out object? value)
    {
        if (_access.TryGetValue is null)
        {
            value = null;
            return false;
        }

        var result = _access.TryGetValue(key);
        value = result.Value;
        return result.Found;
    }

    /// <inheritdoc />
    public IDescriptorSet? GetValueDescriptors(object? key) =>
        _access.GetValueDescriptors?.Invoke(key);

    /// <inheritdoc />
    public bool TryAdd(object? key, object? value) =>
        CanAdd && _access.TryAdd?.Invoke(key, value) == true;

    /// <inheritdoc />
    public bool TrySetValue(object? key, object? value) =>
        CanSet && _access.TrySetValue?.Invoke(key, value) == true;

    /// <inheritdoc />
    public bool TryRemove(object? key) => CanRemove && _access.TryRemove?.Invoke(key) == true;
}

/// <summary>Converts boxed descriptor values to the generated property's CLR type.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public static class SparseDescriptorValue
{
    /// <summary>Attempts a type-safe cast, including null for nullable and reference types.</summary>
    public static bool TryGet<T>(object? value, out T result)
    {
        if (value is T typed)
        {
            result = typed;
            return true;
        }

        if (value is null && default(T) is null)
        {
            result = default!;
            return true;
        }

        result = default!;
        return false;
    }
}
