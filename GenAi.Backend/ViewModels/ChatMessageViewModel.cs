namespace GenAi.Backend.ViewModels;

public class ChatMessageViewModel
{
    public event Func<Task>? Changed;

    private async Task InvokeChanged()
    {
        foreach (var task in Changed?.GetInvocationList()?.Cast<Func<Task>>() ?? [])
        {
            await task();
        }
    }

    public string Color { get; set; } = "";

    public string Label { get; set; } = "";

    public string Content { get; set; } = "";

    public bool AutoCollapse => string.IsNullOrEmpty(Label);

    public async Task AppendContent(string message)
    {
        Content += message;
        await InvokeChanged();
    }
}
