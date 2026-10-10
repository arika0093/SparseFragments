#!/usr/bin/env python3
"""Verify annotated Markdown samples match canonical fixtures exactly (#68, #213).

Usage: check-docs-samples.py <guide> <fixture> <id> [<id> ...]
       check-docs-samples.py --coverage <guide> <id> [<id> ...]
       check-docs-samples.py --registry <registry.tsv> [--root <dir>] [--only <markdown>]

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

Registry mode (#213) replaces both lists above with a single central file
(docs-samples-registry.tsv). It discovers README.md plus all docs/**/*.md
from the filesystem, so a new guide needs no shell edit: any
`<!-- sample: id -->` absent from the registry fails the build, as do
missing registry paths, missing fixture markers, unused registry entries,
duplicate ids within a file, unguarded runnable fences, and fixture regions
without assertion bodies. JSON output specimens (#212) use the same
discovery through `json-specimen` registry rows (`<!-- json-specimen: id -->`
in Markdown, `// json-specimen: id` regions in fixtures).
"""

import glob
import os
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
    if len(argv) >= 2 and argv[1] == "--registry":
        return registry_check(argv)
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


# Registry mode (#213). One central file maps Markdown + sample id to the
# canonical fixture; discovery covers README.md and every docs/**/*.md, so
# new guides are enforced without editing a shell file.
#
# Fenced-snippet rule: every ```csharp (or ```cs) fence must sit inside a
# registered `<!-- sample: -->` region or directly follow a
# `<!-- illustrative: reason -->` marker with a non-empty reason. One marker
# covers the next runnable fence only. Sketches that must not compile (error
# illustrations, ellipsized shapes, excerpts using names defined elsewhere)
# carry the illustrative marker; anything the fixture proves carries a sample
# marker instead.
#
# Assertion rule: every non-model fixture region (or the lines following it)
# must contain an assertion body (DocsCheck.Require, Require(, ShouldBe, or
# Assert), so the documented result runs instead of merely compiling. Ids
# ending in -model/-models declare shapes; their behavior is exercised
# through the constructing samples. The shell scripts additionally execute
# the packed-package consumers and require their success markers, which
# proves the test ran.

RUNNABLE_FENCE_LANGUAGES = ("csharp", "cs")

ASSERTION_TOKENS = ("DocsCheck.Require", "Require(", "ShouldBe", "Assert.")

MODEL_ID_SUFFIXES = ("-models", "-model")

ASSERTION_TRAIL_LINES = 40


def discover_markdown(root):
    found = []
    readme = os.path.join(root, "README.md")
    if os.path.isfile(readme):
        found.append("README.md")
    for path in sorted(glob.glob(os.path.join(root, "docs", "**", "*.md"), recursive=True)):
        found.append(os.path.relpath(path, root).replace(os.sep, "/"))
    return found


def load_registry(path):
    entries = []
    errors = []
    seen = set()
    try:
        with open(path, encoding="utf-8") as handle:
            raw_lines = handle.read().splitlines()
    except OSError as error:
        return entries, ["cannot read registry '%s': %s" % (path, error)]
    for lineno, raw in enumerate(raw_lines, start=1):
        line = raw.strip()
        if not line or line.startswith("#"):
            continue
        parts = raw.rstrip("\n").split("\t")
        parts = [part.strip() for part in parts]
        if len(parts) < 3 or any(not part for part in parts[:3]):
            errors.append(
                "registry '%s' line %d: expected "
                "'<markdown> TAB <id> TAB <fixture> [TAB <kind>]'." % (path, lineno)
            )
            continue
        markdown, sample_id, fixture = parts[0], parts[1], parts[2]
        kind = parts[3] if len(parts) > 3 else "sample"
        if kind not in ("sample", "json-specimen"):
            errors.append(
                "registry '%s' line %d: unknown kind '%s' (want 'sample' "
                "or 'json-specimen')." % (path, lineno, kind)
            )
            continue
        key = (kind, markdown, sample_id)
        if key in seen:
            errors.append(
                "registry '%s' line %d: duplicate entry for %s '%s' in '%s'."
                % (path, lineno, kind, sample_id, markdown)
            )
            continue
        seen.add(key)
        entries.append((markdown, sample_id, fixture, kind))
    return entries, errors


