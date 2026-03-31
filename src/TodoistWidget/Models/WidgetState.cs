using System.Text.Json;
using System.Text.Json.Serialization;

namespace TodoistWidget.Models;

public sealed class WidgetState
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("cachedTasks")]
    public List<TodoistTask> CachedTasks { get; set; } = [];

    [JsonPropertyName("lastFetchUtc")]
    public string? LastFetchUtc { get; set; }

    [JsonPropertyName("lastError")]
    public string? LastError { get; set; }

    public bool HasToken => !string.IsNullOrWhiteSpace(Token);

    public string Serialize() => JsonSerializer.Serialize(this, WidgetStateContext.Default.WidgetState);

    public static WidgetState Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new WidgetState();
        try
        {
            return JsonSerializer.Deserialize(json, WidgetStateContext.Default.WidgetState) ?? new WidgetState();
        }
        catch
        {
            return new WidgetState();
        }
    }
}

[JsonSerializable(typeof(WidgetState))]
[JsonSerializable(typeof(List<TodoistTask>))]
[JsonSerializable(typeof(TodoistTask))]
[JsonSerializable(typeof(TodoistDue))]
[JsonSerializable(typeof(TodoistTask[]))]
[JsonSerializable(typeof(TodoistApiResponse))]
internal sealed partial class WidgetStateContext : JsonSerializerContext
{
}
