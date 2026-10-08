# Public API approvals

This suite pins the public surface that v0.1 supports. It runs in the
`public-api` strict gate, which never accepts drift on its own.

## What is checked

- `SparseFragments.approved.txt`: runtime assembly.
- `SparseFragments.Generator.approved.txt`: generator assembly.
- `SparseFragments.GeneratedApiFixtures.approved.txt`: compiled output of the
  generator for the fixture models in
  `../SparseFragments.GeneratedApiFixtures`. It covers `Fragment`, `Patch`,
  `ChangeSet`, `ChangeSetPayload`, `Observable`, `FragmentBuilder`, typed
  transitions, model extensions (`CreateChangeSet`, `CreateEditSession`,
  `ToObservable`), and the absence of removed APIs such as `Submit` and
  `ChangeSet.ApplyInPlace`.
- `SparseFragments.Blazor.approved.txt`: Blazor helpers. The package ships
  net8.0 and net10.0 from the same sources with no conditional compilation, so
  one snapshot covers both builds.
- `SparseFragmentsApiAudienceTests`: every runtime type keeps its audience
  classification.

PublicApiGenerator renders nested contracts under their declaring models, so
the fixture declarations stay in the snapshot. The fixtures are frozen
scaffolding: do not rename types or members unless the contract itself changes.
Generated contract diffs appear in the nested blocks.

## How to update snapshots after an intentional change

1. Run the suite with the update flag set:

   ```sh
   SPARSEFRAGMENTS_UPDATE_PUBLIC_API=1 dotnet test tests/SparseFragments.PublicApi.Tests --configuration Release
   ```

   On PowerShell, set the variable first with
   `$env:SPARSEFRAGMENTS_UPDATE_PUBLIC_API="1"`.

2. Inspect the diff under `Approvals/`. Each hunk must trace back to the
   intended change. Private emitter refactors and formatting leave no trace
   here; if they move a snapshot, something public changed.

3. Rebuild and run the suite without the flag. It must pass.

4. Submit the updated snapshots for review in the same change. CI runs without
   the flag, so an unreviewed drift fails the `public-api` job.
