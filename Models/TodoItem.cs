namespace FloatTodo.Models;

public sealed class TodoItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public bool IsCompleted { get; init; }
    public required string CreatedUtc { get; init; }
    public string? CompletedUtc { get; init; }
}
