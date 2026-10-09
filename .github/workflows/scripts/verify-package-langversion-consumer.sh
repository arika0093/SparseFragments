#!/usr/bin/env bash
# Verifies the C# 9.0 minimum language version for generated-code consumers (#116).
#
# Builds tests/fixtures/consumers/package-sparse-langversion against the freshly
# packed SparseFragments package from a local feed (no ProjectReference fallback)
# with an isolated global-packages directory (no stale cache). The fixture pins
# <LangVersion>9.0</LangVersion> explicitly instead of inheriting preview from
# tests/Directory.Build.props, covers ordinary and nested partial models,
# nullable members, init handling, keyed/dictionary transitions and ChangeSet
# JSON payloads, and targets netstandard2.0 (build-only), net48, net8.0 and
# net10.0. Runnable frameworks must reach the success marker (the net48 .exe
# runs on Windows; elsewhere net48 is a compile-only gate).
#
# A negative probe then rebuilds with LangVersion=8.0 and requires an
# understandable failure that names language version 9.0, proving C# 8.0 is not
# silently supported.
#
# Usage: verify-package-langversion-consumer.sh <package-directory>
set -euo pipefail

package_directory="${1:-}"
fixture_dir="tests/fixtures/consumers/package-sparse-langversion"
fixture_csproj="${fixture_dir}/PackageSparse.LangVersion.Consumer.csproj"
success_marker="SparseFragments C# 9.0 consumer passed."

if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi
if [[ ! -f "${fixture_csproj}" ]]; then
    echo "Language-version consumer fixture missing at '${fixture_csproj}'." >&2
    exit 1
fi

# The gate proves nothing if the fixture inherits preview from
# tests/Directory.Build.props, so the explicit pin is asserted here rather
# than assumed from the csproj contents alone.
if ! grep -F -q "<LangVersion>9.0</LangVersion>" "${fixture_csproj}"; then
    echo "Language-version fixture does not pin '<LangVersion>9.0</LangVersion>' explicitly." >&2
    exit 1
fi
effective_langversion="$(dotnet msbuild "${fixture_csproj}" -getProperty:LangVersion -nologo -v q 2>/dev/null | tr -d '[:space:]')"
if [[ "${effective_langversion}" != "9.0" ]]; then
    echo "Language-version fixture resolves to LangVersion='${effective_langversion}', expected '9.0' (preview inheritance?)." >&2
    exit 1
fi
echo "Language-version fixture pins LangVersion=9.0 (effective: ${effective_langversion})."

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

assert_candidate_resolved() {
    local assets_file="$1"
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
}

for framework in netstandard2.0 net48 net8.0 net10.0; do
    dotnet build "${fixture_csproj}" \
        --configuration Release --framework "${framework}" \
        -p:SparseFragmentsPackageVersion="${version}" \
        -p:RestoreAdditionalProjectSources="${feed}"
    assert_candidate_resolved "${fixture_dir}/obj/project.assets.json"
done

for framework in net8.0 net10.0; do
    output="$(dotnet "${fixture_dir}/bin/Release/${framework}/PackageSparse.LangVersion.Consumer.dll" 2>&1)"
    echo "${output}"
    if [[ "${output}" != *"${success_marker}"* ]]; then
        echo "Language-version fixture did not reach its success marker on '${framework}'." >&2
        exit 1
    fi
done

# The net48 .exe only executes on Windows; elsewhere the successful net48
# compile above is the C# 9.0 gate for that TFM.
case "$(uname -s 2>/dev/null || echo unknown)" in
    MINGW* | MSYS* | CYGWIN* | Windows_NT)
        output="$("$(realpath "${fixture_dir}")/bin/Release/net48/PackageSparse.LangVersion.Consumer.exe" 2>&1)"
        echo "${output}"
        if [[ "${output}" != *"${success_marker}"* ]]; then
            echo "Language-version fixture did not reach its success marker on 'net48'." >&2
            exit 1
        fi
        ;;
    *)
        echo "Skipping net48 execution on non-Windows host (net48 compile gate passed)."
        ;;
esac

# Negative probe: C# 8.0 must fail with an understandable diagnostic naming
# language version 9.0. The packed package path matters here: with a
# ProjectReference fallback, -p:LangVersion would also flow into the generator
# build itself, so this probe only runs against the packed nupkg.
set +e
negative_output="$(dotnet build "${fixture_csproj}" \
    --configuration Release --framework net8.0 \
    -p:SparseFragmentsPackageVersion="${version}" \
    -p:RestoreAdditionalProjectSources="${feed}" \
    -p:LangVersion=8.0 2>&1)"
negative_status=$?
set -e
if [[ "${negative_status}" -eq 0 ]]; then
    echo "Negative probe unexpectedly succeeded with LangVersion=8.0; C# 8.0 must not compile generated code." >&2
    exit 1
fi
if ! grep -F -q "9.0" <<<"${negative_output}"; then
    echo "Negative probe failed without naming language version 9.0; the failure mode is not understandable." >&2
    echo "${negative_output}" | tail -n 20 >&2
    exit 1
fi
echo "Negative probe: LangVersion=8.0 fails as expected (names language version 9.0)."

echo "SparseFragments C# 9.0 language-version consumer verified (SparseFragments ${version})."
