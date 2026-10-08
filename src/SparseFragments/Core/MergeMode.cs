namespace SparseFragments;

/// <summary>How a higher-priority fragment combines with a lower-priority value.</summary>
public enum MergeMode
{
    /// <summary>Shape-aware default: nested models merge member by member, everything else replaces.</summary>
    Default = 0,

    /// <summary>The higher-priority present value replaces the lower value.</summary>
    Replace = 1,

    /// <summary>Nested model fragments are merged member by member.</summary>
    Deep = 2,

    /// <summary>Present collections are concatenated from low to high priority.</summary>
    Append = 3,

    /// <summary>Present collections are combined as an insertion-ordered set union.</summary>
    SetUnion = 4,

    /// <summary>A generated member delegates all merge operations to a registered strategy type.</summary>
    Custom = 5,
}
