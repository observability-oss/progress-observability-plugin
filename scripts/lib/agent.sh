#!/usr/bin/env bash
# Shared agent plumbing for the skill test scripts, so they work with either
# Claude Code or GitHub Copilot CLI. Sourced, not executed.
#
# Both read the plugin's own skills/ folder - no repackaging - but from their
# own conventional directory, which is also what the public install docs tell
# people to use, so this script tests the documented path:
#   claude  -> .claude/skills/
#   copilot -> .github/skills/   (it also accepts .agents/skills or .claude/skills)
# (The copilot/ bundle in this repo is for VS Code Copilot, a different surface.)
#
# Pick one explicitly with AGENT=claude|copilot, otherwise whichever is on PATH.
#
# Both invocations are verified against real `--help` output (Copilot CLI
# 1.0.75). --allow-all-tools is REQUIRED for Copilot's non-interactive mode;
# without it the agent blocks on a permission prompt and the script hangs.
# Override either without editing this file:
#
#   export AGENT_CMD='copilot -p {PROMPT} --allow-all --model gpt-5.4'
#
# {PROMPT} is replaced with the prompt as a single argv element.

AGENT="${AGENT:-}"
if [ -z "$AGENT" ]; then
  if   command -v claude  >/dev/null 2>&1; then AGENT=claude
  elif command -v copilot >/dev/null 2>&1; then AGENT=copilot
  else
    echo "ERROR: no agent CLI found. Install one:" >&2
    echo "  npm install -g @anthropic-ai/claude-code   # then: claude" >&2
    echo "  npm install -g @github/copilot             # then: copilot" >&2
    exit 2
  fi
fi
command -v "$AGENT" >/dev/null 2>&1 || { echo "ERROR: AGENT=$AGENT but '$AGENT' is not on PATH"; exit 2; }

# Default headless invocations. Override with AGENT_CMD.
if [ -z "${AGENT_CMD:-}" ]; then
  case "$AGENT" in
    claude)  AGENT_CMD='claude -p {PROMPT} --permission-mode acceptEdits' ;;
    copilot) AGENT_CMD='copilot -p {PROMPT} --allow-all-tools --no-color --log-level none' ;;
  esac
fi

# skill_dir <repo> - where this agent looks for skills
skill_dir() {
  case "$AGENT" in
    copilot) echo "$1/.github/skills" ;;
    *)       echo "$1/.claude/skills" ;;
  esac
}

install_skill() {
  local dir; dir=$(skill_dir "$1")
  mkdir -p "$dir"
  rm -rf "$dir/instrument-agent"
  cp -r "$PLUGIN_ROOT/skills/instrument-agent" "$dir/"
}

uninstall_skill() {
  local dir; dir=$(skill_dir "$1")
  rm -rf "$dir/instrument-agent"
  rmdir "$dir" 2>/dev/null || true
}

# run_agent <repo> <prompt>  - drive the agent headlessly in that repo.
# DEBUG=1 shows the agent's own output instead of swallowing it - use it the
# first time you run against a new CLI, otherwise a permission prompt or a bad
# flag just looks like a hang.
run_agent() {
  local repo="$1" prompt="$2"
  local argv=()
  local tok
  for tok in $AGENT_CMD; do
    if [ "$tok" = "{PROMPT}" ]; then argv+=("$prompt"); else argv+=("$tok"); fi
  done
  # Copilot refuses ABSOLUTE reads outside its allowlist even when they are
  # inside its own cwd - observed as "Permission denied and could not request
  # permission from user" on the fixture's own requirements.txt, followed by a
  # successful relative read. It recovers, but burns a round trip each time.
  # --add-dir puts the repo on the allowlist explicitly.
  if [ "$AGENT" = copilot ] && [ "${AGENT_CMD#*--add-dir}" = "$AGENT_CMD" ]; then
    argv+=(--add-dir "$repo")
  fi
  # Always keep the agent's output: a failure you cannot explain without
  # re-running is a failure that wastes a premium request to diagnose.
  # AGENT_LOG is set by the caller; DEBUG=1 additionally streams it.
  local log="${AGENT_LOG:-/dev/null}"
  if [ -n "${DEBUG:-}" ]; then
    echo "   \$ ${argv[*]:0:1} -p <prompt> ${argv[*]:3}" >&2
    (cd "$repo" && "${argv[@]}" 2>&1 | tee "$log")
  else
    (cd "$repo" && "${argv[@]}" >"$log" 2>&1)
  fi
}

