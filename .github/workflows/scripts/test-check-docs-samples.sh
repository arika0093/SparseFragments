#!/usr/bin/env bash
# Maintained negative probes for the docs sample enforcement (#213).
#
# Each probe builds a minimal Markdown/fixture/registry tree under a temp
# directory and runs check-docs-samples.py (or check-docs-links.py) against
# it. Probes that must fail assert a non-zero exit; the illustrative control
# must pass. Any unexpected verdict fails this script, so CI catches verifier
# regressions the same way it catches docs drift.
#
# Probes: (a) unregistered sample marker in a new guide, (b) changed sample
# code, (c) missing fixture marker, (d) broken internal link,
# (e) mutated JSON specimen, (f) illustrative control passes,
# (g) duplicate sample id, (h) unused registry entry, (i) unguarded runnable
# fence, (j) fixture region without an assertion body, (k) empty illustrative
# reason, (l) mutated JSON inside a C# raw-string wrapper, (m) -store shape
# exemption passes.
#
# Usage: test-check-docs-samples.sh
set -euo pipefail

scripts_dir="$(dirname "$0")"
checker="${scripts_dir}/check-docs-samples.py"
link_checker="${scripts_dir}/check-docs-links.py"
passed=0
failed=0

write_baseline() {
    local root="$1"
    mkdir -p "${root}/docs"
    cat > "${root}/docs/guide.md" <<'EOF'
# Guide

<!-- sample: probe-one -->
```csharp
var value = 1;
// value == 1
```
<!-- /sample -->
EOF
    cat > "${root}/fixture.cs" <<'EOF'
// sample: probe-one
var value = 1;
// value == 1
DocsCheck.Require(value == 1, "one");
// /sample
EOF
    printf 'docs/guide.md\tprobe-one\tfixture.cs\n' > "${root}/registry.tsv"
}

# expect_fail <name> <root>: checker must exit non-zero; prints its output.
expect_fail() {
    local name="$1"
    local root="$2"
    local output
    if output="$(python3 "${checker}" --registry "${root}/registry.tsv" --root "${root}" 2>&1)"; then
        echo "PROBE FAIL [${name}]: verifier passed but should have failed." >&2
        failed=$((failed + 1))
    else
        echo "PROBE PASS [${name}]: verifier failed as expected:"
        echo "${output}" | head -n 4 | sed 's/^/    /'
        passed=$((passed + 1))
    fi
}

# expect_pass <name> <root>: checker must exit zero.
expect_pass() {
    local name="$1"
    local root="$2"
    local output
    if output="$(python3 "${checker}" --registry "${root}/registry.tsv" --root "${root}" 2>&1)"; then
        echo "PROBE PASS [${name}]: verifier passed as expected."
        passed=$((passed + 1))
    else
        echo "PROBE FAIL [${name}]: verifier failed but should have passed:" >&2
        echo "${output}" | head -n 4 | sed 's/^/    /' >&2
        failed=$((failed + 1))
    fi
}

tmp_base="$(mktemp -d)"
trap 'rm -rf "${tmp_base}"' EXIT

# Baseline control: a registered, matching, asserted sample passes.
root="${tmp_base}/baseline"
write_baseline "${root}"
expect_pass "baseline control" "${root}"

# (a) New guide with an unregistered runnable sample fails without a shell edit.
root="${tmp_base}/unknown-marker"
write_baseline "${root}"
cat >> "${root}/docs/guide.md" <<'EOF'

<!-- sample: probe-two -->
```csharp
var other = 2;
```
<!-- /sample -->
EOF
expect_fail "a/unknown sample marker" "${root}"

# (b) Changed documented code fails exact comparison.
root="${tmp_base}/changed-code"
write_baseline "${root}"
sed -i 's/var value = 1;/var value = 2;/' "${root}/docs/guide.md"
expect_fail "b/changed sample code" "${root}"

# (c) Missing fixture marker fails with file/id.
root="${tmp_base}/missing-fixture-marker"
write_baseline "${root}"
cat > "${root}/fixture.cs" <<'EOF'
// Nothing registered here.
var value = 1;
EOF
expect_fail "c/missing fixture marker" "${root}"

# (d) Broken internal link fails the link checker.
root="${tmp_base}/broken-link"
mkdir -p "${root}/docs"
cat > "${root}/docs/guide.md" <<'EOF'
# Guide

See [the missing page](missing.md).
EOF
if link_output="$(python3 "${link_checker}" "${root}" "docs/guide.md" 2>&1)"; then
    echo "PROBE FAIL [d/broken internal link]: link checker passed but should have failed." >&2
    failed=$((failed + 1))
else
    echo "PROBE PASS [d/broken internal link]: link checker failed as expected:"
    echo "${link_output}" | head -n 4 | sed 's/^/    /'
    passed=$((passed + 1))
fi

