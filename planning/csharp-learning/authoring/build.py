from pathlib import Path
import html, json, re

HERE = Path(__file__).parent
OUT = HERE.parent
lessons = []
for filename in ['foundations.json', 'runtime.json']:
    lessons.extend(json.loads((HERE / filename).read_text())['lessons'])
lessons.sort(key=lambda lesson: lesson['id'])
assert len(lessons) == 16
questions = [q for lesson in lessons for q in lesson['questions']]
assert len(questions) == len({q['id'] for q in questions}) == 64
for lesson in lessons:
    assert len(lesson['questions']) == 4
    for q in lesson['questions']:
        assert q['hint'] and q['explanation']
        if q['type'] == 'choice':
            assert 0 <= q['answer'] < len(q['options'])
            assert all(option['why'] for option in q['options'])
        elif q['type'] == 'fill':
            assert q['accepted'] and all(x.strip() for x in q['accepted'])
        else:
            assert q['type'] == 'order' and len(set(q['lines'])) == len(q['lines']) > 1

essentials = [
    ('Language / platform', 'C# is what you write. .NET runs it and supplies libraries. The SDK builds it. ASP.NET Core handles web apps.'),
    ('Type / object', '`Jar` describes a kind of object. `new Jar(2)` makes one. `jar` holds a reference to that instance.'),
    ('Function / method', 'A method is a function declared on a type. Keep plain calculations small; introduce an object when shared state needs operations.'),
    ('Parameter / inheritance', '`IText inner` receives an object. `: IText` implements a contract. `: Parent` derives from a class.'),
    ('Query / results', '`Where(...)` usually describes work. `ToList()` runs it now and stores the results. The query variable is not a cache.'),
    ('Task / result', '`Task<int>` represents eventual completion with an integer. `await` obtains the integer or propagates failure/cancellation.'),
    ('Async / threads', 'Calling a C# async Task method starts it immediately. An incomplete await can yield; it does not require a new thread.'),
    ('Absent / empty', '`null` means absent. `""` is present empty text. `??` replaces only null; `!` provides no runtime protection.'),
    ('Copy / shared data', 'Class assignment copies a reference. Struct assignment copies its value. A record’s `with` copy still shares nested references.'),
    ('Readonly / immutable', '`readonly`, `init`, and read-only interfaces limit specific operations. They do not automatically freeze nested objects.'),
    ('Disposal / garbage collection', '`using var` releases a disposable resource at scope exit. Garbage collection reclaims managed memory on its own schedule.'),
    ('Typed / validated', 'A typed object can still contain invalid values. Validate external input before relying on it; successful parsing is only one step.'),
]
syntax = [
    ('`var count = 3;`', 'Infer int once; the variable cannot later hold text.', '`count = 3` permits later rebinding to another type.'),
    ('`foreach (var x in xs)`', 'Visit each element of a sequence.', '`for x in xs:`'),
    ('`x => x * 2`', 'Lambda; `=>` also appears in short method/property bodies.', '`lambda x: x * 2`'),
    ('`Func<int, int>`', 'A callable taking int and returning int; last type is the result.', 'A typed callable, not the integer result.'),
    ('`string?` / `long?`', 'Nullable reference annotation / nullable value-type wrapper.', 'Both allow absence conceptually, but C# represents them differently.'),
    ('`x?.Name ?? "Guest"`', 'Read safely; replace null with Guest.', 'Use explicit `is not None`; Python `or` also replaces empty/zero.'),
    ('`x is "red" or "blue"`', 'C# pattern matching; this is not a regular expression.', '`x in ("red", "blue")` for this simple case.'),
    ('`Where` / `Any` / `ToList`', 'Filter a query / answer a bool now / materialize results.', 'Generator/filter / `any` / `list` are useful analogies.'),
    ('`async Task<T>`', 'An async method eventually supplies T; `Task` alone has no result.', 'Calling Python `async def` normally creates a coroutine; execution differs.'),
    ('`using var item = ...;`', 'Dispose item when the enclosing scope exits.', '`with` is the closest resource-lifetime comparison.'),
    ('`await foreach`', 'Consume an asynchronous sequence one element at a time.', '`async for`'),
    ('`[Description("...")]`', 'Attach metadata that a library may inspect.', 'Not Python decorator syntax that necessarily replaces/wraps a function.'),
]
sources = [
    ('Object-oriented building blocks', 'https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/object-oriented/'),
    ('Primary constructors', 'https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/primary-constructors'),
    ('Records and equality', 'https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/types/records'),
    ('LINQ queries and execution', 'https://learn.microsoft.com/en-us/dotnet/csharp/linq/get-started/introduction-to-linq-queries'),
    ('Async and await', 'https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/'),
    ('Async streams', 'https://learn.microsoft.com/en-us/dotnet/csharp/asynchronous-programming/generate-consume-asynchronous-stream'),
    ('Resource disposal with using', 'https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/using'),
    ('Ranges and indexes', 'https://learn.microsoft.com/en-us/dotnet/csharp/tutorials/ranges-indexes'),
    ('JSON nullable annotations', 'https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/nullable-annotations'),
    ('JSON source generation', 'https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation'),
    ('Configuration providers', 'https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0'),
    ('Minimal API binding', 'https://learn.microsoft.com/en-us/aspnet/core/fundamentals/minimal-apis/parameter-binding?view=aspnetcore-10.0'),
    ('Dependency lifetimes', 'https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection/service-lifetimes'),
]

