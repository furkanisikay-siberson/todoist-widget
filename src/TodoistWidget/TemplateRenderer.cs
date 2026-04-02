using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using TodoistWidget.Models;

namespace TodoistWidget;

internal static class TemplateRenderer
{
    private static readonly ConcurrentDictionary<string, string> _templateCache = new();

    private static string LoadTemplate(string templateName)
    {
        return _templateCache.GetOrAdd(templateName, name =>
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            var path = Path.Combine(dir, "Templates", name);
            return File.ReadAllText(path);
        });
    }

    public static string GetSetupTemplate() => LoadTemplate("SetupCard.json");
    public static string GetSetupData() => "{}";

    public static string GetErrorTemplate() => LoadTemplate("ErrorCard.json");
    public static string GetErrorData(string message)
    {
        return JsonSerializer.Serialize(new { errorMessage = message });
    }

    public static string GetEmptyTemplate() => LoadTemplate("EmptyCard.json");
    public static string GetEmptyData() => "{}";

    public static string GetTaskListTemplate() => LoadTemplate("TaskListCard.json");

    public static string GetSettingsTemplate() => LoadTemplate("SettingsCard.json");

    public static string GetAddTaskTemplate() => LoadTemplate("AddTaskCard.json");
    public static string GetAddTaskData() => "{}";
    public static string GetSettingsData(WidgetState state)
    {
        return JsonSerializer.Serialize(new
        {
            userName = state.UserName ?? "",
            email = state.Email ?? ""
        });
    }

    // Widget Board cok buyuk data render edemiyor, gorev sayisini sinirla
    private const int MaxTasksMedium = 7;
    private const int MaxTasksTotal = 15;

    private static readonly System.Globalization.CultureInfo TrCulture = new("tr-TR");

    // Markdown link pattern: [text](url)
    private static readonly Regex MarkdownLinkRegex = new(@"\[([^\]]+)\]\([^\)]+\)", RegexOptions.Compiled);

    public static string GetTaskListData(List<TodoistTask> tasks, string? userName = null, string? avatarUrl = null, string? offlineNotice = null)
    {
        var overdue = new List<object>();
        var todayList = new List<object>();
        var total = 0;

        foreach (var task in tasks)
        {
            if (total >= MaxTasksTotal) break;

            var isOverdue = task.Due?.IsOverdue == true;
            var item = new
            {
                id = task.Id,
                content = TruncateContent(CleanContent(task.Content), 40),
                subtitle = BuildSubtitle(task, isOverdue)
            };

            if (isOverdue)
                overdue.Add(item);
            else
                todayList.Add(item);

            total++;
        }

        var data = new
        {
            userName = userName ?? "",
            avatarUrl = avatarUrl ?? "",
            todayDateHeader = DateTime.Now.ToString("dddd, d MMM", TrCulture),
            overdueTasks = overdue,
            overdueCount = overdue.Count,
            todayTasks = todayList,
            todayCount = todayList.Count,
            offlineNotice = offlineNotice ?? ""
        };

        return JsonSerializer.Serialize(data);
    }

    /// <summary>Markdown linklerini temizler: [text](url) -> text</summary>
    private static string CleanContent(string content)
    {
        return MarkdownLinkRegex.Replace(content, "$1");
    }

    /// <summary>Gorev alt satirini olusturur: tarih + tekrar + etiketler</summary>
    private static string BuildSubtitle(TodoistTask task, bool isOverdue)
    {
        var parts = new List<string>();

        // Tarih bilgisi
        if (task.Due != null)
        {
            var dateStr = isOverdue
                ? task.Due.ShortDateDisplay
                : "";

            // Saat bilgisi
            if (!string.IsNullOrEmpty(task.Due.DateTime) &&
                System.DateTime.TryParse(task.Due.DateTime, out var dt))
            {
                var timeStr = dt.ToString("H:mm", TrCulture);
                dateStr = string.IsNullOrEmpty(dateStr) ? timeStr : $"{dateStr} {timeStr}";
            }

            if (task.Due.IsRecurring)
            {
                dateStr = string.IsNullOrEmpty(dateStr)
                    ? "\u21bb tekrar"
                    : $"{dateStr} \u21bb";
            }

            if (!string.IsNullOrEmpty(dateStr))
                parts.Add(dateStr);
        }

        // Etiketler
        if (task.Labels.Length > 0)
        {
            foreach (var label in task.Labels)
            {
                parts.Add($"# {label}");
            }
        }

        return string.Join("  \u00b7  ", parts);
    }

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;

        // StringInfo ile kes - emoji/surrogate pair'leri ortadan bolme
        var info = new System.Globalization.StringInfo(content);
        var textElements = info.LengthInTextElements;

        if (textElements <= maxLength) return content;

        // Guvenli kesme: text element bazinda kes
        var result = info.SubstringByTextElements(0, Math.Min(maxLength - 1, textElements));
        return result + "\u2026";
    }
}
