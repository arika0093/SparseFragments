#!/usr/bin/env bash
# Verifies the .NET Framework 4.8 packed-package consumer (#49).
#
# The fixture installs the release-candidate SparseFragments package from a
# local feed (no ProjectReference fallback), builds for net48, executes the
# resulting .exe under .NET Framework, and must reach its success marker.
# This job is Windows-only; Blazor stays excluded from net48.
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-sparse-netfx"
fixture_csproj="${fixture_dir}/PackageSparse.NetFx.Consumer.csproj"
fixture_exe="PackageSparse.NetFx.Consumer.exe"
success_marker="SparseFragments net48 consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi

# The core glob pins the version digit so the Blazor package
# (SparseFragments.Blazor.*.nupkg) never resolves here.
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
feed="$(realpath "${package_directory}")"

# Isolated restore (#79, adopted per #81): restore into a fresh global-packages
# directory so a stale same-version entry in the shared cache cannot satisfy
# restore without proving the fresh nupkg is consumable. External feeds stay
# available; the local feed is appended via RestoreAdditionalProjectSources.
# cygpath keeps this working under Git Bash on Windows (native dotnet.exe
# needs a Windows path); on Linux the POSIX path is used directly.
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
    -p:SparseFragmentsPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"

# Post-restore assertion: the candidate must have resolved at this version and
# been extracted into the isolated global-packages dir (proves the fresh nupkg
# was consumed, not a stale cache entry).
assets_file="${fixture_dir}/obj/project.assets.json"
if [[ ! -f "${assets_file}" ]]; then
    echo "Restore did not produce '${assets_file}'." >&2
    exit 1
fi
if ! grep -F -q "SparseFragments/${version}" "${assets_file}"; then
    echo "Isolated restore did not resolve 'SparseFragments/${version}' (see '${assets_file}')." >&2
    exit 1
fi
if [[ ! -d "${isolated_packages}/sparsefragments/${lower_version}" ]]; then
    echo "Isolated global-packages has no 'sparsefragments/${lower_version}': restore did not extract the candidate nupkg." >&2
    exit 1
fi
output="$("$(realpath "${fixture_dir}")/bin/Release/net48/${fixture_exe}" 2>&1)"
echo "${output}"
if [[ "${output}" != *"${success_marker}"* ]]; then
    echo "SparseFragments net48 fixture did not reach its success marker." >&2
    exit 1
fi

echo "SparseFragments net48 consumer verified (SparseFragments ${version})."
