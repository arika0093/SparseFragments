#!/usr/bin/env bash
# Verifies representative docs/*.md samples (#45).
#
# The canonical compile-checked sources are
# tests/fixtures/consumers/package-sparse-docs/*.cs (one topic file per guide
# page) and tests/fixtures/consumers/package-sparse-blazor/Program.cs (Blazor
# edit sessions). This script guards against drift between each guide and its
# canonical fixture source, then builds and runs both fixtures against the
# packed release-candidate packages so CI fails when the public generated API
# breaks a documented sample.
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
    '[SparseKey(' \
    'ISparseKeyed' \
    'SparseKey =>' \
    'Patch.Between' \
    '.Apply(before)' \
    'IsEmpty'

check_sample "rebase" "docs/rebase.md" "${docs_fixture_dir}/RebaseSamples.cs" \
    'Patch.Rebase' \
    'RebaseResult' \
    'HasConflicts' \
    'SparsePatchConflictKind.Scalar' \
    'Conflicts.Single()' \
    'conflict.Path'

check_sample "json-patch" "docs/json-patch.md" "${docs_fixture_dir}/JsonPatchSamples.cs" \
    'FromJsonPatch' \
    'ToJsonPatch' \
    'JsonPatchException' \
    'JsonPatchErrorKind.MissingTarget' \
    'Encoding.UTF8.GetBytes'

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

check_sample "blazor" "docs/blazor.md" "${blazor_fixture_dir}/Program.cs" \
    'CreateEditSession' \
    'HasChanges' \
    'CreatePatch' \
    'AcceptChanges' \
    'EditContext' \
    'CreateValidationStore' \
    'session.Field(' \
    'AddValidationError'

# 2. Compile and run the canonical fixtures against the packed packages.
# The core glob pins the version digit so the Blazor package
# (SparseFragments.Extensions.Blazor.*.nupkg, #46) never resolves here.
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
blazor_package="$(ls "${package_directory}"/SparseFragments.Extensions.Blazor.*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${blazor_package}" ]]; then
    echo "No SparseFragments.Extensions.Blazor package found in '${package_directory}'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

for framework in net8.0 net10.0; do
    dotnet build "${docs_fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseFragmentsPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
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
    output="$(dotnet "${blazor_fixture_dir}/bin/Release/${framework}/${blazor_fixture_dll}" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${blazor_success_marker}"* ]]; then
        echo "Blazor samples fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

echo "Docs samples verified (SparseFragments ${version})."