def e(text):
    return html.escape(str(text), quote=True)

def rich(text):
    return re.sub(r'`([^`]+)`', r'<code>\1</code>', e(text))

def code(text, label='C# · small example'):
    return f'<div class="code-panel"><div class="code-label">{e(label)}</div><pre><code>{e(text)}</code></pre></div>'

def section(lesson, index):
    glossary = ''.join(f'<dt>{rich(r["term"])}</dt><dd>{rich(r["meaning"])}</dd>' for r in lesson.get('reference', []))
    comparison = f'<details><summary>Python connection</summary><pre>{e(lesson["python"])}</pre></details>' if lesson.get('python') else ''
    return f'''<details class="sheet-section" id="{e(lesson['id'])}">
<summary class="sheet-title"><span class="inline-number">{index:02}</span><span>{e(lesson['title'])}<small>{e(lesson['subtitle'])}</small></span><span class="expand-label">Open +</span></summary>
<div class="sheet-grid"><div><ul class="rules">{''.join('<li>'+rich(r)+'</li>' for r in lesson['rules'])}</ul><div class="trap"><strong>Watch this distinction</strong>{rich(lesson['trap'])}</div></div>
<div>{code(lesson['code'])}{comparison}<p class="connection">In the agent code: {rich(lesson['agentConnection'])}</p></div></div>
<details class="reference-block"><summary>More symbols, briefly</summary><dl>{glossary}</dl></details>
<p class="no-print" style="margin:22px 0 0"><a href="learning-game.html#{e(lesson['id'])}">Practice this concept →</a></p></details>'''

css=(HERE/'style.css').read_text()
sheet_css='''.sheet-title{display:flex;align-items:center;gap:14px;list-style:none;font-size:21px;font-weight:700;padding:0}.sheet-title::-webkit-details-marker{display:none}.sheet-title small{display:block;font-size:12px;font-weight:400;color:var(--muted);margin-top:5px}.expand-label{margin-left:auto;flex-shrink:0;font-size:12px;color:var(--accent)}.sheet-section[open]>.sheet-title{margin-bottom:24px}.sheet-section[open]>.sheet-title .expand-label{font-size:0}.sheet-section[open]>.sheet-title .expand-label::after{content:'Close −';font-size:12px}.reference-block dl{columns:2;column-gap:35px}.reference-block dt,.reference-block dd{break-inside:avoid}@media(max-width:720px){.sheet-title{font-size:18px}.reference-block dl{columns:1}}@media print{.sheet-title{display:flex!important;font-size:15pt;break-after:avoid}.sheet-title small{font-size:8pt}.expand-label{display:none}.sheet-grid{margin-top:14px}.sheet-section{break-inside:auto}.code-panel,.trap,.reference-block dt,.reference-block dd{break-inside:avoid}.sheet-section>summary{display:flex!important}.reference-block>summary{display:block!important;font-size:9pt}.sheet-section[open]>.sheet-title{margin-bottom:10px}}'''
sheet_js='''const sections=[...document.querySelectorAll('.sheet-section')];function openHash(){const id=location.hash.slice(1);const node=document.getElementById(id);if(node&&node.classList.contains('sheet-section')){node.open=true;requestAnimationFrame(()=>node.scrollIntoView());}}document.getElementById('expand').onclick=()=>sections.forEach(s=>s.open=true);document.getElementById('collapse').onclick=()=>sections.forEach(s=>s.open=false);document.getElementById('print').onclick=()=>window.print();let prior=[];window.addEventListener('beforeprint',()=>{prior=[...document.querySelectorAll('details')].map(d=>[d,d.open]);prior.forEach(([d])=>d.open=true);});window.addEventListener('afterprint',()=>prior.forEach(([d,open])=>d.open=open));window.addEventListener('hashchange',openHash);openHash();'''
sheet=f'''<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light dark"><title>C# field notes · a Python-friendly cheat sheet</title><link rel="icon" href="data:,"><style>{css}{sheet_css}</style></head><body>
<header class="masthead"><a class="brand" href="learning-game.html"><span class="brand-mark">C#</span><span>Field notes<span class="brand-sub">A Python-friendly cheat sheet</span></span></a><nav aria-label="Cheat-sheet navigation"><a href="learning-game.html">Play the learning game →</a><button id="print" type="button">Print / save PDF</button></nav></header>
<main class="sheet"><section class="sheet-hero"><div class="eyebrow">The same concepts · much smaller examples</div><h1>C# without the big application.</h1><p class="lead">Start with these distinctions. Open a topic when you need its syntax. You can use functions and objects together; you do not need an inheritance hierarchy for every task.</p><p class="small">Adapted from your Copilot notes and the agent walkthrough. Covers all 49 language/framework topics in that guide, grouped into 16 short lessons.</p></section>
<h2>The distinctions that do most of the work</h2><section class="essentials">{''.join(f'<div class="essential"><strong>{e(title)}</strong>{rich(body)}</div>' for title,body in essentials)}</section>
<h2>Read the symbols</h2><table class="field-table"><thead><tr><th>C#</th><th>Read it as</th><th>Python connection / caution</th></tr></thead><tbody>{''.join(f'<tr><td>{rich(a)}</td><td>{rich(b)}</td><td>{rich(c)}</td></tr>' for a,b,c in syntax)}</tbody></table>
<div class="no-print" style="display:flex;gap:10px;flex-wrap:wrap;margin:30px 0 15px"><button id="expand" type="button">Expand all topics</button><button id="collapse" type="button">Collapse all</button></div><p class="small">The snippets focus on one idea. Console examples assume the usual SDK implicit imports; framework excerpts state their context. The game checks answers locally rather than compiling your input.</p>
{''.join(section(l,i) for i,l in enumerate(lessons,1))}
<section class="sheet-section"><h2>A useful hybrid style</h2><p>Use small functions for transformations and calculations. Use records for data that travels between functions. Use classes for state plus operations, and interfaces or delegates when a dependency needs to be replaced. Add inheritance only when the relationship and shared behavior justify it.</p><p class="sheet-source">Return to the <a href="../walkthroughs/agent-builder-2026-09-08.html">full implementation walkthrough</a> when a small example makes sense. This relative link works in the development branch learning folder; the cheat sheet itself needs no other file to display.</p></section>
<section class="sheet-section"><h2>Official references, when you want more</h2><ul class="sources-list">{''.join(f'<li><a href="{e(url)}" target="_blank" rel="noopener">{e(title)}</a></li>' for title,url in sources)}</ul><p class="small">Prepared 8 September 2026 for .NET 10. Python parallels explain intent; they do not assert identical runtime behavior.</p></section></main><footer><span>Self-contained field notes · read offline</span><span>Use your browser’s Print command for a paper or PDF copy.</span></footer><script>{sheet_js}</script></body></html>'''
(OUT/'cheat-sheet.html').write_text(sheet)

