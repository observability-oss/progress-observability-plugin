#!/usr/bin/env python3
"""Generate the classic Copilot instruction file and skill payloads.

Single source of truth: the MCP contract, the skill workflows, and the eval frame
each live in one place. This script lifts the region between
`<!-- copilot:start -->` and `<!-- copilot:end -->` from each source, demotes its
headings one level so they nest under the template's sections, and substitutes it
into the `<!-- include:KEY -->` slots in copilot-instructions.template.md. Skills
that carry executable assets are also mirrored byte-for-byte into the classic
Copilot bundle.

Usage:
  python scripts/build_copilot.py           # write the generated file
  python scripts/build_copilot.py --check    # exit 1 if the file is out of date
"""

import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TEMPLATE = ROOT / "scripts" / "copilot-instructions.template.md"
OUTPUT = ROOT / "copilot" / ".github" / "copilot-instructions.md"
MIRRORED_SKILL_TREES = {
    ROOT / "skills" / "build-template-agent": (
        ROOT / "copilot" / "skills" / "build-template-agent"
    ),
}
EXCLUDED_TREE_PARTS = frozenset({"bin", "obj", "__pycache__", ".DS_Store"})

# include KEY -> (source path relative to repo root, heading demotion levels)
SOURCES = {
    "MCP_CONTRACT": ("references/mcp.md", 1),
    "HEALTH_CHECK": ("skills/health-check/SKILL.md", 1),
    "BUILD_TEMPLATE_AGENT": ("skills/build-template-agent/SKILL.md", 1),
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


def is_excluded_tree_part(name: str) -> bool:
    return (
        name in EXCLUDED_TREE_PARTS
        or name == ".env"
        or (name.startswith(".env.") and name != ".env.example")
    )


def ignored_tree_names(_directory: str, names: list[str]) -> list[str]:
    return [name for name in names if is_excluded_tree_part(name)]


def tree_files(root: Path) -> dict[str, bytes]:
    if not root.is_dir():
        raise SystemExit(f"ERROR: missing skill payload directory {root}")

    result: dict[str, bytes] = {}
    for path in sorted(root.rglob("*")):
        relative = path.relative_to(root)
        if any(is_excluded_tree_part(part) for part in relative.parts):
            continue
        if path.is_symlink():
            raise SystemExit(
                f"ERROR: generated Copilot payload cannot contain symlink {path}"
            )
        if path.is_file():
            result[relative.as_posix()] = path.read_bytes()
    return result


def write_skill_payload(source: Path, output: Path) -> None:
    if output.exists():
        shutil.rmtree(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(
        source,
        output,
        ignore=ignored_tree_names,
    )


def main() -> int:
    generated = build()
    check = "--check" in sys.argv[1:]
    current = OUTPUT.read_text() if OUTPUT.exists() else None
    stale_payloads = [
        output
        for source, output in MIRRORED_SKILL_TREES.items()
        if not output.is_dir() or tree_files(output) != tree_files(source)
    ]
    if check:
        stale = []
        if current != generated:
            stale.append(OUTPUT)
        stale.extend(stale_payloads)
        if not stale:
            print("Copilot instructions and skill payloads are up to date.")
            return 0
        print(
            "ERROR: generated Copilot files are out of date:\n  "
            + "\n  ".join(str(path.relative_to(ROOT)) for path in stale)
            + "\n"
            "Run `python scripts/build_copilot.py` and commit the result.",
            file=sys.stderr,
        )
        return 1
    OUTPUT.write_text(generated)
    for source, output in MIRRORED_SKILL_TREES.items():
        write_skill_payload(source, output)
    print(f"Wrote {OUTPUT.relative_to(ROOT)} ({generated.count(chr(10)) + 1} lines).")
    for output in MIRRORED_SKILL_TREES.values():
        print(f"Wrote {output.relative_to(ROOT)} ({len(tree_files(output))} files).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
