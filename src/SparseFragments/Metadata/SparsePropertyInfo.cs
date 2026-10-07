namespace SparseFragments;

using System.ComponentModel;

/// <summary>
/// Describes a single generated sparse model property. Instances are produced by generated
/// model metadata and carry only <see cref="Type"/> identity; no reflection is performed.
/// </summary>
/// <remarks>Advanced vocabulary: generic model inspection.</remarks>
[EditorBrowsable(EditorBrowsableState.Advanced)]
public sealed class SparsePropertyInfo
{
    /// <summary>Initializes a new instance of the <see cref="SparsePropertyInfo"/> class.</summary>
    /// <param name="name">The CLR property name.</param>
    /// <param name="propertyType">The CLR property type.</param>
    /// <param name="isNullable">Whether the property type is nullable.</param>
    /// <param name="isNestedModel">Whether the property is a nested sparse model.</param>
    /// <param name="nestedModelType">The nested sparse model type, if <paramref name="isNestedModel"/>.</param>
    /// <param name="mergeMode">The merge behavior applied to the property.</param>
    /// <param name="mergeStrategyType">
    /// The custom <see cref="FragmentMergeStrategy{T}"/> type when <paramref name="mergeMode"/> is
    /// <see cref="MergeMode.Custom"/>; otherwise <c>null</c>.
    /// </param>
    /// <param name="collectionKind">The concrete collection shape of the property.</param>
    /// <param name="collectionSemantic">How the property participates in structural merge and keying.</param>
    /// <param name="keyKind">How the stable identity of a keyed element is declared.</param>
    /// <param name="keyPropertyNames">The key property names in key order; empty when there is no key.</param>
    /// <param name="keyType">The stable key type, if the property is keyed; otherwise <c>null</c>.</param>
    /// <param name="jsonPropertyName">The JSON property name used on the wire.</param>
    /// <param name="isRequired">Whether the property is required.</param>
    /// <param name="isInitOnly">Whether the property has an init-only setter.</param>
    /// <param name="isReadOnly">Whether the property has no setter.</param>
    public SparsePropertyInfo(
        string name,
        Type propertyType,
        bool isNullable,
        bool isNestedModel,
        Type? nestedModelType,
        MergeMode mergeMode,
        Type? mergeStrategyType,
        SparseCollectionKind collectionKind,
        SparseCollectionSemantic collectionSemantic,
        SparseKeyKind keyKind,
        IReadOnlyList<string> keyPropertyNames,
        Type? keyType,
        string jsonPropertyName,
        bool isRequired,
        bool isInitOnly,
        bool isReadOnly
    )
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(propertyType);
        ArgumentNullException.ThrowIfNull(keyPropertyNames);
        ArgumentNullException.ThrowIfNull(jsonPropertyName);

        Name = name;
        PropertyType = propertyType;
        IsNullable = isNullable;
        IsNestedModel = isNestedModel;
        NestedModelType = nestedModelType;
        MergeMode = mergeMode;
        MergeStrategyType = mergeStrategyType;
        CollectionKind = collectionKind;
        CollectionSemantic = collectionSemantic;
        KeyKind = keyKind;
        var keyNames = new string[keyPropertyNames.Count];
        for (var i = 0; i < keyNames.Length; i++)
        {
            keyNames[i] = keyPropertyNames[i];
        }
        KeyPropertyNames = keyNames;
        KeyType = keyType;
        JsonPropertyName = jsonPropertyName;
        IsRequired = isRequired;
        IsInitOnly = isInitOnly;
        IsReadOnly = isReadOnly;
    }

    /// <summary>Gets the CLR property name.</summary>
    public string Name { get; }

    /// <summary>Gets the CLR property type.</summary>
    public Type PropertyType { get; }

    /// <summary>Gets whether the property type is nullable.</summary>
    public bool IsNullable { get; }

    /// <summary>Gets whether the property is a nested sparse model.</summary>
    public bool IsNestedModel { get; }

    /// <summary>Gets the nested sparse model type, if <see cref="IsNestedModel"/>.</summary>
    public Type? NestedModelType { get; }

    /// <summary>Gets the merge behavior applied to the property.</summary>
    public MergeMode MergeMode { get; }

    /// <summary>
    /// Gets the custom <see cref="FragmentMergeStrategy{T}"/> type when <see cref="MergeMode"/> is
    /// <see cref="MergeMode.Custom"/>; otherwise <c>null</c>.
    /// </summary>
    public Type? MergeStrategyType { get; }

    /// <summary>Gets the concrete collection shape of the property.</summary>
    public SparseCollectionKind CollectionKind { get; }

    /// <summary>Gets how the property participates in structural merge and keying.</summary>
    public SparseCollectionSemantic CollectionSemantic { get; }

    /// <summary>Gets how the stable identity of a keyed element is declared.</summary>
    public SparseKeyKind KeyKind { get; }

    /// <summary>Gets the key property names in key order; empty when there is no key.</summary>
    public IReadOnlyList<string> KeyPropertyNames { get; }

    /// <summary>Gets the stable key type, if the property is keyed; otherwise <c>null</c>.</summary>
    public Type? KeyType { get; }

    /// <summary>Gets the JSON property name used on the wire.</summary>
    public string JsonPropertyName { get; }

    /// <summary>Gets whether the property is required.</summary>
    public bool IsRequired { get; }

    /// <summary>Gets whether the property has an init-only setter.</summary>
    public bool IsInitOnly { get; }

    /// <summary>Gets whether the property has no setter.</summary>
    public bool IsReadOnly { get; }
}
