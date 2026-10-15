#!/usr/bin/env bash
# Stale same-version NuGet candidate isolation regression.
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
lower_version="$(printf '%s' "${version}" | tr '[:upper:]' '[:lower:]')"

candidate_sha512="$(python3 -c "import base64,hashlib,sys; print(base64.b64encode(hashlib.sha512(open(sys.argv[1], 'rb').read()).digest()).decode())" "${package}")"
if [[ -z "${candidate_sha512}" ]]; then
    echo "Could not compute candidate package hash." >&2
    exit 1
fi

work_root="$(mktemp -d)"
polluted_packages="$(mktemp -d)"
isolated_packages="$(mktemp -d)"
trap 'rm -rf "${work_root}" "${polluted_packages}" "${isolated_packages}"' EXIT

to_native_path() {
    if command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        printf '%s' "$1"
    fi
}

# Build a distinguishable stale package with the same ID/version.
stale_src="${work_root}/stale-src"
stale_feed="${work_root}/stale-feed"
mkdir -p "${stale_src}" "${stale_feed}"
cat > "${stale_src}/StaleSparseTypes.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <PackageId>SparseTypes</PackageId>
    <Version>${version}</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <IsPackable>true</IsPackable>
    <IncludeBuildOutput>true</IncludeBuildOutput>
    <Description>Stale same-version decoy for candidate isolation regression.</Description>
  </PropertyGroup>
</Project>
EOF
cat > "${stale_src}/StaleCandidateMarker.cs" <<'EOF'
public static class StaleCandidateMarker
{
    public const string Value = "STALE_SAME_VERSION_DECOY";
}
EOF
dotnet pack "${stale_src}/StaleSparseTypes.csproj" \
    --configuration Release --output "${stale_feed}" --verbosity quiet
stale_package="$(ls "${stale_feed}"/SparseTypes.[0-9]*.nupkg 2>/dev/null | head -n 1 || true)"
if [[ -z "${stale_package}" ]]; then
    echo "Stale decoy pack produced no nupkg in '${stale_feed}'." >&2
    exit 1
fi
stale_sha512="$(python3 -c "import base64,hashlib,sys; print(base64.b64encode(hashlib.sha512(open(sys.argv[1], 'rb').read()).digest()).decode())" "${stale_package}")"
if [[ "${stale_sha512}" == "${candidate_sha512}" ]]; then
    echo "Stale decoy is byte-identical to the candidate; the regression cannot distinguish them." >&2
    exit 1
fi
echo "Stale decoy built at same ID/version '${version}' with a distinct hash."

# Simulate a polluted global cache holding the stale same-version package.
export NUGET_PACKAGES="$(to_native_path "${polluted_packages}")"
dotnet restore "${fixture_csproj}" \
    -p:SparseTypesPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="$(to_native_path "${stale_feed}")" \
    --verbosity quiet
if [[ ! -d "${polluted_packages}/sparsetypes/${lower_version}" ]]; then
    echo "Polluted global-packages has no 'sparsetypes/${lower_version}': stale seeding failed." >&2
    exit 1
fi
polluted_sha512="$(cat "${polluted_packages}/sparsetypes/${lower_version}/sparsetypes.${lower_version}.nupkg.sha512" 2>/dev/null || true)"
if [[ -z "${polluted_sha512}" ]]; then
    echo "Polluted cache entry has no recorded nupkg hash." >&2
    exit 1
fi
# The legacy existence-only check would pass against this polluted cache, which
# is exactly the false positive closed.
echo "Polluted cache holds a same-version entry (existence-only check would pass)."
if [[ "${polluted_sha512}" == "${candidate_sha512}" ]]; then
    echo "Polluted cache unexpectedly matches the candidate hash; stale seeding failed." >&2
    exit 1
fi
echo "Content check rejects the stale entry (hash mismatch vs candidate)."

# Run the real candidate-consumer path with a fresh isolated cache and prove
# the extracted bytes are the current local-feed candidate, not the stale one.
export NUGET_PACKAGES="$(to_native_path "${isolated_packages}")"
dotnet build "${fixture_csproj}" \
    --configuration Release --framework net8.0 \
    -p:SparseTypesPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}"
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
isolated_sha512="$(cat "${isolated_packages}/sparsetypes/${lower_version}/sparsetypes.${lower_version}.nupkg.sha512" 2>/dev/null || true)"
if [[ "${isolated_sha512}" != "${candidate_sha512}" ]]; then
    echo "Isolated cache hash does not match the candidate nupkg: stale bytes may have been consumed." >&2
    echo "candidate: ${candidate_sha512}" >&2
    echo "isolated:  ${isolated_sha512}" >&2
    exit 1
fi
if grep -r -F -q "STALE_SAME_VERSION_DECOY" "${isolated_packages}/sparsetypes/${lower_version}" 2>/dev/null; then
    echo "Isolated cache contains the stale decoy marker." >&2
    exit 1
fi
output="$(dotnet "${fixture_dir}/bin/Release/net8.0/${fixture_dll}" 2>&1)"
echo "${output}"
if [[ "${output}" != *"${success_marker}"* ]]; then
    echo "Package consumer fixture did not reach its success marker on 'net8.0'." >&2
    exit 1
fi

echo "Stale same-version isolation verified (SparseTypes ${version})."
