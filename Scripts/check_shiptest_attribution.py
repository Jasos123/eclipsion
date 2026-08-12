#!/usr/bin/env python3
"""Validate and optionally repair attribution for Shiptest-derived RSI assets."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path


SHIPTEST_SOURCE = "https://github.com/shiptest-ss13/Shiptest"
SHIPTEST_LICENSE = "CC-BY-SA-3.0"
LICENSE_URI = "https://creativecommons.org/licenses/by-sa/3.0/"
GENERIC_CHANGE_NOTICE = (
    "Ported to SS14's RSI format; further visual changes, if any, were not "
    "documented in the original port."
)

# This RSI is a collection of independently authored states under multiple licenses.
# Its copyright field records that the Shiptest-derived portions retain CC-BY-SA-3.0.
MIXED_LICENSE_COLLECTIONS = {
    "Resources/Textures/_Crescent/Objects/Materials/ore.rsi/meta.json":
        "CC-BY-NC-SA-3.0",
}

COPYRIGHT_RE = re.compile(r'("copyright"\s*:\s*)("(?:\\.|[^"\\])*")')
LICENSE_RE = re.compile(r'("license"\s*:\s*)(null|"(?:\\.|[^"\\])*")')
CHANGE_RE = re.compile(
    r"\b(modif(?:ied|ication)?|edit(?:ed)?|resprit(?:ed)?|adapt(?:ed|ation)?|"
    r"convert(?:ed)?|port(?:ed)?|unmodified|recolor(?:ed)?|reshad(?:ed|ing)?)\b",
    re.IGNORECASE,
)


def relative_path(path: Path, root: Path) -> str:
    return path.relative_to(root).as_posix()


def append_sentence(text: str, sentence: str) -> str:
    text = text.rstrip()
    if text and text[-1] not in ".!?":
        text += "."
    return f"{text} {sentence}".strip()


def replace_json_string(raw: str, pattern: re.Pattern[str], value: str) -> str:
    match = pattern.search(raw)
    if match is None:
        return raw
    encoded = json.dumps(value, ensure_ascii=False)
    return raw[:match.start()] + match.group(1) + encoded + raw[match.end():]


def repair_metadata(path: Path, root: Path, raw: str, data: dict[str, object]) -> str:
    rel_path = relative_path(path, root)
    copyright_text = str(data.get("copyright") or "").strip()

    if rel_path not in MIXED_LICENSE_COLLECTIONS and data.get("license") != SHIPTEST_LICENSE:
        raw = replace_json_string(raw, LICENSE_RE, SHIPTEST_LICENSE)

    if SHIPTEST_SOURCE.lower() not in copyright_text.lower():
        copyright_text = append_sentence(
            copyright_text,
            f"Source repository: {SHIPTEST_SOURCE}.",
        )

    if not CHANGE_RE.search(copyright_text):
        copyright_text = append_sentence(copyright_text, GENERIC_CHANGE_NOTICE)

    if rel_path in MIXED_LICENSE_COLLECTIONS:
        contradictory_notice = (
            " All rights reserved. Do not modify, publish, use, relicense, or edit."
        )
        copyright_text = copyright_text.replace(contradictory_notice, "")
        mixed_notice = (
            "Shiptest-derived portions remain CC-BY-SA-3.0; other portions retain "
            "the licenses and authorship documented in this metadata."
        )
        if "shiptest-derived portions remain cc-by-sa-3.0" not in copyright_text.lower():
            copyright_text = append_sentence(copyright_text, mixed_notice)

    return replace_json_string(raw, COPYRIGHT_RE, copyright_text)


def validate_global_files(root: Path) -> list[str]:
    errors: list[str] = []
    required = {
        root / "Resources" / "ShiptestAttribution.txt": (
            SHIPTEST_SOURCE,
            LICENSE_URI,
            SHIPTEST_LICENSE,
        ),
        root / "LEGAL.md": (SHIPTEST_SOURCE, LICENSE_URI),
        root / "README.md": (SHIPTEST_SOURCE, "Resources/ShiptestAttribution.txt"),
    }

    for path, needles in required.items():
        rel_path = relative_path(path, root)
        if not path.is_file():
            errors.append(f"{rel_path}: required attribution file is missing")
            continue
        text = path.read_text(encoding="utf-8")
        for needle in needles:
            if needle not in text:
                errors.append(f"{rel_path}: missing required reference {needle!r}")

    return errors


def validate_metadata(path: Path, root: Path, data: dict[str, object]) -> list[str]:
    errors: list[str] = []
    rel_path = relative_path(path, root)
    copyright_text = str(data.get("copyright") or "")
    expected_license = MIXED_LICENSE_COLLECTIONS.get(rel_path, SHIPTEST_LICENSE)

    if data.get("license") != expected_license:
        errors.append(
            f"{rel_path}: expected license {expected_license!r}, got {data.get('license')!r}"
        )
    if SHIPTEST_SOURCE.lower() not in copyright_text.lower():
        errors.append(f"{rel_path}: missing Shiptest source repository URL")
    if not CHANGE_RE.search(copyright_text):
        errors.append(f"{rel_path}: missing modification or porting notice")
    if rel_path in MIXED_LICENSE_COLLECTIONS:
        if "shiptest-derived portions remain cc-by-sa-3.0" not in copyright_text.lower():
            errors.append(f"{rel_path}: mixed-license collection lacks Shiptest license scope")
        if "all rights reserved" in copyright_text.lower():
            errors.append(f"{rel_path}: contradictory all-rights-reserved notice remains")

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
        help="Repository root (defaults to the parent of Scripts)",
    )
    parser.add_argument(
        "--fix",
        action="store_true",
        help="Repair license, source, and modification notices before validating",
    )
    args = parser.parse_args()
    root = args.root.resolve()
    textures = root / "Resources" / "Textures"
    errors = validate_global_files(root)
    checked = 0
    changed = 0

    for path in textures.rglob("meta.json"):
        raw = path.read_bytes().decode("utf-8-sig")
        try:
            data = json.loads(raw)
        except json.JSONDecodeError as exc:
            if re.search(r"shiptest", raw, re.IGNORECASE):
                errors.append(f"{relative_path(path, root)}: invalid JSON: {exc}")
            continue

        copyright_text = str(data.get("copyright") or "")
        if "shiptest" not in copyright_text.lower():
            continue

        checked += 1
        if args.fix:
            repaired = repair_metadata(path, root, raw, data)
            if repaired != raw:
                path.write_bytes(repaired.encode("utf-8"))
                raw = repaired
                data = json.loads(raw)
                changed += 1

        errors.extend(validate_metadata(path, root, data))

    if errors:
        print(f"Shiptest attribution validation failed with {len(errors)} error(s):")
        for error in errors:
            print(f"- {error}")
        return 1

    action = f"; repaired {changed}" if args.fix else ""
    print(f"Validated {checked} Shiptest-attributed RSI metadata files{action}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
