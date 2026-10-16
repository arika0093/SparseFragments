namespace SparseTypes.Generator.Shared;

internal sealed class SparseDescriptorEmissionContext
{
    private SparseDescriptorEmissionContext(
        string receiver,
        string? ownerModelType,
        string? observableType
    )
    {
        Receiver = receiver;
        OwnerModelType = ownerModelType;
        ObservableType = observableType;
    }

    internal static SparseDescriptorEmissionContext Inline { get; } = new("this", null, null);

    internal string Receiver { get; }

    private string? OwnerModelType { get; }

    private string? ObservableType { get; }

    internal static SparseDescriptorEmissionContext ForFactory(
        string ownerModelType,
        string observableType
    ) => new("observable", ownerModelType, observableType);

    internal string Member(string name) => Receiver + "." + name;

    internal string Attributes(SparseMemberModel member)
    {
        if (string.IsNullOrEmpty(member.Property.AttributeExpressions))
        {
            return "global::System.Array.Empty<global::System.Attribute>()";
        }

        return OwnerModelType is null
            ? "new global::System.Attribute[] { " + member.Property.AttributeExpressions + " }"
            : OwnerModelType + ".__SparseAttributes_" + member.Id + "()";
    }

    internal string MethodDeclaration(
        string modelType,
        SparseDescriptorDialect dialect,
        string pathType
    )
    {
        if (ObservableType is null)
        {
            return "internal "
                + dialect.DescriptorSetInterface
                + " "
                + SparseObservableDescriptorEmitter.AccessorName(modelType)
                + "("
                + pathType
                + " pathPrefix)";
        }

        return "internal static "
            + dialect.DescriptorSetInterface
            + " Create("
            + ObservableType
            + " observable, "
            + pathType
            + " pathPrefix)";
    }
}
