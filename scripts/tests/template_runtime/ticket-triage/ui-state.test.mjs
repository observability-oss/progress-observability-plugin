// Offline checks execute the shipped script with a tiny DOM/fetch harness, not a model or browser.
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const html = readFileSync(fileURLToPath(new URL('../../../../skills/build-template-agent/assets/ticket-triage/wwwroot/index.html', import.meta.url)), 'utf8');
const elements = [], ids = new Map(), requests = [];
let checks = 0;
function check(value, label) { assert.ok(value, label); checks++; }

class Element {
  constructor(tag = 'div') {
    this.tagName = tag; this.children = []; this.dataset = {}; this.handlers = {}; this.attributes = {};
    this.hidden = false; this.disabled = false; this.textContent = ''; this.value = ''; this.placeholder = '';
    this.className = ''; this.scrolls = []; this.focuses = []; elements.push(this);
  }
  get firstChild() { return this.children[0]; }
  addEventListener(name, action) { (this.handlers[name] ||= []).push(action); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  removeAttribute(name) { delete this.attributes[name]; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  querySelectorAll(selector) {
    const matches = [];
    const visit = node => {
      for (const child of node.children || []) {
        if (selector === 'select' && child.tagName === 'select') matches.push(child);
        visit(child);
      }
    };
    visit(this); return matches;
  }
  scrollIntoView(options) { this.scrolls.push(options); }
  focus(options) { this.focuses.push(options); }
  async emit(name) {
    if (this.disabled && (name === 'click' || name === 'submit')) return;
    for (const action of this.handlers[name] || []) await action({ preventDefault() {} });
  }
}

for (const match of html.matchAll(/<([a-z][a-z0-9]*)\b([^>]*\bid="([^"]+)"[^>]*)>/g)) {
  const element = new Element(match[1]); const attributes = match[2];
  ids.set(match[3], element); element.id = match[3];
  element.hidden = /\bhidden\b/.test(attributes);
  element.className = attributes.match(/class="([^"]+)"/)?.[1] || '';
  element.placeholder = attributes.match(/placeholder="([^"]+)"/)?.[1] || '';
  for (const attribute of attributes.matchAll(/([a-z][a-z0-9-]*)="([^"]*)"/g)) element.attributes[attribute[1]] = attribute[2];
}

const byId = id => ids.get(id);
const settle = () => new Promise(resolve => setImmediate(resolve));
const response = (data, ok = true) => ({ ok, json: async () => data });
const tickets = [
  { id:'T-1001', title:'Production dashboards unavailable', description:'Status checks fail for several customers.', issueType:'outage', environment:'production', impact:'multiple_customers', workaroundAvailable:false },
  { id:'T-1005', title:'Reports look wrong', description:'A report differs from expectations.', issueType:'defect', environment:null, impact:'single_customer', workaroundAvailable:true },
];
const decision = (ticketId = 'T-1001', missingFields = []) => ({
  ticketId, status:missingFields.length ? 'needs_information' : 'proposed',
  suggestedQueue:missingFields.length ? null : ticketId === 'T-1001' ? 'Operations' : 'Engineering',
  suggestedPriority:missingFields.length ? null : ticketId === 'T-1001' ? 'P1' : 'P2', missingFields,
  evidence:[{ field:'issueType', value:ticketId === 'T-1001' ? 'outage' : 'defect', source:`tickets.json#${ticketId}` }],
  policyRefs:missingFields.length ? [] : [`triage-policy#${ticketId === 'T-1001' ? 'production-outage' : 'defect'}`],
});
const reply = (answer, question = null, ticketId = 'T-1001', missingFields = [], scenario = { active:false, suppliedFields:[] }) => ({
  answer, question, recommendation:decision(ticketId, missingFields), scenario,
  traceId:'1234567890abcdef1234567890abcdef', toolsUsed:['GetTicket'],
});

const context = vm.createContext({
  document: { getElementById: byId, createElement: tag => new Element(tag) },
  AbortController, setTimeout, clearTimeout,
  fetch: (url, options) => url === '/api/tickets' ? Promise.resolve(response({ tickets, mockData:true }))
    : url === '/api/health' ? Promise.resolve(response({ status:'ready', ticketsLoaded:2, documentsLoaded:1, tracingEnabled:false }))
    : new Promise(resolve => requests.push({ body:JSON.parse(options.body), signal:options.signal, resolve })),
});

const questionForm = html.match(/<form id="question-form"[\s\S]*?<\/form>/)?.[0] || '';
check(questionForm.indexOf('id="question-response"') > questionForm.indexOf('id="question"'), 'answer region stays below the question input inside the Ask card');
check(questionForm.includes('href="#result"') && questionForm.includes('tabindex="-1"') && questionForm.includes('aria-describedby="answered-question question-answer question-grounding-copy"') && html.includes('id="live-status"') && html.includes('aria-live="polite"'), 'answer keeps a nearby evidence path plus explicit screen-reader navigation');
check(!html.includes('innerHTML'), 'page has no HTML injection sink');

