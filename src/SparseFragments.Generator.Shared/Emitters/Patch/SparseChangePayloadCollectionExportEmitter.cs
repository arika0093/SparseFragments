namespace SparseFragments.Generator.Shared;

/// <summary>Emits the collection patch payload export helper for one member.</summary>
/// <remarks>Runs inside the collection patch class so whole/item internals stay accessible without widening their visibility.</remarks>
internal static class SparseChangePayloadCollectionExportEmitter
{
    internal static void AppendCollectionExport(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        string? implementationNamespace = null,
        SparseMemberOperationSplit? split = null
    )
    {
        var id = member.Id;
        var change = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Change",
            id
        );
        if (split is not null)
        {
            // ChangeSet payload projection calls through the facade bridge.
            split.Shell.AppendLineAt(
                3,
                "/// <summary>Exports whole and granular operations as a payload change entry.</summary>"
            );
            split.Shell.AppendLineAt(
                3,
                "internal void __SparseExportChangePayload("
                    + change
                    + " entry) => "
                    + split.OperationsType
                    + ".__SparseExportChangePayload(this, entry);"
            );
            code.AppendLineAt(
                3,
                "/// <summary>Exports whole and granular operations as a payload change entry.</summary>"
            );
            code.AppendLineAt(
                3,
                "internal static void __SparseExportChangePayload("
                    + SparseKeyedCollectionEmitter.CollectionPatchName(member)
                    + " self, "
                    + change
                    + " entry)"
            );
        }
        else
        {
            code.AppendLineAt(
                3,
                "/// <summary>Exports whole and granular operations as a payload change entry.</summary>"
            );
            code.AppendLineAt(3, "internal void __SparseExportChangePayload(" + change + " entry)");
        }
        code.AppendLineAt(3, "{");
        code.AppendLineAt(
            4,
            split is null ? "var collection" + id + " = this;" : "var collection" + id + " = self;"
        );
        code.AppendLineAt(4, "var entry" + id + " = entry;");
        AppendExportEntryBody(code, member, dialect, modelType, id, implementationNamespace);
        code.AppendLineAt(3, "}");
    }

    private static void AppendExportEntryBody(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        int id,
        string? implementationNamespace = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var state = runtime + "ChangePayloadState";
        var kind = runtime + "FragmentOperationKind";
        var isKeyed = SparseChangeSetBasicsEmitter.IsKeyed(member);
        var collectionType = isKeyed
            ? SparseKeyedCollectionEmitter.MemberListType(member)
            : SparseKeyedCollectionEmitter.DictionaryType(member);
        code.AppendLineAt(4, "if (collection" + id + ".__whole.Kind != " + kind + ".Keep)");
        code.AppendLineAt(4, "{");
        code.AppendLineAt(
            5,
            "entry" + id + ".Before = " + endpoint + "<" + collectionType + ">.Redacted();"
        );
        code.AppendLineAt(
            5,
            "entry"
                + id
                + ".After = collection"
                + id
                + ".__whole.Kind == "
                + kind
                + ".Set ? "
                + endpoint
                + "<"
                + collectionType
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + collectionType
                + ">.Present(collection"
                + id
                + ".__whole.Value)) : new "
                + endpoint
                + "<"
                + collectionType
                + "> { State = "
                + state
                + ".Missing };"
        );
        code.AppendLineAt(4, "}");
        code.AppendLineAt(4, "else");
        code.AppendLineAt(4, "{");
        if (isKeyed)
            AppendExportKeyedItems(code, member, dialect, modelType, id, implementationNamespace);
        else
            AppendExportDictItems(code, member, dialect, modelType, id, implementationNamespace);
        code.AppendLineAt(4, "}");
    }

    private static void AppendExportKeyedItems(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        int id,
        string? implementationNamespace = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var itemKind = runtime + "ChangePayloadItemKind";
        var item = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Item",
            id
        );
        var itemValueType = SparseChangeSetBasicsEmitter.ElementTypeOf(member);
        var isModelValue = member.Collection.ElementType.IsFragmentModel;
        var childCore = isModelValue
            ? SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                dialect,
                member.Collection.ElementType.NonNullableName,
                "Core",
                implementationNamespace
            )
            : string.Empty;
        var keyOf = SparseKeyedCollectionEmitter.KeyOfMethod(member);
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__added is not null) foreach (var element in collection"
                + id
                + ".__added)"
        );
        code.AppendLineAt(
            6,
            "entry"
                + id
                + ".Items.Add(new "
                + item
                + " { Key = "
                + keyOf
                + "(element), After = "
                + endpoint
                + "<"
                + itemValueType
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + itemValueType
                + ">.Present(element)), Kind = "
                + itemKind
                + ".Add, BeforeIndex = -1, AfterIndex = -1"
                + (
                    SparseKeyedCollectionEmitter.HasTemporaryKey(member)
                        ? ", TemporaryKey = "
                            + SparseKeyedCollectionEmitter.TemporaryKeyOfMethod(member)
                            + "(element)"
                        : ""
                )
                + " });"
        );
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__removed is not null) foreach (var removedKey in collection"
                + id
                + ".__removed)"
        );
        code.AppendLineAt(
            6,
            "entry"
                + id
                + ".Items.Add(new "
                + item
                + " { Key = removedKey, Before = "
                + endpoint
                + "<"
                + itemValueType
                + ">.Redacted(), Kind = "
                + itemKind
                + ".Remove, BeforeIndex = -1, AfterIndex = -1 });"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Temporary removals and edits travel with their Guid identity;
            // unassigned permanent keys alone would collide on restore.
            code.AppendLineAt(
                5,
                "if (collection"
                    + id
                    + ".__removedTemp is not null) foreach (var removedTemp in collection"
                    + id
                    + ".__removedTemp)"
            );
            code.AppendLineAt(
                6,
                "entry"
                    + id
                    + ".Items.Add(new "
                    + item
                    + " { Key = "
                    + (member.Collection.UnassignedKeyExpression ?? "default!")
                    + ", TemporaryKey = removedTemp, Kind = "
                    + itemKind
                    + ".Remove, BeforeIndex = -1, AfterIndex = -1 });"
            );
        }
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__edited is not null) foreach (var editedPair in collection"
                + id
                + ".__edited)"
        );
        code.AppendLineAt(5, "{");
        if (isModelValue)
            code.AppendLineAt(
                6,
                "entry"
                    + id
                    + ".Items.Add(new "
                    + item
                    + " { Key = editedPair.Key, Kind = "
                    + itemKind
                    + ".Edit, Edit = "
                    + childCore
                    + ".FromPatchCore(editedPair.Value), BeforeIndex = -1, AfterIndex = -1 });"
            );
        else
            code.AppendLineAt(
                6,
                "entry"
                    + id
                    + ".Items.Add(new "
                    + item
                    + " { Key = editedPair.Key, Before = "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.Redacted(), After = "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional("
                    + runtime
                    + "Optional<"
                    + itemValueType
                    + ">.Present(editedPair.Value)), Kind = "
                    + itemKind
                    + ".Edit, BeforeIndex = -1, AfterIndex = -1 });"
            );
        code.AppendLineAt(5, "}");
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            code.AppendLineAt(
                5,
                "if (collection"
                    + id
                    + ".__editedTemp is not null) foreach (var editedTempPair in collection"
                    + id
                    + ".__editedTemp)"
            );
            code.AppendLineAt(5, "{");
            if (isModelValue)
                code.AppendLineAt(
                    6,
                    "entry"
                        + id
                        + ".Items.Add(new "
                        + item
                        + " { Key = "
                        + (member.Collection.UnassignedKeyExpression ?? "default!")
                        + ", TemporaryKey = editedTempPair.Key, Kind = "
                        + itemKind
                        + ".Edit, Edit = "
                        + childCore
                        + ".FromPatchCore(editedTempPair.Value), BeforeIndex = -1, AfterIndex = -1 });"
                );
            else
                code.AppendLineAt(
                    6,
                    "entry"
                        + id
                        + ".Items.Add(new "
                        + item
                        + " { Key = "
                        + (member.Collection.UnassignedKeyExpression ?? "default!")
                        + ", TemporaryKey = editedTempPair.Key, Before = "
                        + endpoint
                        + "<"
                        + itemValueType
                        + ">.Redacted(), After = "
                        + endpoint
                        + "<"
                        + itemValueType
                        + ">.FromOptional("
                        + runtime
                        + "Optional<"
                        + itemValueType
                        + ">.Present(editedTempPair.Value)), Kind = "
                        + itemKind
                        + ".Edit, BeforeIndex = -1, AfterIndex = -1 });"
                );
            code.AppendLineAt(5, "}");
        }
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__order is not null) entry"
                + id
                + ".AfterOrder = new global::System.Collections.Generic.List<"
                + SparseChangeSetBasicsEmitter.KeyTypeOf(member)
                + ">(collection"
                + id
                + ".__order);"
        );
        if (SparseKeyedCollectionEmitter.HasTemporaryKey(member))
        {
            // Parallel temporary order exports as the entry temporary order
            // (assigned slots contribute nothing to the Guid sequence).
            code.AppendLineAt(
                5,
                "if (collection"
                    + id
                    + ".__orderTemp is not null) { entry"
                    + id
                    + ".TempAfterOrder = new global::System.Collections.Generic.List<global::System.Guid>(); foreach (var __ot in collection"
                    + id
                    + ".__orderTemp) if (__ot.HasValue) entry"
                    + id
                    + ".TempAfterOrder.Add(__ot.Value); }"
            );
        }
    }

    private static void AppendExportDictItems(
        SharedIndentedBuilder code,
        SparseMemberModel member,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        int id,
        string? implementationNamespace = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var endpoint = runtime + "ChangePayloadEndpoint";
        var itemKind = runtime + "ChangePayloadItemKind";
        var item = SparseChangeSetPayloadEmitter.PayloadMemberTypeName(
            dialect,
            modelType,
            "Item",
            id
        );
        var itemValueType = SparseChangeSetBasicsEmitter.ValueTypeOf(member);
        var isModelValue = member.Collection.ValueType?.IsFragmentModel == true;
        var childCore =
            isModelValue && member.Collection.ValueType.HasValue
                ? SparseChangeSetPayloadEmitter.QualifiedChildPayloadType(
                    dialect,
                    member.Collection.ValueType.Value.NonNullableName,
                    "Core",
                    implementationNamespace
                )
                : string.Empty;
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__set is not null) foreach (var setPair in collection"
                + id
                + ".__set)"
        );
        code.AppendLineAt(
            6,
            "entry"
                + id
                + ".Items.Add(new "
                + item
                + " { Key = setPair.Key, After = "
                + endpoint
                + "<"
                + itemValueType
                + ">.FromOptional("
                + runtime
                + "Optional<"
                + itemValueType
                + ">.Present(setPair.Value)), Kind = "
                + itemKind
                + ".Add });"
        );
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__removed is not null) foreach (var removedKey in collection"
                + id
                + ".__removed)"
        );
        code.AppendLineAt(
            6,
            "entry"
                + id
                + ".Items.Add(new "
                + item
                + " { Key = removedKey, Before = "
                + endpoint
                + "<"
                + itemValueType
                + ">.Redacted(), Kind = "
                + itemKind
                + ".Remove });"
        );
        code.AppendLineAt(
            5,
            "if (collection"
                + id
                + ".__edited is not null) foreach (var editedPair in collection"
                + id
                + ".__edited)"
        );
        code.AppendLineAt(5, "{");
        if (isModelValue)
            code.AppendLineAt(
                6,
                "entry"
                    + id
                    + ".Items.Add(new "
                    + item
                    + " { Key = editedPair.Key, Kind = "
                    + itemKind
                    + ".Edit, Edit = "
                    + childCore
                    + ".FromPatchCore(editedPair.Value) });"
            );
        else
            code.AppendLineAt(
                6,
                "entry"
                    + id
                    + ".Items.Add(new "
                    + item
                    + " { Key = editedPair.Key, Before = "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.Redacted(), After = "
                    + endpoint
                    + "<"
                    + itemValueType
                    + ">.FromOptional("
                    + runtime
                    + "Optional<"
                    + itemValueType
                    + ">.Present(editedPair.Value)), Kind = "
                    + itemKind
                    + ".Edit });"
            );
        code.AppendLineAt(5, "}");
    }
}
