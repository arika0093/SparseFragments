namespace SparseTypes.Playground.Components;

public sealed record ChangePreviewRow
{
    public string Target { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Before { get; init; } = string.Empty;
    public string After { get; init; } = string.Empty;
}
