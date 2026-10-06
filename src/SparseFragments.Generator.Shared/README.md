# SparseFragments.Generator.Shared

Internal-only generator infrastructure shared by `SparseFragments.Generator`.

These sources are compiled directly into the generator via an in-repo
`Compile` glob in `src/SparseFragments.Generator/SparseFragments.Generator.csproj`.
No `SparseFragments.Generator.Shared` NuGet package is shipped: there is a
single in-repo path on purpose (see issue #21), so the shipped generator
always matches the local sources.

Downstream generators must not reference this directory as a package.
