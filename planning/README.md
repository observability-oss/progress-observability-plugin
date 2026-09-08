# Agent builder development notes

This directory belongs to the `build-custom-agent` development branch. It keeps
planning, test history and learning material outside the clean PR branch and
distributed skill payloads.

## Start here

- [Current implementation and handoff — 8 September](./agent-builder-handoff-2026-09-08.md)
- [Interactive C#/.NET walkthrough](./walkthroughs/agent-builder-2026-09-08.html)
  — open the HTML in a browser; it contains its own code, styles and source
  snapshot. It needs no server, .NET installation or model credentials.
  Start with chapters 1–3, then follow each template and the custom builder.
- [Initial review — 6 September](./agent-builder-review-2026-09-06.md)
  — findings that led to the latest fixes; see the current handoff for their resolution.
- [Earlier handoff — 6 September](./agent-builder-handoff-2026-09-06.md)
- [Compact validation evidence](./evidence/2026-09-08/)

The HTML is a dated snapshot, not a live view of the checkout. Its embedded
source was checked against all 218 covered files at implementation commit
`e6e9521`. Its original metadata records `d34f734` plus then-uncommitted changes;
those changes were subsequently committed as `e6e9521`. Absolute source paths
inside the guide refer to the original PR checkout, but full source reading and
navigation use embedded content and remain available if that checkout moves.
Browser reading progress is local to that browser/location and is not stored in Git.

## Source ownership

Edit `skills/build-template-agent/` and `skills/build-custom-agent/`.
`copilot/skills/` contains generated copies for the standalone classic Copilot
bundle. After source edits, run from the repository root:

```sh
python3 scripts/build_copilot.py
python3 scripts/build_copilot.py --check
```

Keep product changes on `agent-builder-clear-pr` and bring its reviewed commits
into this development branch when needed, retaining `planning/`. Do not merge
this whole development branch back into the clean PR just to transfer a code
fix; select the product-only changes so these development artifacts stay here.

## Earlier plans and validation

These preserve what was planned or observed at their recorded checkpoints.
Their TODOs, paths and counts are not the current acceptance checklist.

- [Custom builder plan](./build-custom-agent.md)
- [Initial template validation](./prebuilt-template-validation.md)
- [Template interaction validation](./prebuilt-template-ux-validation.md)
- [Operations simplification validation](./operations-data-analyst-validation.md)
- [Removing builder MCP/environment dependencies](./remove-builder-mcp-env-dependencies.md)
