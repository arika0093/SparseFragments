namespace SparseTypes.Generator.Shared;

/// <summary>Emits the product-owned session capability base interfaces.</summary>
/// <remarks>Separated from the core session skeleton so capability growth does not inflate the core emitter; shared by standard and relocated session placements.</remarks>
internal static class SparseEditSessionCapabilityEmitter
{
    /// <summary>Appends product-owned session capability interfaces shared by both session placements.</summary>
    /// <remarks>Each capability is appended only when the contract is bound and the matching members are emitted, so per-model relocation and disabled features stay identical.</remarks>
    /// <param name="sessionBases">Accumulated base interfaces.</param>
    /// <param name="modelType">Owning model type name.</param>
    /// <param name="config">Owning generator configuration.</param>
    /// <param name="patchDialect">Product patch dialect.</param>
    /// <param name="hasOwnedHistory">Whether owned-history members are emitted.</param>
    internal static string AppendSessionCapabilityBases(
        string sessionBases,
        string modelType,
        SparseGeneratorConfig config,
        SparseFragmentPatchEmitter.SparsePatchDialect patchDialect,
        bool hasOwnedHistory
    )
    {
        var capabilities = config.SessionCapabilityContracts;
        if (capabilities is null || !capabilities.HasAny)
        {
            return sessionBases;
        }

        if (capabilities.HistoryInterface is { Length: > 0 } history && hasOwnedHistory)
        {
            sessionBases += ", global::" + history;
        }

        if (SparseEditSessionStateEmitter.HasStateSurface(patchDialect))
        {
            if (capabilities.FieldStateInterface is { Length: > 0 } fieldState)
            {
                sessionBases += ", global::" + fieldState + "<" + modelType + ">";
            }

            if (capabilities.CollectionStateInterface is { Length: > 0 } collectionState)
            {
                sessionBases += ", global::" + collectionState;
            }

            // The snapshot member exists only when the view type is bound, so
            // the base stays off for products without the view type.
            if (
                capabilities.StateSnapshotInterface is { Length: > 0 } snapshot
                && patchDialect.StateContracts?.HasQueryView == true
            )
            {
                sessionBases += ", global::" + snapshot;
            }
        }

        if (capabilities.BatchEditInterface is { Length: > 0 } batchEdit)
        {
            sessionBases += ", global::" + batchEdit;
        }

        if (
            capabilities.DescriptorSessionInterface is { Length: > 0 } descriptors
            && config.DescriptorDialect is not null
            && config.EffectiveEmissionFeatures.EmitObservable
        )
        {
            sessionBases += ", global::" + descriptors;
        }

        return sessionBases;
    }
}