# (e) Mutated JSON specimen fails through the same discovery process (#212 hook).
root="${tmp_base}/json-specimen"
mkdir -p "${root}/docs"
cat > "${root}/docs/guide.md" <<'EOF'
# Guide

<!-- json-specimen: payload-wire -->
```json
{ "label": { "after": "b" } }
```
<!-- /json-specimen -->
EOF
cat > "${root}/fixture.cs" <<'EOF'
// json-specimen: payload-wire
{ "label": { "after": "CHANGED" } }
// /json-specimen
EOF
printf 'docs/guide.md\tpayload-wire\tfixture.cs\tjson-specimen\n' > "${root}/registry.tsv"
expect_fail "e/mutated JSON specimen" "${root}"

# (f) Illustrative control: sketches with a reason pass.
root="${tmp_base}/illustrative"
write_baseline "${root}"
cat >> "${root}/docs/guide.md" <<'EOF'

<!-- illustrative: error illustration; the shape does not compile by design -->
```csharp
// Does not compile
var broken = undefinedName;
```
EOF
expect_pass "f/illustrative control" "${root}"

# (g) Duplicate sample id within one file fails.
root="${tmp_base}/duplicate-id"
write_baseline "${root}"
cat >> "${root}/docs/guide.md" <<'EOF'

<!-- sample: probe-one -->
```csharp
var value = 1;
```
<!-- /sample -->
EOF
expect_fail "g/duplicate sample id" "${root}"

# (h) Unused registry entry fails.
root="${tmp_base}/unused-entry"
write_baseline "${root}"
printf 'docs/guide.md\tprobe-stale\tfixture.cs\n' >> "${root}/registry.tsv"
expect_fail "h/unused registry entry" "${root}"

# (i) Unguarded runnable fence fails.
root="${tmp_base}/unguarded-fence"
write_baseline "${root}"
cat >> "${root}/docs/guide.md" <<'EOF'

```csharp
var stray = session.CreateChangeSet();
```
EOF
expect_fail "i/unguarded runnable fence" "${root}"

# (j) Fixture region without an assertion body fails.
root="${tmp_base}/no-assertion"
mkdir -p "${root}/docs"
cat > "${root}/docs/guide.md" <<'EOF'
# Guide

<!-- sample: probe-bare -->
```csharp
var bare = 1;
// bare == 1
```
<!-- /sample -->
EOF
cat > "${root}/fixture.cs" <<'EOF'
// sample: probe-bare
var bare = 1;
// bare == 1
// /sample
EOF
printf 'docs/guide.md\tprobe-bare\tfixture.cs\n' > "${root}/registry.tsv"
expect_fail "j/missing assertion body" "${root}"

# (k) Empty illustrative reason fails.
root="${tmp_base}/empty-reason"
write_baseline "${root}"
cat >> "${root}/docs/guide.md" <<'EOF'

<!-- illustrative: -->
```csharp
var sketchy = 1;
```
EOF
expect_fail "k/empty illustrative reason" "${root}"

# (l) Mutated JSON inside a C# raw-string wrapper fails deep comparison.
# This locks the #212 fixture shape: the Markdown fence holds raw JSON while
# the fixture region wraps the specimen in a `"""..."""` literal.
root="${tmp_base}/json-wrapper"
mkdir -p "${root}/docs"
cat > "${root}/docs/guide.md" <<'EOF'
# Guide

<!-- json-specimen: payload-wire -->
```json
{ "label": { "after": "b" } }
```
<!-- /json-specimen -->
EOF
cat > "${root}/fixture.cs" <<'EOF'
// json-specimen: payload-wire
public const string Wire = """{ "label": { "after": "CHANGED" } }""";
// /json-specimen
EOF
printf 'docs/guide.md\tpayload-wire\tfixture.cs\tjson-specimen\n' > "${root}/registry.tsv"
expect_fail "l/mutated JSON in raw-string wrapper" "${root}"

# (m) Supporting-store shape passes without an assertion body. Like -model
# ids, a -store region declares a shape exercised through its consuming
# sample (rebase-server-store through rebase-server-save), so the assertion
# rule exempts it.
root="${tmp_base}/store-shape"
mkdir -p "${root}/docs"
cat > "${root}/docs/guide.md" <<'EOF'
# Guide

<!-- sample: probe-store -->
```csharp
sealed class ProbeStore
{
}
```
<!-- /sample -->
EOF
cat > "${root}/fixture.cs" <<'EOF'
// sample: probe-store
sealed class ProbeStore
{
}
// /sample
EOF
printf 'docs/guide.md\tprobe-store\tfixture.cs\n' > "${root}/registry.tsv"
expect_pass "m/store shape exemption" "${root}"

echo "Probes: ${passed} passed, ${failed} failed."
if [[ "${failed}" -ne 0 ]]; then
    exit 1
fi
echo "Docs enforcement probes passed."
