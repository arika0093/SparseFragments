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
# (SparseFragments.Extensions.Blazor.*.nupkg) never resolves here.
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

dotnet build "${fixture_csproj}" \
    --configuration Release \
    -p:SparseFragmentsPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"
output="$("$(realpath "${fixture_dir}")/bin/Release/net48/${fixture_exe}" 2>&1)"
echo "${output}"
if [[ "${output}" != *"${success_marker}"* ]]; then
    echo "SparseFragments net48 fixture did not reach its success marker." >&2
    exit 1
fi

echo "SparseFragments net48 consumer verified (SparseFragments ${version})."
