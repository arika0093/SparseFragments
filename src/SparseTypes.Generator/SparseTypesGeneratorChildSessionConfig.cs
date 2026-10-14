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
            EditSessionWithChildrenInterfaceMetadataName =
                "SparseTypes.ISparseEditSessionWithChildren",
        };
}
