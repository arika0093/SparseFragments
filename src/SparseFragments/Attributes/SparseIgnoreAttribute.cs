using System;

namespace SparseFragments;

/// <summary>Excludes a property from all generated SparseFragments APIs.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseIgnoreAttribute : Attribute;
