# SparseTypes.EntityFrameworkCore

Direct ChangeSet-to-entity mapping from DTOs onto tracked EF Core entities.

Status: production-usable mapping for issue #228. The accepted
`ISparseChangeSet` is both the strict write mask and the value source: only
changed members execute their setters, new values come from the typed
transitions, and there is no desired DTO argument. `Apply` never queries,
never calls `SaveChanges`, and never rebases. Orchestration (read-side
mapping, `RebaseOnto`, authorization, load/query, `SaveChanges`, transactions)
stays application-owned.

How it works:

- Implement `IEntityModelMapping<TEntity, TDto>` in a dedicated
  `[SparseFragmentMapper]` class and declare overrides in `Configure`. The
  ordinary generator emits conventional defaults (same-name scalars, nested
  objects, collection markers) into an external companion; explicit entries
  replace each default exactly once. Neither the mapper nor the entity needs to
  be partial (partial mappers with a parameterless constructor additionally
  gain an `OrderMapper.Create(db.Model)` shorthand). Keyed child collections
  use `Collection` with path-scoped `Element` child overrides (omitted when
  the conventional child mappings suffice) plus explicit key, factory,
  removal (`Delete`/`Detach`/`SoftDelete`/`Remove`), and opt-in `Order`
  policies.
- Build with `OrderMapper.Create(model)` for partial mappers, or
  `SparseEntityMapper<TEntity, TDto>.Create(mapping, model)` otherwise.
  Creation validates the full coverage manifest against the EF `IModel`, so a
  dormant unmapped member fails here. Each call builds a fresh plan for that
  mapping instance; reuse the returned mapper for repeated applies.
- Call `Apply(context, entity, accepted)` with the tracked entity and the
  accepted/rebased change set, then save explicitly. `ApplyWithResult`
  additionally correlates created children by path-scoped DTO temporary
  identity for the authoritative DTO and client `Reconcile`.

Safety defaults:

- Identity (primary/alternate keys), generated, concurrency-token, and shadow
  members are never written unless explicitly allowed with `AllowWrite`.
- `Missing` after-state is not silent null: the default policy throws, and each
  member can opt into `Ignore`, `SetDefault`, or `SetNull`.
- Validation precedes any write: a deterministic failure leaves the entity
  graph and the `ChangeTracker` untouched.
- Keyed collections sync by stable delta over loaded navigations only:
  unloaded navigations, unknown or foreign-parent keys, colliding identities,
  duplicate path-scoped temporaries, incomplete order evidence, and missing
  removal policies fail explicitly instead of guessing. A loaded navigation is
  not proof of completeness: filtered includes are unsupported for
  removal/reorder transitions.
- No unassigned sentinel is ever inferred from `default(TKey)`; assigned keys
  always take precedence over temporary lookup.
- Changed declared-only collection paths, unkeyed sequences, and skip
  navigations are rejected with a diagnostic.
- Unmapped or ambiguous paths fail explicitly instead of disappearing.

See `docs/efcore-mapping.md` in the repository for the full API, lifecycle,
and the supported/unsupported matrix.
