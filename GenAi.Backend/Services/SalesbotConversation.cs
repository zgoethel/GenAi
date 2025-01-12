using Microsoft.Extensions.AI;

namespace GenAi.Backend.Services;

public class SalesbotConversation(
    UniqueConversation conversation,
    OllamaService ollama)
    : IDisposable
{
    public async Task<string> Begin(Func<StreamingChatCompletionUpdate, Task> wordCallback)
    {
        await conversation.SendMessage(ChatRole.System, SD.Prompts.SalesbotInstructions, respond: false);

        var response = await conversation.SendMessage(ChatRole.User, SD.Prompts.SalesbotBegin, wordCallback: wordCallback);
        return response!.Text!;
    }

    public async IAsyncEnumerable<string> GeneratePossibleResponses()
    {
        var alreadyUsed = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            var possible = await ollama.CreateAiResponse(
                conversation.ChatHistory
                    .Where((it) => it.Role == ChatRole.User || it.Role == ChatRole.Assistant)
                    .Prepend(new(ChatRole.System, SD.Prompts.CustomerInstructions))
                    .Append(new(ChatRole.User, SD.Prompts.CustomerBegin(alreadyUsed)))
                    .ToList());

            alreadyUsed.Add(possible.Text!);
            yield return possible.Text!;
        }
    }

    public async Task<string> Tell(string message, Func<StreamingChatCompletionUpdate, Task> wordCallback)
    {
        var response = await conversation.SendMessage(new(ChatRole.User, message), wordCallback: wordCallback);
        return response!.Text!;
    }

    public async Task Converse(Func<string, Task> write, Func<Task> writeLine, Func<CancellationToken, Task<string>> readLine, CancellationToken cancellationToken)
    {
        await write(UniqueConversation.PrefixAssistant);

        await Begin(async (word) =>
        {
            await write(word.Text!);
        });
        await writeLine();

        while (!cancellationToken.IsCancellationRequested && !isDisposed)
        {
            await write("Select one of the possible responses (1-3), or enter a custom response.\n");

            var possibleResponses = new List<string>();
            var index = 1;
            await foreach (var possible in GeneratePossibleResponses())
            {
                possibleResponses.Add(possible);

                await write($"  {index++}. {possible}\n");
            }

            await write("\n");

            await write("Your prompt (1-3, or text): ");
            await writeLine();

            if (cancellationToken.IsCancellationRequested || isDisposed)
            {
                break;
            }

            var userPrompt = await readLine(cancellationToken);
            if (int.TryParse(userPrompt, out var i)
                && i >= 1 && i <= 3)
            {
                userPrompt = possibleResponses[i - 1];
            }

            await write(UniqueConversation.PrefixUser);
            await write(userPrompt);
            await writeLine();

            await write(UniqueConversation.PrefixAssistant);

            if (cancellationToken.IsCancellationRequested || isDisposed)
            {
                break;
            }

            await Tell(userPrompt, async (word) =>
            {
                await write(word.Text!);
            });
            await writeLine();
        }
    }

    private bool isDisposed;

    void IDisposable.Dispose()
    {
        isDisposed = true;
    }
}
