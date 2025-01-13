using Microsoft.Extensions.Configuration;
using SixLabors.ImageSharp;
using System.Net.Http.Json;

namespace GenAi.Backend.Services;

public class WebUiService(
    IConfiguration config
    ) : IDisposable
{
    private class WebUiResponse
    {
        public List<string> Images { get; set; } = [];
    }

    private readonly SemaphoreSlim concurrentLock = new(1, 1);

    private string Config_Endpoint => config["WebUi:Endpoint"]!;

    void IDisposable.Dispose()
    {
        concurrentLock.Dispose();
    }

    public async Task<string> CreateAiResponse(string prompt, TimeSpan? waitTimeout = null)
    {
        waitTimeout ??= TimeSpan.FromSeconds(200);
        if (waitTimeout == TimeSpan.Zero)
        {
            await concurrentLock.WaitAsync();
        }
        else
        {
            if (!await concurrentLock.WaitAsync(waitTimeout.Value))
            {
                throw new Exception("Timed out waiting for a turn to process the prompt");
            }
        }
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(200);

            using var imageRequest = client.PostAsJsonAsync(
                $"{Config_Endpoint.TrimEnd('/')}/sdapi/v1/txt2img",
                new
                {
                    prompt,
                    steps = 5
                });

            var imageResponse = await imageRequest.Result.Content.ReadFromJsonAsync<WebUiResponse>();
            using var imageStream = new MemoryStream(Convert.FromBase64String(imageResponse!.Images.First()));

            var data = await Image.IdentifyAsync(imageStream);

            return $"data:{data.Metadata.DecodedImageFormat!.DefaultMimeType};base64,{imageResponse.Images.First()}";
        } finally
        {
            concurrentLock.Release();
        }
    }
}
