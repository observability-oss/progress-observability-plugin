#!/usr/bin/env bash
# Structural test for the instrument-agent skill, across all three fixture
# repos. Runs the skill headless over every uninstrumented fixture and judges
# the result against the reference solution in expected/<fixture>.
#
# NO CREDENTIALS AND NO NETWORK CALLS TO THE PLATFORM. This tests detect+wire
# only - it never runs the instrumented app and never reads MCP. That makes it
# the half of the loop you can run anywhere, including for .NET where you may
# not want to spend model calls. For the full end-to-end (run the app, assert
# spans over MCP) use each repo's own e2e path.
#
# Why a judge instead of `diff`: expected/ carries explanatory comments the
# skill will not reproduce word-for-word, so an exact diff fails every time.
# The criteria below are what actually matter; a model applies them.
#
# Usage:
#   ./scripts/skill_structural_test.sh                      # clones all three, tests all
#   USE_LOCAL=1 ./scripts/skill_structural_test.sh          # use ~/instrument-test-*
#   ./scripts/skill_structural_test.sh ~/instrument-test-python
#   ./scripts/skill_structural_test.sh ~/instrument-test-python keyless langchain
#
# Requires: claude CLI, git, jq.
# Fixture edits are reverted as it goes; nothing is committed or pushed.

set -uo pipefail

PLUGIN_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SKILL_DIR="$PLUGIN_ROOT/skills/instrument-agent"
[ -d "$SKILL_DIR" ] || { echo "ERROR: skill not found at $SKILL_DIR"; exit 2; }
command -v jq >/dev/null || { echo "ERROR: jq not found"; exit 2; }
# shellcheck source=lib/agent.sh
WORKDIR=$(mktemp -d)
trap 'rm -rf "$WORKDIR"' EXIT
# shellcheck source=lib/agent.sh
. "$PLUGIN_ROOT/scripts/lib/agent.sh"
echo "agent: $AGENT"

# Criteria the judge applies. Kept here, in one place, so this file and the
# weekly Routine cannot drift apart on what "correct" means.
read -r -d '' CRITERIA <<'EOF'
GENERAL (all fixtures)
- Only instrumentation changed: dependency, import, init, env wiring,
  decorators where needed. App logic untouched.
- Init runs before any LLM client is constructed.
- The key comes from the environment. No hardcoded keys, no invented version pins.

PYTHON
- Observability.instrument() placed BEFORE the framework import, not merely
  before a client call - every framework fixture builds its objects at module
  scope, so import order is the whole test. Moved imports carry noqa: E402.
- httpx declared in requirements (traceloop-sdk imports it without declaring it).
- No decorators added to auto-instrumented frameworks.
- META PACKAGE (langchain, llamaindex only): traceloop gates the instrumentor on
  a distribution name. langchain-core alone does NOT enable it, nor does
  llama-index-core. The skill must add "langchain" / "llama-index" to
  requirements.txt. Wiring perfectly but omitting this is a FAIL: the user gets
  LLM spans and no structure, silently. Score this separately.
- HAYSTACK IS THE EXCEPTION to init-before-import. Correct order is:
  `import haystack.tracing`, THEN Observability.instrument(), THEN an explicit
  haystack.tracing.auto_enable_tracing(), THEN the `from haystack import ...`
  lines. Plain init-before-all-haystack-imports is a FAIL - it reintroduces a
  spurious errored auto_enable trace on every run.

TYPESCRIPT
- ESM ("type": "module") needs the register/hooks import first, or a bootstrap
  entry; LangChain must be imported dynamically after init.
- await Observability.shutdown() before exit.

DOTNET
- .AddObservability() sits between AsIChatClient() and AsAIAgent().
- ObservabilityTracer.Initialize early, Shutdown in finally.
EOF

