using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SparseFragments.Generated;

/// <summary>Static declared metadata for one model property.</summary>
/// <remarks>
/// Instance-independent: names, declared types, attributes and shape can be
/// inspected without creating an observable. Instance-bound reads/writes live
/// on <see cref="ISparseInstanceAccess"/> instead.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparsePropertyMetadata
{
    /// <summary>Gets the CLR property name.</summary>
    string Name { get; }

    /// <summary>Gets the property's declared CLR type.</summary>
    Type DeclaredType { get; }

    /// <summary>Gets the runtime observable/view type returned by current reads.</summary>
    Type ViewType { get; }

    /// <summary>Gets whether the property accepts null.</summary>
    bool IsNullable { get; }

    /// <summary>Gets whether the property is nullable-oblivious.</summary>
    bool IsNullableOblivious { get; }

    /// <summary>Gets whether the property is a C# required member.</summary>
    bool IsRequired { get; }

    /// <summary>Gets whether the property itself can be replaced.</summary>
    bool IsEditable { get; }

    /// <summary>Gets whether the property itself cannot be replaced.</summary>
    bool IsReadOnly { get; }

    /// <summary>Gets the property's declared attributes.</summary>
    IReadOnlyList<Attribute> Attributes { get; }

    /// <summary>Gets static declared shape information for this property.</summary>
    SparseDescriptorShape Shape { get; }
}

/// <summary>Static declared metadata for one generated model.</summary>
/// <remarks>
/// Inspectable without an observable instance. Per-model generated code owns
/// the single cached instance; invariant entries are shared, never rebuilt
/// per access.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseModelMetadata
{
    /// <summary>Gets the modeled CLR type.</summary>
    Type ModelType { get; }

    /// <summary>Gets property metadata in declaration order.</summary>
    IReadOnlyList<ISparsePropertyMetadata> Properties { get; }

    /// <summary>Looks up property metadata by CLR name.</summary>
    bool TryGet(string name, out ISparsePropertyMetadata metadata);
}

/// <summary>Instance-bound value and structural access for one property.</summary>
/// <remarks>
/// Separated from <see cref="ISparsePropertyMetadata"/> so static metadata can
/// be cached and shared while each access observes live observable state.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseInstanceAccess
{
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

    /// <summary>Gets set operations for this property, when supported.</summary>
    ISetDescriptor? Set { get; }
}

/// <summary>Classifies a model-independent change record.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public enum SparseChangeKind
{
    /// <summary>A value changed while remaining present.</summary>
    Changed = 0,

    /// <summary>A value was added.</summary>
    Added = 1,

    /// <summary>A value was removed.</summary>
    Removed = 2,

    /// <summary>A keyed collection order changed.</summary>
    Order = 3,
}

/// <summary>A model-independent flattened change entry.</summary>
/// <remarks>
/// Mirrors the per-model <c>ChangeInfo</c> shape (path plus presence-aware
/// before/after) without naming generated CLR types, so diff inspection tools
/// work across models and assemblies. AOT-safe: plain data, no reflection.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseChangeRecord
{
    /// <summary>Creates a change record.</summary>
    public SparseChangeRecord(
        string path,
        Optional<object?> before,
        Optional<object?> after,
        SparseChangeKind kind
    )
    {
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Before = before;
        After = after;
        Kind = kind;
    }

    /// <summary>Gets the changed member path.</summary>
    public string Path { get; }

    /// <summary>Gets the presence-aware value before the change.</summary>
    public Optional<object?> Before { get; }

    /// <summary>Gets the presence-aware value after the change.</summary>
    public Optional<object?> After { get; }

    /// <summary>Gets the kind of change.</summary>
    public SparseChangeKind Kind { get; }
}

/// <summary>Model-independent change enumeration over Patch/ChangeSet state.</summary>
/// <remarks>
/// Implemented by model-bound projections; consumers inspect
/// <see cref="SparseChangeRecord"/> entries without referencing generated
/// CLR types. Per-model <c>EnumerateChanges()</c> results project into this
/// contract (see the descriptor capability seam for generator wiring).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public interface ISparseChangeInspector
{
    /// <summary>Enumerates flattened change records in deterministic order.</summary>
    IReadOnlyList<SparseChangeRecord> EnumerateChangeRecords();
}

/// <summary>Cached invariant property metadata shared across accesses.</summary>
/// <remarks>
/// Emitted once per model (see the #176 DescriptorFactory seam); every
/// instance bridge references the same entry instead of reallocating
/// attributes and shape per access. AOT-safe: no reflection.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseStaticPropertyMetadata : ISparsePropertyMetadata
{
    /// <summary>Creates cached metadata.</summary>
    public SparseStaticPropertyMetadata(
        string name,
        Type declaredType,
        Type? viewType,
        bool isNullable,
        bool isEditable,
        Attribute[] attributes,
        SparseDescriptorShape? shape = null,
        bool isRequired = false,
        bool isNullableOblivious = false
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(declaredType);
        ArgumentNullException.ThrowIfNull(attributes);
        Name = name;
        DeclaredType = declaredType;
        ViewType = viewType ?? declaredType;
        IsNullable = isNullable;
        IsEditable = isEditable;
        IsReadOnly = !isEditable;
        IsRequired = isRequired;
        IsNullableOblivious = isNullableOblivious;
        Attributes = Array.AsReadOnly((Attribute[])attributes.Clone());
        Shape = shape ?? new SparseDescriptorShape();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type DeclaredType { get; }

    /// <inheritdoc />
    public Type ViewType { get; }

    /// <inheritdoc />
    public bool IsNullable { get; }

    /// <inheritdoc />
    public bool IsNullableOblivious { get; }

    /// <inheritdoc />
    public bool IsRequired { get; }

    /// <inheritdoc />
    public bool IsEditable { get; }

    /// <inheritdoc />
    public bool IsReadOnly { get; }

    /// <inheritdoc />
    public IReadOnlyList<Attribute> Attributes { get; }

    /// <inheritdoc />
    public SparseDescriptorShape Shape { get; }
}

/// <summary>Cached per-model metadata shared across sessions and views.</summary>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparseStaticModelMetadata : ISparseModelMetadata
{
    private readonly Dictionary<string, ISparsePropertyMetadata> _byName;

    /// <summary>Creates model metadata from cached property entries.</summary>
    public SparseStaticModelMetadata(
        Type modelType,
        IReadOnlyList<ISparsePropertyMetadata> properties
    )
    {
        ModelType = modelType ?? throw new ArgumentNullException(nameof(modelType));
        ArgumentNullException.ThrowIfNull(properties);
        var copy = new ISparsePropertyMetadata[properties.Count];
        _byName = new Dictionary<string, ISparsePropertyMetadata>(
            properties.Count,
            StringComparer.Ordinal
        );
        for (var index = 0; index < properties.Count; index++)
        {
            var entry =
                properties[index]
                ?? throw new ArgumentException(
                    "Model metadata cannot contain null entries.",
                    nameof(properties)
                );
            copy[index] = entry;
            _byName.Add(entry.Name, entry);
        }

        Properties = Array.AsReadOnly(copy);
    }

    /// <inheritdoc />
    public Type ModelType { get; }

    /// <inheritdoc />
    public IReadOnlyList<ISparsePropertyMetadata> Properties { get; }

    /// <inheritdoc />
    public bool TryGet(string name, out ISparsePropertyMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(name);
        return _byName.TryGetValue(name, out metadata!);
    }
}
