using SparseTypes.Generator.Shared;

namespace SparseTypes.Generator;

internal static class SparseTypesGeneratorChildSessionConfig
{
    internal static SparseGeneratorConfig Configure(SparseGeneratorConfig config) =>
        config with
        {
            EditChildPathInterfaceMetadataName = "SparseTypes.ISparseChildEditPath",
            EditChildSessionInterfaceMetadataName = "SparseTypes.ISparseEditChildSession",
            EditChildSessionImplementationType = "global::SparseTypes.SparseEditChildSession",
            EditChildSessionStatusType = "global::SparseTypes.SparseChildSessionStatus",
            EditSessionWithChildrenInterfaceMetadataName =
                "SparseTypes.ISparseEditSessionWithChildren",
            EditSessionWithChildStatusInterfaceMetadataName =
                "SparseTypes.ISparseEditSessionWithChildStatus",
            EditSessionWithChangeSetInterfaceMetadataName =
                "SparseTypes.ISparseEditSessionWithChangeSet",
            SessionCapabilityContracts = new SparseSessionCapabilityContracts(
                HistoryInterface: "SparseTypes.ISparseEditSessionWithHistory",
                FieldStateInterface: "SparseTypes.ISparseEditSessionWithFieldState",
                CollectionStateInterface: "SparseTypes.ISparseEditSessionWithCollectionState",
                BatchEditInterface: "SparseTypes.ISparseEditSessionWithBatchEdit",
                DescriptorSessionInterface: "SparseTypes.ISparseEditSessionWithDescriptors",
                StateSnapshotInterface: "SparseTypes.ISparseEditSessionWithStateSnapshot"
            ),
            PatchDialect = config.PatchDialect is { } patchDialect
                ? patchDialect with
                {
                    ReconcileConflictExpression = static (model, reason) =>
                        "new global::SparseTypes.SparseConflict(global::SparseTypes.SparsePath.Root(typeof("
                        + model
                        + ")), global::SparseTypes.SparseConflictKind.Reconciliation, global::SparseTypes.Optional<object?>.Missing, global::SparseTypes.Optional<object?>.Missing, global::SparseTypes.Optional<object?>.Missing, "
                        + reason
                        + ")",
                    ChildPathOverlapExpression = static (left, right) =>
                        left
                        + ".Equals("
                        + right
                        + ") || "
                        + left
                        + ".IsAncestorOf("
                        + right
                        + ") || "
                        + right
                        + ".IsAncestorOf("
                        + left
                        + ")",
                    ChildConflictPathPrefixExpression = static (conflict, prefix) =>
                        conflict + ".WithPathPrefix(" + prefix + ")",
                }
                : null,
        };
}
