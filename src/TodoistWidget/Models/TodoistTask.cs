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

    [JsonPropertyName("checked")]
    public bool Checked { get; set; }

    [JsonPropertyName("labels")]
    public string[] Labels { get; set; } = [];

    [JsonPropertyName("child_order")]
    public int ChildOrder { get; set; }
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

            // v1 API: "2026-02-11T07:00:00" veya "2026-02-11" formati
            if (System.DateTime.TryParse(Date, out var dueDateTime))
            {
                return dueDateTime.Date < System.DateTime.Now.Date;
            }
            return false;
        }
    }

    /// <summary>Kisaltilmis tarih gosterimi: "11 Sub" veya "Bugun"</summary>
    public string ShortDateDisplay
    {
        get
        {
            if (string.IsNullOrEmpty(Date)) return "";
            if (System.DateTime.TryParse(Date, out var dt))
            {
                if (dt.Date == System.DateTime.Today) return "Bugun";
                if (dt.Date == System.DateTime.Today.AddDays(-1)) return "Dun";
                return dt.ToString("d MMM", new System.Globalization.CultureInfo("tr-TR"));
            }
            return "";
        }
    }
}

/// <summary>API v1 yanit wrapperi: {"results": [...]}</summary>
public sealed class TodoistApiResponse
{
    [JsonPropertyName("results")]
    public TodoistTask[] Results { get; set; } = [];
}
