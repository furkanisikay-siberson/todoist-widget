using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
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

    // Widget Board cok buyuk data render edemiyor, gorev sayisini sinirla
    private const int MaxTasksMedium = 7;
    private const int MaxTasksTotal = 15;

    public static string GetTaskListData(List<TodoistTask> tasks, string? offlineNotice = null)
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
                content = TruncateContent(task.Content, 30),
                priorityIcon = GetPriorityIcon(task.Priority),
                dueDisplay = task.Due?.ShortDateDisplay ?? ""
            };

            if (isOverdue)
                overdue.Add(item);
            else
                todayList.Add(item);

            total++;
        }

        var data = new
        {
            overdueTasks = overdue,
            overdueCount = overdue.Count,
            todayTasks = todayList,
            todayCount = todayList.Count,
            offlineNotice = offlineNotice ?? ""
        };

        return JsonSerializer.Serialize(data);
    }

    private static string GetPriorityIcon(int priority) => priority switch
    {
        4 => "\ud83d\udd34",  // P1 - Urgent (red circle)
        3 => "\ud83d\udfe0",  // P2 - High (orange circle)
        2 => "\ud83d\udd35",  // P3 - Medium (blue circle)
        _ => "\u26aa"          // P4 - Normal (white circle)
    };

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
