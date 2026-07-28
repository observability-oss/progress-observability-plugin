#!/usr/bin/env bash
# FULL end-to-end skill test across all three fixture repos: run the skill on an
# uninstrumented fixture, then actually run the instrumented app (real LLM call
# where the fixture makes one) and assert the spans arrived by querying the
# Progress Observability MCP server with that repo's own harness.
#
# This is the real thing, not a structural judge. It proves the skill's output
# WORKS, not just that it looks right. For the credential-free half - detect and
# wire only, judged against expected/ - use skill_structural_test.sh instead.
#
# Run and verify commands here are lifted from each repo's e2e workflow, so this
# and CI stay honest with each other. The difference: CI runs expected/ (the
# reference solution, proving the pipeline), this runs agents/ AFTER the skill
# has edited it (proving the skill).
#
# Service names get a -skill suffix so these runs never collide with CI's.
#
# Usage:
#   export OBSERVABILITY_API_KEY="ac_p_..."      # Integration key - the app writes
#   export OBSERVABILITY_MCP_API_KEY="acm_..."   # MCP key - the harness reads
#   export OPENROUTER_API_KEY="sk-or-..."        # optional; LLM fixtures skip without it
#   ./scripts/skill_e2e_all.sh                   # everything available
#   USE_LOCAL=1 ./scripts/skill_e2e_all.sh python   # use ~/instrument-test-*
#   ./scripts/skill_e2e_all.sh python            # one language
#   ./scripts/skill_e2e_all.sh python keyless    # one fixture
#
# Requires: claude CLI, git, and the toolchain for whichever languages you run
# (python3+pip, node 22+, dotnet 8). Missing toolchains are skipped, not failed.
# NOTE: this cannot run inside the Claude Code sandbox - the collector and
# mcp.observability.progress.com are proxy-blocked there. Run it on your machine.

set -uo pipefail

PLUGIN_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SKILL_DIR="$PLUGIN_ROOT/skills/instrument-agent"

# shellcheck source=lib/agent.sh
. "$PLUGIN_ROOT/scripts/lib/agent.sh"
echo "agent: $AGENT"
[ -n "${OBSERVABILITY_API_KEY:-}"     ] || { echo "ERROR: OBSERVABILITY_API_KEY not set (Integration key, ac_p_...)"; exit 2; }
[ -n "${OBSERVABILITY_MCP_API_KEY:-}" ] || { echo "ERROR: OBSERVABILITY_MCP_API_KEY not set (MCP key, acm_...)"; exit 2; }

WORKDIR=$(cd "$(mktemp -d)" && pwd -P)   # -P: resolve /var -> /private/var, or the
                                         # agent hits "Permission denied" on its own cwd
# KEEP_LOGS=<dir> sends the per-fixture agent/app logs somewhere durable and
# predictable, written AS THEY HAPPEN - so you can tail them from another
# terminal while the run is going, and still have them afterwards. Without it
# they live in the temp dir and vanish on exit.
LOGDIR="${KEEP_LOGS:-$WORKDIR}"
mkdir -p "$LOGDIR"
if [ -n "${KEEP_LOGS:-}" ]; then
  # Clear last run's files. Otherwise `tail -f $LOGDIR/*.log` shows a mix of
  # this run and the previous one, and stale output reads as current - which is
  # worse than no output at all.
  rm -f "$LOGDIR"/agent-*.log "$LOGDIR"/run-*.log "$LOGDIR"/diff-*.patch
  echo "logs: $LOGDIR  (cleared; tail -f $LOGDIR/*.log)"
fi
trap 'rm -rf "$WORKDIR"' EXIT

PASS=0; FAIL=0; SKIP=0
declare -a RESULTS

# fixture | needs_llm | expect
PY_FIXTURES=(
  "keyless|no|triage,classify,route,fetch_ticket"
  "openai|yes|openai.chat"
  "langchain|yes|RunnableSequence,execute_task,llm_call"
  "langgraph|yes|LangGraph.workflow,execute_task,llm_call"
  "crewai|yes|crewai.workflow,.agent,task,llm_call"
  "openai-agents|yes|Agent Workflow,.agent,llm_call"
  "llamaindex|yes|SummaryIndexRetriever.task,CompactAndRefine.task,llm_call"
  "agno|yes|.agent,llm_call"
  "haystack|yes|haystack.pipeline.run,haystack.component.run,llm_call"
  "mcp|yes|initialize.mcp,.tool,llm_call"
)
TS_FIXTURES=(
  "keyless|no|triage,classify,route,fetchTicket"
  "openai|yes|llm_call"
  "langchain|yes|RunnableSequence,llm_call"
)
NET_FIXTURES=(
  "keyless|no|llm_call"
  "openrouter|yes|llm_call"
  "agent|yes|llm_call,tool"
  # tool is the load-bearing half here: .AddObservability() placed before
  # UseFunctionInvocation() still yields llm_call, and loses every tool span.
  "hosted|yes|llm_call,tool"
)

