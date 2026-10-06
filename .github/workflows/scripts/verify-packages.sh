#!/usr/bin/env bash
# Verifies packed SparseFragments NuGet package integrity: expected package
# IDs, target assets, and analyzer/build assets that packing can silently omit.
#
# Three packages ship (#46, #70): SparseFragments (core runtime + generator
# analyzer), SparseFragments.Blazor (Blazor edit sessions), and
# SparseFragments.Generator.Shared (source-only downstream-generator sources).
# The Blazor package ships a dedicated README.Blazor.md via its own
# PackageReadmeFile (#37, #46, #52).
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

# Each shipped lib package must carry every asset in the target policy.
# Generator.Shared is source-only (no lib/) and is tracked separately below.
declare -A portable_package_assets=(
    [SparseFragments]="netstandard2.0"
    [SparseFragments.Blazor]="net8.0 net10.0"
)

expected_package_ids=(
    "${!portable_package_assets[@]}"
    SparseFragments.Generator.Shared
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
            require_readme 'README.Blazor.md'
            require_dependency 'SparseFragments'
            ;;
        SparseFragments.Generator.Shared)
            # Source-only (#70): no lib/ assembly, no legacy content/ copy, no
            # buildTransitive/ propagation. Direct-consumer build logic ships in
            # build/ only and sources arrive via contentFiles with structure.
            require_entry 'build/SparseFragments.Generator.Shared.props'
            require_readme 'README.md'
            if grep -q -i -E '^lib/' <<<"${entries}"; then
                echo "Package '${package_id}' must not ship a lib/ assembly: it is source-only (#70)." >&2
                exit 1
            fi
            if grep -q -E '^buildTransitive/' <<<"${entries}"; then
                echo "Package '${package_id}' must not ship buildTransitive/ assets: build logic is direct-consumer only (#70)." >&2
                exit 1
            fi
            if grep -q -E '^content/' <<<"${entries}"; then
                echo "Package '${package_id}' must not ship a legacy content/ copy: contentFiles is the source transport (#70)." >&2
                exit 1
            fi
            if ! grep -q -F '<developmentDependency>true</developmentDependency>' <<<"${nuspec}"; then
                echo "Package '${package_id}' must mark <developmentDependency>true</developmentDependency> (#70)." >&2
                exit 1
            fi
            if ! grep -q 'contentFiles/cs/netstandard2.0/.*\.cs$' <<<"${entries}"; then
                echo "Package '${package_id}' must pack shared sources under 'contentFiles/cs/netstandard2.0/' (#70)." >&2
                exit 1
            fi
            # Nested structure must be preserved (no flattening hazard): every
            # Shared source lives in a subdirectory, so every contentFiles .cs
            # entry must contain a second path segment.
            while IFS= read -r content_entry; do
                relative="${content_entry#contentFiles/cs/netstandard2.0/}"
                if [[ "${relative}" != */* ]]; then
                    echo "Package '${package_id}' flattens Shared sources: '${content_entry}' has no subdirectory (#70)." >&2
                    exit 1
                fi
            done < <(grep 'contentFiles/cs/netstandard2.0/.*\.cs$' <<<"${entries}")
            # Representative Shared sources across categories (analysis, IR/model,
            # naming, emitter/helper) so an omitted file fails verification.
            for expected_source in \
                'contentFiles/cs/netstandard2.0/Analysis/SparseCollectionAnalyzer.cs' \
                'contentFiles/cs/netstandard2.0/Models/SparseGeneratorModels.cs' \
                'contentFiles/cs/netstandard2.0/Naming/SparseNaming.cs' \
                'contentFiles/cs/netstandard2.0/Naming/SparseWellKnownNames.cs' \
                'contentFiles/cs/netstandard2.0/Infrastructure/SharedIndentedBuilder.cs' \
                'contentFiles/cs/netstandard2.0/Infrastructure/SparseFragmentEmitHelpers.cs' \
                'contentFiles/cs/netstandard2.0/Emitters/SparseObservableEmitter.cs' \
            ; do
                if ! grep -q -F -x "${expected_source}" <<<"${entries}"; then
                    echo "Package '${package_id}' is missing expected Shared source '${expected_source}' (#70)." >&2
                    exit 1
                fi
            done
            # Every Shared .cs in the source tree must be packed (no silent omission).
            shared_source_root="src/SparseFragments.Generator.Shared"
            if [[ -d "${shared_source_root}" ]]; then
                while IFS= read -r source_file; do
                    relative="${source_file#${shared_source_root}/}"
                    expected_entry="contentFiles/cs/netstandard2.0/${relative}"
                    if ! grep -q -F -x "${expected_entry}" <<<"${entries}"; then
                        echo "Package '${package_id}' is missing Shared source '${relative}' (expected '${expected_entry}') (#70)." >&2
                        exit 1
                    fi
                done < <(cd "${shared_source_root}" && find . -type f -name '*.cs' -not -path './obj/*' -not -path './bin/*' -not -path './build/*' | sed 's|^\./||' | sort)
            fi
            continue
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
    if [[ -z "${portable_package_assets[${found_id}]:-}" && "${found_id}" != "SparseFragments.Generator.Shared" ]]; then
        unexpected_package_ids+=("${found_id}")
    fi
done
if [[ ${#unexpected_package_ids[@]} -gt 0 ]]; then
    echo "Unexpected NuGet package IDs: ${unexpected_package_ids[*]}." >&2
    exit 1
fi

echo "Verified ${#found_package_ids[@]} NuGet packages and their expected target assets."
