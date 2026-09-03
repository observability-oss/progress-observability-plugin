using DocsQa;
using System.Text.Json;

var folder = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "docs");
var store = new DocumentStore(folder);
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    passed++; Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Check(true, name); return; }
    throw new InvalidOperationException("FAIL: " + name);
}
Check(store.DocumentCount == 4 && store.Sections.Count == 8, "four documents and eight section fixtures");
Check(store.Sections.Select(section => section.SourceId).Distinct().Count() == 8, "stable unique section IDs");
Check(store.Search("audit logs retained")[0].SourceId == "retention#audit-history", "audit retrieval ranks exact relevant section first");
Check(store.Search("CSV export download available")[0].SourceId == "exports#export-availability", "export retrieval ranks availability first");
Check(store.Search("How long are audit logs retained, and how long is a CSV export download available?")
    .Select(section => section.DocumentId).Distinct().Count() >= 2, "two-topic retrieval spans documents");
Check(store.Search("the how and what with a document").Count == 0, "common words produce no false grounding");
Check(store.Search("lunar rover battery capacity").Count == 0, "unsupported topic has no match");
Check(store.Search("udit").Count == 0, "partial-word substring does not match audit");
Check(store.Search("AUDIT logs").SequenceEqual(store.Search("audit log")), "case and simple plural normalization");
Check(store.Search("retention")[0].DocumentId == "retention", "document titles contribute useful retrieval terms");
Check(store.Search("workspace documents members", 1).Count == 1, "result limit is respected");
Check(store.Search("audit logs").SequenceEqual(store.Search("audit logs")), "deterministic ranking");
Check(store.Read("retention#audit-history")?.Excerpt.Contains("90 days", StringComparison.Ordinal) == true, "exact read returns fixture excerpt");
Check(store.Read("../appsettings.json") is null, "source IDs cannot read arbitrary files");
Reject(() => store.Search(" "), "empty search rejected");
Reject(() => store.Search(new string('x', 4001)), "oversized search rejected");
Reject(() => store.Search("audit", 5), "unbounded result limit rejected");
Reject(() => store.Read(""), "empty source ID rejected");
Reject(() => DocumentStore.ValidateQuestion(null), "null question rejected");
Check(DocumentStore.ValidateQuestion("  audit logs  ") == "audit logs", "question trimmed");
Check(new DocumentStore(Path.Combine(folder, "missing-folder")).Search("audit").Count == 0, "missing corpus is empty not fabricated");
var tools = new DocumentTools(store);
Reject(() => tools.Complete("Invented answer [fake#source]", "trace"), "answer without actual retrieval rejected");
tools.ReadSection("retention#audit-history");
Check(tools.Citations.Count == 0, "source must be retrieved before it can be cited");
using var search = JsonDocument.Parse(tools.SearchDocuments("audit logs retained"));
Check(search.RootElement.GetProperty("status").GetString() == "found", "search tool emits structured result");
Reject(() => tools.Complete("90 days", "trace"), "retrieval without actual section read rejected");
tools.ReadSection("retention#audit-history");
var answer = tools.Complete("Audit logs are retained for 90 days.", "0123456789abcdef0123456789abcdef");
Check(answer.Citations.Count == 1 && answer.Citations[0].SourceId == "retention#audit-history", "citations captured from actual tool read");
Check(answer.ToolCalls.SequenceEqual(new[] { "ReadSection", "SearchDocuments", "ReadSection" }), "actual tool sequence retained");
tools.ReadSection("retention#audit-history");
Check(tools.Citations.Count == 1, "repeated reads deduplicate citations");
var independent = new DocumentTools(store);
Check(independent.Citations.Count == 0 && independent.Calls.Count == 0, "fresh request has no leaked sources or calls");
independent.SearchDocuments("lunar rover battery capacity");
var missing = independent.Complete("Invented battery capacity: 600 kWh.", "trace");
Check(missing.Status == "not_found" && missing.Citations.Count == 0 && !missing.Answer.Contains("600", StringComparison.Ordinal), "no match suppresses fabricated answer");
var capped = new DocumentTools(store);
for (var index = 0; index < DocumentTools.MaxToolCalls; index++) capped.SearchDocuments("audit");
Reject(() => capped.SearchDocuments("audit"), "seventh tool execution rejected");
Check(capped.Calls.Count == DocumentTools.MaxToolCalls, "call cap cannot grow capture beyond bound");
Reject(() => tools.Complete(" ", "trace"), "blank grounded answer rejected");
Reject(() => tools.Complete(new string('x', 4001), "trace"), "oversized grounded answer rejected");
var contextual = new AskRequest("  How long does it last?  ", new(" audit logs ", " 90 days "), "retention#audit-history").Validate(store);
Check(contextual.Question == "How long does it last?" && contextual.PreviousTurn == new PreviousTurn("audit logs", "90 days"), "bounded previous turn and current question normalized");
Check(new AskRequest(new string('q', 4000), new(new string('p', 4000), new string('a', 4000))).Validate(store).PreviousTurn!.Answer!.Length == 4000, "exact context bounds accepted");
foreach (var invalid in new AskRequest[] {
    new("audit", new(null, "answer")), new("audit", new("prior", null)), new("audit", new("prior", " ")),
    new("audit", new(new string('q', 4001), "answer")), new("audit", new("prior", new string('a', 4001))),
    new("audit", SourceId: ""), new("audit", SourceId: new string('x', 161)), new("audit", SourceId: "missing#section"), new("audit", SourceId: "../appsettings.json") })
    Reject(() => invalid.Validate(store), "invalid context or source scope rejected before model execution");
Reject(() => new DocumentTools(store, "missing#section"), "tool scope validates exact bundled section existence");
var scoped = new DocumentTools(store, "exports#export-availability");
using var scopedSearch = JsonDocument.Parse(scoped.SearchDocuments("audit logs CSV export download"));
Check(scopedSearch.RootElement.GetProperty("sections").EnumerateArray().All(section => section.GetProperty("SourceId").GetString() == "exports#export-availability"), "scoped search never returns another section");
using var outside = JsonDocument.Parse(scoped.ReadSection("retention#audit-history"));
Check(outside.RootElement.GetProperty("reason").GetString() == "outside_source_scope" && scoped.Citations.Count == 0, "out-of-scope read cannot supply citation evidence");
scoped.ReadSection("exports#export-availability");
Check(scoped.Complete("24 hours", "trace").Citations.Single().SourceId == "exports#export-availability", "scoped citation comes only from selected section read");
var unrelatedScope = new DocumentTools(store, "retention#audit-history");
unrelatedScope.SearchDocuments("lunar rover battery");
Check(unrelatedScope.Complete("999 days based on previous answer", "trace").Status == "not_found", "scope alone cannot ground an unsupported question");
var reset = new AskRequest("lunar rover").Validate(store);
Check(reset.PreviousTurn is null && reset.SourceId is null, "new independent request has no implicit context or scope");
passed += await RuntimeTests.RunAsync(store);
passed += await PrivacyTests.RunAsync(store);
Console.WriteLine($"DOCS_QA_TESTS=pass; assertions={passed}");
