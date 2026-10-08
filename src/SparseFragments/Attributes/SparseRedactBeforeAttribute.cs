using System;

namespace SparseFragments;

/// <summary>Redacts a property's before-state from generated ChangePayload transport.</summary>
/// <remarks>Members marked this way emit a redacted before endpoint while the after-state still travels. Apply such payloads with <c>ToPatch</c> rather than <c>ToChangeSet</c>.</remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseRedactBeforeAttribute : Attribute;
