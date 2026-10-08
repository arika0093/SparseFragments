namespace SparseFragments;

/// <summary>The requested mutation for one source-local fragment member.</summary>
public enum FragmentOperationKind
{
    /// <summary>Keep the source member unchanged.</summary>
    Keep,

    /// <summary>Set the source member, including setting it to null/default.</summary>
    Set,

    /// <summary>Remove the member from this source contribution.</summary>
    Remove,
}

/// <summary>A source-local mutation that distinguishes leaving a member alone from removing it.</summary>
public readonly struct FragmentOperation<T>
{
    private readonly T? _value;

    private FragmentOperation(FragmentOperationKind kind, T? value)
    {
        Kind = kind;
        _value = value;
    }

    /// <summary>The operation to apply.</summary>
    public FragmentOperationKind Kind { get; }

    /// <summary>The value for a set operation.</summary>
    public T? Value =>
        Kind == FragmentOperationKind.Set
            ? _value
            : throw new InvalidOperationException("This fragment operation does not set a value.");

    /// <summary>Keeps the source member unchanged.</summary>
    public static FragmentOperation<T> Keep => default;

    /// <summary>Sets the source member to a present value.</summary>
    /// <remarks>Stored by reference; clone before assigning if independence is required.</remarks>
    public static FragmentOperation<T> Set(T? value) => new(FragmentOperationKind.Set, value);

    /// <summary>Creates a set operation from a value, so a patch member can be assigned directly.</summary>
    /// <param name="value">The value to set, including an explicit null or default.</param>
    public static implicit operator FragmentOperation<T>(T? value) => Set(value);

    /// <summary>Removes the source member contribution.</summary>
    public static FragmentOperation<T> Remove => new(FragmentOperationKind.Remove, default);

    /// <summary>Applies this operation to a current source member.</summary>
    /// <remarks>Set aliases the stored reference; only <c>Fragment.From</c> and <c>DeepClone</c> isolate copies.</remarks>
    public Optional<T> Apply(Optional<T> current) =>
        Kind switch
        {
            FragmentOperationKind.Keep => current,
            FragmentOperationKind.Set => Optional<T>.Present(_value),
            FragmentOperationKind.Remove => Optional<T>.Missing,
            _ => throw new InvalidOperationException($"Unknown fragment operation '{Kind}'."),
        };
}
