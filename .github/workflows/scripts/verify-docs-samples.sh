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

# 1b. Exact sample verification (#68). Annotated fenced blocks in the guides
# must match their canonical fixture regions exactly (after normalization),
# so the documented code compiles and produces the documented result.
check_block() {
    local topic="$1"
    local guide="$2"
    local fixture_source="$3"
    shift 3
    if ! python3 "$(dirname "$0")/check-docs-samples.py" "${guide}" "${fixture_source}" "$@"; then
        echo "Docs sample drift [${topic}]: see mismatches above." >&2
        exit 1
    fi
}

check_block "core" "docs/fragments-and-patches.md" "${docs_fixture_dir}/VerifiedSamples.cs" \
    core-models core-create core-layering core-diff core-builder core-patch core-between \
    core-changeset core-typed core-nested-models core-nested core-algebra core-serialization \
    core-change-payload-models core-change-payload
check_block "keyed" "docs/keyed-collections.md" "${docs_fixture_dir}/VerifiedSamples.cs" \
    keyed-first-models keyed-first keyed-typed
check_block "merge-compare" "docs/merge-strategies.md" "${docs_fixture_dir}/MergeStrategies.cs" \
    merge-compare-models merge-compare
check_block "rebase" "docs/rebase.md" "${docs_fixture_dir}/VerifiedSamples.cs" \
    rebase-first-models rebase-first rebase-applied rebase-conflict rebase-presence rebase-policy-models rebase-policy rebase-redacted rebase-e2e rebase-server-save
check_block "rebase-mixed" "docs/rebase.md" "${docs_fixture_dir}/RebaseSamples.cs" \
    mixed-apply rebase-in-place
check_block "payload" "docs/change-payload.md" "${docs_fixture_dir}/ChangePayloadDocs.cs" \
    payload-models payload-scalar-set payload-explicit-null payload-remove \
    payload-nested payload-keyed payload-command payload-conversions payload-mixed \
    payload-invert payload-version
check_block "ui-session" "docs/ui-frameworks.md" "${blazor_fixture_dir}/Program.cs" \
    ui-session-models ui-session ui-blazor-form ui-wpf-session
check_block "ui-flows" "docs/ui-frameworks.md" "${docs_fixture_dir}/UiFrameworks.cs" \
    ui-accept-flow ui-reload ui-reload-conflict ui-revert
check_block "descriptors" "docs/descriptors.md" "${docs_fixture_dir}/UiFrameworks.cs" \
    ui-descriptor-models ui-descriptor-first ui-descriptor-changes

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

# 1c. Internal link and anchor validation (#68).
if ! python3 "$(dirname "$0")/check-docs-links.py" "$(dirname "$0")/../../../" \
    README.md \
    docs/merge-strategies.md \
    docs/keyed-collections.md \
    docs/rebase.md \
    docs/cloning-and-ownership.md \
    docs/model-shapes.md \
    docs/ui-frameworks.md \
    docs/descriptors.md \
    docs/fragments-and-patches.md \
    docs/change-payload.md \
    docs/analyzer.md \
    docs/architecture/README.md \
    docs/architecture/three-layer-ownership.md \
    docs/architecture/semantic-roles.md \
    docs/architecture/runtime-ownership-audit.md; then
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
