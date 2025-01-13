using HtmlAgilityPack;
using Microsoft.Extensions.AI;

namespace GenAi.Backend.Services;

public class GeneralbotConversation(
    UniqueConversation conversation,
    OllamaService ollama,
    WebUiService webUi)
    : IDisposable, IConversation
{
    public async Task<string> Begin(Func<StreamingChatCompletionUpdate, Task> wordCallback)
    {
        await conversation.SendMessage(ChatRole.System, SD.Prompts.GeneralbotInstructions, respond: false);

        var response = await conversation.SendMessage(ChatRole.User, SD.Prompts.GeneralbotBegin, wordCallback: wordCallback);
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

    public async Task<string> IdentifyChatMessage(string message, int depth = 0)
    {
        if (depth == 0)
        {
            var responses = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    var sample = await IdentifyChatMessage(message, 1);
                    responses.Add(sample);
                } catch (Exception)
                {
                }
            }
            return responses
                .GroupBy((it) => it)
                .Cast<IEnumerable<string>>()
                .DefaultIfEmpty(["G"])
                .MaxBy((it) => it.Count())!
                .First();
        }

        var chatType = await ollama.CreateAiResponse(
            [
                new(ChatRole.System, SD.Prompts.ChatIdentificationInstructions),
                new(ChatRole.User, message)
            ]);

        switch (chatType.Text!.Trim().Split("\n").First())
        {
            case SD.ChatIdentification.General:
            case SD.ChatIdentification.CreateImage:
            case SD.ChatIdentification.ReadSite:
            case SD.ChatIdentification.SuggestQuestions:
                return chatType.Text!.Trim().Split("\n").First();

            default:
                break;
        }

        if (depth > 3)
        {
            throw new Exception("Failed to identify the type of prompt message provided");
        }
        return await IdentifyChatMessage(message, depth + 1);
    }

    public async Task<string> Tell(string message, Func<StreamingChatCompletionUpdate, Task> wordCallback)
    {
        var response = await conversation.SendMessage(new(ChatRole.User, message), wordCallback: wordCallback);
        return response!.Text!;
    }

    public async Task<string> GenerateImagePrompt(string message)
    {
        var imagePrompt = await ollama.CreateAiResponse(
            [
                new(ChatRole.System, SD.Prompts.ImagePromptInstructions),
                new(ChatRole.User, message)
            ]);

        return imagePrompt.Text!;
    }

    public async Task Converse(
        Func<string, Task> write,
        Func<Task> writeLine,
        Func<CancellationToken, Task<string>> readLine,
        CancellationToken cancellationToken)
    {
        await write(SD.Labels.PrefixAssistant);

        await Begin(async (word) =>
        {
            await write(word.Text!);
        });
        await writeLine();

        while (!cancellationToken.IsCancellationRequested && !isDisposed)
        {
            if (cancellationToken.IsCancellationRequested || isDisposed)
            {
                break;
            }

            var userPrompt = await readLine(cancellationToken);

        reprocessMessage:
            await write(SD.Labels.PrefixUser);

            await write(userPrompt);
            await writeLine();

            if (cancellationToken.IsCancellationRequested || isDisposed)
            {
                break;
            }

            var chatIdentification = await IdentifyChatMessage(userPrompt);

            await write($"Identification = {chatIdentification}");
            await writeLine();

            if (cancellationToken.IsCancellationRequested || isDisposed)
            {
                break;
            }

            switch (chatIdentification)
            {
                case SD.ChatIdentification.General:

                    await write(SD.Labels.PrefixAssistant);

                    await Tell(userPrompt, async (word) =>
                    {
                        await write(word.Text!);
                    });
                    await writeLine();

                    break;

                case SD.ChatIdentification.CreateImage:

                    await write("");
                    
                    await conversation.SendMessage(new ChatMessage(ChatRole.User, userPrompt), respond: false);
                    await conversation.SendMessage(new ChatMessage(ChatRole.Assistant, "Image generated."), respond: false);

                    var imagePrompt = await GenerateImagePrompt(userPrompt);

                    await write("Generating an image with the following prompt:\n\n");
                    await write($"`{imagePrompt}`");

                    try
                    {
                        var image = await webUi.CreateAiResponse(imagePrompt);

                        await write($"\n\n<center><img src=\"{image}\" class=\"mb-2 border shadow\" style=\"width: 300px;max-width: 100%;\" />\n\nComplete!</center>");
                    } catch (Exception ex)
                    {
                        await write($"\n\n```\n{ex.ToString()}\n```\n\nRefer to the error above.");
                    }
                    await writeLine();

                    break;

                case SD.ChatIdentification.ReadSite:

                    await write("Provide a full URL with the protocol included. This source can be referenced going forward.\n\n");
                    await write("Enter \"Cancel\" to exit.");
                    await writeLine();

                    await conversation.SendMessage(new(ChatRole.User, userPrompt), respond: false);
                    await conversation.SendMessage(new(ChatRole.Assistant, "Content loaded."), respond: false);

                    var requestUrl = await readLine(cancellationToken);

                    await write(SD.Labels.PrefixUser);

                    await write(requestUrl);
                    await writeLine();

                    if (requestUrl.Trim().ToLower() == "cancel")
                    {
                        break;
                    }

                    await write(SD.Labels.PrefixAssistant);

                    try
                    {
                        using var client = new HttpClient();

                        var loadedPage = await client.GetAsync(requestUrl.Trim());
                        var loadedContent = await loadedPage.Content.ReadAsStringAsync();

                        var dom = new HtmlDocument();
                        dom.LoadHtml(loadedContent);

                        var plainText = dom.DocumentNode.InnerText;

#if DEBUG
                        Console.WriteLine("HTML Content:");
                        Console.WriteLine(loadedContent);
                        Console.WriteLine();

                        Console.WriteLine("Plaintext Content:");
                        Console.WriteLine(plainText);
                        Console.WriteLine();
#endif
                        await conversation.SendMessage(new ChatMessage(ChatRole.User, "Site Content:\n" + plainText), respond: false);
                        
                        await Tell("What do you think about that?", async (word) =>
                        {
                            await write(word.Text!);
                        });
                        //await write("I have read the contents of the site!");
                    } catch (Exception ex)
                    {
                        await write($"I encountered an issue loading the site: \"{ex.Message}\"");
                    }
                    await writeLine();

                    break;

                case SD.ChatIdentification.SuggestQuestions:

                    await write("Select one of the possible responses (1-3), or enter a custom response.\n");

                    await conversation.SendMessage(new ChatMessage(ChatRole.User, userPrompt), respond: false);

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

                    userPrompt = await readLine(cancellationToken);
                    if (int.TryParse(userPrompt, out var i) && i >= 1 && i <= 3)
                    {
                        userPrompt = possibleResponses[i - 1];
                    }

                    goto reprocessMessage;
            }
        }
    }

    private bool isDisposed;

    void IDisposable.Dispose()
    {
        isDisposed = true;
    }
}