def md_open_pattern(kind):
    return re.compile(r"\s*<!--\s*%s:\s*(\S+)\s*-->" % re.escape(kind))


def md_close_marker(kind):
    return "<!-- /%s -->" % kind


def fixture_open_pattern(kind):
    return re.compile(r"\s*//\s*%s:\s*(\S+)" % re.escape(kind))


def fixture_close_pattern(kind):
    return re.compile(r"\s*//\s*/%s" % re.escape(kind))


class MarkdownScan:
    """One pass over a Markdown file: sample blocks plus fence guards."""

    def __init__(self, rel):
        self.rel = rel
        self.blocks = {"sample": {}, "json-specimen": {}}
        self.first_line = {"sample": {}, "json-specimen": {}}
        self.duplicates = []
        self.errors = []
        self.fence_errors = []


def scan_markdown(root, rel):
    scan = MarkdownScan(rel)
    path = os.path.join(root, *rel.split("/"))
    try:
        with open(path, encoding="utf-8") as handle:
            lines = handle.read().splitlines()
    except OSError as error:
        scan.errors.append("cannot read guide '%s': %s" % (rel, error))
        return scan
    kinds = ("sample", "json-specimen")
    openers = {kind: md_open_pattern(kind) for kind in kinds}
    current_kind = None
    current_id = None
    in_sample_fence = False
    pending_illustrative = None
    fence_open = False
    fence_lang = ""
    fence_start = 0
    fence_guarded = False
    for lineno, line in enumerate(lines, start=1):
        opened = False
        for kind in kinds:
            marker = openers[kind].fullmatch(line)
            if marker is not None:
                found_id = marker.group(1)
                if current_kind is not None:
                    scan.errors.append(
                        "guide '%s' line %d: <!-- %s: %s --> opens inside "
                        "<!-- %s: %s -->; close the outer block first."
                        % (rel, lineno, kind, found_id, current_kind, current_id)
                    )
                if found_id in scan.blocks[kind]:
                    scan.duplicates.append((kind, found_id, lineno))
                else:
                    scan.first_line[kind][found_id] = lineno
                scan.blocks[kind].setdefault(found_id, [])
                current_kind, current_id = kind, found_id
                in_sample_fence = False
                pending_illustrative = None
                opened = True
                break
        if opened:
            continue
        if current_kind is not None and line.strip() == md_close_marker(current_kind):
            current_kind, current_id = None, None
            in_sample_fence = False
            continue
        illust = re.fullmatch(r"\s*<!--\s*illustrative:\s*(.*?)\s*-->", line)
        if illust is not None:
            reason = illust.group(1).strip()
            if not reason:
                scan.errors.append(
                    "guide '%s' line %d: <!-- illustrative: --> needs a reason "
                    "naming why the snippet must not compile." % (rel, lineno)
                )
            elif current_kind is not None:
                scan.errors.append(
                    "guide '%s' line %d: illustrative marker inside "
                    "<!-- %s: %s --> is unnecessary; samples are already guarded."
                    % (rel, lineno, current_kind, current_id)
                )
            else:
                pending_illustrative = lineno
            continue
        fence = re.match(r"^\s*```(\S*)\s*$", line)
        if fence is not None:
            if current_kind is not None:
                in_sample_fence = not in_sample_fence
            if not fence_open:
                fence_open = True
                fence_lang = fence.group(1)
                fence_start = lineno
                fence_guarded = current_kind is not None or pending_illustrative is not None
            else:
                if fence_lang in RUNNABLE_FENCE_LANGUAGES and not fence_guarded:
                    scan.fence_errors.append(
                        "guide '%s' line %d: ```%s fence is not guarded; wrap it "
                        "in a registered <!-- sample: id --> block or precede it "
                        "with <!-- illustrative: reason -->."
                        % (rel, fence_start, fence_lang)
                    )
                if fence_lang in RUNNABLE_FENCE_LANGUAGES and pending_illustrative is not None:
                    pending_illustrative = None
                fence_open = False
                fence_lang = ""
                fence_start = 0
                fence_guarded = False
            continue
        if current_kind is not None:
            if in_sample_fence:
                scan.blocks[current_kind][current_id].append(line)
            continue
    if current_kind is not None:
        scan.errors.append(
            "guide '%s': <!-- %s: %s --> (line %d) is never closed with '%s'."
            % (
                rel,
                current_kind,
                current_id,
                scan.first_line[current_kind][current_id],
                md_close_marker(current_kind),
            )
        )
    if fence_open and fence_lang in RUNNABLE_FENCE_LANGUAGES and not fence_guarded:
        scan.fence_errors.append(
            "guide '%s' line %d: ```%s fence is never closed and is not guarded."
            % (rel, fence_start, fence_lang)
        )
    return scan


