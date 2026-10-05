# SparseFragments.Generator.Shared

Source-only generator infrastructure shared by `SparseFragments.Generator` and downstream generators.

This package ships C# sources only — there is no runtime assembly.
Referencing it adds the shared sources to the consuming generator via
`build/SparseFragments.Generator.Shared.props`:

```xml
<PackageReference
  Include="SparseFragments.Generator.Shared"
  Version="0.1.*"
  PrivateAssets="all" />
```

`PrivateAssets="all"` is recommended: the sources are compiled into the
generator itself and must not flow transitively to generator consumers.

Consuming generators additionally need the same compile surface the
sources were written against:

```xml
<PropertyGroup>
  <ImplicitUsings>enable</ImplicitUsings>
  <PolyUseEmbeddedAttribute>true</PolyUseEmbeddedAttribute>
  <PolyArgumentExceptions>true</PolyArgumentExceptions>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.3.1" PrivateAssets="all" />
  <PackageReference Include="Polyfill" Version="11.4.1" PrivateAssets="all" />
</ItemGroup>
```
