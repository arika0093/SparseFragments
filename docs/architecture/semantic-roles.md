# Semantic roles and product naming

Scope: separate what generated code means from what it is called.
Shared analysis and emitters describe generated types through semantic
roles. The owning generator binds those roles to its public names. This
document lists the roles, the bindings, and which emitter assumptions
are migrated here versus left to the emission infrastructure track.

Related work: #176 landed model-specific placement and the public facade
layout. #98 established the runtime-dialect boundary. #177 defines the
Runtime, Generated-Once, and Per-Model layers this decoupling serves.
This record was written for #179; the role model and binding mechanism
below describe the current implementation. The standalone vocabulary is
this product's configured (and shipped) binding, not a migration
leftover: downstream generators rebind the same roles through
`FamilyNames`.

## Role model

| Role | Meaning | Standalone binding | Bound through |
| --- | --- | --- | --- |
| State | Presence-aware snapshot of one model | `Fragment` | `SparseFamilyNames.Fragment` |
| State builder | Mutable construction helper | `FragmentBuilder` | `SparseFamilyNames.FragmentBuilder` |
| Operation | Baseline-free mutation | `Patch` | `SparseFamilyNames.Patch` |
| Transition | Baseline-aware change between states | `ChangeSet` | `SparseFamilyNames.ChangeSet` |
| Payload | Versioned transport envelope | `ChangePayload` | `SparseFamilyNames.ChangePayload` |
| Observable view | Bindable proxy over live state | `Observable` | `SparseFamilyNames.Observable` |
| Read-only view | Recursive view over live state | `ReadOnlyView` | `SparseFamilyNames.ReadOnlyView` |

`SparseGeneratorConfig.FamilyNames` carries the bindings. Null keeps
the standalone vocabulary through `EffectiveFamilyNames`, so existing
generators and the isolated downstream probe compile unchanged. The
standalone default API is preserved.

## Reference rules

Child and nested references resolve through `SparseSemanticReference`,
never by string surgery in emitters:

- `ChildFragmentType(host, family)` builds the state reference behind
  one method. Analysis calls it when computing `ChildFragmentType`, and
  `SparseFragmentExpressions` calls it for element equality, so both
  paths agree on the bound state name.
- `ChildPatchType` and `ChildChangeSetType` replace a trailing
  state-role suffix with the bound role name. Inputs without that
  suffix (hand-mapped downstream names) gain the role name instead of
  losing characters to a blind strip.
- `ChildObservableType` prefers the collision-resolved name recorded at
  analysis and otherwise mirrors the patch derivation.
- `ObservableRootName` and `ReadOnlyViewRootName` delegate to
  `SparseGeneratedPlacement.ResolveUiTypeName` with the bound base
  name, unifying the previously duplicated collision loops.
- `SparseEmissionFeatures.GetEmittedTypeNames(family)` reports the
  bound roots for collision checks; the parameterless overload keeps
  the standalone set.

A downstream generator binds its own names (for example state `State`,
operation `Operation`, transition `Delta`) and supplies
`ChildPatchName`/`ChildChangeSetName` delegates built from the same
resolver. Emitted patch, change-set, keyed-sequence, and dictionary
output then names only the product vocabulary and the product runtime
namespace. The `SemanticReferenceTests` fixture pins this: custom
family output contains no `.Fragment`, `.Patch`, or `.ChangeSet`
references and no `global::SparseFragments` token.

## Migrated assumptions

- Analysis child-state naming (`SparseModelDiscovery`) and
  observable/read-only discovery base names now read the bound family.
- `SparseFragmentExpressions` element equality takes the bound state
  name through an optional family parameter; the default keeps the
  standalone output byte for byte.
- `SparseFragmentPatchEmitter.ChildPatch` and `DefaultChildChangeSet`
  delegate to the resolver with the standalone bindings; output is
  unchanged and the product generator keeps passing these method groups
  as its dialect defaults.
- Observable and read-only root-name builders delegate to the shared
  placement resolver with family overloads; the original signatures
  remain and behave as before.
- `SparseEmissionFeatures` collision names follow the bound family.

## Landed assumptions and remaining seams

Root declarations keep the standalone names (`Fragment`,
`FragmentBuilder`, `Patch`, `ChangeSet`, `ChangePayload`) because the
standalone family is this product's binding, alongside operation and
member names (`From`, `DeepClone`, `__SparseAreEqual`, `ToPatch`,
`ToChangeSet`, merge-strategy and policy field owners). A downstream
generator rebinds the same roles through `FamilyNames` and the
`SparseSemanticReference` resolver, with `ChildPatchName` /
`ChildChangeSetName` delegates built from the same resolver. Per-child
UI fallbacks of the form `ObservableTypeName ?? "Observable"` and
`ReadOnlyViewTypeName ?? "ReadOnlyView"` in the descriptor and read-only
emitters remain until UI paths carry family context; the relocated UI
types themselves live in the per-model implementation containers
(#190, #191). Runtime member names on dialect-owned types (`AreEqual`,
`MergeSet`, `CreateCloneContext`, `TryRebaseAppend`, provenance entry
points) are stable contracts documented in the runtime ownership audit
(#186); dialectizing them is follow-up work for the helper migrations
(#181–#183, landed for the approved families), not a silent emitter
change. Family-aware overloads now exist across the declaration,
observable, read-only, descriptor, and implementation emitters, with
product call sites passing the configured `EffectiveFamilyNames` and the
standalone family kept as the default; the emission-infrastructure track
(#178) is landed. The current configuration surface,
including `EmissionFeatures` and the dialect members, is documented in
the [Shared README](../../src/SparseFragments.Generator.Shared/README.md)
and verified by the downstream dialect fixture tests and
`SemanticReferenceTests`.

Generated-Once generic algorithms already obtain runtime types through
generic parameters or explicit dialect contracts rather than fixed
product names: equality, merge, clone-context, optional, comparer, and
rebase helpers arrive as injected type-name strings at every emitter
call site, and Shared sources contain no `global::SparseFragments`
literal. The downstream dialect fixture tests verify this property for
patch, change-set, keyed, dictionary, rebase, and payload paths.