def fixture_region_with_trail(fixture_lines, kind, sample_id):
    opener = fixture_open_pattern(kind)
    closer = fixture_close_pattern(kind)
    regions = []
    current = None
    for index, line in enumerate(fixture_lines):
        opened = opener.fullmatch(line)
        if opened is not None:
            if opened.group(1) == sample_id:
                current = []
            continue
        if current is not None and closer.fullmatch(line):
            regions.append((current, index))
            current = None
            continue
        if current is not None:
            current.append(line)
    texts = []
    for region, close_index in regions:
        window = region + fixture_lines[close_index + 1 : close_index + 1 + ASSERTION_TRAIL_LINES]
        texts.append("\n".join(window))
    return regions, texts


def is_model_id(sample_id):
    return sample_id.endswith(MODEL_ID_SUFFIXES)


def compare_blocks(guide, fixture, sample_id, actual, expected):
    if actual == expected:
        return None
    notes = [
        "Docs sample drift [%s]: guide '%s' block differs from fixture '%s' region."
        % (sample_id, guide, fixture)
    ]
    width = max(len(actual), len(expected))
    for index in range(width):
        left = actual[index] if index < len(actual) else None
        right = expected[index] if index < len(expected) else None
        if left != right:
            notes.append("  md line %d: %r" % (index + 1, left))
            notes.append("  fixture  : %r" % (right,))
            break
    notes.append(
        "  (md %d lines, fixture %d lines after normalization)"
        % (len(actual), len(expected))
    )
    return notes


