#!/usr/bin/env python3
"""Generate the eval-builder web tool's data file from the plugin's sources.

Single source of truth: the judge methodology lives in
skills/generate-eval/references/frame.md and the research registry in
skills/generate-eval/references/citations.md. This script parses both and emits
a JavaScript data file consumed by the static eval-builder page on
https://observability-oss.github.io — so the web tool can never drift from the
skill.

Usage:
  python scripts/build_web_frame.py --out /path/to/eval-builder-data.js
"""

import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FRAME = ROOT / "skills" / "generate-eval" / "references" / "frame.md"
CITATIONS = ROOT / "skills" / "generate-eval" / "references" / "citations.md"

MODE_ORDER = [
    "faithfulness", "relevance", "helpfulness", "tool_call",
    "safety", "tone", "conciseness", "format", "custom",
]


def parse_pick_when(text: str) -> dict:
    """Rows of the failure-mode table: | `mode` | Pick it when |"""
    out = {}
    for m in re.finditer(r"^\|\s*`(\w+)`\s*\|\s*(.+?)\s*\|\s*$", text, re.M):
        out[m.group(1)] = m.group(2)
    return out


def parse_modes(text: str) -> dict:
    """### <mode> sections: pass/fail definitions + numbered steps."""
    out = {}
    sections = re.split(r"^### ", text, flags=re.M)[1:]
    for sec in sections:
        name = sec.split("\n", 1)[0].strip()
        if name not in MODE_ORDER:
            continue
        body = sec.split("\n", 1)[1]
        # stop at the next ## heading if present
        body = re.split(r"^## ", body, flags=re.M)[0]
        p = re.search(r"\*\*pass\*\*:\s*(.+)", body)
        f = re.search(r"\*\*fail\*\*:\s*(.+)", body)
        steps = [m.group(1).strip() for m in re.finditer(r"^\s+\d+\.\s+(.+)$", body, re.M)]
        if name == "custom":
            # custom's steps are the "Render the procedure as:" list
            steps = [m.group(1).strip() for m in re.finditer(r"^\d+\.\s+(.+)$", body, re.M)] or steps
        out[name] = {
            "pass": p.group(1).strip() if p else None,
            "fail": f.group(1).strip() if f else None,
            "steps": steps,
        }
    return out


def parse_fixed_line(text: str, marker: str) -> str:
    m = re.search(re.escape(marker) + r".*?\n>\s*(.+?)(?:\n[^>]|\n$)", text, re.S)
    if not m:
        raise SystemExit(f"ERROR: fixed line {marker} not found in frame.md")
    return " ".join(line.strip().lstrip("> ").strip() for line in m.group(1).splitlines()).strip()


def parse_citations(text: str) -> dict:
    out = {}
    for m in re.finditer(r"^\|\s*(\w+)\s*\|\s*(.+?)\s*\|\s*(.+?)\s*\|\s*(\d{4})\s*\|\s*(\S+)\s*\|\s*$", text, re.M):
        key, title, authors, year, url = m.groups()
        if key == "Key":
            continue
        out[key] = {"title": title, "authors": authors, "year": int(year), "url": url}
    return out


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    args = ap.parse_args()

    frame = FRAME.read_text()
    cites = CITATIONS.read_text()

    modes = parse_modes(frame)
    pick_when = parse_pick_when(frame)
    missing = [k for k in MODE_ORDER if k not in modes]
    if missing:
        raise SystemExit(f"ERROR: modes missing from frame.md: {missing}")

    data = {
        "modes": {k: {**modes[k], "pickWhen": pick_when.get(k, "")} for k in MODE_ORDER},
        "modeOrder": MODE_ORDER,
        "fixedLines": {
            "reference": parse_fixed_line(frame, "**REFERENCE_GROUNDING_LINE**"),
            "length": parse_fixed_line(frame, "**LENGTH_LINE**"),
            "security": parse_fixed_line(frame, "**SECURITY_NOTE**"),
        },
        "citations": parse_citations(cites),
        # mapping mirrors frame.md#citation-mapping (web tool has no few-shot path)
        "citationMapping": {
            "always": ["Husain2024", "Yan2024", "Liu2023", "Miller2024"],
            "referenced": ["Kim2023", "Kim2024"],
            "pairwise": ["Zheng2023"],
            "swapAndAgree": ["Wang2023"],
            "lengthControl": ["Saito2023", "Dubois2024"],
            "crossFamilyJudge": ["Panickssery2024", "Verga2024"],
            "custom": ["Saha2023"],
        },
    }

    banner = (
        "// GENERATED FILE — do not edit by hand.\n"
        "// Source of truth: progress-observability-plugin/skills/generate-eval/references/\n"
        "//   frame.md (definitions, steps, fixed lines) + citations.md (research registry)\n"
        "// Regenerate: python scripts/build_web_frame.py --out <this file>\n"
    )
    out = Path(args.out)
    out.write_text(banner + "const FRAME = " + json.dumps(data, indent=1) + ";\n")
    print(f"Wrote {out} ({len(data['modes'])} modes, {len(data['citations'])} citations).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