md=['# C# field notes for a Python programmer','', 'A simplified companion to the Copilot notes and agent walkthrough. .NET 10; 8 September 2026.','', '[Interactive learning game](./learning-game.html) · [Formatted, printable cheat sheet](./cheat-sheet.html)','', '## The distinctions that do most of the work','']
md += [f'- **{title}:** {body}' for title,body in essentials]
md += ['', '## Read the symbols','', '| C# | Meaning | Python connection / caution |','|---|---|---|']
md += [f'| {a} | {b} | {c} |' for a,b,c in syntax]
md += ['', 'Examples assume ordinary SDK implicit imports unless a framework context is stated.','']
for index,l in enumerate(lessons,1):
    md += [f'## {index}. {l["title"]}','',*['- '+r for r in l['rules']],'','```csharp',l['code'],'```','', '**Watch:** '+l['trap'],'']
    if l.get('python'):md += ['<details><summary>Python connection</summary>','','```python',l['python'],'```','','</details>','']
    if l.get('reference'):md += ['<details><summary>More symbols, briefly</summary>','',*['- **'+r['term']+':** '+r['meaning'] for r in l['reference']],'','</details>','']
    md += ['In the agents: '+l['agentConnection'],'']
md += ['## Keep the design small','','Use functions for transformations; records for data; classes for state plus operations. Pass dependencies explicitly. Introduce interfaces or delegates when replacing a dependency helps. Inheritance is one option, not a prerequisite.','','## Official references','']
md += [f'- [{title}]({url})' for title,url in sources]
(OUT/'cheat-sheet.md').write_text('\n'.join(md)+'\n')

def learner_copy(value):
    if isinstance(value,dict): return {key:learner_copy(val) for key,val in value.items() if key not in ['verification','extraVerifications','sources']}
    if isinstance(value,list): return [learner_copy(item) for item in value]
    return value

payload=json.dumps({'version':1,'lessons':learner_copy(lessons)},ensure_ascii=False,separators=(',',':')).replace('<','\\u003c').replace('\u2028','\\u2028').replace('\u2029','\\u2029')
game=(HERE/'game-shell.html').read_text().replace('/* KIT_CSS */',css).replace('KIT_DATA',payload).replace('/* KIT_JS */',(HERE/'game.js').read_text())
(OUT/'learning-game.html').write_text(game)
manifest={'lessons':len(lessons),'questions':len(questions),'questionTypes':{kind:sum(q['type']==kind for q in questions) for kind in ['choice','fill','order']},'mappedConcepts':sorted({cid for l in lessons for cid in l['conceptIds']}),'bytes':{name:(OUT/name).stat().st_size for name in ['cheat-sheet.html','cheat-sheet.md','learning-game.html']}}
(HERE/'build-results.json').write_text(json.dumps(manifest,indent=2)+'\n')
print(json.dumps(manifest,indent=2))
