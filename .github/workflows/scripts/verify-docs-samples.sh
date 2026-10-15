#!/usr/bin/env bash
# Verifies docs/*.md samples.
set -euo pipefail

package_directory="${1:-}"
docs_fixture_dir="tests/fixtures/consumers/package-sparse-docs"
docs_fixture_csproj="${docs_fixture_dir}/PackageSparse.Docs.Consumer.csproj"
docs_fixture_dll="PackageSparse.Docs.Consumer.dll"
docs_success_marker="SparseTypes docs consumer passed."
blazor_fixture_dir="tests/fixtures/consumers/package-sparse-blazor"
blazor_fixture_csproj="${blazor_fixture_dir}/PackageSparse.Blazor.Consumer.csproj"
blazor_fixture_dll="PackageSparse.Blazor.Consumer.dll"
blazor_success_marker="SparseTypes Blazor consumer passed."

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

# Representative API tokens must appear in both the guide and its canonical
# fixture source so the fixture cannot silently drift from the docs.
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

check_sample "keyed-collections" "docs/explanation/keyed-collections.md" "${docs_fixture_dir}/KeyedCollections.cs" \
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

check_sample "rebase" "docs/explanation/rebase.md" "${docs_fixture_dir}/RebaseSamples.cs" \
    'RebaseOnto' \
    'RebaseResult' \
    'HasConflicts' \
    'SparseConflictKind.Scalar' \
    'conflicts.Single()' \
    'conflict.Path'

check_sample "merge-strategies" "docs/explanation/merge-strategies.md" "${docs_fixture_dir}/MergeStrategies.cs" \
    '[SparseMerge(MergeMode.Append)]' \
    '[SparseMerge(MergeMode.SetUnion)]' \
    'FragmentMergeStrategy' \
    'AreEqual'

check_sample "cloning-and-ownership" "docs/explanation/cloning-and-ownership.md" "${docs_fixture_dir}/CloningAndOwnership.cs" \
    'DeepClone' \
    'SparseCloneReferenceSafe' \
    'Fragment.From' \
    '.Apply(patch)'

check_sample "model-shapes" "docs/reference/model-shapes.md" "${docs_fixture_dir}/ModelShapes.cs" \
    '[SparseModel]' \
    'public partial class' \
    'IReadOnlyList<string> Plugins' \
    'Diff'

check_sample "ui-frameworks" "docs/explanation/ui-frameworks.md" "${docs_fixture_dir}/UiFrameworks.cs" \
    'CreateEditSession' \
    'session.Observable' \
    'PropertyChanged' \
    'CreateChangeSet'

# Registry verification covers README and docs, including fence guards,
# fixture assertions, and JSON specimens.
if ! python3 "$(dirname "$0")/check-docs-samples.py" --registry "$(dirname "$0")/docs-samples-registry.tsv"; then
    echo "Docs sample drift: see mismatches above." >&2
    exit 1
fi

check_sample "blazor" "docs/explanation/ui-frameworks.md" "${blazor_fixture_dir}/Program.cs" \
    'CreateEditSession' \
    'HasChanges' \
    'CreateChangeSet' \
    'CreatePatch' \
    'AcceptChanges' \
    'EditContext' \
    'CreateValidationStore' \
    'session.Field(' \
    'AddValidationError'

# Internal link and anchor validation. Files are discovered
# rather than enumerated, so new guides (including Descriptor, architecture,
# and benchmark pages) are covered automatically.
mapfile -t docs_files < <(find docs -name '*.md' | sort)
if ! python3 "$(dirname "$0")/check-docs-links.py" "$(dirname "$0")/../../../" \
    README.md "${docs_files[@]}"; then
    echo "Docs link check failed; see broken links above." >&2
    exit 1
fi

# Compile and run the canonical fixtures against the packed packages.
package="$(ls "${package_directory}"/SparseTypes.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseTypes package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseTypes\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
blazor_package="$(ls "${package_directory}"/SparseTypes.Blazor.*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${blazor_package}" ]]; then
    echo "No SparseTypes.Blazor package found in '${package_directory}'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

# Use a fresh cache to avoid stale packages with the same version.
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
        -p:SparseTypesPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${docs_fixture_dir}/obj/project.assets.json" "SparseTypes" "sparsetypes"
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
        -p:SparseTypesPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${blazor_fixture_dir}/obj/project.assets.json" "SparseTypes" "sparsetypes"
    assert_candidate_resolved "${blazor_fixture_dir}/obj/project.assets.json" "SparseTypes.Blazor" "sparsetypes.blazor"
    output="$(dotnet "${blazor_fixture_dir}/bin/Release/${framework}/${blazor_fixture_dll}" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${blazor_success_marker}"* ]]; then
        echo "Blazor samples fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

echo "Docs samples verified (SparseTypes ${version})."
