#!/usr/bin/env bash
# Verifies the standalone SparseFragments README samples (#315).
#
# The canonical compile-checked source is
# tests/fixtures/consumers/package-sparse-readme/Program.cs, which mirrors the
# Usage sections of README.md (model shape,
# sparse construction, nested fragments, merge, typed patch, deep clone, and
# one ChangeSet JSON round-trip). This script guards against drift
# between the README and that canonical source, then builds and runs the
# fixture against the packed packages so CI fails when the public generated
# API breaks the documented samples.
#
# Usage: verify-sparsefragments-readme.sh <package-directory> [readme-path]
set -euo pipefail

package_directory="${1:-}"
readme_path="${2:-README.md}"
fixture_dir="tests/fixtures/consumers/package-sparse-readme"
fixture_csproj="${fixture_dir}/PackageSparse.Readme.Consumer.csproj"
fixture_program="${fixture_dir}/Program.cs"

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory> [readme-path]" >&2
    exit 2
fi
if [[ ! -f "${readme_path}" ]]; then
    echo "README file '${readme_path}' does not exist." >&2
    exit 1
fi
if [[ ! -f "${fixture_csproj}" || ! -f "${fixture_program}" ]]; then
    echo "Canonical README fixture missing under '${fixture_dir}'." >&2
    exit 1
fi

# 1. The merge sample constructs Child.Fragment directly, so Child must expose
# generated APIs. Since #329 reachable partial nested models auto-generate
# Fragment/Patch without requiring [SparseFragmentModel], Child must stay a
# partial class in both files (not necessarily decorated).
if ! grep -E -q 'public partial class Child' "${readme_path}"; then
    echo "README must declare 'Child' as 'public partial class Child': the merge sample constructs 'new Child.Fragment { ... }' directly." >&2
    exit 1
fi
if ! grep -E -q 'public partial class Child' "${fixture_program}"; then
    echo "Canonical fixture must declare 'Child' as 'public partial class Child'." >&2
    exit 1
fi

# 2. Representative API tokens must appear in both the README and the
# canonical fixture so the fixture cannot silently drift from the docs.
required_tokens=(
    '[SparseFragmentModel]'
    'new Settings.Fragment'
    'new Child.Fragment'
    '.Merge('
    'Settings.Fragment.Diff'
    'new Settings.Patch'
    '.Unset()'
    '.SetNull()'
    'DeepClone'
    'ToBuilder'
    'ChangeSet.Between'
    'JsonSerializer.Serialize'
)
for token in "${required_tokens[@]}"; do
    if ! grep -F -q "${token}" "${readme_path}"; then
        echo "README '${readme_path}' is missing required sample token '${token}'." >&2
        exit 1
    fi
    if ! grep -F -q "${token}" "${fixture_program}"; then
        echo "Canonical fixture '${fixture_program}' is missing token '${token}' present in the README; keep them in sync." >&2
        exit 1
    fi
done

# 3. Compile and run the canonical fixture against the packed packages.
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
feed="$(realpath "${package_directory}")"

# Isolated restore (#79): restore into a fresh global-packages directory so a
# stale same-version entry in ~/.nuget/packages cannot satisfy restore without
# proving the fresh nupkg is consumable. External feeds stay available; the
# local feed is appended via RestoreAdditionalProjectSources.
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${isolated_packages}"' EXIT
if command -v cygpath >/dev/null 2>&1; then
    export NUGET_PACKAGES="$(cygpath -w "${isolated_packages}")"
else
    export NUGET_PACKAGES="${isolated_packages}"
fi
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"

for framework in net8.0 net10.0; do
    dotnet build "${fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseFragmentsPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
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
    output="$(dotnet "${fixture_dir}/bin/Release/${framework}/PackageSparse.Readme.Consumer.dll" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"SparseFragments README consumer passed."* ]]; then
        echo "SparseFragments README fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

echo "SparseFragments README samples verified (SparseFragments ${version})."