vm.runInContext(html.match(/<script>([\s\S]*?)<\/script>/)[1], context);
await settle(); await settle();
check(byId('question-label').textContent === 'Ask about T-1001' && byId('question-response').hidden, 'selected ticket names the question scope and starts without an answer');

let flight = byId('triage').emit('click');
check(requests[0].body.question === null && byId('triage').textContent === 'Reviewing…', 'ordinary triage enters its own busy state');
requests[0].resolve(response(reply('Triage explanation.'))); await flight;
check(byId('recommendation-answer').textContent === 'Triage explanation.' && !byId('recommendation-answer').hidden, 'ordinary triage keeps its explanation in the recommendation');
check(byId('question-response').hidden, 'ordinary triage does not create a question answer');

byId('question').value = 'Why is this P1?'; flight = byId('question-form').emit('submit');
check(!byId('question-progress').hidden && byId('question-progress').textContent.includes('Why is this P1?'), 'question progress appears beside the submitted input');
check(byId('ask').textContent === 'Answering…' && byId('question-form').attributes['aria-busy'] === 'true', 'question-local controls expose their busy state');
check(byId('recommendation-answer').textContent === 'Triage explanation.' && byId('result-placeholder').hidden, 'question lookup leaves the visible triage result unchanged while pending');
requests[1].resolve(response(reply('Because the production outage affects multiple customers.', 'Why is this P1?'))); await flight; await settle();
check(!byId('question-response').hidden && byId('answered-question').textContent.includes('Why is this P1?'), 'successful response appears in the Ask card with the exact submitted question');
check(byId('question-answer').textContent === 'Because the production outage affects multiple customers.' && byId('recommendation-answer').textContent === 'Triage explanation.', 'question answer is separated from the preserved triage explanation');
check(byId('question-grounding-copy').textContent === 'Grounded in 1 documented ticket fact and 1 policy reference.', 'answer summarizes its returned grounding');
check(byId('question').value === '' && byId('question').placeholder.includes('another question') && byId('question-response').scrolls.length === 1 && byId('question-response').focuses.length === 1, 'success resets the composer and reveals and focuses the adjacent answer');
check(byId('live-status').textContent === 'Answer ready for T-1001.', 'screen readers receive a concise completion announcement');

byId('question').value = 'Can this be assigned?'; flight = byId('question-form').emit('submit');
requests[2].resolve(response({ error:'agent_run_failed', traceId:'abcdef' }, false)); await flight; await settle();
check(!byId('question-error').hidden && byId('question-error').textContent.includes('answer could not complete'), 'question failures appear beside the composer');
check(byId('error').hidden && byId('question').value === 'Can this be assigned?' && byId('question-response').hidden, 'question failure preserves input and does not use the distant page error');

byId('question').value = 'Will this response arrive late?'; const staleFlight = byId('question-form').emit('submit');
const latestTicketButtons = byId('ticket-list').children.map(item => item.children[0]);
await latestTicketButtons[1].emit('click');
check(requests[3].signal.aborted && byId('question-label').textContent === 'Ask about T-1005', 'ticket change cancels the old question and updates visible scope');
check(byId('question-response').hidden && byId('question-error').hidden && byId('question').value === '', 'ticket change clears stale question state');
requests[3].resolve(response(reply('STALE ANSWER', 'Will this response arrive late?'))); await staleFlight; await settle();
check(byId('question-answer').textContent === '' && byId('question-response').hidden, 'late canceled response cannot repopulate another ticket');

flight = byId('triage').emit('click');
requests[4].resolve(response(reply('Environment is required.', null, 'T-1005', ['environment']))); await flight;
byId('question').value = 'What information is missing?'; flight = byId('question-form').emit('submit');
requests[5].resolve(response(reply('The environment is missing.', 'What information is missing?', 'T-1005', ['environment']))); await flight; await settle();
check(!byId('question-response').hidden, 'question answer can accompany a needs-information recommendation');
byId('question').value = 'Keep this unsent draft';
const environment = byId('scenario-fields').querySelectorAll('select')[0]; environment.value = 'production';
flight = byId('scenario-form').emit('submit');
check(requests[6].body.scenario.environment === 'production', 'temporary scenario is sent through the real form path');
requests[6].resolve(response(reply('Scenario triage explanation.', null, 'T-1005', [], { active:true, suppliedFields:[{ field:'environment', value:'production', source:'temporary-scenario#T-1005' }] }))); await flight; await settle();
check(byId('question-response').hidden && byId('question-answer').textContent === '' && byId('question').value === 'Keep this unsent draft', 'successful scenario change clears the stale answer without erasing an unsent draft');
await byId('reset-scenario').emit('click');
check(requests[7].body.ticketId === 'T-1005' && Object.keys(requests[7].body.scenario).length === 0 && byId('question').value === 'Keep this unsent draft', 'clearing a temporary scenario preserves a same-ticket draft');
requests[7].resolve(response(reply('Bundled ticket triage.', null, 'T-1005', ['environment']))); await settle();
check(byId('question').value === 'Keep this unsent draft', 'completed scenario reset still preserves the draft');

console.log(`TICKET_TRIAGE_UI_STATE_TESTS=pass; assertions=${checks}; model_or_network_calls=false`);
