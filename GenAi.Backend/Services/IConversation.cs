namespace GenAi.Backend.Services;

public interface IConversation
{
    Task Converse(
        Func<string, Task> write,
        Func<Task> writeLine,
        Func<CancellationToken, Task<string>> readLine,
        CancellationToken cancellationToken);
}
