#!/usr/bin/env bash
# Verifies the packed SparseFragments.Generator.Shared source-only package (#70).
#
# Builds tests/fixtures/consumers/package-shared-generator (a small Roslyn
# generator using representative Shared APIs) against the packed Shared nupkg
# from a local feed. The fixture has no sibling-source fallback: it restores
# SparseFragments.Generator.Shared only via SparseSharedPackageVersion, and the
# isolated rebuild runs in a temp directory with no access to the sibling
# src/SparseFragments.Generator.Shared source tree. Build success therefore
# proves the Shared sources are compiled from the package (contentFiles +
# build/SparseFragments.Generator.Shared.props) rather than from a
# repository-relative fallback. The probe touches representative categories
# (model/collection analysis, IR/model types, naming, emitter/helpers) so a
# missing Shared source fails compilation.
#
# Usage: verify-package-shared-consumer.sh <package-directory>
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-shared-generator"
fixture_csproj="${fixture_dir}/PackageShared.Generator.csproj"
fixture_source="${fixture_dir}/ProbeGenerator.cs"

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${fixture_csproj}" || ! -f "${fixture_source}" ]]; then
    echo "Shared package consumer fixture missing under '${fixture_dir}'." >&2
    exit 1
fi

package="$(ls "${package_directory}"/SparseFragments.Generator.Shared.*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseFragments.Generator.Shared package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseFragments\.Generator\.Shared\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

# 1. The fixture must be package-only: no sibling csproj fallback and no
# repository-relative Compile glob. This keeps the in-repo direct-Compile path
# canonical for SparseFragments.Generator while the package serves downstream.
if grep -F -q 'SparseFragments.Generator.Shared.csproj' "${fixture_csproj}"; then
    echo "Shared consumer fixture must not reference the sibling Shared csproj; it is package-only." >&2
    exit 1
fi
if grep -E -q 'SparseFragments\.Generator\.Shared[/\\]' "${fixture_csproj}"; then
    echo "Shared consumer fixture must not contain a repository-relative Shared path; it is package-only." >&2
    exit 1
fi
if ! grep -F -q 'PackageReference Include="SparseFragments.Generator.Shared"' "${fixture_csproj}"; then
    echo "Shared consumer fixture must restore 'SparseFragments.Generator.Shared' via PackageReference." >&2
    exit 1
fi
if ! grep -F -q 'PrivateAssets="all"' "${fixture_csproj}"; then
    echo "Shared consumer fixture must reference the Shared package with PrivateAssets=\"all\"." >&2
    exit 1
fi

# 2. The probe must cover representative Shared categories, not a trivial
# single helper, so omitted Shared sources fail compilation.
required_probe_tokens=(
    'SparseCollectionAnalyzer'
    'SparseKeyAnalyzer'
    'SparseModelAnalyzer'
    'SparseTypeModel'
    'SparseCollectionInfo'
    'SparseNaming'
    'SparseWellKnownNames'
    'SparseJsonNaming'
    'SharedIndentedBuilder'
    'SparseRebaseOptionEmitter'
    'SparseChangeSetMemberRebaseEmitter'
    'SparseFragmentPatchCollectionRebaseEmitter'
    'RebasePolicyFieldPrefix'
    'SparseEmissionFeatures'
    'SparseMemberTransport'
    'SparseMemberPolicy'
    'SparseRebasePolicy'
    'SparseWriteContract'
    'SparseWriteMember'
    'SparseFragmentEmitHelpers'
    'SparseFragmentExpressions'
    'SparseObservableEmitter'
)
for token in "${required_probe_tokens[@]}"; do
    if ! grep -F -q "${token}" "${fixture_source}"; then
        echo "Shared consumer probe '${fixture_source}' must reference '${token}' (representative category coverage)." >&2
        exit 1
    fi
done

# 3. Isolated rebuild: copy the fixture to a temp directory with no access to
# the sibling Shared source tree, restore the Shared package from the local
# feed into a fresh global-packages directory (#79) so a stale same-version
# cache entry cannot satisfy restore, and compile the generator. External
# feeds stay available.
work="$(mktemp -d)"
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${work}" "${isolated_packages}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
cp "${fixture_csproj}" "${work}/"
cp "${fixture_source}" "${work}/"

dotnet build "${work}/PackageShared.Generator.csproj" \
    --configuration Release \
    -p:SparseSharedPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

dll="${work}/bin/Release/netstandard2.0/PackageShared.Generator.dll"
if [[ ! -f "${dll}" ]]; then
    echo "Shared consumer fixture did not produce '${dll}'." >&2
    exit 1
fi

# 4. The isolated restore must have resolved the Shared package (not a
# sibling fallback): the generated assets file records the exact version, and
# the fresh global-packages dir must contain the extracted candidate (proves
# the nupkg was consumed, not a stale cache entry).
if ! grep -F -q "SparseFragments.Generator.Shared/${version}" "${work}/obj/project.assets.json"; then
    echo "Isolated restore did not resolve 'SparseFragments.Generator.Shared/${version}' from the local feed." >&2
    exit 1
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"
if [[ ! -d "${isolated_packages}/sparsefragments.generator.shared/${lower_version}" ]]; then
    echo "Isolated global-packages has no 'sparsefragments.generator.shared/${lower_version}': restore did not extract the candidate nupkg." >&2
    exit 1
fi

echo "SparseFragments.Generator.Shared packed package consumer verified (${version})."
