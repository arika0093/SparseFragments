#!/usr/bin/env python3
"""Verify Markdown JSON specimens match canonical fixture specimens (#212).

Usage: check-docs-json.py <guide> <fixture> <id> [<id> ...]
        check-docs-json.py --coverage-json <guide> <id> [<id> ...]

Markdown convention: a line `<!-- json-sample: <id> -->`, then one fenced
```json block, then `<!-- /json-sample -->`. Fixture convention:
a `// json-sample: <id>` line through a `// /json-sample` line wrapping a
triple-quoted raw-string literal that holds the expected JSON. Both sides are parsed as
JSON and compared deeply: object key order is insignificant, array order
(including beforeOrder/afterOrder and item sequences) is significant.
Byte and field ordering against the actual serializer output is asserted
separately at runtime by exact-string DocsCheck assertions in the fixture.

Coverage mode lists every `<!-- json-sample: -->` id in the guide and fails
when an id has no registered fixture guard.
"""

import json
import re
import sys


def extract_md(path):
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    blocks = {}
    current = None
    in_fence = False
    collected = []
    for line in lines:
        marker = re.fullmatch(r"\s*<!--\s*json-sample:\s*(\S+)\s*-->", line)
        if marker is not None:
            current = marker.group(1)
            in_fence = False
            collected = []
            blocks.setdefault(current, [])
            continue
        if current is not None and line.strip() == "<!-- /json-sample -->":
            blocks[current].append("\n".join(collected))
            current = None
            continue
        if current is not None:
            stripped = line.strip()
            if stripped.startswith("```"):
                in_fence = not in_fence
                continue
            if in_fence:
                collected.append(line)
    return blocks


def extract_fixture(path):
    with open(path, encoding="utf-8") as handle:
        lines = handle.read().splitlines()
    blocks = {}
    current = None
    for line in lines:
        marker = re.fullmatch(r"\s*//\s*json-sample:\s*([A-Za-z0-9_-]+)\s*", line)
        if marker is not None:
            current = marker.group(1)
            blocks.setdefault(current, [])
            continue
        if current is not None and re.fullmatch(
            r"\s*//\s*/json-sample\s*", line
        ):
            current = None
            continue
        if current is not None:
            blocks[current].append(line)
    specimens = {}
    for sample_id, body in blocks.items():
        literals = re.findall(r'"""(.*?)"""', "\n".join(body), re.DOTALL)
        specimens[sample_id] = literals
    return specimens


def coverage(argv):
    if len(argv) < 3:
        print("Usage: check-docs-json.py --coverage-json <guide> <id> [<id> ...]")
        return 2
    guide, ids = argv[2], argv[3:]
    try:
        md_blocks = extract_md(guide)
    except OSError as error:
        print("Docs JSON drift: cannot read guide '%s': %s" % (guide, error))
        return 1
    unlisted = sorted(set(md_blocks) - set(ids))
    if unlisted:
        for sample_id in unlisted:
            print(
                "Docs JSON drift: guide '%s' has <!-- json-sample: %s --> with no fixture guard; "
                "register it in a check_json call." % (guide, sample_id)
            )
        return 1
    return 0


def main(argv):
    if len(argv) >= 2 and argv[1] == "--coverage-json":
        return coverage(argv)
    if len(argv) < 4:
        print("Usage: check-docs-json.py <guide> <fixture> <id> [<id> ...]")
        return 2
    guide, fixture, ids = argv[1], argv[2], argv[3:]
    try:
        md_blocks = extract_md(guide)
    except OSError as error:
        print("Docs JSON drift: cannot read guide '%s': %s" % (guide, error))
        return 1
    try:
        fixture_blocks = extract_fixture(fixture)
    except OSError as error:
        print("Docs JSON drift: cannot read fixture '%s': %s" % (fixture, error))
        return 1
    failed = False
    for sample_id in ids:
        if sample_id not in md_blocks or not md_blocks[sample_id]:
            print(
                "Docs JSON drift: guide '%s' has no <!-- json-sample: %s --> block."
                % (guide, sample_id)
            )
            failed = True
            continue
        if sample_id not in fixture_blocks or not fixture_blocks[sample_id]:
            print(
                "Docs JSON drift: fixture '%s' has no // json-sample: %s region."
                % (fixture, sample_id)
            )
            failed = True
            continue
        try:
            md_parsed = [json.loads(block) for block in md_blocks[sample_id]]
        except json.JSONDecodeError as error:
            print(
                "Docs JSON drift [%s]: guide fence is not valid JSON: %s"
                % (sample_id, error)
            )
            failed = True
            continue
        try:
            fixture_parsed = [json.loads(block) for block in fixture_blocks[sample_id]]
        except json.JSONDecodeError as error:
            print(
                "Docs JSON drift [%s]: fixture specimen is not valid JSON: %s"
                % (sample_id, error)
            )
            failed = True
            continue
        if md_parsed != fixture_parsed:
            print(
                "Docs JSON drift [%s]: guide fence differs from fixture specimen (deep JSON, array order significant)."
                % sample_id
            )
            print("  md     : %r" % (md_blocks[sample_id][0][:280],))
            print("  fixture: %r" % (fixture_blocks[sample_id][0][:280],))
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
