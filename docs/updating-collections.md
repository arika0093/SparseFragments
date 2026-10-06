# Updating Collections

Change collection contents by element identity: add, remove, edit, and reorder without replacing the whole list. The element type needs one stable key.

```csharp
public partial class Server
{
    [SparseKey]
    public string Id { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;
}
```

```csharp
var before = Fleet.Fragment.From(new Fleet { Servers = new() { old } });
var after = Fleet.Fragment.From(new Fleet { Servers = new() { edited, added } });

var patch = Fleet.Patch.Between(before, after); // add/remove/edit by key
var applied = patch.Apply(before);              // original untouched

var reorder = new Fleet.Patch();
reorder.Servers.SetOrder(new[] { "b", "a" });   // final key order, not positions
```

Reordering is expressed as the desired final key sequence, independent of content edits. Scalar sequences such as `List<string>` stay whole-value replacements, and dictionaries patch by entry key. Composite keys, duplicate-key rejection, key changes (remove-old plus add-new), and nested keyed collections live in [Keyed collections](keyed-collections.md).