# changed_files <repo> <path> [base_sha] - non-empty if the agent touched anything.
#
# Two traps this avoids:
#  - `git diff` alone is blind to NEW files, so a skill that creates one (a
#    bootstrap entry point, a requirements file) looks like it did nothing.
#  - agents sometimes COMMIT their work, after which `git status` is clean and
#    the run looks like a no-op. Hence the diff against a pinned base commit.
changed_files() {
  local repo="$1" path="$2" base="${3:-}"
  {
    (cd "$repo" && git status --porcelain -- "$path" 2>/dev/null)
    [ -n "$base" ] && (cd "$repo" && git diff --name-only "$base" -- "$path" 2>/dev/null)
  }
}

SKILL_PROMPT_PREFIX="Use the instrument-agent skill on"

# ---------------------------------------------------------------------------
# The fixture repos keep the reference solution in expected/<fixture>, in the
# same working tree as the uninstrumented agents/<fixture>. Agents read it:
# observed runs opening expected/llamaindex/requirements.txt and
# expected/mcp/app.py before writing their own answer. A fixture solved by
# copying the answer key tells us nothing about the skill, and worse, it hides
# exactly the failures we are hunting - the run that peeked got the meta
# package right while the run that did not got it wrong.
#
# So expected/ is moved out of the tree for the duration of the agent's turn
# and restored before anything needs to diff against it.
#
# README.md goes with it, for the same reason and with better evidence. These
# READMEs document the solutions: the wiring per fixture, the expected span
# names, the service-name convention, and - since the .NET one gained a "The
# trap in hosted" section - working code for the one placement the fixtures
# exist to test. A Copilot run on agents/hosted read exactly that range and
# then cited it, so hosted proved nothing about the skill that day. Corroborated
# in the same run: the two fixtures that did not open the README invented their
# own app_name slugs, the two that did used the README's convention.
#
# A real project's README does not explain how to instrument that project. Ours
# does, which makes it an answer key under a different filename.
hide_answer_key() {
  local repo="$1"
  local stash="$WORKDIR/.answer-key-$(basename "$repo")"
  mkdir -p "$stash"
  [ -d "$repo/expected" ] && mv "$repo/expected" "$stash/expected"
  [ -f "$repo/README.md" ] && mv "$repo/README.md" "$stash/README.md"
  return 0
}

restore_answer_key() {
  local repo="$1"
  local stash="$WORKDIR/.answer-key-$(basename "$repo")"
  [ -d "$stash" ] || return 0
  if [ -d "$stash/expected" ]; then
    rm -rf "$repo/expected"
    mv "$stash/expected" "$repo/expected"
  fi
  [ -f "$stash/README.md" ] && mv "$stash/README.md" "$repo/README.md"
  rmdir "$stash" 2>/dev/null
  return 0
}

# ---------------------------------------------------------------------------
# Fixture repo resolution.
#
# Default: shallow-clone each fixture repo into this run's temp dir. That is
# the safer default for two reasons - the scripts edit agents/ and revert with
# `git checkout`, so a run that dies partway would otherwise leave YOUR
# checkout dirty; and a fresh clone tests the committed state, which is what
# CI and the weekly Routine see.
#
# USE_LOCAL=1 uses your existing checkouts in $HOME instead - what you want
# when iterating on a fixture that is not pushed yet.
REPO_ORG="${REPO_ORG:-observability-oss}"

resolve_repo() {   # <repo-name> -> prints path, or returns 1
  local name="$1"
  if [ -n "${USE_LOCAL:-}" ]; then
    local b
    for b in "$HOME" "$(dirname "$PLUGIN_ROOT")"; do
      [ -d "$b/$name" ] && { echo "$b/$name"; return 0; }
    done
    return 1
  fi
  local dest="$WORKDIR/$name"
  [ -d "$dest" ] && { echo "$dest"; return 0; }
  git clone -q --depth 1 "https://github.com/$REPO_ORG/$name" "$dest" 2>/dev/null || return 1
  echo "$dest"
}
