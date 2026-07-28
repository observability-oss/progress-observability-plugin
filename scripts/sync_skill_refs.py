#!/usr/bin/env python3
"""Copy the shared MCP contract into each MCP-reading skill's references/ folder.

Single source of truth: references/mcp.md at the repo root. Each skill that reads
the platform carries a copy at skills/<name>/references/mcp.md so the skill is
fully self-contained — installable on its own via `npx skills add` (skills.sh),
`cp -r`, or any tool that takes one skill directory without the rest of the repo.

Usage:
  python scripts/sync_skill_refs.py           # write the copies
  python scripts/sync_skill_refs.py --check   # exit 1 if any copy is stale
"""

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "references" / "mcp.md"

# scaffold-agent makes no MCP calls and is intentionally excluded.
# instrument-agent writes code but verifies over MCP, so it carries the contract.
MCP_SKILLS = ["trace-triage", "cost-report", "coverage-gaps", "generate-eval", "health-check", "instrument-agent"]

BANNER = (
    "<!-- GENERATED COPY — do not edit. Source of truth: references/mcp.md at the\n"
    "     repo root. Regenerate: python scripts/sync_skill_refs.py -->\n\n"
)


def main() -> int:
    check = "--check" in sys.argv[1:]
    expected = BANNER + SOURCE.read_text()
    stale = []
    for name in MCP_SKILLS:
        target = ROOT / "skills" / name / "references" / "mcp.md"
        if check:
            if not target.exists() or target.read_text() != expected:
                stale.append(str(target.relative_to(ROOT)))
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(expected)
            print(f"wrote {target.relative_to(ROOT)}")
    if check:
        if stale:
            print("ERROR: stale skill copies of references/mcp.md:", *stale, sep="\n  ", file=sys.stderr)
            print("Run `python scripts/sync_skill_refs.py` and commit.", file=sys.stderr)
            return 1
        print("All skill copies of references/mcp.md are in sync.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
