# scripts

Two kinds of thing live here: **generators** that keep derived files in sync with
their source, and **test scripts** that check the `instrument-agent` skill
actually works.

None of these are needed to *use* the plugin. They exist to keep it honest.

## Testing the skill

The skill is prose. The only way to know whether it works is to hand it to a
coding agent, point it at a deliberately uninstrumented app, and look at what
comes out. That is what these two do, against the fixture repos (all three are
**private** — running these scripts requires access to the `observability-oss`
org; nothing in the published skills depends on them):

- [`instrument-test-python`](https://github.com/observability-oss/instrument-test-python) - 10 fixtures
- [`instrument-test-typescript`](https://github.com/observability-oss/instrument-test-typescript) - 3 fixtures
- [`instrument-test-dotnet`](https://github.com/observability-oss/instrument-test-dotnet) - 3 fixtures

Each fixture is a pair: `agents/<name>/` is uninstrumented, `expected/<name>/`
is the reference solution.

**`expected/` is hidden while the agent works.** It lives in the same working
tree, and agents read it - observed runs opening
`expected/llamaindex/requirements.txt` and `expected/mcp/app.py` before writing
their own answer. A fixture solved by copying the answer key tells you nothing,
and it masks the failures worth finding: in one run the fixture that peeked got
the meta package right while the fixture that did not got it wrong. Both scripts
move `expected/` aside for the agent's turn and restore it before diffing.

### `skill_structural_test.sh` - fast, free, no credentials

Runs the skill over each fixture and judges the resulting diff against
`expected/`. Never runs the app, never touches the platform.

```bash
./scripts/skill_structural_test.sh                          # all three repos
./scripts/skill_structural_test.sh ~/instrument-test-python # one repo
./scripts/skill_structural_test.sh ~/instrument-test-python keyless langchain
```

A model applies the criteria rather than `diff` doing it, because `expected/`
carries explanatory comments the skill will never reproduce word-for-word - an
exact match would fail every fixture forever. The criteria live at the top of
the script; that is the one place "correct" is defined.

Treat a verdict as triage, not truth. The judge has produced wrong reasoning
before. When a fixture goes red, read the diff.

### `skill_e2e_all.sh` - the real thing

Runs the skill, then **runs the instrumented app for real** (live LLM call),
then asserts the spans arrived by querying the MCP server with that repo's own
harness. A pass means the code the agent wrote genuinely produces the right
trace depth on the platform - not that it resembles the reference.

```bash
export OBSERVABILITY_API_KEY="ac_p_..."      # Integration key - the app writes
export OBSERVABILITY_MCP_API_KEY="acm_..."   # MCP key - the harness reads
export OPENROUTER_API_KEY="sk-or-..."        # LLM fixtures skip without it

./scripts/skill_e2e_all.sh                   # everything
./scripts/skill_e2e_all.sh python            # one language
./scripts/skill_e2e_all.sh python keyless    # one fixture - start here
```

Start with `python keyless`: no model credentials, so it isolates the
write-to-platform and read-back-over-MCP path before you spend anything.

A full Python pass takes 30-45 minutes, dominated by dependency installs, not
LLM calls. Each fixture gets its **own venv** - crewai, llamaindex and haystack
pull genuinely incompatible trees, so a shared environment breaks partway
through. Service names carry a `-skill` suffix so these runs never collide with
CI's data.

Expected span shapes are pinned per fixture in the script, from measured probe
runs. They are the same assertions the `frameworks` CI workflow uses.

The run step also scans the app's output for tracebacks, because **exit status
alone is not enough**: an exception in a Python `atexit` handler prints a full
traceback and still exits 0. One run wrote `Observability.flush()`, which does
not exist, and the fixture passed with an `AttributeError` in its output.

### How the three test layers differ

| | Exercises | Proves |
|---|---|---|
| `frameworks` CI workflow | `expected/` | the SDK and platform work |
| `skill_structural_test.sh` | skill output, judged | the skill *looks* right |
| `skill_e2e_all.sh` | skill output, executed | the skill's output **works** |

Neither script tests whether the skill triggers from natural language (both name
it explicitly), nor the skill's own verify-over-MCP phase - they tell it to skip
that and verify independently, so the thing under test is not grading itself.

### Agent support

Both work with **Claude Code** or **GitHub Copilot CLI**, via
[`lib/agent.sh`](./lib/agent.sh). Whichever is on `PATH` is used; force one with
`AGENT=claude` or `AGENT=copilot`.

The skill folder itself is identical for both - only the directory each looks in
and the headless flags differ:

| | Skill directory | Invocation |
|---|---|---|
| Claude Code | `.claude/skills/` | `claude -p … --permission-mode acceptEdits` |
| Copilot CLI | `.github/skills/` | `copilot -p … --allow-all-tools` |

`--allow-all-tools` is **required** for Copilot's non-interactive mode; without
it the agent blocks on a permission prompt and the script appears to hang. The
scripts also pass `--add-dir <repo>` for Copilot, because it otherwise refuses
absolute reads inside its own working directory.
Override either invocation without editing anything:

```bash
export AGENT_CMD='copilot -p {PROMPT} --allow-all --model gpt-5.4'
```

`{PROMPT}` is substituted as a single argument.

Testing with a second agent has already earned its keep: a Copilot run caught
two places where the skill relied on the reader guessing well - the `@task` vs
`@tool` choice, and an `app_name` recommendation that contradicted every one of
the skill's own reference solutions.

### Useful switches

| Variable | Effect |
|---|---|
| `DEBUG=1` | Show the agent's own output instead of swallowing it. Use it the first time you run against a new CLI - otherwise a permission prompt is indistinguishable from a hang. |
| `USE_LOCAL=1` | Use your `~/instrument-test-*` checkouts instead of cloning. For iterating on fixtures you have not pushed. |
| `KEEP_LOGS=<dir>` | Write the per-fixture agent log, app log **and the diff the skill produced** to `<dir>` as they happen, instead of a temp dir that is deleted on exit. Lets you `tail -f` mid-run, and read afterwards what the skill actually wrote. |
| `AGENT`, `AGENT_CMD` | Pick / override the agent (above). |
| `REPO_ORG` | Clone fixtures from a different org (default `observability-oss`). |

By default each run **shallow-clones the fixture repos into a temp dir**. The
scripts edit `agents/` and revert with `git checkout`, so a run that dies partway
would otherwise leave your own checkout dirty. Cloning also means you test the
committed state - the same thing CI sees - with no "did I pull first?" step.

### Behind a corporate registry

The fixtures install from public PyPI and npm. If your machine is pointed at an
internal mirror instead, first find out whether that mirror **proxies upstream**
or only serves internally published packages:

```bash
npm view tsx dist.tarball     # a package nobody publishes internally
```

Ask for the **tarball, not the version**. A version number is the same answer
either way and tells you nothing; the tarball host is where the bytes actually
come from. A proxying registry rewrites it to itself:

```
https://your-mirror.example.com/.../tsx/-/tsx-4.23.1.tgz    → proxies, nothing to do
```

Same idea on the Python side — `pip download --no-deps -d /tmp/probe httpx -v`
prints the URL it fetched from.

A 404 means it is private-only, and the fixtures need public npm scoped back in.
Put that in the **fixture checkout**, not your global config, so nothing else
changes:

```bash
# in each instrument-test-* clone
cat > .npmrc <<'EOF'
@progress:registry=https://registry.npmjs.org/
registry=https://registry.npmjs.org/
EOF
```

Two settings commonly shipped with corporate mirrors are worth knowing about:

- **`ignore-scripts=true`** — harmless here. Neither `@progress/observability`
  nor `@github/copilot` has an install script; Copilot's platform binary ships
  as an `optionalDependencies` entry, which npm resolves normally.
- **`min-release-age=<days>`** — also harmless, but it makes resolution
  *silently* pick an older version, and the fixtures carry `^` ranges with no
  lockfiles. Two machines can then install different trees from the same
  fixture. If you are chasing a result you cannot reproduce, check the installed
  versions before anything else.

### Requirements

`claude` or `copilot`, `git`, `jq`. Plus the toolchain for whichever languages
you run: python3, node 22+, dotnet 8. Missing toolchains are skipped, not failed.

Both scripts avoid `mapfile` and namerefs and guard empty-array expansion, so
they work on the bash 3.2 that macOS still ships.

## Generators

Derived files, each with a single source of truth.

| Script | Generates | From |
|---|---|---|
| `sync_skill_refs.py` | `skills/*/references/mcp.md` | root `references/mcp.md` |
| `build_copilot.py` | `copilot/.github/copilot-instructions.md` | the skills + `copilot-instructions.template.md` |
| `build_web_frame.py` | the eval-builder page's data file | `skills/generate-eval/references/{frame,citations}.md` |

`sync_skill_refs.py` and `build_copilot.py` take `--check`, which exits non-zero
if the output is stale - suitable for CI. `build_web_frame.py` has no `--check`;
it requires `--out <path>` and always writes.

```bash
python scripts/sync_skill_refs.py --check
python scripts/build_copilot.py --check
```

Each skill carries its own copy of the MCP contract so it stays self-contained
and installable on its own via `npx skills add`. Run the sync after editing the
root copy, and `build_copilot.py` after editing any `SKILL.md` - the Copilot
bundle is generated, never hand-edited.