record() { RESULTS+=("$(printf '%-8s %-16s %s' "$1" "$2" "$3")"); }

run_lang() {
  local lang="$1"; shift
  local want=("$@")
  local repo prefix
  case "$lang" in
    python) repo=$(resolve_repo instrument-test-python);     prefix="ins-test-py" ;;
    ts)     repo=$(resolve_repo instrument-test-typescript); prefix="ins-test-ts" ;;
    dotnet) repo=$(resolve_repo instrument-test-dotnet);     prefix="ins-test-dotnet" ;;
  esac
  [ -n "$repo" ] || { echo "SKIP $lang: could not obtain instrument-test-$lang (clone failed, or USE_LOCAL set with no checkout)"; return; }
  echo "repo: $repo"

  # No namerefs / mapfile anywhere in this script: macOS still ships bash 3.2.
  local fixtures
  case "$lang" in
    python) command -v python3 >/dev/null || { echo "SKIP python: no python3"; return; }
            fixtures=$(printf '%s\n' "${PY_FIXTURES[@]}") ;;
    ts)     command -v node    >/dev/null || { echo "SKIP ts: no node"; return; }
            fixtures=$(printf '%s\n' "${TS_FIXTURES[@]}") ;;
    dotnet) command -v dotnet  >/dev/null || { echo "SKIP dotnet: no dotnet sdk"; return; }
            fixtures=$(printf '%s\n' "${NET_FIXTURES[@]}") ;;
  esac

  echo ""
  echo "=============================================================="
  echo "  $lang  ($(basename "$repo"))"
  echo "=============================================================="

  install_skill "$repo"
  # Agents sometimes commit their work (Copilot does). After a commit
  # `git status` is clean and `git checkout --` restores nothing, so detection
  # and cleanup must both work against a pinned base commit instead.
  local base_sha; base_sha=$(cd "$repo" && git rev-parse HEAD)

  while IFS='|' read -r f needs_llm expect; do
    [ -n "$f" ] || continue
    if [ ${#want[@]} -gt 0 ] && [[ ! " ${want[*]} " =~ " $f " ]]; then continue; fi
    [ -d "$repo/agents/$f" ] || { record "$lang" "$f" "SKIP  no such fixture"; ((SKIP++)); continue; }
    if [ "$needs_llm" = yes ] && [ -z "${OPENROUTER_API_KEY:-}${OPENAI_API_KEY:-}" ]; then
      record "$lang" "$f" "SKIP  needs OPENROUTER_API_KEY or OPENAI_API_KEY"; ((SKIP++)); continue
    fi

    local svc="${prefix}-${f}-skill"
    echo "-- $f -> $svc"
    (cd "$repo" && git reset -q --hard "$base_sha" && git clean -fdq agents/ 2>/dev/null)

    # 1. let the skill instrument it (wire only; we run and verify ourselves)
    AGENT_LOG="$LOGDIR/agent-$lang-$f.log"
    hide_answer_key "$repo"
    run_agent "$repo" "$SKILL_PROMPT_PREFIX agents/$f. Detect and wire only - do NOT run the app and do NOT verify over MCP, this script does that. Make the file edits and stop. Do NOT run git commands and do NOT commit - the harness manages version control."
    restore_answer_key "$repo"
    if [ -z "$(changed_files "$repo" "agents/$f" "$base_sha")" ]; then
      record "$lang" "$f" "FAIL  no changes | $(tail -2 "$AGENT_LOG" 2>/dev/null | tr '\n' ' ' | cut -c1-160)"
      ((FAIL++)); continue
    fi

    # Keep what the skill actually wrote. The clone is deleted on exit, so
    # without this a run tells you the verdict but never the diff behind it -
    # and re-running to see it costs another premium request.
    {
      (cd "$repo" && git --no-pager diff "$base_sha" -- "agents/$f")
      (cd "$repo" && git status --porcelain -- "agents/$f" | grep '^??' | while read -r _ nf; do
         echo "--- NEW FILE $nf ---"; cat "$repo/$nf"; done)
    } > "$LOGDIR/diff-$lang-$f.patch" 2>/dev/null

    local since; since=$(date -u +%Y-%m-%dT%H:%M:%SZ)
    local ok=1
    local RUNLOG="$LOGDIR/run-$lang-$f.log"

    # 2. install + run the instrumented app for real
    case "$lang" in
      python) # One venv per fixture: the ten Python fixtures have genuinely
              # conflicting dependency trees (crewai/litellm vs llamaindex vs
              # haystack), so a shared env breaks partway through. Also keeps
              # macOS system Python untouched.
              VENV="$WORKDIR/venv-$f"
              python3 -m venv "$VENV" >"$RUNLOG" 2>&1 || ok=0
              "$VENV/bin/pip" install -q -r "$repo/agents/$f/requirements.txt" certifi >>"$RUNLOG" 2>&1 || ok=0
              (cd "$repo" && OBSERVABILITY_APP_NAME="$svc" "$VENV/bin/python" "agents/$f/app.py" >>"$RUNLOG" 2>&1) || ok=0 ;;
      ts)     (cd "$repo/agents/$f" && npm install --silent >"$RUNLOG" 2>&1) || ok=0
              (cd "$repo/agents/$f" && OBSERVABILITY_APP_NAME="$svc" npm start >>"$RUNLOG" 2>&1) || ok=0 ;;
      dotnet) (cd "$repo" && OBSERVABILITY_APP_NAME="$svc" dotnet run --project "agents/$f" >"$RUNLOG" 2>&1) || ok=0 ;;
    esac
    if [ "$ok" -ne 1 ]; then
      # surface the actual error rather than "did not run"
      record "$lang" "$f" "FAIL  app: $(grep -m1 -E "Error|error:|Exception|Traceback|No module|error CS" "$RUNLOG" 2>/dev/null | cut -c1-160)"
      ((FAIL++)); (cd "$repo" && git reset -q --hard "$base_sha"); continue
    fi

    # A Python app can raise and still exit 0 - an exception in an atexit
    # handler prints a traceback and leaves the status at zero. Observed: a run
    # wrote Observability.flush(), which does not exist, and the fixture passed
    # with an AttributeError in its output. Exit status alone is not enough.
    if grep -qE "Traceback \(most recent call last\)|Exception ignored" "$RUNLOG" 2>/dev/null; then
      record "$lang" "$f" "FAIL  app raised (exit 0): $(grep -m1 -E "^[A-Za-z_.]+(Error|Exception):" "$RUNLOG" | cut -c1-80)"
      ((FAIL++)); (cd "$repo" && git reset -q --hard "$base_sha"); continue
    fi

    # 3. assert over MCP with the repo's own harness
    local out
    case "$lang" in
      ts) out=$(cd "$repo" && node harness/verify-mcp.mjs --service "$svc" --since "$since" --expect "$expect" --timeout 300 2>&1) ;;
      *)  # use the fixture venv's python when there is one - it has certifi,
          # which the harness needs for TLS on macOS
          HARNESS_PY="python3"; [ -x "$WORKDIR/venv-$f/bin/python" ] && HARNESS_PY="$WORKDIR/venv-$f/bin/python"
          out=$(cd "$repo" && "$HARNESS_PY" harness/verify_mcp.py --service "$svc" --since "$since" --expect "$expect" --timeout 300 2>&1) ;;
    esac
    if [ $? -eq 0 ]; then
      record "$lang" "$f" "PASS  spans confirmed on the platform"; ((PASS++))
    else
      # The verifier's no-spans-at-all exit says TIMEOUT, not FAIL - leaving the
      # summary line blank on the one failure that most needs explaining. Match
      # it too, and fall back to the last output line so this can never again
      # report a failure with no reason attached.
      why=$(echo "$out" | grep -m1 -E 'FAIL|TIMEOUT|observed|Error')
      [ -n "$why" ] || why=$(echo "$out" | grep -v '^[[:space:]]*$' | tail -1)
      record "$lang" "$f" "FAIL  $(echo "$why" | cut -c1-160)"; ((FAIL++))
    fi

    (cd "$repo" && git reset -q --hard "$base_sha" && git clean -fdq agents/ 2>/dev/null)
  done <<< "$fixtures"

  uninstall_skill "$repo"
}

LANGS=(python ts dotnet)
if [ $# -gt 0 ]; then LANGS=("$1"); shift; fi
for l in "${LANGS[@]}"; do run_lang "$l" "$@"; done

echo ""
echo "=============================================================="
[ ${#RESULTS[@]} -gt 0 ] && printf '%s\n' "${RESULTS[@]}"
echo "--------------------------------------------------------------"
echo "PASS $PASS   FAIL $FAIL   SKIP $SKIP"
[ "$FAIL" -eq 0 ]
