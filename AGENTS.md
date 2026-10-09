# AGENTS.md

Guidance for AI agents working in this repository. Always follow it, and consult the linked issues/docs when in doubt.

## Coding conventions

- Follow `.editorconfig` and the rules enforced by `SonarAnalyzer.CSharp`.
  - Avoid disabling warnings or deliberately circumventing checks unless truly necessary.
- As a guideline, keep file size under 700 lines (excluding whitespace and comments).
  - When editing existing files, consider splitting them.
  - When splitting code, avoid temporary `partial class` divisions; split class responsibilities instead.
- Avoid functions with too many parameters. Aim for 3–4 parameters in public APIs and around 6-7 in private methods.
  - Similarly, avoid parameters with meaningless default values. Except in public APIs, fix the call sites instead.
- Comments are encouraged but should be brief (1–2 lines) and explain the "why."
  - For significant changes, include the rationale in the commit message as well.
- Prefer explicit record declarations with init-only properties.
  - Do not use positional record syntax such as `record Item(string Name, int Count)`.
- Do not use the empty property pattern `x is {}`.
- **Favor low-allocation**, efficient code while keeping the implementation straightforward and maintainable.
  - If necessary, create and run benchmarks to improve performance.
- Format C# changes with CSharpier and verify them with `dotnet csharpier check`.

## SparseFragments.Generator.Shared
### Position
the generator's standalone, independently reusable code.
It is a **source-only package** compiled into the consuming generator, and downstream products (e.g. Configlue) consume it from outside this repository.

### Rules
- **Must not depend on `SparseFragments`** (the runtime package, its namespaces, or its type names).
  - Never hard-code names such as `global::SparseFragments.Optional<...>`, `RebaseResult`, or `CompilerServices.SparseFragmentRuntime` in shared emitters/analyzers.
- **Customizability is required.**
  - Anything product-specific must be injected through the configuration layer: `SparseGeneratorConfig` (attribute/interface metadata names, merge enum values, reserved generated names, diagnostic IDs, hint/structural-host suffixes), `SparseRuntimeDialect` and `SparsePatchDialect` (runtime/facade types, result/conflict types, member names).
  - Dialects are explicit; there is no implicit SparseFragments fallback.
- Shared owns the product-neutral semantics: analysis, Fragment/Patch/ChangeSet emission (storage, Between/FromPatch/ToPatch/Invert/Compose/Rebase, typed transitions), and STJ/AOT serialization mechanics. One implementation, no forks downstream.
- `SparseFragments.Generator` is just one consumer: it provides its own config and dialects. Product-specific behavior belongs there, not in Shared.
- Treat Shared's public/emitted surface as an external API: 
  - Always emit XML docs for any `public` code.
  - changes alter downstream generator binaries, so keep them deliberate. 
  - Prefer extending the existing dialect/policy infrastructure over inventing parallel abstractions.
- When changing Shared, verify with the isolated package consumer probe (a consumer with no SparseFragments runtime reference), not only the in-repo generator.
- Update `src/SparseFragments.Generator.Shared/README.md` when consumer-facing requirements change.

## Writing documentation

- Before writing or editing any documentation or prose (README, `docs/`, XML docs, issue/PR text), you **MUST** read the writing guideline first.
  - https://github.com/blader/humanizer/blob/main/SKILL.md (remove AI writing patterns)
  - https://gist.github.com/k16shikano/fd287c3133457c4fd8f5601d34aa817d (Japanese technical writing norms)
  - https://github.com/effector/patronum/blob/main/.cursor/.agents/skills/writing-documentation-with-diataxis/SKILL.md (Diátaxis framework)
  - Do not paraphrase it into this file; follow the original.
- Apply it to every documentation, apply the same principles.
- Existing docs are English. Keep the language of the file you edit.
- Code samples in `docs/*.md` are guarded by `verify-docs-samples.sh` against compile-checked fixtures in `package-sparse-docs/*.cs`; the README Quick Start and presence blocks are guarded the same way by `verify-sparsefragments-readme.sh` against `package-sparse-readme/Program.cs`.
  - When changing a sample, update the matching fixture too (`<!-- sample: name -->` markers in Markdown, `// sample: name` regions in fixtures). Every marker must be registered in the verify scripts; unregistered markers fail the build.
  - Snippets that must not compile (error illustrations, ellipsized shapes) carry `<!-- illustrative: reason -->` instead of sample markers and stay outside exact verification.

## Contributing
### Commit Rules
- Describe the changes, their background, and rationale in detail.
  - This information will help subsequent LLM investigations and future maintenance.
  - Prefer writing in the commit message over adding comments where possible.
- Do not automatically add `Co-authored-by`. This is **mandatory** to follow.

### Guidelines for contributors
- Encourage users to open an Issue first and wait for feedback before creating a Pull Request.
  - If you can quickly create a PR, the maintainer can too.
- If you create a PR, include the following message at the top.
  - Exception: when the library maintainer creates the PR.

```markdown
> [!WARNING]
> This PR is generated by an AI agent. It will be ignored as long as this message remains.
> If you see this message, you must remove this comment.
```
