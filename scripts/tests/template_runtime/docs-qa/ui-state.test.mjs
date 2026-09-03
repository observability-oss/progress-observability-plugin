// Offline checks execute the shipped script with a tiny DOM/fetch harness, not a model or browser.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const html = readFileSync(fileURLToPath(new URL('../../../../skills/build-template-agent/assets/docs-qa/wwwroot/index.html', import.meta.url)), 'utf8');
const elements = [], ids = new Map(), requests = [];
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }
class Element {
  constructor(tag = 'div') { this.tagName = tag; this.children = []; this.dataset = {}; this.handlers = {}; this.attributes = {}; this.hidden = false; this.disabled = false; this.textContent = ''; this.value = ''; this.className = ''; elements.push(this); }
  addEventListener(name, action) { (this.handlers[name] ||= []).push(action); }
  setAttribute(name, value) { this.attributes[name] = value; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  focus() { }
  scrollIntoView() { }
  async emit(name) { if (this.disabled && name === 'click') return; for (const action of this.handlers[name] || []) await action({ preventDefault() {} }); }
}
for (const match of html.matchAll(/<([a-z][a-z0-9]*)\b([^>]*\bid="([^"]+)"[^>]*)>/g)) {
  const element = new Element(match[1]); ids.set(match[3], element); element.id = match[3];
  element.hidden = /\bhidden\b/.test(match[2]); element.className = match[2].match(/class="([^"]+)"/)?.[1] || '';
}
for (const match of html.matchAll(/data-question="([^"]+)"/g)) { const element = new Element('button'); element.dataset.question = match[1]; }
const findAll = selector => elements.filter(element => selector.split(',').some(part => {
  part = part.trim();
  return part.startsWith('#') ? element.id === part.slice(1) : part.startsWith('.') ? element.className.split(' ').includes(part.slice(1)) : part === '[data-question]' && !!element.dataset.question;
}));
const byId = id => ids.get(id);
const audit = { sourceId: 'retention#audit-history', documentId: 'retention', title: 'Audit retention', heading: 'Audit history', excerpt: 'Audit logs are retained for 90 days.' };
const exports = { sourceId: 'exports#export-availability', documentId: 'exports', title: 'Data exports', heading: 'Export availability', excerpt: 'CSV export download links last 24 hours.' };
const reply = (answer, citations = [audit]) => ({ status: 'answered', answer, citations, toolCalls: ['SearchDocuments', 'ReadSection'], traceId: '1234567890abcdef1234567890abcdef' });
const response = data => ({ ok: true, json: async () => data });
const context = vm.createContext({
  document: { querySelector: selector => byId(selector.slice(1)), querySelectorAll: findAll, createElement: tag => new Element(tag) },
  window: { innerWidth: 390 }, AbortController, AbortSignal,
  fetch: (url, options) => url === '/api/health' ? Promise.resolve(response({ status: 'ready', documentsLoaded: 2, sectionsLoaded: 2, tracingEnabled: false }))
    : url === '/api/documents' ? Promise.resolve(response([audit, exports]))
    : new Promise(resolve => requests.push({ body: JSON.parse(options.body), signal: options.signal, resolve })),
});
vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
await new Promise(resolve => setImmediate(resolve));
check(findAll('.section-button').length === 2, 'real page script renders bundled source controls');
check(!html.includes('innerHTML'), 'page has no HTML injection sink');

byId('question').value = 'How long are audit logs retained?';
let flight = byId('ask-form').emit('submit');
check(byId('question').disabled && findAll('.section-button').every(button => button.disabled), 'in-flight question and source choices are locked');
check(requests[0].body.previousTurn === null && requests[0].body.sourceId === null, 'initial ask has no inherited context or scope');
requests[0].resolve(response(reply('Audit logs are retained for 90 days.'))); await flight;
check(!byId('result').hidden && byId('submitted-question').textContent.includes('Scope: all bundled documents'), 'answer shows immutable submitted scope provenance');
await byId('follow-up').emit('click');
check(!byId('follow-up-context').hidden && byId('question').value === '', 'follow-up action opens explicit previous-question context');
byId('question').value = 'How long does it last?'; flight = byId('ask-form').emit('submit');
assert.deepEqual(requests[1].body.previousTurn, { question: 'How long are audit logs retained?', answer: 'Audit logs are retained for 90 days.' }); checks++;
requests[1].resolve(response(reply('It lasts 90 days.'))); await flight;
check(byId('submitted-question').textContent.includes('Follow-up to: How long are audit logs retained?'), 'follow-up result identifies the exact prior question used');

await findAll('.section-button').find(button => button.dataset.sourceId === exports.sourceId).emit('click');
await byId('scope-source').emit('click');
check(!byId('scope-context').hidden && byId('scope-label').textContent.includes('Export availability'), 'source preview action creates a visible exact-section scope');
await byId('clear-scope').emit('click'); check(byId('scope-context').hidden, 'source scope can be explicitly cleared');
await byId('scope-source').emit('click');
byId('question').value = 'How long is this available?'; flight = byId('ask-form').emit('submit');
check(requests[2].body.sourceId === exports.sourceId && requests[2].body.previousTurn === null, 'section question sends selected ID without unrelated prior answer');
await findAll('.section-button').find(button => button.dataset.sourceId === audit.sourceId).emit('click');
check(byId('source-id').textContent === exports.sourceId, 'disabled preview selection cannot drift during lookup');
requests[2].resolve(response(reply('The link lasts 24 hours.', [exports]))); await flight;
check(byId('submitted-question').textContent.includes('Scope: Data exports · Export availability'), 'scoped result retains immutable section provenance');
await byId('follow-up').emit('click');
await byId('clear-follow-up').emit('click'); check(byId('follow-up-context').hidden, 'follow-up context can be explicitly cleared');
await byId('follow-up').emit('click');
byId('question').value = 'Can I use it later?'; const staleFlight = byId('ask-form').emit('submit');
check(requests[3].body.previousTurn.answer === 'The link lasts 24 hours.' && requests[3].body.sourceId === exports.sourceId, 'scoped follow-up carries only the latest answer and selected scope');
await byId('new-question').emit('click');
check(requests[3].signal.aborted && byId('result').hidden && byId('question').value === '', 'New question aborts in-flight work and clears result/input');
check(byId('scope-context').hidden && byId('follow-up-context').hidden && byId('source-id').textContent === '', 'New question clears all context and source selection');
byId('question').value = 'Fresh audit lookup'; flight = byId('ask-form').emit('submit');
check(requests[4].body.previousTurn === null && requests[4].body.sourceId === null, 'next request after reset is genuinely isolated');
requests[3].resolve(response(reply('STALE ANSWER', [exports]))); await staleFlight;
check(byId('result').hidden && byId('question').disabled && byId('answer').textContent === '', 'late canceled result cannot overwrite or unlock a newer request');
requests[4].resolve(response(reply('Fresh answer <img onerror=evil()>'))); await flight;
check(byId('answer').textContent === 'Fresh answer <img onerror=evil()>' && !byId('question').disabled, 'latest result renders as literal text and releases input lock');
await byId('follow-up').emit('click'); await byId('scope-source').emit('click');
await findAll('[data-question]')[0].emit('click');
check(byId('follow-up-context').hidden && byId('scope-context').hidden && byId('result').hidden, 'example selection starts a clean independent question');
console.log(`DOCS_UI_STATE_TESTS=pass; assertions=${checks}; model_or_network_calls=false`);
