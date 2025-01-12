using Microsoft.Extensions.AI;

namespace GenAi.Backend.Services;

public class UniqueConversation(
    OllamaService ollama)
{
    public List<ChatMessage> ChatHistory = [];

    public async Task<ChatMessage?> SendMessage(
        ChatMessage message,
        bool respond = true,
        Func<StreamingChatCompletionUpdate, Task>? wordCallback = null,
        TimeSpan? waitTimeout = null)
    {
        ChatHistory.Add(message);

        if (respond)
        {
            return await CreateAiResponse(wordCallback: wordCallback, waitTimeout: waitTimeout);
        }
        return null;
    }

    public async Task<ChatMessage?> SendMessage(
        ChatRole role,
        string message,
        bool respond = true,
        Func<StreamingChatCompletionUpdate, Task>? wordCallback = null,
        TimeSpan? waitTimeout = null)
    {
        return await SendMessage(new(role, message), respond: respond, wordCallback: wordCallback, waitTimeout: waitTimeout);
    }

    public async Task<ChatMessage> CreateAiResponse(
        Func<StreamingChatCompletionUpdate, Task>? wordCallback = null,
        TimeSpan? waitTimeout = null)
    {
        var response = await ollama.CreateAiResponse(ChatHistory, wordCallback: wordCallback, waitTimeout: waitTimeout);
        ChatHistory.Add(response);

        return response;
    }
}