def registry_check(argv):
    # check-docs-samples.py --registry <tsv> [--root <dir>] [--only <markdown>]
    registry = None
    root = os.getcwd()
    only = None
    index = 2
    while index < len(argv):
        token = argv[index]
        if token == "--root" and index + 1 < len(argv):
            root = argv[index + 1]
            index += 2
        elif token == "--only" and index + 1 < len(argv):
            only = argv[index + 1].replace(os.sep, "/")
            index += 2
        elif registry is None and not token.startswith("--"):
            registry = argv[index]
            index += 1
        else:
            print("Usage: check-docs-samples.py --registry <registry.tsv> [--root <dir>] [--only <markdown>]")
            return 2
    if registry is None:
        print("Usage: check-docs-samples.py --registry <registry.tsv> [--root <dir>] [--only <markdown>]")
        return 2
    if not os.path.isabs(registry):
        registry = os.path.join(root, registry)
    errors = []
    entries, load_errors = load_registry(registry)
    errors.extend(load_errors)
    discovered = discover_markdown(root)
    if only is not None:
        if only not in discovered and not os.path.isfile(os.path.join(root, *only.split("/"))):
            errors.append("scope '%s' does not exist." % only)
            discovered = []
        else:
            discovered = [only] if only in discovered else [only]
            entries = [entry for entry in entries if entry[0] == only]
    scans = {}
    for rel in discovered:
        scans[rel] = scan_markdown(root, rel)
        scan = scans[rel]
        errors.extend(scan.errors)
        for kind, dup_id, dup_line in scan.duplicates:
            first = scan.first_line[kind][dup_id]
            errors.append(
                "guide '%s': duplicate <!-- %s: %s --> at lines %d and %d; "
                "ids must be unique within a file."
                % (rel, kind, dup_id, first, dup_line)
            )
        errors.extend(scan.fence_errors)
    # Every marker in every discovered file must be registered (#213: new
    # guides fail without a shell edit).
    registered_pairs = {(kind, markdown, sample_id) for markdown, sample_id, _, kind in entries}
    for rel in discovered:
        scan = scans[rel]
        for kind in ("sample", "json-specimen"):
            for found_id, found_line in scan.first_line[kind].items():
                if (kind, rel, found_id) not in registered_pairs:
                    errors.append(
                        "guide '%s' line %d: <!-- %s: %s --> has no registry entry; "
                        "add it to docs-samples-registry.tsv with its canonical fixture."
                        % (rel, found_line, kind, found_id)
                    )
    # Every registry entry must resolve: file and marker exist, fixture and
    # region exist, blocks match, behavior is asserted.
    fixture_cache = {}
    for markdown, sample_id, fixture, kind in entries:
        md_path = os.path.join(root, *markdown.split("/"))
        if not os.path.isfile(md_path):
            errors.append("registry: guide '%s' does not exist." % markdown)
            continue
        scan = scans.get(markdown)
        if scan is None:
            scan = scan_markdown(root, markdown)
            scans[markdown] = scan
            errors.extend(scan.errors)
            errors.extend(scan.fence_errors)
        if sample_id not in scan.blocks[kind]:
            errors.append(
                "registry: guide '%s' has no <!-- %s: %s --> block; the entry is unused."
                % (markdown, kind, sample_id)
            )
            continue
        fixture_norm = fixture.replace(os.sep, "/")
        fixture_path = os.path.join(root, *fixture.split("/"))
        if not os.path.isfile(fixture_path):
            errors.append(
                "registry: fixture '%s' for %s '%s' in '%s' does not exist."
                % (fixture, kind, sample_id, markdown)
            )
            continue
        if fixture_norm not in fixture_cache:
            try:
                with open(fixture_path, encoding="utf-8") as handle:
                    fixture_cache[fixture_norm] = handle.read().splitlines()
            except OSError as error:
                errors.append("cannot read fixture '%s': %s" % (fixture, error))
                continue
        fixture_lines = fixture_cache[fixture_norm]
        regions, windows = fixture_region_with_trail(fixture_lines, kind, sample_id)
        if not regions:
            errors.append(
                "registry: fixture '%s' has no // %s: %s region for '%s'."
                % (fixture, kind, sample_id, markdown)
            )
            continue
        if kind == "sample":
            flat = []
            for region, _ in regions:
                flat.extend(region)
            mismatch = compare_blocks(
                markdown,
                fixture,
                sample_id,
                normalize(scan.blocks[kind][sample_id]),
                normalize(flat),
            )
            if mismatch is not None:
                errors.extend(mismatch)
            if not is_model_id(sample_id):
                if not any(
                    token in window for window in windows for token in ASSERTION_TOKENS
                ):
                    errors.append(
                        "registry: fixture '%s' region // sample: %s (for '%s') "
                        "has no assertion body; execute the documented result with "
                        "DocsCheck.Require (or Require/ShouldBe/Assert) so behavior "
                        "is verified, not just compiled." % (fixture, sample_id, markdown)
                    )
        else:
            flat = []
            for region, _ in regions:
                flat.extend(region)
            mismatch = compare_blocks(
                markdown,
                fixture,
                "json-specimen:%s" % sample_id,
                normalize(scan.blocks[kind][sample_id]),
                normalize(flat),
            )
            if mismatch is not None:
                errors.extend(mismatch)
    for message in errors:
        print(message)
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
