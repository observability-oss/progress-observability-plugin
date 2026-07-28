#!/usr/bin/env python3
"""Generate copilot/.github/copilot-instructions.md from the plugin's sources.

Single source of truth: the MCP contract, the skill workflows, and the eval frame
each live in one place. This script lifts the region between
`<!-- copilot:start -->` and `<!-- copilot:end -->` from each source, demotes its
headings one level so they nest under the template's sections, and substitutes it
into the `<!-- include:KEY -->` slots in copilot-instructions.template.md.

Usage:
  python scripts/build_copilot.py           # write the generated file
  python scripts/build_copilot.py --check    # exit 1 if the file is out of date
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "scripts" / "copilot-instructions.template.md"
OUTPUT = ROOT / "copilot" / ".github" / "copilot-instructions.md"

# include KEY -> (source path relative to repo root, heading demotion levels)
SOURCES = {
    "MCP_CONTRACT": ("references/mcp.md", 1),
    "HEALTH_CHECK": ("skills/health-check/SKILL.md", 1),
    "SCAFFOLD_AGENT": ("skills/scaffold-agent/SKILL.md", 1),
    "INSTRUMENT_AGENT": ("skills/instrument-agent/SKILL.md", 1),
    "TRACE_TRIAGE": ("skills/trace-triage/SKILL.md", 1),
    "COST_REPORT": ("skills/cost-report/SKILL.md", 1),
    "COVERAGE_GAPS": ("skills/coverage-gaps/SKILL.md", 1),
    "EVAL_FRAME": ("skills/generate-eval/references/frame.md", 1),
}

START = "<!-- copilot:start -->"
END = "<!-- copilot:end -->"
INCLUDE_RE = re.compile(r"^<!-- include:(\w+) -->$", re.MULTILINE)
HEADING_RE = re.compile(r"^(#{1,6})(\s)", re.MULTILINE)


def extract(path: Path) -> str:
    text = path.read_text()
    if START not in text or END not in text:
        raise SystemExit(f"ERROR: {path} is missing copilot:start/end markers")
    body = text.split(START, 1)[1].split(END, 1)[0]
    return body.strip("\n")


def demote(md: str, levels: int) -> str:
    if levels <= 0:
        return md
    return HEADING_RE.sub(lambda m: "#" * min(len(m.group(1)) + levels, 6) + m.group(2), md)


def build() -> str:
    template = TEMPLATE.read_text()

    def sub(m):
        key = m.group(1)
        if key not in SOURCES:
            raise SystemExit(f"ERROR: template references unknown include key '{key}'")
        rel, levels = SOURCES[key]
        return demote(extract(ROOT / rel), levels)

    result = INCLUDE_RE.sub(sub, template)
    # Guard: every include slot must have been filled.
    leftover = INCLUDE_RE.search(result)
    if leftover:
        raise SystemExit(f"ERROR: unresolved include '{leftover.group(1)}'")
    return result.rstrip("\n") + "\n"


def main() -> int:
    generated = build()
    check = "--check" in sys.argv[1:]
    current = OUTPUT.read_text() if OUTPUT.exists() else None
    if check:
        if current == generated:
            print("copilot-instructions.md is up to date.")
            return 0
        print(
            "ERROR: copilot-instructions.md is out of date.\n"
            "Run `python scripts/build_copilot.py` and commit the result.",
            file=sys.stderr,
        )
        return 1
    OUTPUT.write_text(generated)
    print(f"Wrote {OUTPUT.relative_to(ROOT)} ({generated.count(chr(10)) + 1} lines).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
