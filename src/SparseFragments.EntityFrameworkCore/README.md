# SparseFragments.EntityFrameworkCore (PoC)

ChangeSet-aware mapping from DTOs onto tracked EF Core entities.

Status: proof of concept for issues #224 (scalar/nested mapping) and #225
(keyed child collections). The accepted `ISparseChangeSet` is the strict write
mask: only changed members execute their setters, and `Apply` never queries,
never calls `SaveChanges`, and never rebases. Orchestration (read-side
mapping, `RebaseOnto`, authorization, load/query, `SaveChanges`, transactions)
stays application-owned.

How it works:

- Implement `IEntityModelMapping<TEntity, TDto>` and declare overrides in
  `Configure`. Same-name scalar members are inferred; anything else uses
  `Member`, `Object`, `Ignore`, `OnMissing`, or `AllowWrite`. Keyed child
  collections use `Collection` with path-scoped `Element` child mappings plus
  explicit key, factory, removal (`Delete`/`Detach`/`SoftDelete`/`Remove`), and
  opt-in `Order` policies.
- Build a plan with `SparseEntityMapper<TEntity, TDto>.Create(mapping, model)`.
  Plans are compiled once and cached per EF `IModel` plus mapping type.
- Call `Apply(context, entity, desired, accepted)` with the tracked entity, the
  accepted after-state DTO, and the accepted change set, then save explicitly.
  `ApplyWithResult` additionally correlates created children by DTO temporary
  identity for the authoritative DTO and client `Reconcile`.

Safety defaults:

- Identity (primary/alternate keys), generated, concurrency-token, and shadow
  members are never written unless explicitly allowed with `AllowWrite`.
- `Missing` after-state is not silent null: the default policy throws, and each
  member can opt into `Ignore`, `SetDefault`, or `SetNull`.
- Keyed collections sync by stable key over loaded navigations only: unloaded
  navigations, unknown keys, colliding identities, partial orders, and
  missing removal policies fail explicitly instead of guessing.
- Changed marker-only collection paths are rejected with a diagnostic.
- Unmapped or ambiguous changed paths fail explicitly instead of disappearing.

See `docs/efcore-mapping.md` in the repository for the full API, lifecycle,
and PoC limitations.
