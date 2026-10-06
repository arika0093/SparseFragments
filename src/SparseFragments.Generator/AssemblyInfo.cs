using System.Runtime.CompilerServices;

// Shared generator sources compile into this assembly; expose internals to the
// test assembly so centralized analysis/validation/emission helpers have direct
// unit coverage alongside generator-level tests. Excluded from the approved
// public API surface (see PublicApiCheck).
[assembly: InternalsVisibleTo("SparseFragments.Tests")]
