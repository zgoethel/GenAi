using HtmlAgilityPack;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenAi.Backend.Services;

public class GeneralbotConversation(
    UniqueConversation conversation,
    OllamaService ollama,
    WebUiService webUi)
    : IDisposable, IConversation
{
    private readonly HashSet<string> UrlsAlreadyLoaded = [];

    public async Task<string> Begin(Func<StreamingChatCompletionUpdate, Task> wordCallback)
    {
        await conversation.SendMessage(ChatRole/*.System*/.User, SD.Prompts.GeneralbotInstructions, respond: false);

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
                    .Prepend(new(ChatRole/*.System*/.User, SD.Prompts.CustomerInstructions))
                    .Append(new(ChatRole.User, SD.Prompts.CustomerBegin(alreadyUsed)))
                    .ToList(),
                endpointPrefix: "Cheap");

            alreadyUsed.Add(possible.Text!);
            yield return possible.Text!;
        }
    }

    public class ChatIdentificationDetails
    {
        public bool UserWantsImageGenerated { get; set; }
        public bool IncludeTextResponseInAdditionToImage { get; set; }
        public bool UserWantsSimpleChatResponse { get; set; }
        public List<string> UrlsFromUserChatBotShouldRead { get; set; } = [];
    }

    public async Task<(string, string)> IdentifyChatMessage(
        string message,
        Func<string, Task> write,
        Func<Task> writeLine)
    {
        var chatRewritten = await ollama.CreateAiResponse(
            [
                new(ChatRole/*.System*/.User, SD.Prompts.ChatPromptRewriteInstructions),
                .. conversation.ChatHistory,
                new(ChatRole/*.System*/.User, SD.Prompts.ChatPromptRewriteInstructions),
                new(ChatRole.User, message)
            ]);
        await write(chatRewritten.Text ?? "");
        await write("\n");

        try
        {
            var chatIntentDetails = await ollama.CreateAiResponse(
                [
                    new(ChatRole/*.System*/.User, SD.Prompts.ChatPromptIntentSummaryInstructions),
                    new(ChatRole.User, chatRewritten.Text)
                ]);
            await write(chatIntentDetails.Text ?? "");

            var openJson = chatIntentDetails.Text?.IndexOf('{') ?? -1;
            var closeJson = chatIntentDetails.Text?.LastIndexOf('}') ?? -1;

            if (openJson == -1 || closeJson == -1)
            {
                return (SD.ChatIdentification.General, message);
            }
            var jsonOnly = chatIntentDetails.Text![openJson..(closeJson + 1)];
            var identified = JsonSerializer.Deserialize<ChatIdentificationDetails>(jsonOnly)!;

            foreach (var url in identified.UrlsFromUserChatBotShouldRead
                .Select((it) => it.Contains("://") ? it : $"https://{it}")
                .Where((it) => !UrlsAlreadyLoaded.Contains(it)))
            {
                try
                {
                    await write($"\n\nLoading `{url}`...");

                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                    
                    var content = await client.GetStringAsync(url);

                    var dom = new HtmlDocument();
                    dom.LoadHtml(content);

                    var plainText = dom.DocumentNode.SelectSingleNode("//body").InnerText;
                    var whiteSpaceRegex = new Regex("\\s\\s+");
                    plainText = whiteSpaceRegex.Replace(plainText, " ");

                    Console.WriteLine("Plaintext Content:");
                    Console.WriteLine(plainText);
                    Console.WriteLine();

                    var contentCrop = await ollama.CreateAiResponse(
                        [
                            new(ChatRole/*.System*/.User, "Recite the exact provided content word for word, cropping it down to the relevant body content and removing fragments such as navigational link text and irrelevant code."),
                            new(ChatRole.User, plainText[0..Math.Min(plainText.Length, 20000)])
                        ],
                        endpointPrefix: "Cheap");

                    plainText = contentCrop.Text ?? plainText;
                    plainText = plainText[0..Math.Min(plainText.Length, 5000)];

#if DEBUG
                    /*
                    Console.WriteLine("HTML Content:");
                    Console.WriteLine(content);
                    Console.WriteLine();
                    */

                    Console.WriteLine("Reworded Content:");
                    Console.WriteLine(plainText);
                    Console.WriteLine();
#endif

                    await conversation.SendMessage(new ChatMessage(ChatRole.User, $"Contents of `{url}`:\n" + plainText), respond: false);

                    UrlsAlreadyLoaded.Add(url);
                } catch (Exception ex)
                {
                    await write($"\n\n*Error loading `{url}`:*\n```\n{ex}\n```");
                }
            }

            if (identified.UserWantsImageGenerated)
            {
                return (SD.ChatIdentification.CreateImage, chatRewritten.Text ?? message);
            } else
            {
                return (SD.ChatIdentification.General, message);
            }
        } finally
        {
            await writeLine();
        }
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
                new(ChatRole/*.System*/.User, SD.Prompts.ImagePromptInstructions),
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

            var (chatIdentification, useMessage) = await IdentifyChatMessage(userPrompt, write, writeLine);

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

                    await Tell(useMessage, async (word) =>
                    {
                        await write(word.Text!);
                    });
                    await writeLine();

                    break;

                case SD.ChatIdentification.CreateImage:

                    await write("");
                    
                    await conversation.SendMessage(new ChatMessage(ChatRole.User, useMessage), respond: false);
                    await conversation.SendMessage(new ChatMessage(ChatRole.Assistant, "Image generated."), respond: false);

                    var imagePrompt = await GenerateImagePrompt(useMessage);

                    await write("Generating an image with the following prompt:\n\n");
                    await write($"`{imagePrompt}`");

                    await Task.Delay(200);
                    await write("");

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

                    await conversation.SendMessage(new(ChatRole.User, useMessage), respond: false);
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

                        var plainText = dom.DocumentNode.SelectSingleNode("//body").InnerText;
                        plainText = plainText[0..Math.Min(plainText.Length, 5000)];

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

                    await conversation.SendMessage(new ChatMessage(ChatRole.User, useMessage), respond: false);

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
