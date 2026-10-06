# Concurrent Edits

Reconcile an edit authored against an older version with a newer committed state instead of overwriting it. Rebase replays the local patch onto the current state and returns the compatible remainder plus structured conflicts.

```csharp
var result = Settings.Patch.Rebase(baseState, localPatch, currentState);

if (!result.HasConflicts)
{
    var reconciled = result.Patch.Apply(currentState);
}
else
{
    foreach (var conflict in result.Conflicts)
    {
        // conflict.Path, conflict.Kind, baseline/desired/current values
    }
}
```

Untouched members replay cleanly; already-applied edits become no-ops; incompatible edits on the same member come back as conflicts rather than silent overwrites. Keyed collections rebase by stable key. Full conflict taxonomy and presence transitions live in [Patch rebase](rebase.md).
