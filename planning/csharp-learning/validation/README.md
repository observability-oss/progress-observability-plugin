# Learning-kit validation — 8 September 2026

[Results](./results.json) records 57 passing C# verification cases and 358 browser checks. [Coverage](./coverage.json) maps the 49 walkthrough concepts and Copilot notes into the 16 lessons.

C# cases were compiled with the installed .NET 10.0.203 SDK and C# 14 against local .NET/ASP.NET 10.0.7 reference assemblies. There are 54 distinct programs: 54 output-check cases, one expected CS0029 failure, and two compile-only cases repeating one ASP.NET route. No compiler warnings remained. Complete verification programs are embedded in the authoring JSON.

Browser checks exercised every question through an incorrect and then correct answer, specific feedback, ordering, hints, review clearing, mixed rounds, first-try accounting, answer reveals, saved progress, import/export, corrupt and blocked storage, all learning views at desktop/tablet/mobile sizes, and cheat-sheet printing/expansion. Screenshot review checked desktop, dark and mobile layouts. The final mobile adjustment wraps code at a readable 13px rather than shrinking it.

The game records completed practice separately from first unaided answers. Imported progress is checked before replacing current progress. No external request occurred during the full exercise run. Browser QA used a loopback server because the automation tool blocks file URLs; the deliverables themselves embed all runtime resources and perform no fetches.

Raw compiler outputs, browser scripts/logs and screenshots remain in the task artifact directory:
`/Users/dipeykov/.codex/visualizations/2026/09/06/01a0786d-8469-70c0-a13c-c0ba8f342244/csharp-learning/authoring/`.

Rebuilding HTML after editing content is not a new compiler or browser verification. Recheck changed examples and affected interactions before updating these results. The agent runtime/packages were not changed or retested by this documentation task.
