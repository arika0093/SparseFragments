using System.Runtime.CompilerServices;

// Expose internals to tests; excluded from public API (see PublicApiCheck).
[assembly: InternalsVisibleTo("SparseFragments.Tests")]
