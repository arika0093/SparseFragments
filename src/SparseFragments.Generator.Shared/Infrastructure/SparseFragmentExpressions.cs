namespace SparseFragments.Generator.Shared;

/// <summary>Shared expressions for model/fragment clones, semantic equality and built-in collection merging.</summary>
/// <remarks>
/// Collection equality is chosen statically per declared member shape (issue #187): sequences
/// emit <c>AreSequenceEqual{T}</c>, sets emit <c>AreSetEqual{T}</c>, and dictionaries emit
/// <c>AreDictionaryEqual{TKey,TValue}</c> through the injected value-comparer reference, so a
/// downstream runtime can bind concrete shapes (<c>T[]</c>, <c>List{T}</c>, <c>HashSet{T}</c>,
/// <c>Dictionary{TKey,TValue}</c>, ...) to specialized overloads without runtime shape
/// probing. Members with a custom comparison comparer keep their generated comparer field;
/// nested fragment elements and dictionary values pass a generated semantic comparer lambda.
/// Members declared as <c>object</c> or with unsupported shapes keep the dynamic
/// <c>AreEqual</c> fallback, which preserves unknown/custom collection semantics.
/// </remarks>
internal sealed class SparseFragmentExpressions(
    string cloneContext,
    string valueComparer,
    string collectionMerger,
    string optionalType,
    SparseFamilyNames? familyNames = null
)
{
    private string ValueComparer { get; } = valueComparer;
    private string CollectionMerger { get; } = collectionMerger;
    private string CloneContext { get; } = cloneContext;
    private string OptionalType { get; } = optionalType;

    // Null keeps the standalone vocabulary so existing downstream call sites
    // compile unchanged; products with their own names pass their bindings.
    private SparseFamilyNames Family { get; } = familyNames ?? SparseFamilyNames.Standalone;

    /// <summary>Builds the equality expression for one member's present values.</summary>
    /// <remarks>
    /// Custom comparer members use their generated comparer field. Array/list shapes use the
    /// typed sequence overload (with a fragment-equality lambda for fragment elements);
    /// dictionary shapes use the typed dictionary overload (with a fragment-equality lambda
    /// for fragment values); set shapes use the typed set overload; everything else uses
    /// the dynamic fallback. Overload resolution against the member's declared type picks
    /// any statically specialized runtime overload; interface-declared or unknown shapes
    /// transparently keep the enumerable fallback with identical semantics.
    /// </remarks>
    public string ValueEqualityExpression(SparseMemberModel member, string left, string right)
    {
        if (member.ComparisonComparerType is not null)
        {
            return SparseFragmentEmitHelpers.ComparisonComparerField(member)
                + ".Equals("
                + left
                + ", "
                + right
                + ")";
        }

        var collection = member.Collection;
        if (
            collection.ElementType.IsFragmentModel
            && collection.CloneKind
                is SparseCloneCollectionKind.Array
                    or SparseCloneCollectionKind.List
        )
        {
            return $"{ValueComparer}.AreSequenceEqual<{collection.ElementType.Name}>({left}, {right}, static (__left, __right) => {FragmentElementEquality(collection.ElementType, "__left", "__right")})";
        }

        if (
            collection.ValueType is { IsFragmentModel: true } dictionaryValue
            && collection.CloneKind == SparseCloneCollectionKind.Dictionary
        )
        {
            return $"{ValueComparer}.AreDictionaryEqual<{collection.ElementType.Name}, {dictionaryValue.Name}>({left}, {right}, static (__left, __right) => {FragmentElementEquality(dictionaryValue, "__left", "__right")})";
        }

        return collection.CloneKind switch
        {
            SparseCloneCollectionKind.Array or SparseCloneCollectionKind.List =>
                $"{ValueComparer}.AreSequenceEqual<{collection.ElementType.Name}>({left}, {right})",
            SparseCloneCollectionKind.Set =>
                $"{ValueComparer}.AreSetEqual<{collection.ElementType.Name}>({left}, {right})",
            SparseCloneCollectionKind.Dictionary when collection.ValueType is not null =>
                $"{ValueComparer}.AreDictionaryEqual<{collection.ElementType.Name}, {collection.ValueType.Value.Name}>({left}, {right})",
            _ => $"{ValueComparer}.AreEqual({left}, {right})",
        };
    }

    private string FragmentElementEquality(SparseTypeModel element, string left, string right)
    {
        // The state-role name is product-owned; resolve the reference through
        // the shared semantic model instead of rebuilding a product literal.
        var fragment = SparseSemanticReference.ChildFragmentType(element.NonNullableName, Family);
        var optionalFragment = OptionalType + "<" + fragment + "?>";
        var equal =
            fragment
            + ".__SparseAreEqual("
            + optionalFragment
            + ".Present("
            + fragment
            + ".From("
            + left
            + ")), "
            + optionalFragment
            + ".Present("
            + fragment
            + ".From("
            + right
            + ")))";
        if (!element.IsReferenceType)
        {
            return equal;
        }

        return "(object?)"
            + left
            + " is null ? (object?)"
            + right
            + " is null : (object?)"
            + right
            + " is not null && "
            + equal;
    }

    public string CloneValueExpression(SparseTypeModel type, string access)
    {
        if (type.IsFragmentModel)
        {
            return type.IsReferenceType
                ? $"{access} is null ? default! : (({type.Name}){access}).DeepClone({CloneContext})"
                : $"(({type.Name}){access}).DeepClone({CloneContext})";
        }

        var cloneHelperName = type.PocoCloneHelperName;
        if (cloneHelperName is not null)
        {
            return type.IsReferenceType
                ? $"{access} is null ? default! : {cloneHelperName}({access}, {CloneContext})"
                : $"{cloneHelperName}({access}, {CloneContext})";
        }

        return access;
    }

    public string CloneModelExpression(SparseMemberModel member, string access)
    {
        if (member.ChildModel is not null && !member.ChildIsStructural)
        {
            return member.ChildIsReferenceType
                ? $"{access} is null ? null! : {access}.DeepClone({CloneContext})"
                : $"(({member.Property.Type.Name}){access}).DeepClone({CloneContext})";
        }

        var cloneHelperName = member.Property.Type.PocoCloneHelperName;
        if (cloneHelperName is not null)
        {
            return member.Property.Type.IsReferenceType
                ? $"{access} is null ? null! : {cloneHelperName}({access}, {CloneContext})"
                : $"{cloneHelperName}({access}, {CloneContext})";
        }

        var cloned = CloneCollectionExpression(member, access);
        return member.Property.Type.IsReferenceType
            ? $"{access} is null ? null! : {cloned}"
            : cloned;
    }

    public string CloneFragmentExpression(SparseMemberModel member, string access)
    {
        if (member.ChildModel is not null)
        {
            return $"{access}?.DeepClone({CloneContext})";
        }

        var cloneHelperName = member.Property.Type.PocoCloneHelperName;
        if (cloneHelperName is not null)
        {
            return $"{access} is null ? null : {cloneHelperName}({access}!, {CloneContext})";
        }

        var cloned = CloneCollectionExpression(member, access + "!");
        return $"(object?){access} is null ? default : {cloned}";
    }

    public string CloneCollectionExpression(SparseMemberModel member, string access)
    {
        var collection = member.Collection;
        if (collection.CloneKind == SparseCloneCollectionKind.Unsupported)
        {
            return access;
        }

        var elementType = collection.ElementType.Name;

        if (collection.ValueType is not null)
        {
            if (collection.CloneKind == SparseCloneCollectionKind.Dictionary)
            {
                var keySelector = CloneSelector(collection.ElementType, "key");
                var valueSelector = CloneSelector(collection.ValueType.Value, "value");
                return $"__CloneDictionary<{collection.ElementType.Name}, {collection.ValueType.Value.Name}, {member.Property.Type.Name}>({access}, {CloneContext}, {keySelector}, {valueSelector})";
            }

            return access;
        }

        var selector = CloneSelector(collection.ElementType, "item");
        return collection.CloneKind switch
        {
            SparseCloneCollectionKind.Array =>
                $"__CloneArray<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            SparseCloneCollectionKind.List =>
                $"__CloneList<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            SparseCloneCollectionKind.Set => member.PortableSetView
            && IsInterfaceSet(collection.NamedTypeDefinition)
                ? $"__CloneSetView<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})"
                : $"__CloneSet<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            _ => access,
        };
    }

    // A null selector means the existing clone policy keeps each element unchanged.
    // Recursive elements continue to receive their context-aware clone delegate.
    private string CloneSelector(SparseTypeModel type, string parameter)
    {
        var expression = CloneValueExpression(type, parameter);
        return expression == parameter ? "null" : $"{parameter} => {expression}";
    }

    private static bool IsInterfaceSet(string? namedTypeDefinition) =>
        namedTypeDefinition
            is SparseWellKnownNames.InterfaceSetTypeDefinition
                or SparseWellKnownNames.ReadOnlySetTypeDefinition;

    public string BuildCollectionMerge(SparseMemberModel member, string lower, string higher)
    {
        var elementType = member.Collection.ElementType.Name;
        if (member.Collection.Kind == SparseCollectionKind.Set)
        {
            return $"{CollectionMerger}.MergeSet<{elementType}>({lower}, {higher})";
        }

        if (member.MergeMode == SparseMergeModes.SetUnion)
        {
            var method =
                member.Collection.Kind == SparseCollectionKind.List
                    ? "MergeDistinctList"
                    : "MergeDistinctArray";
            return $"{CollectionMerger}.{method}<{elementType}>({lower}, {higher})";
        }

        if (member.Collection.Kind == SparseCollectionKind.List)
        {
            return $"{CollectionMerger}.MergeAppendList<{elementType}>({lower}, {higher})";
        }

        return $"global::System.Linq.Enumerable.ToArray(global::System.Linq.Enumerable.Concat({lower}, {higher}))";
    }

    /// <summary>Builds an expression that materializes a sequence of elements into the member's collection type.</summary>
    /// <remarks>Sets normalize to <c>HashSet{T}</c>; other unsupported shapes are <c>Unsupported</c>.</remarks>
    public static string MaterializeCollection(SparseMemberModel member, string elements)
    {
        var elementType = member.Collection.ElementType.Name;
        return member.Collection.CloneKind switch
        {
            SparseCloneCollectionKind.Set =>
                $"new global::System.Collections.Generic.HashSet<{elementType}>({elements})",
            SparseCloneCollectionKind.Array =>
                $"global::System.Linq.Enumerable.ToArray({elements})",
            _ => $"new global::System.Collections.Generic.List<{elementType}>({elements})",
        };
    }
}
