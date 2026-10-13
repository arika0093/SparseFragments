using System;

namespace SparseTypes;

/// <summary>Excludes a property from all generated SparseTypes APIs.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class SparseIgnoreAttribute : Attribute;
