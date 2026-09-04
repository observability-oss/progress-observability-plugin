// Offline checks execute the shipped script with a tiny DOM/fetch harness, not a model or browser.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const html = readFileSync(fileURLToPath(new URL('../../../../skills/build-template-agent/assets/operations-data-analyst/wwwroot/index.html', import.meta.url)), 'utf8');
const elements = [], ids = new Map(), analysisRequests = [], viewRequests = [];
let checks = 0, initialView = true;
function check(value, label) { assert.ok(value, label); checks++; }

class Element {
  constructor(tag = 'div') {
    this.tagName = tag; this.children = []; this.dataset = {}; this.handlers = {}; this.attributes = {};
    this.hidden = false; this.disabled = false; this.textContent = ''; this.value = ''; this.className = '';
    this.options = []; elements.push(this);
    this.classList = { toggle: (name, enabled) => { this.attributes[`class:${name}`] = !!enabled; } };
  }
  get selectedOptions() { return [this.options.find(option => option.value === this.value) || this.options[0]]; }
  addEventListener(name, action) { (this.handlers[name] ||= []).push(action); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  append(...children) {
    this.children.push(...children);
    if (this.tagName === 'select') this.options.push(...children.filter(child => child.tagName === 'option'));
  }
  replaceChildren(...children) { this.children = children; }
  before() { }
  focus() { }
  async emit(name) {
    if (this.disabled && (name === 'click' || name === 'submit')) return;
    for (const action of this.handlers[name] || []) await action({ preventDefault() {}, key: '' });
  }
  requestSubmit() { void this.emit('submit'); }
}

for (const match of html.matchAll(/<([a-z][a-z0-9]*)\b([^>]*\bid="([^"]+)"[^>]*)>/g)) {
  const element = new Element(match[1]); const attributes = match[2];
  ids.set(match[3], element); element.id = match[3];
  element.hidden = /\bhidden\b/.test(attributes);
}
for (const match of html.matchAll(/data-question="([^"]+)"/g)) {
  const element = new Element('button'); element.dataset.question = match[1];
}

const byId = id => ids.get(id);
const option = (value, text) => Object.assign(new Element('option'), { value, text });
byId('service').options = [option('all', 'All services')]; byId('service').value = 'all';
byId('metric').options = [option('errorRatePercent', 'Error rate'), option('errors', 'Errors')]; byId('metric').value = 'errorRatePercent';
byId('grouping').options = [option('day', 'Daily trend'), option('service', 'By service'), option('period', 'Compare periods')]; byId('grouping').value = 'day';
byId('start').value = '2026-08-01'; byId('end').value = '2026-08-30';

const descendants = (root, tag) => {
  const matches = [];
  const visit = node => { for (const child of node.children || []) { if (child.tagName === tag) matches.push(child); visit(child); } };
  visit(root); return matches;
};
const findAll = selector => [...new Set(selector.split(',').flatMap(part => {
  part = part.trim();
  if (part === '[data-question]') return elements.filter(element => element.dataset.question !== undefined);
  if (part === '#kpis button') return descendants(byId('kpis'), 'button');
  if (part === '#filters input') return [byId('start'), byId('end')];
  if (part === '#filters select') return [byId('service'), byId('metric')];
  if (part.startsWith('#')) return [byId(part.slice(1))];
  return [];
}))];

const selectedView = { service: 'all', start: '2026-08-01', end: '2026-08-30', metric: 'errorRatePercent', grouping: 'day', comparison: null };
const dashboard = {
  status: 'ok', reason: null, services: ['auth', 'checkout'], datasetStart: '2026-08-01', datasetEnd: '2026-08-30',
  summary: { rowCount: 2, requests: 200, errors: 3, errorRatePercent: 1.5, averageResponseMs: 125 },
  daily: [], rows: [
    { date: '2026-08-01', service: 'auth', requests: 100, errors: 1, totalResponseMs: 10000 },
    { date: '2026-08-01', service: 'checkout', requests: 100, errors: 2, totalResponseMs: 15000 },
  ], outliers: [],
};
const chart = { title: 'Error rate over time', unit: '%', kind: 'line', points: [{ label: '2026-08-01', value: 1.5 }] };
const highlights = [{ key: 'overall', label: 'Error rate', value: 1.5, unit: '%', caption: 'Request-weighted across 1 day', focus: null, explorable: true }];
const viewData = { view: selectedView, dashboard, chart, highlights };
const evidence = [{
  tool: 'ExploreMetrics', arguments: { view: selectedView, includeOutliers: false, includeAllMetrics: false },
  result: { view: selectedView, chart, summary: { status: 'ok', reason: null, summary: dashboard.summary, busiestDays: [], maxDailyRequests: 200 }, comparison: null, outliers: null },
  modelResult: { view: selectedView, chart, selectedTotal: 1.5, comparisonChange: null },
}];
const reply = answer => ({ status: 'answered', answer, traceId: '1234567890abcdef1234567890abcdef', ...viewData, evidence, lastQuestion: 'What is the error rate?' });
const jsonResponse = data => ({ ok: true, json: async () => data });
const streamResponse = events => {
  const payload = new TextEncoder().encode(events.map(event => JSON.stringify(event)).join('\n') + '\n');
  let sent = false;
  return { ok: true, body: { getReader: () => ({ read: async () => sent ? { done: true } : (sent = true, { value: payload, done: false }) }) } };
};
const flush = async () => { for (let index = 0; index < 4; index++) await new Promise(resolve => setImmediate(resolve)); };

