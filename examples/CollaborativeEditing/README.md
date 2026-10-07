# Collaborative Editing Example

A runnable client/server application showing how SparseFragments fits into a real editing workflow instead of single APIs in isolation:

```text
PostgreSQL
  → EF Core entities
  → mapped SparseFragments model
  → Blazor / WPF editing
  → typed Patch
  → JSON Patch over HTTP
  → revision check
  → persist, rebase, or report conflict
```

It combines several SparseFragments features that are documented independently elsewhere: sparse state and `Merge`, typed patches, keyed collections, the JSON Patch bridge, rebase, and UI integration.

## Run the Example

Prerequisites:

* .NET 10 SDK;
* Docker (Aspire starts PostgreSQL as a container);
* Windows for the WPF client (everything else runs anywhere).

Start the AppHost (PostgreSQL + server + Blazor):

```sh
dotnet run --project examples/CollaborativeEditing/CollaborativeEditing.AppHost
```

Open the Aspire dashboard link from the output, then the `blazor` resource. The demo workspace is pre-seeded.

Start the WPF client on Windows with the server running:

```sh
dotnet run --project examples/CollaborativeEditing/CollaborativeEditing.Client.Wpf
```

Run the integration tests (no Docker needed; they use an in-memory store):

```sh
dotnet test examples/CollaborativeEditing/CollaborativeEditing.Tests
```

## Project Layout

* [Aspire orchestration](CollaborativeEditing.AppHost/Program.cs) — PostgreSQL, the API server, and the Blazor client. WPF is intentionally not referenced (Windows-only); it is launched separately.
* `CollaborativeEditing.ServiceDefaults` — shared Aspire service defaults (telemetry, health checks).
* [Shared contract model](CollaborativeEditing.Contracts/Models.cs) — `Workspace`, `WorkspaceSettings`, `Quest`. Both clients and the server compile against these types.
* [Server](CollaborativeEditing.Server/Program.cs) — ASP.NET Core + EF Core. Owns persistence, mapping, validation, and optimistic concurrency.
* [Blazor editor](CollaborativeEditing.Client.Blazor/Pages/WorkspaceEditor.razor) with its [HTTP client](CollaborativeEditing.Client.Blazor/WorkspaceApiClient.cs) — Blazor WebAssembly editor using the edit-session integration.
* [WPF code-behind save/rebase flow](CollaborativeEditing.Client.Wpf/MainWindow.xaml.cs) — WPF editor using the generated `T.Observable` wrapper.
* `CollaborativeEditing.Tests` — integration coverage for the persistence and concurrency workflow below the UI.

## Data and Persistence Flow

```text
PostgreSQL
  ↕
EF entities (WorkspaceEntity, QuestEntity)
  ↕ mapping (WorkspaceMapper)
API / SparseFragments model (Workspace, Quest)
```

The mapping boundary is deliberate: the server assembles persistence rows into the application model before SparseFragments ever sees it, so EF concerns never leak into the client contracts. See the [entity-to-model mapper](CollaborativeEditing.Server/CollabLogic.cs) and the [EF Core model](CollaborativeEditing.Server/Data/CollabDbContext.cs).

Settings demonstrate layering as an application scenario: the server keeps system defaults and per-workspace overrides, returning both the stored override and the merged effective settings. See [Fragments and patches](../../docs/fragments-and-patches.md) for `Merge` semantics.

## Edit and Save Flow

One normal save, end to end:

```text
GET model + revision
→ retain baseline Fragment
→ edit
→ Patch.Between(baseline, current)
→ ToJsonPatch
→ PATCH + If-Match
→ FromJsonPatch
→ Apply
→ validate
→ map/persist (Revision++)
→ return canonical state + new revision
→ adopt new baseline
```

Implementation: [server PATCH endpoint](CollaborativeEditing.Server/Program.cs); [Blazor HTTP client](CollaborativeEditing.Client.Blazor/WorkspaceApiClient.cs) and [Blazor editor page](CollaborativeEditing.Client.Blazor/Pages/WorkspaceEditor.razor); [WPF code-behind save/rebase flow](CollaborativeEditing.Client.Wpf/MainWindow.xaml.cs).

JSON Patch is the transport format only: each side keeps a typed patch plus its baseline, which is what preserves keyed add/remove/edit meaning across positional RFC 6902 paths. See [JSON Patch](../../docs/json-patch.md).

Revision/`If-Match`/ETag handling here is the example's own optimistic-concurrency scheme, not something SparseFragments requires.

After a successful save, clients adopt the server-returned canonical representation and revision as the next baseline rather than assuming the local result is canonical. That leaves room for server normalization, validation transforms, and mapping logic.

## Concurrent Edits and Automatic Rebase

Concrete scenario: Blazor and WPF both load revision 10. Blazor renames a quest and saves (revision 11). WPF edited a different field from revision 10 and saves with `If-Match: "10"`:

```text
WPF saves with revision 10
→ 412 + latest revision-11 state
→ Patch.Rebase(baseline@10, localPatch, current@11)
→ no conflicts
→ regenerate JSON Patch against revision 11
→ retry → revision 12 (UI notes "rebased")
```

See [Rebase](../../docs/rebase.md) for the complete semantic rules. Try it: open both clients, edit different fields, save one, then save the other and watch the automatic retry.

## Conflict Scenario

Edit the same quest title differently in both clients and save both. The stale save returns 412; rebase reports structured conflicts instead of submitting an arbitrary winner. The UI lists each conflict's path (`Path`/`PathText`), kind, and base/local/current values, matching the shape described in [Rebase](../../docs/rebase.md). No general merge editor is included; the data shown is what a real one would build on.

## Keyed Collection Scenario

The quest list demonstrates everything from [Keyed collections](../../docs/keyed-collections.md) against persisted state: add, remove, per-key edit, reorder (final key order survives the JSON round trip), concurrent edits to different keys (auto-rebase succeeds), and a same-key conflict (remove-while-editing, or two different edits to one member). Reproduce each from the two clients and compare the revision log in the Aspire dashboard.

## Blazor and WPF Comparison

| Client | Binding/edit integration | Patch source |
| --- | --- | --- |
| Blazor | `SparseEditSession` / `EditContext` | edit-session baseline vs current model |
| WPF | generated `T.Observable` wrapper | retained `Fragment` baseline vs current model |

Both front ends feed the same patch/rebase transport workflow. See [UI frameworks](../../docs/ui-frameworks.md) for the binding adapters.

## Related SparseFragments Documentation

| Example concept | Canonical documentation |
| --- | --- |
| sparse state, Merge, Patch | [Fragments and patches](../../docs/fragments-and-patches.md) |
| stable collection identity | [Keyed collections](../../docs/keyed-collections.md) |
| HTTP JSON Patch bridge | [JSON Patch](../../docs/json-patch.md) |
| stale edit reconciliation | [Rebase](../../docs/rebase.md) |
| Blazor / WPF integration | [UI frameworks](../../docs/ui-frameworks.md) |
| runtime model/patch inspection | [Inspection](../../docs/inspection.md) |
