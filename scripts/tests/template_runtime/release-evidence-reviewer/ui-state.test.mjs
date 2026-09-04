// Offline checks execute the shipped script with a tiny DOM/fetch harness, not a model or browser.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const html = readFileSync(fileURLToPath(new URL('../../../../skills/build-template-agent/assets/release-evidence-reviewer/wwwroot/index.html', import.meta.url)), 'utf8');
const elements = [], ids = new Map(), requests = [];
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }

class Element {
  constructor(tag = 'div') {
    this.tagName = tag; this.children = []; this.dataset = {}; this.handlers = {}; this.attributes = {};
    this.hidden = false; this.disabled = false; this.textContent = ''; this.value = ''; this.placeholder = '';
    this.className = ''; this._innerHTML = ''; this.removed = false; elements.push(this);
    this.classList = { add: name => { this.className += (this.className ? ' ' : '') + name; } };
  }
  get innerHTML() { return this._innerHTML; }
  set innerHTML(value) { this._innerHTML = value; this.children = []; }
  addEventListener(name, action) { (this.handlers[name] ||= []).push(action); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  append(...children) { this.children.push(...children); }
  remove() { this.removed = true; }
  scrollIntoView() { }
  focus() { }
  async emit(name) {
    if (this.disabled && (name === 'click' || name === 'submit')) return;
    for (const action of this.handlers[name] || []) await action({ preventDefault() {} });
  }
}

for (const match of html.matchAll(/<([a-z][a-z0-9]*)\b([^>]*)>/g)) {
  const [, tag, attributes] = match;
  const id = attributes.match(/\bid="([^"]+)"/)?.[1];
  const prompt = attributes.match(/\bdata-prompt="([^"]+)"/)?.[1];
  if (!id && prompt === undefined) continue;
  const element = new Element(tag);
  if (id) { ids.set(id, element); element.id = id; }
  if (prompt !== undefined) element.dataset.prompt = prompt;
  if (/\bdata-followup\b/.test(attributes)) element.dataset.followup = '';
  element.hidden = /\bhidden\b/.test(attributes);
}
ids.get('conversation').innerHTML = 'initial release-review content';

const byId = id => ids.get(id);
const findAll = selector => elements.filter(element =>
  selector === '[data-prompt]' ? element.dataset.prompt !== undefined :
    selector === '[data-followup]' ? element.dataset.followup !== undefined : false);
const response = (data, ok = true) => ({ ok, json: async () => data });
const reply = (answer, project = 'Atlas') => ({
  answer, project, traceId: '1234567890abcdef1234567890abcdef', toolsUsed: ['CheckReleaseReadiness'],
  evidence: { project, status: project === 'Atlas' ? 'Blocked' : 'Ready', checks: [] },
});
const context = vm.createContext({
  document: {
    querySelector: selector => byId(selector.slice(1)),
    querySelectorAll: findAll,
    createElement: tag => new Element(tag),
  },
  AbortController, setTimeout, clearTimeout,
  fetch: (url, options) => url === '/api/health'
    ? Promise.resolve(response({ status: 'ready', documentsLoaded: 3, tracingEnabled: false }))
    : new Promise(resolve => requests.push({ body: JSON.parse(options.body), signal: options.signal, resolve })),
});

vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
await new Promise(resolve => setImmediate(resolve));
check(byId('status-text').textContent.includes('ready · 3 evidence documents'), 'health state uses the running app response');

byId('message').value = 'Check Project Atlas.';
let flight = byId('chat-form').emit('submit');
check(requests[0].body.context === null && requests[0].body.message === 'Check Project Atlas.', 'first review has no inherited project context');
check(byId('message').disabled && byId('send').textContent === 'Reviewing…', 'review controls expose their in-flight state');
requests[0].resolve(response(reply('Atlas is <b>Blocked</b>.'))); await flight;
check(byId('review-scope').textContent === 'Reviewing Atlas' && findAll('[data-followup]').every(button => !button.hidden), 'successful project review enables only project-scoped follow-ups');
check(byId('conversation').children.at(-1).children[0].textContent === 'Atlas is <b>Blocked</b>.', 'model text is rendered literally');

const followup = findAll('[data-followup]')[0];
await followup.emit('click');
flight = byId('chat-form').emit('submit');
assert.deepEqual(requests[1].body.context, { project: 'Atlas', question: 'Check Project Atlas.', answer: 'Atlas is <b>Blocked</b>.' }); checks++;
requests[1].resolve(response(reply('Rollback ownership is still missing.'))); await flight;
check(byId('review-scope').textContent === 'Reviewing Atlas', 'follow-up retains the selected project');

byId('message').value = 'Retry this exact question';
flight = byId('chat-form').emit('submit');
requests[2].resolve(response({ error: 'agent_run_failed' }, false)); await flight;
check(byId('message').value === 'Retry this exact question', 'failed request restores the submitted draft');

byId('message').value = 'Will this arrive after reset?';
const staleFlight = byId('chat-form').emit('submit');
await byId('new-review').emit('click');
check(requests[3].signal.aborted && byId('review-scope').textContent === 'New review · no project selected', 'New review aborts work and clears project context');
requests[3].resolve(response(reply('STALE PROJECT ANSWER'))); await staleFlight;
check(byId('conversation').children.length === 0 && byId('message').value === '', 'late response cannot repopulate the reset review');

console.log(`RELEASE_UI_STATE_TESTS=pass; assertions=${checks}; model_or_network_calls=false`);