const context = vm.createContext({
  document: {
    getElementById: byId, querySelectorAll: findAll,
    createElement: tag => new Element(tag), createElementNS: (_namespace, tag) => new Element(tag),
  },
  AbortController, TextDecoder, Intl, performance: { now: () => 0 },
  setInterval: () => 1, clearInterval: () => {},
  fetch: (url, options) => {
    if (url === '/api/view' && initialView) { initialView = false; return Promise.resolve(jsonResponse(viewData)); }
    if (url === '/api/view') return new Promise(resolve => viewRequests.push({ body: JSON.parse(options.body), signal: options.signal, resolve }));
    if (url === '/api/analyze/stream') return new Promise(resolve => analysisRequests.push({ body: JSON.parse(options.body), signal: options.signal, resolve }));
    throw new Error(`unexpected URL ${url}`);
  },
});

vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
await flush();
check(byId('chart-title').textContent === chart.title && byId('ask').disabled === false, 'initial view renders deterministic data without a model call');

byId('question').value = 'What is the error rate?';
await byId('ask-form').emit('submit');
assert.deepEqual(analysisRequests[0].body, { question: 'What is the error rate?', view: selectedView, lastQuestion: null, approvedView: null }); checks++;
analysisRequests[0].resolve(streamResponse([
  { type: 'view', data: viewData }, { type: 'text', text: 'Draft explanation.' },
  { type: 'done', data: reply('Final <img onerror=evil()> explanation.') },
]));
await flush();
check(byId('answer').textContent === 'Final <img onerror=evil()> explanation.' && byId('submitted-question').textContent === 'What is the error rate?', 'streamed answer and submitted question render literally');
check(byId('trace').textContent.includes('1 agent tool call') && byId('kpis').children.length === 1, 'done event retains evidence and calculated highlights');

const tileFlight = byId('kpis').children[0].emit('click');
check(JSON.stringify(analysisRequests[1].body.approvedView) === JSON.stringify(selectedView), 'metric-tile exploration fixes the exact rendered view');
analysisRequests[1].resolve(streamResponse([{ type: 'view', data: viewData }, { type: 'done', data: reply('The selected error rate is 1.5%.') }]));
await tileFlight;

byId('question').value = 'Will this answer arrive after reset?';
await byId('ask-form').emit('submit');
const stale = analysisRequests[2];
const resetFlight = byId('reset').emit('click');
check(stale.signal.aborted && viewRequests[0].body === null, 'Reset aborts analysis and requests the default deterministic view');
viewRequests[0].resolve(jsonResponse(viewData)); await resetFlight;
stale.resolve(streamResponse([{ type: 'done', data: reply('STALE ANSWER') }])); await flush();
check(byId('question').value === '' && !byId('answer').textContent.includes('STALE'), 'late analysis cannot repopulate a reset view');

byId('question').value = 'Show a recoverable explanation failure';
await byId('ask-form').emit('submit');
check(analysisRequests[3].body.lastQuestion === null, 'Reset clears follow-up context for the next question');
analysisRequests[3].resolve(streamResponse([{ type: 'view', data: viewData }, { type: 'error', error: 'agent_run_failed', traceId: 'abcdef' }]));
await flush();
check(byId('analyst-title').textContent === 'Explanation unavailable' && byId('answer').textContent.includes('calculated view is ready') && byId('chart-title').textContent === chart.title,
  'post-view failure preserves calculated data and reports only explanation failure');

byId('question').value = 'Stop this request';
await byId('ask-form').emit('submit');
const stopped = analysisRequests[4];
await byId('cancel').emit('click');
check(stopped.signal.aborted && byId('analyst-title').textContent === 'Analysis stopped', 'Stop aborts the active request without clearing displayed data');
stopped.resolve(streamResponse([{ type: 'done', data: reply('LATE STOPPED ANSWER') }])); await flush();
check(byId('analyst-title').textContent === 'Analysis stopped' && !byId('answer').textContent.includes('LATE'), 'stopped response cannot overwrite the current state');

console.log(`OPERATIONS_UI_STATE_TESTS=pass; assertions=${checks}; model_or_network_calls=false`);
