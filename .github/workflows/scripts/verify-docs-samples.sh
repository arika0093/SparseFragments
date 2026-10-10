#!/usr/bin/env bash
# Verifies docs/*.md samples (#45, #68, #201, #213).
#
# The canonical compile-checked sources are
# tests/fixtures/consumers/package-sparse-docs/*.cs (one topic file per guide
# page) and tests/fixtures/consumers/package-sparse-blazor/Program.cs (Blazor
# edit sessions). This script guards against drift between each guide and its
# canonical fixture source, then builds and runs both fixtures against the
# packed release-candidate packages so CI fails when the public generated API
# breaks a documented sample.
#
# Adding a verified sample: wrap the fenced block in `<!-- sample: <id> -->`
# ... `<!-- /sample -->`, add the matching `// sample: <id>` ...
# `// /sample` region to the canonical fixture (and execute it from Run() so
# the documented result is verified, not just compiled), then add one row to
# docs-samples-registry.tsv (`<markdown> TAB <id> TAB <fixture>`). No shell
# edit is needed: the registry check discovers README.md and every
# docs/**/*.md from the filesystem, so an unregistered marker fails the
# build. Snippets that must not compile (error illustrations, ellipsized
# shapes, excerpts using names defined elsewhere) carry
# `<!-- illustrative: reason -->` instead of a sample marker and stay outside
# exact verification; every runnable fence needs one of the two guards.
#
# Tests consume the packed release-candidate packages via a local feed (no
# ProjectReference fallback, no second pack). The Blazor fixture resolves both
# shipped packages at one shared version (#46).
#
# Usage: verify-docs-samples.sh <package-directory>
set -euo pipefail

package_directory="${1:-}"
docs_fixture_dir="tests/fixtures/consumers/package-sparse-docs"
docs_fixture_csproj="${docs_fixture_dir}/PackageSparse.Docs.Consumer.csproj"
docs_fixture_dll="PackageSparse.Docs.Consumer.dll"
docs_success_marker="SparseFragments docs consumer passed."
blazor_fixture_dir="tests/fixtures/consumers/package-sparse-blazor"
blazor_fixture_csproj="${blazor_fixture_dir}/PackageSparse.Blazor.Consumer.csproj"
blazor_fixture_dll="PackageSparse.Blazor.Consumer.dll"
blazor_success_marker="SparseFragments Blazor consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${docs_fixture_csproj}" ]]; then
    echo "Docs samples fixture missing at '${docs_fixture_csproj}'." >&2
    exit 1
fi
if [[ ! -f "${blazor_fixture_csproj}" ]]; then
    echo "Blazor samples fixture missing at '${blazor_fixture_csproj}'." >&2
    exit 1
fi

# 1. Representative API tokens must appear in both the guide and its canonical
# fixture source so the fixture cannot silently drift from the docs. Failures
# name the affected topic/sample explicitly.
check_sample() {
    local topic="$1"
    local guide="$2"
    local fixture_source="$3"
    shift 3
    if [[ ! -f "${guide}" ]]; then
        echo "Docs sample drift [${topic}]: guide '${guide}' does not exist." >&2
        exit 1
    fi
    if [[ ! -f "${fixture_source}" ]]; then
        echo "Docs sample drift [${topic}]: canonical fixture '${fixture_source}' does not exist." >&2
        exit 1
    fi
    local token
    for token in "$@"; do
        if ! grep -F -q "${token}" "${guide}"; then
            echo "Docs sample drift [${topic}]: guide '${guide}' is missing sample token '${token}'." >&2
            exit 1
        fi
        if ! grep -F -q "${token}" "${fixture_source}"; then
            echo "Docs sample drift [${topic}]: canonical fixture '${fixture_source}' is missing token '${token}' from '${guide}'; keep them in sync." >&2
            exit 1
        fi
    done
}

check_sample "keyed-collections" "docs/keyed-collections.md" "${docs_fixture_dir}/KeyedCollections.cs" \
    '[SparseKey]' \
    'Key => ' \
    'CreateChangeSet' \
    'ToPatch().ApplyTo' \
    'IsEmpty' \
    'IsChanged' \
    'Added' \
    'Removed' \
    'Edited' \
    'BeforeOrder' \
    'AfterOrder' \
    'OrderChanged' \
    'GetChange' \
    'IsEdited'

check_sample "rebase" "docs/rebase.md" "${docs_fixture_dir}/RebaseSamples.cs" \
    'RebaseOnto' \
    'RebaseResult' \
    'HasConflicts' \
    'SparseConflictKind.Scalar' \
    'conflicts.Single()' \
    'conflict.Path'

check_sample "merge-strategies" "docs/merge-strategies.md" "${docs_fixture_dir}/MergeStrategies.cs" \
    '[SparseMerge(MergeMode.Append)]' \
    '[SparseMerge(MergeMode.SetUnion)]' \
    'FragmentMergeStrategy' \
    'AreEqual'

