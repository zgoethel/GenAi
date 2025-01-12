using GenAi.Backend.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GenAi.Backend.ViewModels;

public class HomeViewModel(
    IServiceProvider sp)
{
    public event Func<Task>? Changed;

    private async Task InvokeChanged()
    {
        foreach (var task in Changed?.GetInvocationList()?.Cast<Func<Task>>() ?? [])
        {
            await task();
        }
    }

    public event Func<Task>? ExpectingUserInput;

    private async Task InvokeExpectingUserInput()
    {
        foreach (var task in ExpectingUserInput?.GetInvocationList()?.Cast<Func<Task>>() ?? [])
        {
            await task();
        }
    }

    private TaskCompletionSource<string> userInputTask = new();
    private ChatMessageViewModel? currentChatMessage;

    public SalesbotConversation? Convo { get; set; }

    public List<ChatMessageViewModel> ChatLog { get; set; } = [];

    public bool WaitingForInput { get; set; }

    public async Task CreateOrContinueMessage(string message)
    {
        if (currentChatMessage is null)
        {
            currentChatMessage = new();

            switch (message)
            {
                case UniqueConversation.PrefixUser:
                    currentChatMessage.Color = "#d2efe0";
                    currentChatMessage.Label = message;
                    break;

                case UniqueConversation.PrefixAssistant:
                    currentChatMessage.Color = "#bbd3f5";
                    currentChatMessage.Label = message;
                    break;

                default:
                    currentChatMessage.Content = message;
                    break;
            }

            ChatLog.Add(currentChatMessage);
            await InvokeChanged();
        } else
        {
            await currentChatMessage.AppendContent(message);
        }
    }

    public async Task EndMessage()
    {
        currentChatMessage = null;
        await InvokeChanged();
    }

    public async Task<string> ReadUserInput(CancellationToken cancellationToken)
    {
        WaitingForInput = true;
        await InvokeChanged();

        await Task.Delay(200);
        await InvokeExpectingUserInput();

        return await userInputTask.Task.WaitAsync(cancellationToken);
    }

    public async Task ProvideInput(string message)
    {
        userInputTask.SetResult(message);

        WaitingForInput = false;
        await InvokeChanged();

        userInputTask = new();
    }

    public async Task BeginConversation(CancellationToken cancellationToken)
    {
        if (Convo is not null)
        {
            throw new Exception("There is already an active conversation");
        }
        Convo = sp.GetRequiredService<SalesbotConversation>();

        await Convo.Converse(CreateOrContinueMessage, EndMessage, ReadUserInput, cancellationToken);
    }
}
