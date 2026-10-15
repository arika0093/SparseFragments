#!/usr/bin/env bash
# Verifies the packed SparseTypes NuGet package as users consume it.
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-sparse"
fixture_csproj="${fixture_dir}/PackageSparse.Consumer.csproj"
fixture_dll="PackageSparse.Consumer.dll"
success_marker="SparseTypes packed consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${fixture_csproj}" ]]; then
    echo "Package consumer fixture missing at '${fixture_csproj}'." >&2
    exit 1
fi

# The core glob pins the version digit so the Blazor package
# (SparseTypes.Blazor.*.nupkg, ) never resolves here.
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
    if [[ ! -f "${assets_file}" ]]; then
        echo "Restore did not produce '${assets_file}'." >&2
        exit 1
    fi
    if ! grep -F -q "SparseTypes/${version}" "${assets_file}"; then
        echo "Isolated restore did not resolve 'SparseTypes/${version}' (see '${assets_file}')." >&2
        exit 1
    fi
    if [[ ! -d "${isolated_packages}/sparsetypes/${lower_version}" ]]; then
        echo "Isolated global-packages has no 'sparsetypes/${lower_version}': restore did not extract the candidate nupkg." >&2
        exit 1
    fi
}

for framework in net8.0 net10.0; do
    dotnet build "${fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseTypesPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${fixture_dir}/obj/project.assets.json"
    output="$(dotnet "${fixture_dir}/bin/Release/${framework}/${fixture_dll}" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${success_marker}"* ]]; then
        echo "Package consumer fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

netstandard_csproj="tests/fixtures/consumers/package-sparse-netstandard/PackageSparse.NetStandard.Consumer.csproj"
if [[ ! -f "${netstandard_csproj}" ]]; then
    echo "netstandard2.0 consumer fixture missing at '${netstandard_csproj}'." >&2
    exit 1
fi
dotnet build "${netstandard_csproj}" \
    --configuration Release \
    -p:SparseTypesPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"
assert_candidate_resolved "tests/fixtures/consumers/package-sparse-netstandard/obj/project.assets.json"

# Compile a netstandard2.0 consumer without System.Text.Json.
# Any generated JSON dependency must fail this build.
nojson_dir="tests/fixtures/consumers/package-sparse-nojson"
nojson_csproj="${nojson_dir}/PackageSparse.NoJson.Consumer.csproj"
if [[ ! -f "${nojson_csproj}" ]]; then
    echo "Serializer-free consumer fixture missing at '${nojson_csproj}'." >&2
    exit 1
fi
if grep -r -F 'Include="System.Text.Json"' "${nojson_dir}" --include='*.csproj' >/dev/null; then
    echo "Serializer-free fixture must not reference the System.Text.Json package." >&2
    exit 1
fi
if grep -r -F "using System.Text.Json" "${nojson_dir}" --include='*.cs' >/dev/null; then
    echo "Serializer-free fixture must not use System.Text.Json APIs." >&2
    exit 1
fi
dotnet build "${nojson_csproj}" \
    --configuration Release \
    -p:SparseTypesPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"
assert_candidate_resolved "${nojson_dir}/obj/project.assets.json"
if grep -F -q "System.Text.Json" "${nojson_dir}/obj/project.assets.json"; then
    echo "Serializer-free restore closure must not contain System.Text.Json." >&2
    exit 1
fi

echo "SparseTypes packed package consumer verified (SparseTypes ${version})."
