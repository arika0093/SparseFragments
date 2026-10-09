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

    /// <summary>Creates an instance-bound view that fails safely once stale.</summary>
    /// <param name="inner">The live descriptors to guard.</param>
    /// <param name="isLive">Whether the captured instance is still current.</param>
    public static IDescriptorSet Guarded(IDescriptorSet inner, Func<bool> isLive)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(isLive);
        var guarded = new IDescriptor[inner.Members.Count];
        for (var index = 0; index < guarded.Length; index++)
        {
            guarded[index] = SparseDescriptor.Guarded(inner.Members[index], isLive);
        }

        return new SparseDescriptorSet(guarded);
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
    private readonly Func<ISetDescriptor?>? _getSet;

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
        Type? viewType = null,
        Func<ISetDescriptor?>? getSet = null
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
        _getSet = getSet;
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

    /// <inheritdoc />
    public ISetDescriptor? Set => _getSet?.Invoke();

    /// <summary>Creates an instance-bound view that fails safely once stale.</summary>
    /// <param name="inner">The live descriptors to guard.</param>
    /// <param name="isLive">Whether the captured instance is still current.</param>
    /// <remarks>
    /// Reads observe the captured instance; <c>TrySetValue</c> returns false and
    /// structural accessors return null after <paramref name="isLive" /> fails,
    /// so retained descriptors never silently mutate an orphan.
    /// </remarks>
    public static IDescriptor Guarded(IDescriptor inner, Func<bool> isLive) =>
        new GuardedDescriptor(inner, isLive);

    private sealed class GuardedDescriptor : IDescriptor
    {
        private readonly IDescriptor _inner;
        private readonly Func<bool> _isLive;

        public GuardedDescriptor(IDescriptor inner, Func<bool> isLive)
        {
            _inner = inner;
            _isLive = isLive;
        }

        public string Name => _inner.Name;

        public string Path => _inner.Path;

        public Type Type => _inner.Type;

        public Type ViewType => _inner.ViewType;

        public bool IsNullable => _inner.IsNullable;

        public bool IsEditable => _inner.IsEditable;

        public bool IsReadOnly => _inner.IsReadOnly;

        public IReadOnlyList<Attribute> Attributes => _inner.Attributes;

        public object? GetValue() => _inner.GetValue();

        public bool TrySetValue(object? value) => _isLive() && _inner.TrySetValue(value);

        public IDescriptorSet? Child =>
            _isLive() && _inner.Child is { } child
                ? SparseDescriptorSet.Guarded(child, _isLive)
                : null;

        public IArrayDescriptor? Array =>
            _isLive() && _inner.Array is { } array
                ? SparseArrayDescriptor.Guarded(array, _isLive)
                : null;

        public IDictDescriptor? Dictionary =>
            _isLive() && _inner.Dictionary is { } dictionary
                ? SparseDictionaryDescriptor.Guarded(dictionary, _isLive)
                : null;

        public ISetDescriptor? Set => _isLive() ? _inner.Set : null;
    }
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

    /// <summary>Gets or sets the item model resolver used for staleness checks.</summary>
    /// <remarks>Returns the unwrapped model for an index, or null when unknown.</remarks>
    public Func<int, object?>? GetItemModel { get; set; }
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

    internal object? ModelAt(int index) => _access.GetItemModel?.Invoke(index);

    internal bool HasModelResolver => _access.GetItemModel is not null;

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

    /// <summary>Creates an instance-bound view that fails safely once stale.</summary>
    /// <param name="inner">The live sequence to guard.</param>
    /// <param name="isLive">Whether the captured collection is still current.</param>
    /// <remarks>
    /// Mutations return false after <paramref name="isLive" /> fails. Item
    /// descriptors additionally verify the index still resolves to the captured
    /// model when the access bag provides <c>GetItemModel</c>.
    /// </remarks>
    public static IArrayDescriptor Guarded(IArrayDescriptor inner, Func<bool> isLive) =>
        new GuardedArrayDescriptor(inner, isLive);

    private sealed class GuardedArrayDescriptor : IArrayDescriptor
    {
        private readonly IArrayDescriptor _inner;
        private readonly Func<bool> _isLive;

        public GuardedArrayDescriptor(IArrayDescriptor inner, Func<bool> isLive)
        {
            _inner = inner;
            _isLive = isLive;
        }

        public Type ItemType => _inner.ItemType;

        public Type ItemViewType => _inner.ItemViewType;

        public bool IsItemNullable => _inner.IsItemNullable;

        public int Count => _inner.Count;

        public bool CanAdd => _isLive() && _inner.CanAdd;

        public bool CanSetItem => _isLive() && _inner.CanSetItem;

        public bool CanInsert => _isLive() && _inner.CanInsert;

        public bool CanRemove => _isLive() && _inner.CanRemove;

        public bool CanMove => _isLive() && _inner.CanMove;

        public object? GetItem(int index) => _inner.GetItem(index);

        public IDescriptorSet? GetItemDescriptors(int index)
        {
            if (!_isLive())
            {
                return null;
            }

            var set = _inner.GetItemDescriptors(index);
            if (set is null)
            {
                return null;
            }

            if (_inner is SparseArrayDescriptor { HasModelResolver: true } sparse)
            {
                var captured = sparse.ModelAt(index);
                var indexCopy = index;
                return SparseDescriptorSet.Guarded(
                    set,
                    () => _isLive() && IdentityMatches(sparse, indexCopy, captured)
                );
            }

            return SparseDescriptorSet.Guarded(set, _isLive);
        }

        private static bool IdentityMatches(
            SparseArrayDescriptor sparse,
            int index,
            object? captured
        )
        {
            if ((uint)index >= (uint)sparse.Count)
            {
                return false;
            }

            return ReferenceEquals(sparse.ModelAt(index), captured);
        }

        public bool TrySetItem(int index, object? value) =>
            _isLive() && _inner.TrySetItem(index, value);

        public bool TryAdd(object? value) => _isLive() && _inner.TryAdd(value);

        public bool TryInsert(int index, object? value) =>
            _isLive() && _inner.TryInsert(index, value);

        public bool TryRemoveAt(int index) => _isLive() && _inner.TryRemoveAt(index);

        public bool TryMove(int oldIndex, int newIndex) =>
            _isLive() && _inner.TryMove(oldIndex, newIndex);
    }
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

    /// <summary>Gets or sets the value model resolver used for staleness checks.</summary>
    /// <remarks>Returns the unwrapped model for a key, or null when unknown.</remarks>
    public Func<object?, object?>? GetValueModel { get; set; }

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

    internal object? ValueModelAt(object? key) => _access.GetValueModel?.Invoke(key);

    internal bool HasValueResolver => _access.GetValueModel is not null;

    /// <inheritdoc />
    public bool TryAdd(object? key, object? value) =>
        CanAdd && _access.TryAdd?.Invoke(key, value) == true;

    /// <inheritdoc />
    public bool TrySetValue(object? key, object? value) =>
        CanSet && _access.TrySetValue?.Invoke(key, value) == true;

    /// <inheritdoc />
    public bool TryRemove(object? key) => CanRemove && _access.TryRemove?.Invoke(key) == true;

    /// <summary>Creates an instance-bound view that fails safely once stale.</summary>
    /// <param name="inner">The live dictionary to guard.</param>
    /// <param name="isLive">Whether the captured collection is still current.</param>
    /// <remarks>
    /// Mutations return false after <paramref name="isLive" /> fails. Value
    /// descriptors additionally verify the key still resolves to the captured
    /// model when the access bag provides <c>GetValueModel</c>.
    /// </remarks>
    public static IDictDescriptor Guarded(IDictDescriptor inner, Func<bool> isLive) =>
        new GuardedDictionaryDescriptor(inner, isLive);

    private sealed class GuardedDictionaryDescriptor : IDictDescriptor
    {
        private readonly IDictDescriptor _inner;
        private readonly Func<bool> _isLive;

        public GuardedDictionaryDescriptor(IDictDescriptor inner, Func<bool> isLive)
        {
            _inner = inner;
            _isLive = isLive;
        }

        public Type KeyType => _inner.KeyType;

        public Type ValueType => _inner.ValueType;

        public Type ValueViewType => _inner.ValueViewType;

        public bool IsValueNullable => _inner.IsValueNullable;

        public int Count => _inner.Count;

        public bool CanAdd => _isLive() && _inner.CanAdd;

        public bool CanRemove => _isLive() && _inner.CanRemove;

        public bool CanSet => _isLive() && _inner.CanSet;

        public IEnumerable<object?> Keys => _inner.Keys;

        public bool TryGetValue(object? key, out object? value) =>
            _inner.TryGetValue(key, out value);

        public IDescriptorSet? GetValueDescriptors(object? key)
        {
            if (!_isLive())
            {
                return null;
            }

            var set = _inner.GetValueDescriptors(key);
            if (set is null)
            {
                return null;
            }

            if (_inner is SparseDictionaryDescriptor { HasValueResolver: true } sparse)
            {
                var captured = sparse.ValueModelAt(key);
                return SparseDescriptorSet.Guarded(
                    set,
                    () => _isLive() && ReferenceEquals(sparse.ValueModelAt(key), captured)
                );
            }

            return SparseDescriptorSet.Guarded(set, _isLive);
        }

        public bool TryAdd(object? key, object? value) => _isLive() && _inner.TryAdd(key, value);

        public bool TrySetValue(object? key, object? value) =>
            _isLive() && _inner.TrySetValue(key, value);

        public bool TryRemove(object? key) => _isLive() && _inner.TryRemove(key);
    }
}

