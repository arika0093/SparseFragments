using System.Collections.Immutable;
using System.Linq;

namespace SparseFragments.Generator.Shared;

/// <summary>Emits the immutable baseline-aware ChangeSet sibling for a generated model (issue #85).</summary>
/// <remarks>
/// ChangeSet is a thin immutable wrapper retaining before/after presence-aware state
/// plus the canonical forward Patch. Diff/rebase semantics delegate to the existing
/// Patch implementation so scalar, nested, collection, keyed, and custom-strategy
/// behavior is preserved exactly. Patch.Between/Invert/Rebase remain for compatibility
/// with ChangeSet.Between as the canonical diff entry.
/// </remarks>
internal static class SparseChangeSetEmitter
{
    public static void AppendChangeSet(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members
    )
    {
        var runtime = SparseFragmentPatchEmitter.Runtime;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var changes = runtime + "SparsePatchChange";
        var readOnlyChanges = "global::System.Collections.Generic.IReadOnlyList<" + changes + ">";
        var rebaseResult = "global::SparseFragments.RebaseResult<ChangeSet>";
        var prefix = SparseNaming.PatchApiPrefix(
            members.Select(static member => member.Property.Name)
        );
        var between = "Patch." + prefix + "Between";
        var rebase = "Patch." + prefix + "Rebase";

        code.AppendLineAt(1, "public sealed class ChangeSet");
        code.AppendLineAt(1, "{");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " _before;");
        code.AppendLineAt(2, "private readonly " + optionalFragment + " _after;");
        code.AppendLineAt(2, "private readonly Patch _patch;");
        code.AppendLineAt(
            2,
            "private ChangeSet("
                + optionalFragment
                + " before, "
                + optionalFragment
                + " after, Patch patch)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "_before = before;");
        code.AppendLineAt(3, "_after = after;");
        code.AppendLineAt(3, "_patch = patch;");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Whether this change set contains no semantic changes.</summary>"
        );
        code.AppendLineAt(2, "public bool IsEmpty => _patch.__SparseIsEmpty();");
        code.AppendLineAt(
            2,
            "/// <summary>Gets the non-empty member changes in this change set.</summary>"
        );
        code.AppendLineAt(
            2,
            "public " + readOnlyChanges + " Changes => _patch.__SparseGetChanges();"
        );
        code.AppendLineAt(
            2,
            "/// <summary>Derives the canonical baseline-aware diff between two states.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static ChangeSet Between("
                + optionalFragment
                + " before, "
                + optionalFragment
                + " after)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var patch = " + between + "(before, after);");
        code.AppendLineAt(3, "return new ChangeSet(before, after, patch);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Attaches a known baseline to an arbitrary patch.</summary>"
        );
        code.AppendLineAt(
            2,
            "public static ChangeSet FromPatch(" + optionalFragment + " baseline, Patch patch)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (patch is null) throw new global::System.ArgumentNullException(nameof(patch));"
        );
        code.AppendLineAt(3, "var after = patch.Apply(baseline);");
        code.AppendLineAt(
            3,
            "return new ChangeSet(baseline, after, " + between + "(baseline, after));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Discards baseline information and returns the equivalent desired-operation patch.</summary>"
        );
        code.AppendLineAt(2, "public Patch ToPatch() => " + between + "(_before, _after);");
        code.AppendLineAt(
            2,
            "/// <summary>Swaps the transition direction without requiring a separate baseline.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Invert()");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "return new ChangeSet(_after, _before, " + between + "(_after, _before));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Composes sequential transitions; overlapping paths must be semantically contiguous.</summary>"
        );
        code.AppendLineAt(2, "public ChangeSet Compose(ChangeSet next)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (next is null) throw new global::System.ArgumentNullException(nameof(next));"
        );
        code.AppendLineAt(
            3,
            "if (!Fragment.__SparseAreEqual(_after, next._before)) throw new global::System.InvalidOperationException(\"ChangeSet composition requires the first after-state to equal the second before-state.\");"
        );
        code.AppendLineAt(
            3,
            "return new ChangeSet(_before, next._after, " + between + "(_before, next._after));"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(2, "/// <summary>Composes two sequential change sets.</summary>");
        code.AppendLineAt(2, "public static ChangeSet Compose(ChangeSet first, ChangeSet second)");
        code.AppendLineAt(2, "{");
        code.AppendLineAt(
            3,
            "if (first is null) throw new global::System.ArgumentNullException(nameof(first));"
        );
        code.AppendLineAt(3, "return first.Compose(second);");
        code.AppendLineAt(2, "}");
        code.AppendLineAt(
            2,
            "/// <summary>Rebases this change onto a newer state without requiring the original baseline.</summary>"
        );
        code.AppendLineAt(
            2,
            "public " + rebaseResult + " RebaseOnto(" + optionalFragment + " current)"
        );
        code.AppendLineAt(2, "{");
        code.AppendLineAt(3, "var rebase = " + rebase + "(_before, _patch, current);");
        code.AppendLineAt(3, "if (rebase.Conflicts.Count == 0 && rebase.Patch.__SparseIsEmpty())");
        code.AppendLineAt(
            4,
            "return " + rebaseResult + ".Success(new ChangeSet(current, current, new Patch()));"
        );
        code.AppendLineAt(3, "var rebasedAfter = rebase.Patch.Apply(current);");
        code.AppendLineAt(
            3,
            "return new "
                + rebaseResult
                + "(new ChangeSet(current, rebasedAfter, rebase.Patch), rebase.Conflicts);"
        );
        code.AppendLineAt(2, "}");
        code.AppendLineAt(1, "}");
    }
}
