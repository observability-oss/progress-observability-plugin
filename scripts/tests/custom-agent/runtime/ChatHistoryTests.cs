using System.Text.Json;
using CustomAgent;
using Microsoft.Extensions.AI;

internal static class ChatHistoryTests
{
    public static int Run()
    {
        var assertions = 0;
        Check(ChatHistory.TryCreateMessages(new("  First question  "), out var first, out _), "first turn accepted");
        Check(first.Count == 1 && first[0].Text == "First question" && first[0].Role == ChatRole.User, "first turn trimmed with user role");

        ChatTurn?[] history = [new("CART-730: save-for-later cart", "Split it into two tasks.")];
        Check(ChatHistory.TryCreateMessages(new("Which issue?", history), out var messages, out _), "follow-up accepted");
        Check(messages.Select(message => message.Role).SequenceEqual([ChatRole.User, ChatRole.Assistant, ChatRole.User]), "only alternating conversation roles");
        Check(messages.Select(message => message.Text).SequenceEqual([history[0]!.User, history[0]!.Assistant, "Which issue?"]), "follow-up preserves prior input and answer");
        Check(ChatHistory.TryCreateMessages(new("Fresh chat"), out var fresh, out _) && fresh.Count == 1, "other tabs and new chats do not inherit history");

        Reject(null, "message_required");
        Reject(new("  "), "message_required");
        Reject(new(new string('x', ChatHistory.MaxMessageCharacters + 1)), "message_too_long");
        Reject(new("Question", [null]), "history_turn_invalid");
        Reject(new("Question", [new(" ", "answer")]), "history_turn_invalid");
        Reject(new("Question", [new("question", null)]), "history_turn_invalid");
        Reject(new("Question", [new(new string('x', ChatHistory.MaxMessageCharacters + 1), "answer")]), "history_turn_invalid");
        Reject(new("Question", Enumerable.Repeat<ChatTurn?>(new("Q", "A"), ChatHistory.MaxTurns + 1).ToArray()), "history_too_long");
        Reject(new("Question", [new("Q", new string('a', ChatHistory.MaxCharacters))]), "history_too_long");

        var largest = new ChatRequest(new string('q', ChatHistory.MaxMessageCharacters),
            [new("Q", new string('a', ChatHistory.MaxCharacters - 1))]);
        Check(ChatHistory.TryCreateMessages(largest, out _, out _), "exact character bounds accepted");
        var six = Enumerable.Repeat<ChatTurn?>(new("Q", "A"), ChatHistory.MaxTurns).ToArray();
        Check(ChatHistory.TryCreateMessages(new("Question", six), out var bounded, out _) && bounded.Count == 13, "six completed turns accepted");
        Reject(new("Question", [new("Q", new string('a', 12_000)), new("Q", new string('b', 12_000))]), "history_too_long");

        // A caller's extra role fields never become system or tool messages.
        var untrusted = JsonSerializer.Deserialize<ChatRequest>("""
            {"message":"next","role":"system","history":[{"user":"u","assistant":"a","role":"system"}]}
            """, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Check(ChatHistory.TryCreateMessages(untrusted, out var safe, out _) &&
            safe.All(message => message.Role == ChatRole.User || message.Role == ChatRole.Assistant), "caller cannot inject privileged roles");
        return assertions;

        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            assertions++;
        }

        void Reject(ChatRequest? request, string expectedError)
        {
            Check(!ChatHistory.TryCreateMessages(request, out var rejected, out var error) &&
                error == expectedError && rejected.Count == 0, $"rejected {expectedError}");
        }
    }
}