check_sample "cloning-and-ownership" "docs/cloning-and-ownership.md" "${docs_fixture_dir}/CloningAndOwnership.cs" \
    'DeepClone' \
    'SparseCloneReferenceSafe' \
    'Fragment.From' \
    '.Apply(patch)'

check_sample "model-shapes" "docs/model-shapes.md" "${docs_fixture_dir}/ModelShapes.cs" \
    '[SparseFragmentModel]' \
    'public partial class' \
    'IReadOnlyList<string> Plugins' \
    'Diff'

check_sample "ui-frameworks" "docs/ui-frameworks.md" "${docs_fixture_dir}/UiFrameworks.cs" \
    'CreateEditSession' \
    'session.Observable' \
    'PropertyChanged' \
    'CreateChangeSet'

# 1b. Registry-driven verification (#213). The central registry maps every
# Markdown file plus sample id to its canonical fixture; discovery covers
# README.md and all docs/**/*.md, so a new guide with an unregistered marker
# fails here without a shell edit. The check additionally enforces the
# fenced-snippet rule (every runnable fence is sampled or illustrative),
# requires assertion bodies in fixture regions, and validates JSON specimens
# through the same discovery once #212 registers them.
if ! python3 "$(dirname "$0")/check-docs-samples.py" --registry "$(dirname "$0")/docs-samples-registry.tsv"; then
    echo "Docs sample drift: see mismatches above." >&2
    exit 1
fi

check_sample "blazor" "docs/ui-frameworks.md" "${blazor_fixture_dir}/Program.cs" \
    'CreateEditSession' \
    'HasChanges' \
    'CreateChangeSet' \
    'CreatePatch' \
    'AcceptChanges' \
    'EditContext' \
    'CreateValidationStore' \
    'session.Field(' \
    'AddValidationError'

# 1c. Internal link and anchor validation (#68, #201). Files are discovered
# rather than enumerated, so new guides (including Descriptor, architecture,
# and benchmark pages) are covered automatically.
mapfile -t docs_files < <(find docs -name '*.md' | sort)
if ! python3 "$(dirname "$0")/check-docs-links.py" "$(dirname "$0")/../../../" \
    README.md "${docs_files[@]}"; then
    echo "Docs link check failed; see broken links above." >&2
    exit 1
fi

# 2. Compile and run the canonical fixtures against the packed packages.
# The core glob pins the version digit so the Blazor package
# (SparseFragments.Blazor.*.nupkg, #46) never resolves here.
package="$(ls "${package_directory}"/SparseFragments.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseFragments package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseFragments\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
blazor_package="$(ls "${package_directory}"/SparseFragments.Blazor.*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${blazor_package}" ]]; then
    echo "No SparseFragments.Blazor package found in '${package_directory}'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

# Isolated restore (#79): restore into a fresh global-packages directory so a
# stale same-version entry in ~/.nuget/packages cannot satisfy restore without
# proving the fresh nupkgs are consumable. External feeds stay available; the
# local feed is appended via RestoreAdditionalProjectSources.
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${isolated_packages}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"

assert_candidate_resolved() {
    local assets_file="$1"
    local package_id="$2"
    local lower_id="$3"
    if [[ ! -f "${assets_file}" ]]; then
        echo "Restore did not produce '${assets_file}'." >&2
        exit 1
    fi
    if ! grep -F -q "${package_id}/${version}" "${assets_file}"; then
        echo "Isolated restore did not resolve '${package_id}/${version}' (see '${assets_file}')." >&2
        exit 1
    fi
    if [[ ! -d "${isolated_packages}/${lower_id}/${lower_version}" ]]; then
        echo "Isolated global-packages has no '${lower_id}/${lower_version}': restore did not extract the candidate nupkg." >&2
        exit 1
    fi
}

for framework in net8.0 net10.0; do
    dotnet build "${docs_fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseFragmentsPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${docs_fixture_dir}/obj/project.assets.json" "SparseFragments" "sparsefragments"
    output="$(dotnet "${docs_fixture_dir}/bin/Release/${framework}/${docs_fixture_dll}" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${docs_success_marker}"* ]]; then
        echo "Docs samples fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

for framework in net8.0 net10.0; do
    dotnet build "${blazor_fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseFragmentsPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${blazor_fixture_dir}/obj/project.assets.json" "SparseFragments" "sparsefragments"
    assert_candidate_resolved "${blazor_fixture_dir}/obj/project.assets.json" "SparseFragments.Blazor" "sparsefragments.blazor"
    output="$(dotnet "${blazor_fixture_dir}/bin/Release/${framework}/${blazor_fixture_dll}" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${blazor_success_marker}"* ]]; then
        echo "Blazor samples fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

echo "Docs samples verified (SparseFragments ${version})."
