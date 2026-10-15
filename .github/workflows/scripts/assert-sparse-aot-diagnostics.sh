#!/usr/bin/env bash
# SparseTypes NativeAOT diagnostic gate.
set -euo pipefail

publish_log="${1:?publish log path required}"
if [[ ! -r "$publish_log" ]]; then
  echo "Publish log is not readable: $publish_log" >&2
  exit 2
fi
unexpected_diagnostics="$(
  grep -E '(warning|error) IL[0-9]{4}:' "$publish_log" \
    || true
)"
if [[ -n "$unexpected_diagnostics" ]]; then
  echo "Unexpected NativeAOT or trimming diagnostics:" >&2
  echo "$unexpected_diagnostics" >&2
  exit 1
fi
echo "SparseTypes NativeAOT diagnostics are clean."
