#!/usr/bin/env bash
# Verifies the packed SparseTypes.Generator.Shared source-only package.
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-shared-generator"
fixture_csproj="${fixture_dir}/PackageShared.Generator.csproj"
fixture_source="${fixture_dir}/ProbeGenerator.cs"
consumer_csproj="${fixture_dir}/PackageShared.Consumer.csproj"
consumer_source="${fixture_dir}/Consumer.cs"

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${fixture_csproj}" || ! -f "${fixture_source}" || ! -f "${consumer_csproj}" || ! -f "${consumer_source}" ]]; then
    echo "Shared package consumer fixture missing under '${fixture_dir}'." >&2
    exit 1
fi

package="$(ls "${package_directory}"/SparseTypes.Generator.Shared.*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${package}" ]]; then
    echo "No SparseTypes.Generator.Shared package found in '${package_directory}'." >&2
    exit 1
fi
version="$(basename "${package}" | sed -E 's/^SparseTypes\.Generator\.Shared\.(.+)\.nupkg$/\1/')"
if [[ -z "${version}" ]]; then
    echo "Could not resolve package version from '$(basename "${package}")'." >&2
    exit 1
fi
feed="$(realpath "${package_directory}")"

# The fixture must be package-only: no sibling csproj fallback and no
# repository-relative Compile glob.
if grep -F -q 'SparseTypes.Generator.Shared.csproj' "${fixture_csproj}"; then
    echo "Shared consumer fixture must not reference the sibling Shared csproj; it is package-only." >&2
    exit 1
fi
if grep -E -q 'SparseTypes\.Generator\.Shared[/\\]' "${fixture_csproj}"; then
    echo "Shared consumer fixture must not contain a repository-relative Shared path; it is package-only." >&2
    exit 1
fi
if ! grep -F -q 'PackageReference Include="SparseTypes.Generator.Shared"' "${fixture_csproj}"; then
    echo "Shared consumer fixture must restore 'SparseTypes.Generator.Shared' via PackageReference." >&2
    exit 1
fi
if ! grep -F -q 'PrivateAssets="all"' "${fixture_csproj}"; then
    echo "Shared consumer fixture must reference the Shared package with PrivateAssets=\"all\"." >&2
    exit 1
fi

# The probe must cover representative Shared categories, not a trivial
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
    'SparseChangeSetEnumeratorEmitter'
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
    'SparseEditSessionEmitter'
    'SparseEditSessionDialect'
    'ChildConflictPathPrefixExpression'
    'RenderCoreSources'
)
for token in "${required_probe_tokens[@]}"; do
    if ! grep -F -q "${token}" "${fixture_source}"; then
        echo "Shared consumer probe '${fixture_source}' must reference '${token}' (representative category coverage)." >&2
        exit 1
    fi
done
for template in SparseEditSessionCore.template SparseEditSessionWithCurrentCore.template; do
    if ! unzip -Z1 "${package}" | grep -F "contentFiles/cs/netstandard2.0/Sessions/${template}" >/dev/null; then
        echo "Shared package is missing the edit-session template '${template}'." >&2
        exit 1
    fi
done

# Rebuild a copied fixture with an isolated cache to prove restore uses this
# package, not a stale cache entry. External feeds remain enabled.
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
mkdir "${work}/consumer"
cp "${consumer_csproj}" "${work}/consumer/"
cp "${consumer_source}" "${work}/consumer/"

dotnet build "${work}/PackageShared.Generator.csproj" \
    --configuration Release \
    -p:SparseSharedPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

# Run the packaged generator in a consumer compilation.
dotnet build "${work}/consumer/PackageShared.Consumer.csproj" \
    --configuration Release \
    -p:SparseSharedPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

dll="${work}/bin/Release/netstandard2.0/PackageShared.Generator.dll"
if [[ ! -f "${dll}" ]]; then
    echo "Shared consumer fixture did not produce '${dll}'." >&2
    exit 1
fi

# The isolated restore must have resolved the Shared package (not a sibling
# fallback).
if ! grep -F -q "SparseTypes.Generator.Shared/${version}" "${work}/obj/project.assets.json"; then
    echo "Isolated restore did not resolve 'SparseTypes.Generator.Shared/${version}' from the local feed." >&2
    exit 1
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"
if [[ ! -d "${isolated_packages}/sparsetypes.generator.shared/${lower_version}" ]]; then
    echo "Isolated global-packages has no 'sparsetypes.generator.shared/${lower_version}': restore did not extract the candidate nupkg." >&2
    exit 1
fi

echo "SparseTypes.Generator.Shared packed package consumer verified (${version})."
