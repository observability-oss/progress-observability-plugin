#!/usr/bin/env bash
# Sync the shared skills from their canonical home into this repo.
#
# The canonical repo owns the skill content. This repo owns the packaging
# (plugin manifest, slash commands, Copilot bundle) and the e2e harness, and
# carries a copy of the skills so those can be built and tested. The copy is
# downstream: edit skills there, run this here.
#
# scaffold-agent is NOT synced - it lives only in this repo, because it creates
# projects rather than reading the platform and is deliberately excluded from
# the canonical set.
#
# Usage:
#   scripts/sync_canonical_skills.sh            # write the copies
#   scripts/sync_canonical_skills.sh --check    # exit 1 if anything would change
#   CANONICAL=/path/to/checkout scripts/sync_canonical_skills.sh   # skip the clone
#   CANONICAL_URL=https://github.com/<org>/<repo>.git ...          # different home
#
# Exit codes: 0 in sync (or synced), 1 drift found in --check, 2 setup failure.

set -euo pipefail

REPO_URL="${CANONICAL_URL:-https://github.com/observability-oss/observability-skills.git}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CHECK=0
[ "${1:-}" = "--check" ] && CHECK=1

# The canonical set. scaffold-agent is absent on purpose - see header.
SKILLS=(cost-report coverage-gaps generate-eval health-check instrument-agent trace-triage)
SHARED=(references/mcp.md references/mcp-schema.json)

# Copy into a staging tree first, so --check never mutates the working copy and
# a failure part-way through cannot leave the repo half-synced.
STAGE=$(mktemp -d)
CLONE=""
# One trap, set once: a second `trap ... EXIT` replaces the first rather than
# adding to it, which is how a temp clone gets left behind.
cleanup() { rm -rf "$STAGE" ${CLONE:+"$CLONE"}; }
trap cleanup EXIT

if [ -n "${CANONICAL:-}" ]; then
  SRC="$CANONICAL"
  [ -d "$SRC/skills" ] || { echo "ERROR: CANONICAL=$SRC has no skills/ directory" >&2; exit 2; }
else
  CLONE=$(mktemp -d)
  SRC="$CLONE"
  git clone --depth 1 -q "$REPO_URL" "$SRC" || { echo "ERROR: could not clone $REPO_URL" >&2; exit 2; }
fi

for s in "${SKILLS[@]}"; do
  [ -d "$SRC/skills/$s" ] || { echo "ERROR: canonical repo is missing skills/$s" >&2; exit 2; }
  mkdir -p "$STAGE/skills"
  cp -r "$SRC/skills/$s" "$STAGE/skills/"
done
mkdir -p "$STAGE/references"
for f in "${SHARED[@]}"; do
  [ -f "$SRC/$f" ] || { echo "ERROR: canonical repo is missing $f" >&2; exit 2; }
  cp "$SRC/$f" "$STAGE/$f"
done

drift=()
for s in "${SKILLS[@]}"; do
  diff -rq "$STAGE/skills/$s" "$ROOT/skills/$s" >/dev/null 2>&1 || drift+=("skills/$s")
done
for f in "${SHARED[@]}"; do
  diff -q "$STAGE/$f" "$ROOT/$f" >/dev/null 2>&1 || drift+=("$f")
done

if [ ${#drift[@]} -eq 0 ]; then
  echo "In sync with $REPO_URL"
  exit 0
fi

if [ "$CHECK" -eq 1 ]; then
  {
    echo "ERROR: this repo has drifted from the canonical skills:"
    printf '  %s\n' "${drift[@]}"
    echo
    echo "Canonical: $REPO_URL"
    echo "If the change belongs in the skills, make it there and re-run the sync."
    echo "If it was made here by mistake, run: scripts/sync_canonical_skills.sh"
  } >&2
  exit 1
fi

for s in "${SKILLS[@]}"; do
  rm -rf "${ROOT:?}/skills/$s"
  cp -r "$STAGE/skills/$s" "$ROOT/skills/"
done
for f in "${SHARED[@]}"; do
  cp "$STAGE/$f" "$ROOT/$f"
done
printf 'synced: %s\n' "${drift[@]}"

# Regenerate what is derived from the skills, or the next CI run fails on a
# staleness check instead of on the thing that actually changed.
python3 "$ROOT/scripts/sync_skill_refs.py" >/dev/null
python3 "$ROOT/scripts/build_copilot.py" >/dev/null
echo "regenerated: skill mcp.md copies, copilot-instructions.md"
