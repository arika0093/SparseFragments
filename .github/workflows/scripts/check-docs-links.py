#!/usr/bin/env python3
"""Validate internal Markdown links and anchors across README and docs/ (#68).

Usage: check-docs-links.py <root> <file> [<file> ...]

Checks `[text](target)` links where target starts with `docs/`, `#`, or is a
relative `.md` path. External URLs and absolute paths are ignored. Anchors use
GitHub heading slugs: lowercase, punctuation removed (except - and _),
spaces become hyphens.
"""

import os
import re
import sys


def slug(heading):
    text = heading.strip().lower()
    text = re.sub(r"[^\w\s\-]", "", text, flags=re.UNICODE)
    return re.sub(r"\s+", "-", text)


def headings_of(path):
    anchors = set()
    with open(path, encoding="utf-8") as handle:
        for line in handle:
            match = re.match(r"^#{1,6}\s+(.*)", line.rstrip("\n"))
            if match:
                anchors.add(slug(match.group(1)))
    return anchors


def main(argv):
    if len(argv) < 3:
        print("Usage: check-docs-links.py <root> <file> [<file> ...]")
        return 2
    root, files = argv[1], argv[2:]
    failed = False
    for rel in files:
        path = os.path.join(root, rel)
        with open(path, encoding="utf-8") as handle:
            text = handle.read()
        for match in re.finditer(r"\[[^\]]*\]\(([^)\s]+)\)", text):
            target = match.group(1)
            if re.match(r"^(https?://|mailto:|#?$|/)", target) or (
                target == "#"
            ):
                if target == "#":
                    continue
                if "://" in target or target.startswith("/"):
                    continue
            if "#" in target:
                file_part, anchor = target.split("#", 1)
            else:
                file_part, anchor = target, None
            if not file_part:
                if anchor and anchor not in headings_of(path):
                    print(
                        "Broken anchor in %s: #%s has no heading." % (rel, anchor)
                    )
                    failed = True
                continue
            if not file_part.endswith(".md"):
                continue
            resolved = os.path.normpath(os.path.join(os.path.dirname(path), file_part))
            if not os.path.isfile(resolved):
                print("Broken link in %s: '%s' does not exist." % (rel, target))
                failed = True
                continue
            if anchor and anchor not in headings_of(resolved):
                print(
                    "Broken anchor in %s: '%s' has no '#%s' heading."
                    % (rel, target, anchor)
                )
                failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
