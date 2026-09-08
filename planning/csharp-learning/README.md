# C# learning kit

Start with the [cheat sheet](./cheat-sheet.html), then open the
[learning game](./learning-game.html). Both HTML files work directly in a
browser without a server, .NET installation, account or model credentials.
Keep them together for their cross-links. A [Markdown copy](./cheat-sheet.md)
is also available.

The kit simplifies the Copilot chat notes and the full agent walkthrough for
a programmer comfortable with Python functions but newer to OOP. It covers
the same 49 language/framework topics in 16 short lessons, with additional
recognition notes for syntax present in the generated source. Examples use
small objects, lists, colors and numbers instead of reproducing the agents.

## How to use it

1. Read the cheat sheet's opening distinctions. Expand just the topic you need;
   the optional reference notes can wait. Print/save PDF expands all topics.
2. In the game, start at lesson 1. Read the small example, optionally open its
   Python comparison, then try the four checkpoints.
3. Use hints freely. Missed, hinted, revealed or skipped questions enter the
   review queue. A correct unaided answer in a fresh attempt clears one.
4. Try a mixed round after several lessons. There are no timers or locked levels.
5. Return to the [full walkthrough](../walkthroughs/agent-builder-2026-09-08.html)
   when the small examples make sense.

The 64 exercises include 42 choices, 15 single-token/expression answers, and
seven line-ordering puzzles. Feedback explains the selected mistake. Completed
practice and correct first unaided answers are recorded separately; neither is
a claim of mastery. The game checks authored answers locally. It does not
compile arbitrary user input or use an LLM to grade it.

## Keeping progress

Progress is saved in local browser storage for the current origin/file location.
Use **Progress → Export progress** before changing browser, device or location,
then import that JSON in the new copy. If storage is blocked, the game still
works for the current visit and export remains available. Progress exports
contain learning results only. The Markdown cheat sheet needs no browser state.

## Files and maintenance

- `cheat-sheet.html`, `cheat-sheet.md`, `learning-game.html`: learner files.
- `authoring/foundations.json`, `authoring/runtime.json`: shared teaching
  content, questions and complete C# verification programs.
- `authoring/game-shell.html`, `authoring/style.css`, `authoring/game.js`:
  the small browser interface.
- `authoring/build.py`: emits all three learner files using only Python's
  standard library. Run `python3 authoring/build.py` from this directory.
- `validation/`: compact coverage and validation reports from creation.

The generated pages embed their own CSS, JavaScript and content. They load no
external scripts or fonts; Microsoft Learn links are optional further reading.
Changes belong on the development branch under `planning/`, outside the
distributed builder skills and the clean product PR.

Prepared 8 September 2026 for .NET 10 / C# 14. The saved walkthrough reflects
implementation commit `e6e9521`; the teaching examples are deliberately new.
