using System.Text.Json.Serialization;

namespace TodoistWidget.Models;

public sealed class TodoistTask
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    [JsonPropertyName("project_id")]
    public string ProjectId { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 1;

    [JsonPropertyName("due")]
    public TodoistDue? Due { get; set; }

    [JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }

    [JsonPropertyName("labels")]
    public string[] Labels { get; set; } = [];

    [JsonPropertyName("order")]
    public int Order { get; set; }
}

public sealed class TodoistDue
{
    [JsonPropertyName("string")]
    public string DisplayString { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("is_recurring")]
    public bool IsRecurring { get; set; }

    [JsonPropertyName("datetime")]
    public string? DateTime { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    public bool IsOverdue
    {
        get
        {
            if (string.IsNullOrEmpty(Date)) return false;
            if (DateOnly.TryParse(Date, out var dueDate))
            {
                return dueDate < DateOnly.FromDateTime(System.DateTime.Now);
            }
            return false;
        }
    }
}