/// <summary>Defines generated accessors for a set descriptor.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseSetDescriptorAccess
{
    /// <summary>Gets or sets the current set count.</summary>
    public Func<int>? Count { get; set; }

    /// <summary>Gets or sets whether items can be added.</summary>
    public Func<bool>? CanAdd { get; set; }

    /// <summary>Gets or sets whether items can be removed.</summary>
    public Func<bool>? CanRemove { get; set; }

    /// <summary>Gets or sets an enumerable of the live item values.</summary>
    public Func<IEnumerable<object?>>? Items { get; set; }

    /// <summary>Gets or sets the membership operation.</summary>
    public Func<object?, bool>? Contains { get; set; }

    /// <summary>Gets or sets the set add operation.</summary>
    public Func<object?, bool>? TryAdd { get; set; }

    /// <summary>Gets or sets the set removal operation.</summary>
    public Func<object?, bool>? TryRemove { get; set; }
}

/// <summary>Describes a set and delegates edits to its live model values.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseSetDescriptor : ISetDescriptor
{
    private readonly SparseSetDescriptorAccess _access;

    /// <summary>Creates a set descriptor with its declared item type and accessors.</summary>
    public SparseSetDescriptor(Type itemType, bool isItemNullable, SparseSetDescriptorAccess access)
    {
        ItemType = itemType ?? throw new ArgumentNullException(nameof(itemType));
        IsItemNullable = isItemNullable;
        _access = access ?? throw new ArgumentNullException(nameof(access));
    }

    /// <inheritdoc />
    public Type ItemType { get; }

    /// <inheritdoc />
    public bool IsItemNullable { get; }

    /// <inheritdoc />
    public int Count =>
        (
            _access.Count
            ?? throw new InvalidOperationException("The generated set count is not configured.")
        )();

    /// <inheritdoc />
    public bool CanAdd => _access.CanAdd?.Invoke() == true;

    /// <inheritdoc />
    public bool CanRemove => _access.CanRemove?.Invoke() == true;

    /// <inheritdoc />
    public IEnumerable<object?> Items =>
        (
            _access.Items
            ?? throw new InvalidOperationException("The generated set items are not configured.")
        )();

    /// <inheritdoc />
    public bool Contains(object? item) => _access.Contains?.Invoke(item) == true;

    /// <inheritdoc />
    public bool TryAdd(object? value) => CanAdd && _access.TryAdd?.Invoke(value) == true;

    /// <inheritdoc />
    public bool TryRemove(object? value) => CanRemove && _access.TryRemove?.Invoke(value) == true;
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
