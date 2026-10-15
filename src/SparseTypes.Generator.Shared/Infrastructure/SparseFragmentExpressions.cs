namespace SparseTypes.Generator.Shared;

/// <summary>Shared expressions for model/fragment clones, semantic equality and built-in collection merging.</summary>
/// <remarks>Collection equality is chosen statically per declared member shape.</remarks>
internal sealed class SparseFragmentExpressions(
    string cloneContext,
    string valueComparer,
    string collectionMerger,
    string optionalType,
    SparseFamilyNames? familyNames = null,
    string? cloneKernelsPrefix = null,
    string memberFieldQualifier = "",
    string pocoHelperQualifier = ""
)
{
    private string ValueComparer { get; } = valueComparer;
    private string CollectionMerger { get; } = collectionMerger;
    private string CloneContext { get; } = cloneContext;

    /// <summary>Optional runtime factory for a disposable clone-context scope.</summary>
    public string? CloneContextScopeFactory { get; init; }

    /// <summary>Emits a clone context whose lifetime covers the enclosing generated operation.</summary>
    /// <param name="code">Target source builder.</param>
    /// <param name="indent">Declaration indentation.</param>
    /// <param name="referenceComparer">Runtime type providing the unscoped context factory.</param>
    public void AppendCloneContext(SharedIndentedBuilder code, int indent, string referenceComparer)
    {
        if (CloneContextScopeFactory is null)
        {
            SparseFragmentEmitHelpers.AppendCloneContext(
                code,
                indent,
                CloneContext,
                referenceComparer
            );
            return;
        }
        var scope = CloneContext + "_scope";
        code.AppendLineAt(indent, "using var " + scope + " = " + CloneContextScopeFactory + "();");
        code.AppendLineAt(indent, "var " + CloneContext + " = " + scope + ".Context;");
    }

    private string OptionalType { get; } = optionalType;

    // Null keeps the standalone vocabulary so existing downstream call sites
    // compile unchanged; products with their own names pass their bindings.
    private SparseFamilyNames Family { get; } = familyNames ?? SparseFamilyNames.Standalone;

    // Generated-Once: with an explicit prefix the collection clone calls
    // resolve to the compilation-scoped kernels.
    private string? CloneKernelsPrefix { get; } = cloneKernelsPrefix;

    private string CloneKernel(string name) =>
        CloneKernelsPrefix is null ? name : CloneKernelsPrefix + "." + name;

    /// <summary>Builds the equality expression for one member's present values.</summary>
    /// <remarks>Custom comparer members use their generated comparer field.</remarks>
    public string ValueEqualityExpression(SparseMemberModel member, string left, string right)
    {
        if (member.ComparisonComparerType is not null)
        {
            // Stage 4: operations-class emission qualifies the generated
            // comparer field through the Fragment alias; surface code is bare.
            return memberFieldQualifier
                + SparseFragmentEmitHelpers.ComparisonComparerField(member)
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
            // Stage 4: POCO helpers live in the per-model operations class
            // under split emission.
            var qualified = pocoHelperQualifier + cloneHelperName;
            return type.IsReferenceType
                ? $"{access} is null ? default! : {qualified}({access}, {CloneContext})"
                : $"{qualified}({access}, {CloneContext})";
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
            var qualified = pocoHelperQualifier + cloneHelperName;
            return member.Property.Type.IsReferenceType
                ? $"{access} is null ? null! : {qualified}({access}, {CloneContext})"
                : $"{qualified}({access}, {CloneContext})";
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
            var qualified = pocoHelperQualifier + cloneHelperName;
            return $"{access} is null ? null : {qualified}({access}!, {CloneContext})";
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
                return $"{CloneKernel("__CloneDictionary")}<{collection.ElementType.Name}, {collection.ValueType.Value.Name}, {member.Property.Type.Name}>({access}, {CloneContext}, {keySelector}, {valueSelector})";
            }

            return access;
        }

        var selector = CloneSelector(collection.ElementType, "item");
        return collection.CloneKind switch
        {
            SparseCloneCollectionKind.Array =>
                $"{CloneKernel("__CloneArray")}<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            SparseCloneCollectionKind.List =>
                $"{CloneKernel("__CloneList")}<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            SparseCloneCollectionKind.Set => member.PortableSetView
            && IsInterfaceSet(collection.NamedTypeDefinition)
                ? $"{CloneKernel("__CloneSetView")}<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})"
                : $"{CloneKernel("__CloneSet")}<{elementType}, {member.Property.Type.Name}>({access}, {CloneContext}, {selector})",
            _ => access,
        };
    }

    // A null selector means the existing clone policy keeps each element
    // unchanged.
    private string CloneSelector(SparseTypeModel type, string parameter)
    {
        var expression = CloneValueExpression(type, parameter);
        return expression == parameter
            ? "null"
            : $"static ({parameter}, {CloneContext}) => {expression}";
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
