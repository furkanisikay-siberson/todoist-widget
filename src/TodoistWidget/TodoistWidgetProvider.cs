using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Windows.Widgets.Providers;
using TodoistWidget.Models;

namespace TodoistWidget;

[ComVisible(true)]
[ComDefaultInterface(typeof(IWidgetProvider))]
[Guid(ClsId)]
public sealed class TodoistWidgetProvider : IWidgetProvider
{
    public const string ClsId = "E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70";
    public const string DefinitionId = "Todoist_Tasks_Widget";

    private static readonly Dictionary<string, WidgetState> _states = new();
    private static readonly object _lock = new();

    public TodoistWidgetProvider()
    {
        RecoverWidgets();
    }

    private static void RecoverWidgets()
    {
        try
        {
            var manager = WidgetManager.GetDefault();
            foreach (var info in manager.GetWidgetInfos())
            {
                var widgetId = info.WidgetContext.Id;
                lock (_lock)
                {
                    if (!_states.ContainsKey(widgetId))
                    {
                        _states[widgetId] = WidgetState.Deserialize(info.CustomState);
                    }
                }
            }
        }
        catch
        {
            // Widget Board henuz hazir degilse veya hata olusursa sessizce devam et
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        var widgetId = widgetContext.Id;
        var state = new WidgetState();

        lock (_lock)
        {
            _states[widgetId] = state;
        }

        UpdateWidgetUI(widgetId, state);
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (_lock)
        {
            _states.Remove(widgetId);
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        var widgetId = widgetContext.Id;
        WidgetState state;

        lock (_lock)
        {
            if (!_states.TryGetValue(widgetId, out state!))
            {
                state = new WidgetState();
                _states[widgetId] = state;
            }
        }

        if (state.HasToken)
        {
            _ = RefreshTasksAsync(widgetId, state);
        }
        else
        {
            UpdateWidgetUI(widgetId, state);
        }
    }

    public void Deactivate(string widgetId)
    {
        // Widget gorunmez oldu, yapacak bir sey yok
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var widgetId = actionInvokedArgs.WidgetContext.Id;
        var verb = actionInvokedArgs.Verb;
        var data = actionInvokedArgs.Data;

        WidgetState state;
        lock (_lock)
        {
            if (!_states.TryGetValue(widgetId, out state!))
            {
                state = WidgetState.Deserialize(actionInvokedArgs.CustomState);
                _states[widgetId] = state;
            }
        }

        switch (verb)
        {
            case "saveToken":
                HandleSaveToken(widgetId, state, data);
                break;

            case "completeTask":
                HandleCompleteTask(widgetId, state, data);
                break;

            case "refresh":
                HandleRefresh(widgetId, state);
                break;

            case "resetToken":
                HandleResetToken(widgetId, state);
                break;
        }
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var widgetId = contextChangedArgs.WidgetContext.Id;

        WidgetState state;
        lock (_lock)
        {
            if (!_states.TryGetValue(widgetId, out state!)) return;
        }

        UpdateWidgetUI(widgetId, state);
    }

    // --- Action Handlers ---

    private void HandleSaveToken(string widgetId, WidgetState state, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("apiToken", out var tokenElement))
            {
                var token = tokenElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    ShowError(widgetId, state, "Token bos olamaz.");
                    return;
                }

                state.Token = token;
                state.LastError = null;

                _ = ValidateAndFetchAsync(widgetId, state);
            }
            else
            {
                ShowError(widgetId, state, "Token algilanamadi. Tekrar deneyin.");
            }
        }
        catch
        {
            ShowError(widgetId, state, "Token isleme hatasi.");
        }
    }

    private void HandleCompleteTask(string widgetId, WidgetState state, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("taskId", out var taskIdElement))
            {
                var taskId = taskIdElement.GetString();
                if (!string.IsNullOrEmpty(taskId))
                {
                    _ = CompleteAndRefreshAsync(widgetId, state, taskId);
                }
            }
        }
        catch
        {
            // Sessizce yoksay - UI bozulmamali
        }
    }

    private void HandleRefresh(string widgetId, WidgetState state)
    {
        if (state.HasToken)
        {
            _ = RefreshTasksAsync(widgetId, state);
        }
        else
        {
            ShowSetup(widgetId, state);
        }
    }

    private void HandleResetToken(string widgetId, WidgetState state)
    {
        state.Token = null;
        state.CachedTasks.Clear();
        state.LastError = null;
        ShowSetup(widgetId, state);
    }

    // --- Async Islemler ---

    private async Task ValidateAndFetchAsync(string widgetId, WidgetState state)
    {
        try
        {
            using var client = new TodoistApiClient(state.Token!);
            var isValid = await client.ValidateTokenAsync();

            if (!isValid)
            {
                state.Token = null;
                ShowError(widgetId, state, "Token gecersiz. Todoist ayarlarindan kontrol edin.");
                return;
            }

            await RefreshTasksAsync(widgetId, state);
        }
        catch
        {
            ShowError(widgetId, state, "Token dogrulanamadi. Internet baglantinizi kontrol edin.");
        }
    }

    private async Task RefreshTasksAsync(string widgetId, WidgetState state)
    {
        if (!state.HasToken) return;

        try
        {
            using var client = new TodoistApiClient(state.Token!);
            var (error, tasks) = await client.GetTodayTasksAsync();

            if (error != null)
            {
                state.LastError = error;

                if (state.CachedTasks.Count > 0)
                {
                    UpdateWidgetWithTasks(widgetId, state, state.CachedTasks, offlineNotice: error);
                }
                else
                {
                    ShowError(widgetId, state, error);
                }
                return;
            }

            state.CachedTasks = tasks;
            state.LastFetchUtc = DateTime.UtcNow.ToString("o");
            state.LastError = null;

            if (tasks.Count == 0)
            {
                ShowEmpty(widgetId, state);
            }
            else
            {
                UpdateWidgetWithTasks(widgetId, state, tasks);
            }
        }
        catch
        {
            if (state.CachedTasks.Count > 0)
            {
                UpdateWidgetWithTasks(widgetId, state, state.CachedTasks, offlineNotice: "Baglanti hatasi");
            }
            else
            {
                ShowError(widgetId, state, "Gorevler alinamadi.");
            }
        }
    }

    private async Task CompleteAndRefreshAsync(string widgetId, WidgetState state, string taskId)
    {
        if (!state.HasToken) return;

        try
        {
            // Optimistic UI: once gorev listeden kaldir
            state.CachedTasks.RemoveAll(t => t.Id == taskId);
            if (state.CachedTasks.Count == 0)
                ShowEmpty(widgetId, state);
            else
                UpdateWidgetWithTasks(widgetId, state, state.CachedTasks);

            // Sonra API'ye gonder
            using var client = new TodoistApiClient(state.Token!);
            var error = await client.CompleteTaskAsync(taskId);

            if (error != null)
            {
                state.LastError = error;
            }

            // Tam listeyi yenile (tekrarlayan gorevler icin)
            await RefreshTasksAsync(widgetId, state);
        }
        catch
        {
            // UI zaten guncellenmisti, sonraki refresh duzeltir
        }
    }

    // --- UI Guncelleme ---

    private void UpdateWidgetUI(string widgetId, WidgetState state)
    {
        if (!state.HasToken)
        {
            ShowSetup(widgetId, state);
        }
        else if (state.CachedTasks.Count > 0)
        {
            UpdateWidgetWithTasks(widgetId, state, state.CachedTasks);
        }
        else if (state.LastError != null)
        {
            ShowError(widgetId, state, state.LastError);
        }
        else
        {
            ShowEmpty(widgetId, state);
        }
    }

    private void ShowSetup(string widgetId, WidgetState state)
    {
        PushUpdate(widgetId, TemplateRenderer.GetSetupTemplate(), TemplateRenderer.GetSetupData(), state);
    }

    private void ShowError(string widgetId, WidgetState state, string message)
    {
        state.LastError = message;
        PushUpdate(widgetId, TemplateRenderer.GetErrorTemplate(), TemplateRenderer.GetErrorData(message), state);
    }

    private void ShowEmpty(string widgetId, WidgetState state)
    {
        PushUpdate(widgetId, TemplateRenderer.GetEmptyTemplate(), TemplateRenderer.GetEmptyData(), state);
    }

    private void UpdateWidgetWithTasks(string widgetId, WidgetState state, List<TodoistTask> tasks, string? offlineNotice = null)
    {
        PushUpdate(widgetId, TemplateRenderer.GetTaskListTemplate(), TemplateRenderer.GetTaskListData(tasks, offlineNotice), state);
    }

    private static void PushUpdate(string widgetId, string template, string data, WidgetState state)
    {
        try
        {
            var updateOptions = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = template,
                Data = data,
                CustomState = state.Serialize()
            };

            WidgetManager.GetDefault().UpdateWidget(updateOptions);
        }
        catch
        {
            // Widget Board ulasilamazsa sessizce yoksay
        }
    }
}
