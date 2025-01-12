using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace GenAi.Backend.Services;

public class OllamaService : IDisposable
{
    private readonly IConfiguration config;
    private readonly SemaphoreSlim concurrentLock;

    private string Config_Endpoint => config["Ollama:Endpoint"]!;
    private string Config_Model => config["Ollama:Model"]!;
    private int Config_MaxConcurrent => int.Parse(config["Ollama:MaxConcurrent"]!);

    public OllamaService(
        IConfiguration config)
    {
        this.config = config;

        concurrentLock = new(Config_MaxConcurrent, Config_MaxConcurrent);
    }

    void IDisposable.Dispose()
    {
        concurrentLock.Dispose();
    }

    public async Task RunInConnection(Func<OllamaChatClient, Task> doWork, TimeSpan? waitTimeout = null)
    {
        waitTimeout ??= TimeSpan.FromSeconds(90);
        if (waitTimeout == TimeSpan.Zero)
        {
            await concurrentLock.WaitAsync();
        } else
        {
            if (!await concurrentLock.WaitAsync(waitTimeout.Value))
            {
                throw new Exception("Timed out waiting for a turn to process the prompt");
            }
        }
        try
        {
            using var client = new OllamaChatClient(Config_Endpoint, Config_Model);

            await doWork(client);
        } finally
        {
            concurrentLock.Release();
        }
    }

    public async Task<ChatMessage> CreateAiResponse(IList<ChatMessage> chatHistory, Func<StreamingChatCompletionUpdate, Task>? wordCallback = null, TimeSpan? waitTimeout = null)
    {
        var response = new StringBuilder();

        async Task run(OllamaChatClient client)
        {
            await foreach (var item in client.CompleteStreamingAsync(chatHistory))
            {
                if (wordCallback is not null)
                {
                    await wordCallback(item);
                }

                response.Append(item.Text);
            }
        }

        await RunInConnection(run, waitTimeout: waitTimeout);

        return new(ChatRole.Assistant, response.ToString());
    }
}
