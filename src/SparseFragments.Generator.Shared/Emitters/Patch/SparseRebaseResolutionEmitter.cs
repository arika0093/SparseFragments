using System.Collections.Immutable;

namespace SparseFragments.Generator.Shared;

/// <summary>Path-based rebase conflict resolution surface and build core (issue #203).</summary>
/// <remarks>
/// Emits the public <c>ChangeSet.Resolution</c> state plus <c>BeginResolution</c>
/// factories and the baseline-aware <c>TryCreateResolved</c> core. Per-path value
/// application lives in <see cref="SparseResolutionApplyEmitter"/>; this file owns
/// the workflow shape only. Product specifics flow through the existing patch
/// dialect (runtime namespace, path type, conflict and rebase-result types), so no new
/// dialect surface is introduced.
/// </remarks>
internal static class SparseRebaseResolutionEmitter
{
    internal static void AppendResolution(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        string modelType,
        SparseOperationTarget? target = null,
        SharedIndentedBuilder? infra = null,
        SharedIndentedBuilder? surfaceInfra = null
    )
    {
        var runtime = dialect.RuntimeNamespace;
        var optionalFragment = runtime + "Optional<Fragment?>";
        var optionalObject = runtime + "Optional<object?>";
        var rebaseResult = dialect.RebaseResult("ChangeSet");
        var conflict = dialect.ConflictType;
        var choices =
            "global::System.Collections.Generic.IReadOnlyList<" + runtime + "ResolvedChoice>";
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);

        var shell = code;
        AppendBeginResolutionShell(shell, rebaseResult, optionalFragment, modelType, target);
        AppendResolutionShell(
            shell,
            runtime,
            rebaseResult,
            optionalFragment,
            modelType,
            choices,
            target
        );

