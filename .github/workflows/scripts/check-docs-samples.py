#!/usr/bin/env python3
"""Verify annotated Markdown samples match canonical fixtures exactly (#68).

Usage: check-docs-samples.py <guide> <fixture> <id> [<id> ...]
       check-docs-samples.py --coverage <guide> <id> [<id> ...]

Markdown convention: a line `<!-- sample: <id> -->`, then a fenced block,
then `<!-- /sample -->`. Fixture convention: `// sample: <id>` ...
`// /sample` (possibly several regions per id; concatenated in order).
Normalization (both sides): drop `using ...;` lines, `dotnet run --file`
directives (`#:package ...`), and blank lines, strip
leading/trailing whitespace, LF line endings. Anything else must match
exactly, so drift in statements, models, or asserted results fails the check.

Coverage mode lists every `<!-- sample: -->` id in the guide and fails when
an id has no registered fixture guard. The verify scripts pass the same id
list to both modes, so a newly added marker without a `check_block`
registration fails the build.
"""

import re
import sys


def extract_md(path):
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    blocks = {}
    current = None
    in_fence = False
    for line in lines:
        marker = re.fullmatch(r"\s*<!--\s*sample:\s*(\S+)\s*-->", line)
        if marker is not None:
            current = marker.group(1)
            in_fence = False
            blocks.setdefault(current, [])
            continue
        if current is not None and line.strip() == "<!-- /sample -->":
            current = None
            continue
        if current is not None:
            stripped = line.strip()
            if stripped.startswith("```"):
                in_fence = not in_fence
                continue
            if in_fence:
                blocks[current].append(line)
    return blocks


def extract_fixture(path):
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    blocks = {}
    current = None
    for line in lines:
        marker = re.fullmatch(r"\s*//\s*sample:\s*(\S+)", line)
        if marker is not None:
            current = marker.group(1)
            blocks.setdefault(current, [])
            continue
        if current is not None and re.fullmatch(r"\s*//\s*/sample", line):
            current = None
            continue
        if current is not None:
            blocks[current].append(line)
    return blocks


def strip_assertions(text):
    # User-facing docs show result comments instead of test-helper assertions
    # (#71). Fixtures keep runtime Requires, so ignore assertion statements on
    # both sides before comparing. Real code statements and result comments
    # must still match exactly.
    text = re.sub(
        r"DocsCheck\s*\.\s*Require\s*\(.*?\);", "", text, flags=re.DOTALL
    )
    text = re.sub(
        r"(?m)^\s*Require\s*\(.*?\);", "", text, flags=re.DOTALL
    )
    text = re.sub(
        r"[^\n]*ShouldBe\w*\s*\(.*?\)\s*;", "", text, flags=re.DOTALL
    )
    return text


def normalize(lines):
    text = strip_assertions("\n".join(lines))
    kept = []
    for line in text.splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        if re.fullmatch(r"using\s[^;]+;", stripped):
            continue
        if stripped.startswith("#:"):
            # dotnet-file directives (e.g. `#:package` in the README
            # quick-start) travel with the doc snippet, never the fixture.
            continue
        kept.append(stripped)
    return kept


def coverage(argv):
    if len(argv) < 3:
        print("Usage: check-docs-samples.py --coverage <guide> <id> [<id> ...]")
        return 2
    guide, ids = argv[2], argv[3:]
    try:
        md_blocks = extract_md(guide)
    except OSError as error:
        print("Docs sample drift: cannot read guide '%s': %s" % (guide, error))
        return 1
    unlisted = sorted(set(md_blocks) - set(ids))
    if unlisted:
        for sample_id in unlisted:
            print(
                "Docs sample drift: guide '%s' has <!-- sample: %s --> with no fixture guard; "
                "register it in a check_block call." % (guide, sample_id)
            )
        return 1
    return 0


def main(argv):
    if len(argv) >= 2 and argv[1] == "--coverage":
        return coverage(argv)
    if len(argv) < 4:
        print("Usage: check-docs-samples.py <guide> <fixture> <id> [<id> ...]")
        return 2
    guide, fixture, ids = argv[1], argv[2], argv[3:]
    try:
        md_blocks = extract_md(guide)
    except OSError as error:
        print("Docs sample drift: cannot read guide '%s': %s" % (guide, error))
        return 1
    try:
        fixture_blocks = extract_fixture(fixture)
    except OSError as error:
        print("Docs sample drift: cannot read fixture '%s': %s" % (guide, error))
        return 1
    failed = False
    for sample_id in ids:
        if sample_id not in md_blocks:
            print(
                "Docs sample drift: guide '%s' has no <!-- sample: %s --> block."
                % (guide, sample_id)
            )
            failed = True
            continue
        if sample_id not in fixture_blocks:
            print(
                "Docs sample drift: fixture '%s' has no // sample: %s region."
                % (fixture, sample_id)
            )
            failed = True
            continue
        expected = normalize(fixture_blocks[sample_id])
        actual = normalize(md_blocks[sample_id])
        if actual != expected:
            print(
                "Docs sample drift [%s]: guide block differs from fixture region."
                % sample_id
            )
            for index, (left, right) in enumerate(
                zip(actual + [None] * len(expected), expected + [None] * len(actual))
            ):
                if left != right:
                    print("  md line %d: %r" % (index + 1, left))
                    print("  fixture  : %r" % (right,))
                    break
            print(
                "  (md %d lines, fixture %d lines after normalization)"
                % (len(actual), len(expected))
            )
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
