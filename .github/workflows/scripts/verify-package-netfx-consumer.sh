#!/usr/bin/env bash
# Verifies the.NET Framework 4.8 packed-package consumer.
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-sparse-netfx"
fixture_csproj="${fixture_dir}/PackageSparse.NetFx.Consumer.csproj"
fixture_exe="PackageSparse.NetFx.Consumer.exe"
success_marker="SparseTypes net48 consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi

# The core glob pins the version digit so the Blazor package
# (SparseTypes.Blazor.*.nupkg) never resolves here.
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

# Restore from the local feed into a fresh package cache to avoid stale entries.
# Convert the feed path for native dotnet.exe under Git Bash on Windows.
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${isolated_packages}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"

dotnet build "${fixture_csproj}" \
    --configuration Release \
    -p:SparseTypesPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

# Verify the version and extraction in the isolated cache.
assets_file="${fixture_dir}/obj/project.assets.json"
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
output="$("$(realpath "${fixture_dir}")/bin/Release/net48/${fixture_exe}" 2>&1)"
echo "${output}"
if [[ "${output}" != *"${success_marker}"* ]]; then
    echo "SparseTypes net48 fixture did not reach its success marker." >&2
    exit 1
fi

echo "SparseTypes net48 consumer verified (SparseTypes ${version})."
