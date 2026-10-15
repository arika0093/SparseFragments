using System;

namespace SparseTypes;

/// <summary>Redacts a property's before-state from generated ChangePayload transport.</summary>
/// <remarks>Members redact the before endpoint but keep the after-state. Use <c>ToPatch</c>, not <c>ToChangeSet</c>, to apply them.</remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseRedactBeforeAttribute : Attribute;
