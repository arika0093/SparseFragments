namespace SparseFragments.Generator.Shared;

/// <summary>Model-targeted rebase entry point for generated change sets.</summary>
/// <remarks>
/// Split from <see cref="SparseChangeSetRebaseEmitter"/> to keep emitter
/// files focused: the ordinary-model overload delegates to the
/// presence-aware core while the facade keeps the ergonomic signature.
/// </remarks>
internal static class SparseChangeSetModelRebaseEmitter
{
    internal static void AppendModelRebase(
        SharedIndentedBuilder shell,
        SharedIndentedBuilder code,
        string modelType,
        string optionalFragment,
        string rebaseResult,
        string optionsType,
        SparseOperationTarget? target
    )
    {
        if (target is not null)
        {
            shell.AppendLineAt(
                2,
                "/// <summary>Rebases this change onto an ordinary model.</summary>"
            );
            shell.AppendLineAt(
                2,
                "public "
                    + rebaseResult
                    + " RebaseOnto("
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null) => "
                    + target.ChangeSetOperationsType
                    + ".RebaseOnto(this, current, options);"
            );
            code.AppendLineAt(2, "/// <summary>Rebases a change onto an ordinary model.</summary>");
            code.AppendLineAt(
                2,
                "internal static "
                    + rebaseResult
                    + " RebaseOnto(ChangeSet self, "
                    + modelType
                    + " current, "
                    + optionsType
                    + "? options = null)"
            );
            code.AppendLineAt(2, "{");
            code.AppendLineAt(
                3,
                "return RebaseOnto(self, "
                    + optionalFragment
                    + ".Present(Fragment.From(current)), options);"
            );
            code.AppendLineAt(2, "}");
            return;
        }
        code.AppendLineAt(2, "/// <summary>Rebases this change onto an ordinary model.</summary>");
        code.AppendLineAt(
            2,
            "public "
                + rebaseResult
                + " RebaseOnto("
                + modelType
                + " current, "
                + optionsType
                + "? options = null) => RebaseOnto("
                + optionalFragment
                + ".Present(Fragment.From(current)), options);"
        );
    }
}