judge_one() {   # repo, fixture, diff
  local repo="$1" fixture="$2" diff="$3"
  local expected
  expected=$(cd "$repo" && find "expected/$fixture" -type f \
    \( -name '*.py' -o -name '*.ts' -o -name '*.cs' -o -name '*.json' -o -name '*.txt' -o -name '*.csproj' \) \
    -exec sh -c 'echo "--- {} ---"; cat "{}"' \; 2>/dev/null)

  # The judge is always Claude when available (structured output); with only
  # Copilot present we fall back to its plain stdout.
  local judge_out
  if command -v claude >/dev/null 2>&1; then
    judge_out=$(claude -p --output-format json --allowedTools '' <<EOF 2>/dev/null | jq -r '.result' 2>/dev/null
You are grading one run of an instrumentation skill. Do not use tools; judge from the text.

CRITERIA:
$CRITERIA

REFERENCE SOLUTION (expected/$fixture):
$expected

WHAT THE SKILL ACTUALLY PRODUCED (git diff of agents/$fixture):
$diff

Reply with exactly one line:
PASS <short reason>
or
FAIL <the specific criterion missed>
Wording need not match the reference; only the criteria above matter. If the
diff is empty, that is a FAIL - the skill changed nothing.
EOF
)
  else
    # -s prints only the agent response (no stats) - that is what makes this
    # parseable. --allow-all-tools is required for non-interactive mode even
    # though the prompt tells it not to use tools.
    judge_out=$(copilot -p "Grade this, using no tools. CRITERIA: $CRITERIA
REFERENCE: $expected
PRODUCED: $diff
Reply with exactly one line: PASS <reason> or FAIL <criterion missed>." \
      --allow-all-tools -s --no-color --log-level none 2>/dev/null \
      | grep -m1 -E '^(PASS|FAIL)')
  fi
  echo "$judge_out"
}

run_repo() {
  local repo="$1"; shift
  [ -d "$repo/agents" ] || { echo "SKIP $repo (no agents/)"; return; }
  # No mapfile: macOS still ships bash 3.2.
  local fixtures=("$@")
  if [ ${#fixtures[@]} -eq 0 ]; then
    fixtures=()
    for d in "$repo"/agents/*/; do fixtures+=("$(basename "$d")"); done
  fi

  echo ""
  echo "=============================================================="
  echo "  $(basename "$repo")  (${#fixtures[@]} fixtures)"
  echo "=============================================================="

  install_skill "$repo"

  for f in "${fixtures[@]}"; do
    [ -d "$repo/agents/$f" ] || { printf '%-14s SKIP (no such fixture)\n' "$f"; continue; }
    (cd "$repo" && git checkout -- agents/ 2>/dev/null)

    hide_answer_key "$repo"
    run_agent "$repo" "$SKILL_PROMPT_PREFIX agents/$f. Detect and wire only: do NOT run the app and do NOT verify over MCP - there are no credentials here. Make the file edits and stop."

    restore_answer_key "$repo"
    local diff verdict
    diff=$(cd "$repo" && git --no-pager diff -- "agents/$f"; cd "$repo" && git status --porcelain -- "agents/$f" | grep '^??' | while read -r _ nf; do echo "--- NEW FILE $nf ---"; cat "$nf"; done)
    if [ -z "$diff" ]; then
      printf '%-14s FAIL  skill made no changes\n' "$f"
    else
      verdict=$(judge_one "$repo" "$f" "$diff")
      printf '%-14s %s\n' "$f" "${verdict:-FAIL  judge returned nothing}"
    fi
    (cd "$repo" && git checkout -- agents/ 2>/dev/null)
  done

  uninstall_skill "$repo"
}

if [ $# -gt 0 ] && [ -d "$1" ]; then
  repo="$1"; shift
  run_repo "$repo" "$@"
else
  found=0
  for r in instrument-test-python instrument-test-typescript instrument-test-dotnet; do
    path=$(resolve_repo "$r") && { run_repo "$path"; found=1; } || echo "SKIP $r (clone failed, or USE_LOCAL set with no checkout)"
  done
  [ "$found" -eq 1 ] || { echo "No fixture repos available."; exit 2; }
fi

echo ""
echo "Done. Fixture edits reverted; nothing committed."
