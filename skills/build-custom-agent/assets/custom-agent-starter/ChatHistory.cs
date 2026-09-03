using Microsoft.Extensions.AI;

namespace CustomAgent;

// The browser sends completed exchanges, not a session ID. Nothing is kept
// globally or on disk, and callers cannot supply system or tool messages.
public sealed record ChatTurn(string? User, string? Assistant);
public sealed record ChatRequest(string? Message, ChatTurn?[]? History = null);

public static class ChatHistory
{
    public const int MaxMessageCharacters = 4_000;
    public const int MaxTurns = 6;
    public const int MaxCharacters = 24_000;

    public static bool TryCreateMessages(
        ChatRequest? request,
        out IReadOnlyList<ChatMessage> messages,
        out string? error)
    {
        messages = [];
        error = null;
        var message = request?.Message?.Trim();
        if (string.IsNullOrWhiteSpace(message))
            error = "message_required";
        else if (message.Length > MaxMessageCharacters)
            error = "message_too_long";
        else if (request?.History is { Length: > MaxTurns })
            error = "history_too_long";
        if (error is not null) return false;

        var result = new List<ChatMessage>();
        var characters = 0;
        foreach (var turn in request!.History ?? [])
        {
            if (turn is null || string.IsNullOrWhiteSpace(turn.User) ||
                string.IsNullOrWhiteSpace(turn.Assistant) || turn.User.Length > MaxMessageCharacters)
            {
                error = "history_turn_invalid";
                return false;
            }
            // Check before adding, including for an untrusted oversized string.
            if (turn.User.Length > MaxCharacters - characters ||
                turn.Assistant.Length > MaxCharacters - characters - turn.User.Length)
            {
                error = "history_too_long";
                return false;
            }
            characters += turn.User.Length + turn.Assistant.Length;
            result.Add(new ChatMessage(ChatRole.User, turn.User));
            result.Add(new ChatMessage(ChatRole.Assistant, turn.Assistant));
        }
        result.Add(new ChatMessage(ChatRole.User, message));
        messages = result;
        return true;
    }
}
