#!/usr/bin/env bash
# Verifies packed SparseFragments NuGet package integrity: expected package
# IDs, target assets, and analyzer/build assets that packing can silently omit.
#
# Two packages ship (#46): SparseFragments (core runtime + generator analyzer)
# and SparseFragments.Blazor (Blazor edit sessions).
# Generator.Shared is an internal-only source directory and is never packed
# (#21). The Blazor package intentionally ships no README: it opts out of the
# inherited PackageReadmeFile (#37), so verification asserts the nupkg neither
# contains a README entry nor references one from its nuspec.
#
# Usage: verify-packages.sh <package-directory>
set -euo pipefail

package_directory="${1:-}"
if [[ -z "${package_directory}" ]]; then
    echo "Usage: $(basename "$0") <package-directory>" >&2
    exit 2
fi

if [[ ! -d "${package_directory}" ]]; then
    echo "Package directory '${package_directory}' does not exist." >&2
    exit 1
fi

# Each shipped package must carry every asset in the target policy.
declare -A portable_package_assets=(
    [SparseFragments]="netstandard2.0"
    [SparseFragments.Blazor]="net8.0 net10.0"
)

expected_package_ids=(
    "${!portable_package_assets[@]}"
)

package_files=()
while IFS= read -r package_file; do
    package_files+=("${package_file}")
done < <(find "${package_directory}" -maxdepth 1 -type f -name '*.nupkg' -print | sort)

if [[ ${#package_files[@]} -ne ${#expected_package_ids[@]} ]]; then
    echo "Expected ${#expected_package_ids[@]} NuGet packages, found ${#package_files[@]} in '${package_directory}'." >&2
    exit 1
fi

require_entry() {
    local entry="$1"
    local size
    size="$(unzip -p "${package_file}" "${entry}" 2>/dev/null | wc -c | tr -d '[:space:]')"
    if [[ -z "${size}" || "${size}" -eq 0 ]]; then
        echo "Package '${package_id}' is missing a non-empty entry '${entry}'." >&2
        exit 1
    fi
}

require_readme() {
    local entry="$1"
    require_entry "${entry}"
    if ! grep -q -F "<readme>${entry}</readme>" <<<"${nuspec}"; then
        echo "Package '${package_id}' ships '${entry}' but its .nuspec does not reference it via <readme>." >&2
        exit 1
    fi
}

require_no_readme() {
    if grep -q -i -E '^README\.md$' <<<"${entries}"; then
        echo "Package '${package_id}' must not ship a README.md entry: it opts out of PackageReadmeFile (#37, #46)." >&2
        exit 1
    fi
    if grep -q -i -F '<readme>' <<<"${nuspec}"; then
        echo "Package '${package_id}' must not reference a <readme> in its .nuspec: it opts out of PackageReadmeFile (#37, #46)." >&2
        exit 1
    fi
}

require_dependency() {
    local dependency_id="$1"
    if ! grep -q -F "<dependency id=\"${dependency_id}\"" <<<"${nuspec}"; then
        echo "Package '${package_id}' is missing expected dependency '${dependency_id}'." >&2
        exit 1
    fi
}

declare -A found_package_ids=()

for package_file in "${package_files[@]}"; do
    entries="$(unzip -Z1 "${package_file}")"

    nuspec_count="$(grep -c -i '\.nuspec$' <<<"${entries}" || true)"
    if [[ "${nuspec_count}" -ne 1 ]]; then
        echo "Package '$(basename "${package_file}")' must contain exactly one .nuspec file." >&2
        exit 1
    fi

    nuspec="$(unzip -p "${package_file}" '*.nuspec')"
    package_id="$(sed -n 's/.*<id>\([^<]*\)<\/id>.*/\1/p' <<<"${nuspec}" | head -n 1)"
    if [[ -z "${package_id}" ]]; then
        echo "Package '$(basename "${package_file}")' has no package ID in its .nuspec metadata." >&2
        exit 1
    fi

    if [[ -n "${found_package_ids[${package_id}]:-}" ]]; then
        echo "Package ID '${package_id}' appears more than once in '${package_directory}'." >&2
        exit 1
    fi
    found_package_ids[${package_id}]=1

    case "${package_id}" in
        SparseFragments)
            require_entry 'analyzers/dotnet/cs/SparseFragments.Generator.dll'
            require_entry 'lib/netstandard2.0/SparseFragments.dll'
            require_readme 'README.md'
            ;;
        SparseFragments.Blazor)
            require_entry 'lib/net8.0/SparseFragments.Blazor.dll'
            require_entry 'lib/net10.0/SparseFragments.Blazor.dll'
            require_no_readme
            require_dependency 'SparseFragments'
            ;;
    esac
done

missing_package_ids=()
for expected_id in "${expected_package_ids[@]}"; do
    if [[ -z "${found_package_ids[${expected_id}]:-}" ]]; then
        missing_package_ids+=("${expected_id}")
    fi
done
if [[ ${#missing_package_ids[@]} -gt 0 ]]; then
    echo "Missing NuGet package IDs: ${missing_package_ids[*]}." >&2
    exit 1
fi

unexpected_package_ids=()
for found_id in "${!found_package_ids[@]}"; do
    if [[ -z "${portable_package_assets[${found_id}]:-}" ]]; then
        unexpected_package_ids+=("${found_id}")
    fi
done
if [[ ${#unexpected_package_ids[@]} -gt 0 ]]; then
    echo "Unexpected NuGet package IDs: ${unexpected_package_ids[*]}." >&2
    exit 1
fi

echo "Verified ${#found_package_ids[@]} NuGet packages and their expected target assets."