        // Internal infrastructure trails the public surface: generated members
        // stay ordered public-first, so single-file bodies collect in a
        // caller-flushed buffer while relocated bodies stream to the
        // operations container (internal-only, already ordered).
        var body = target?.ChangeSetOperations ?? infra ?? code;
        AppendBeginResolutionBody(body, rebaseResult, optionalFragment, target);
        SparseResolutionApplyEmitter.AppendForwarderShell(
            surfaceInfra ?? shell,
            pathType,
            conflict,
            optionalObject,
            target
        );
        AppendTryCreateResolved(
            body,
            members,
            runtime,
            optionalFragment,
            optionalObject,
            rebaseResult,
            conflict,
            choices,
            modelType,
            target
        );
        SparseResolutionApplyEmitter.AppendApplier(
            body,
            members,
            runtime,
            optionalFragment,
            optionalObject,
            conflict,
            pathType,
            modelType,
            dialect,
            target
        );
    }

    internal static void AppendForwarderOnly(
        SharedIndentedBuilder code,
        ImmutableArray<SparseMemberModel> members,
        SparseFragmentPatchEmitter.SparsePatchDialect dialect,
        SparseOperationTarget? target = null,
        SharedIndentedBuilder? infra = null,
        SharedIndentedBuilder? surfaceInfra = null
    )
    {
        // Structural hosts and other model-less change sets still participate as
        // recursion targets through the fragment applier, without resolution state.
        var runtime = dialect.RuntimeNamespace;
        var optionalObject = runtime + "Optional<object?>";
        var conflict = dialect.ConflictType;
        var pathType = SparseFragmentPatchEmitter.GetPathType(dialect);
        SparseResolutionApplyEmitter.AppendForwarderShell(
            surfaceInfra ?? code,
            pathType,
            conflict,
            optionalObject,
            target
        );
        var body = target?.ChangeSetOperations ?? infra ?? code;
        SparseResolutionApplyEmitter.AppendApplier(
            body,
            members,
            runtime,
            runtime + "Optional<Fragment?>",
            optionalObject,
            conflict,
            pathType,
            null,
            dialect,
            target
        );
    }

    private static void AppendBeginResolutionShell(
        SharedIndentedBuilder shell,
        string rebaseResult,
        string optionalFragment,
        string modelType,
        SparseOperationTarget? target
    )
    {
        shell.AppendLineAt(
            2,
            "/// <summary>Begins path-based resolution of a rebase result against the authoritative current state.</summary>"
        );
        shell.AppendLineAt(
            2,
            "/// <remarks>Pass the same current snapshot used for RebaseOnto. TryBuild fails when the state moved instead of resolving against stale values.</remarks>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"rebase\">The original rebase result to resolve.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"current\">The authoritative current-state snapshot.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <returns>Mutable resolution state owning the result and the current snapshot.</returns>"
        );
        shell.AppendLineAt(
            2,
            target is null
                ? "public static Resolution BeginResolution("
                    + rebaseResult
                    + " rebase, "
                    + optionalFragment
                    + " current) => new Resolution(rebase, current);"
                : "public static Resolution BeginResolution("
                    + rebaseResult
                    + " rebase, "
                    + optionalFragment
                    + " current) => "
                    + target.ChangeSetOperationsType
                    + ".BeginResolution(rebase, current);"
        );
        shell.AppendLineAt(
            2,
            "/// <summary>Begins path-based resolution of a rebase result against an ordinary model.</summary>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"rebase\">The original rebase result to resolve.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <param name=\"current\">The authoritative current model.</param>"
        );
        shell.AppendLineAt(
            2,
            "/// <returns>Mutable resolution state owning the result and the current snapshot.</returns>"
        );
        shell.AppendLineAt(
            2,
            "public static Resolution BeginResolution("
                + rebaseResult
                + " rebase, "
                + modelType
                + " current) => BeginResolution(rebase, "
                + optionalFragment
                + ".Present(Fragment.From(current)));"
        );
    }

    private static void AppendResolutionShell(
        SharedIndentedBuilder shell,
        string runtime,
        string rebaseResult,
        string optionalFragment,
        string modelType,
        string choices,
        SparseOperationTarget? target
    )
    {
        shell.AppendLineAt(
            2,
            "/// <summary>Mutable path-based resolution state for '"
                + modelType
                + "' rebase conflicts.</summary>"
        );
        shell.AppendLineAt(
            2,
            "/// <remarks>Framework-neutral inspection and resolution without UI state. Decisions are keyed by canonical paths; typed SparsePath overloads are inherited from the base.</remarks>"
        );
        shell.AppendLineAt(2, "public sealed class Resolution : " + ResolutionBaseFor(runtime));
        shell.AppendLineAt(2, "{");
        shell.AppendLineAt(
            2,
            "    /// <summary>Creates resolution state for a rebase result and its current snapshot.</summary>"
        );
        shell.AppendLineAt(
            2,
            "    /// <param name=\"rebase\">The original rebase result to resolve.</param>"
        );
        shell.AppendLineAt(
            2,
            "    /// <param name=\"current\">The authoritative current-state snapshot.</param>"
        );
        shell.AppendLineAt(
            2,
            "    public Resolution(" + rebaseResult + " rebase, " + optionalFragment + " current)"
        );
        shell.AppendLineAt(2, "        : base(rebase)");
        shell.AppendLineAt(2, "    {");
        shell.AppendLineAt(2, "        _current = current;");
        shell.AppendLineAt(2, "    }");
        shell.AppendLineAt(
            2,
            "    /// <summary>Creates resolution state for a rebase result and an ordinary model.</summary>"
        );
        shell.AppendLineAt(
            2,
            "    /// <param name=\"rebase\">The original rebase result to resolve.</param>"
        );
        shell.AppendLineAt(
            2,
            "    /// <param name=\"current\">The authoritative current model.</param>"
        );
        shell.AppendLineAt(
            2,
            "    public Resolution(" + rebaseResult + " rebase, " + modelType + " current)"
        );
        shell.AppendLineAt(
            2,
            "        : this(rebase, " + optionalFragment + ".Present(Fragment.From(current)))"
        );
        shell.AppendLineAt(2, "    {");
        shell.AppendLineAt(2, "    }");
        shell.AppendLineAt(
            2,
            "    /// <summary>Gets the authoritative current-state snapshot this resolution builds against.</summary>"
        );
        shell.AppendLineAt(2, "    public " + optionalFragment + " Current => _current;");
        shell.AppendLineAt(
            2,
            "    /// <summary>Builds the resolved change set from per-conflict desired values.</summary>"
        );
        shell.AppendLineAt(
            2,
            "    /// <param name=\"choices\">Every conflict with the presence-aware value to write.</param>"
        );
        shell.AppendLineAt(2, "    /// <param name=\"resolved\">The resolved change set.</param>");
        shell.AppendLineAt(
            2,
            "    /// <param name=\"failureReason\">A value-free reason when the build fails.</param>"
        );
        shell.AppendLineAt(
            2,
            "    /// <returns><see langword=\"true\"/> when the change set built.</returns>"
        );
        shell.AppendLineAt(
            2,
            "    protected override bool TryCreateResolved("
                + choices
                + " choices, out ChangeSet? resolved, out string? failureReason) => "
                + (
                    target is null
                        ? "__SparseBuildResolved"
                        : target.ChangeSetOperationsType + ".__SparseBuildResolved"
                )
                + "(Rebase, _current, choices, out resolved, out failureReason);"
        );
        shell.AppendLineAt(
            2,
            "    /// <summary>The authoritative current-state snapshot this resolution builds against (resolution infrastructure).</summary>"
        );
        shell.AppendLineAt(2, "    private readonly " + optionalFragment + " _current;");
        shell.AppendLineAt(2, "}");
    }

    private static string ResolutionBaseFor(string runtime) =>
        runtime + "SparseRebaseResolution<ChangeSet>";

    private static void AppendBeginResolutionBody(
        SharedIndentedBuilder body,
        string rebaseResult,
        string optionalFragment,
        SparseOperationTarget? target
    )
    {
        if (target is null)
        {
            return;
        }

        body.AppendLineAt(
            2,
            "/// <summary>Begins path-based resolution of a rebase result against the authoritative current state.</summary>"
        );
        body.AppendLineAt(
            2,
            "internal static ChangeSet.Resolution BeginResolution("
                + rebaseResult
                + " rebase, "
                + optionalFragment
                + " current) => new ChangeSet.Resolution(rebase, current);"
        );
    }

    private static void AppendTryCreateResolved(
        SharedIndentedBuilder body,
        ImmutableArray<SparseMemberModel> members,
        string runtime,
        string optionalFragment,
        string optionalObject,
        string rebaseResult,
        string conflict,
        string choices,
        string modelType,
        SparseOperationTarget? target
    )
    {
        _ = members;
        _ = runtime;
        _ = optionalObject;
        _ = conflict;
        body.AppendLineAt(
            2,
            "/// <summary>Builds a baseline-aware resolved change set from per-conflict desired values.</summary>"
        );
        body.AppendLineAt(
            2,
            "/// <remarks>Applies the clean rebased patch onto the stored current snapshot, writes each chosen value through the fragment applier, and diffs back against the same snapshot. Never mutates inputs.</remarks>"
        );
        body.AppendLineAt(
            2,
            (
                target is null
                    ? "private static bool __SparseBuildResolved("
                    : "internal static bool __SparseBuildResolved("
            )
                + rebaseResult
                + " rebase, "
                + optionalFragment
                + " current, "
                + choices
                + " choices, out ChangeSet? resolved, out string? failureReason)"
        );
        body.AppendLineAt(2, "{");
        body.AppendLineAt(3, "resolved = null;");
        body.AppendLineAt(3, "failureReason = null;");
        body.AppendLineAt(3, optionalFragment + " workingRoot = current;");
        body.AppendLineAt(3, "foreach (var choice in choices)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "if (!choice.Conflict.Path.IsRoot) continue;");
        body.AppendLineAt(
            4,
            "if (!__SparseApplyRoot(ref workingRoot, choice.Desired, out failureReason)) return false;"
        );
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "if (!workingRoot.IsPresent || workingRoot.Value is null)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "foreach (var choice in choices)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "if (!choice.Conflict.Path.IsRoot)");
        body.AppendLineAt(5, "{");
        body.AppendLineAt(
            6,
            "failureReason = \"Only root presence can be resolved while the current state is missing.\";"
        );
        body.AppendLineAt(6, "return false;");
        body.AppendLineAt(5, "}");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "resolved = ChangeSet.Between(current, workingRoot);");
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "if (!rebase.Rebased.__SparseBeforeMatches(current))");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(
            4,
            "failureReason = \"The current state changed since the rebase; rebase onto the latest state before resolving.\";"
        );
        body.AppendLineAt(4, "return false;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "var applied = rebase.Rebased.ToPatch().Apply(current);");
        body.AppendLineAt(3, "if (!applied.IsPresent || applied.Value is null)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(
            4,
            "failureReason = \"The rebased change no longer applies to the current state.\";"
        );
        body.AppendLineAt(4, "return false;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "var working = applied.Value;");
        body.AppendLineAt(3, "foreach (var choice in choices)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "if (choice.Conflict.Path.IsRoot) continue;");
        body.AppendLineAt(
            4,
            "if (!__SparseTryApplyResolution(working, choice.Conflict.Path, 0, choice.Desired, out var updated, out failureReason)) return false;"
        );
        body.AppendLineAt(4, "if (updated is null)");
        body.AppendLineAt(4, "{");
        body.AppendLineAt(5, "failureReason = \"The resolution produced no fragment.\";");
        body.AppendLineAt(5, "return false;");
        body.AppendLineAt(4, "}");
        body.AppendLineAt(4, "working = updated;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(
            3,
            "resolved = ChangeSet.Between(current, " + optionalFragment + ".Present(working));"
        );
        body.AppendLineAt(3, "failureReason = null;");
        body.AppendLineAt(3, "return true;");
        body.AppendLineAt(2, "}");
        body.AppendLineAt(
            2,
            "/// <summary>Applies a root presence resolution (resolution infrastructure).</summary>"
        );
        body.AppendLineAt(
            2,
            (
                target is null
                    ? "private static bool __SparseApplyRoot(ref "
                    : "internal static bool __SparseApplyRoot(ref "
            )
                + optionalFragment
                + " workingRoot, "
                + optionalObject
                + " desired, out string? error)"
        );
        body.AppendLineAt(2, "{");
        body.AppendLineAt(3, "error = null;");
        body.AppendLineAt(3, "if (!desired.IsPresent)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "workingRoot = " + optionalFragment + ".Missing;");
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "if (desired.Value is null)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(4, "workingRoot = " + optionalFragment + ".Present((Fragment?)null);");
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "if (desired.Value is Fragment fragment)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(
            4,
            "workingRoot = " + optionalFragment + ".Present(fragment.DeepClone());"
        );
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "if (desired.Value is " + modelType + " model)");
        body.AppendLineAt(3, "{");
        body.AppendLineAt(
            4,
            "workingRoot = " + optionalFragment + ".Present(Fragment.From(model));"
        );
        body.AppendLineAt(4, "return true;");
        body.AppendLineAt(3, "}");
        body.AppendLineAt(3, "error = \"The root resolution value must be a fragment or model.\";");
        body.AppendLineAt(3, "return false;");
        body.AppendLineAt(2, "}");
    }
}
