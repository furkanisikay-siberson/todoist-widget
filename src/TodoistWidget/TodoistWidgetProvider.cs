using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Security.Credentials;
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

    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TodoistWidget");

    private static readonly string LogPath = Path.Combine(AppDataDir, "widget.log");
    private const string VaultResource = "TodoistWidget";
    private const string VaultUser = "api_token";

    private static void Log(string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
        try
        {
            Directory.CreateDirectory(AppDataDir);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>Token'i Windows Credential Manager'a kaydeder (sifrelenmis).</summary>
    private static void SaveTokenToVault(string token)
    {
        try
        {
            var vault = new PasswordVault();
            // Onceki kaydi sil (varsa)
            try { vault.Remove(vault.Retrieve(VaultResource, VaultUser)); } catch { }
            vault.Add(new PasswordCredential(VaultResource, VaultUser, token));
        }
        catch (Exception ex)
        {
            Log($"SaveTokenToVault HATA: {ex.Message}");
        }
    }

    /// <summary>Token'i Windows Credential Manager'dan okur.</summary>
    private static string? LoadTokenFromVault()
    {
        try
        {
            var vault = new PasswordVault();
            var cred = vault.Retrieve(VaultResource, VaultUser);
            cred.RetrievePassword();
            return string.IsNullOrWhiteSpace(cred.Password) ? null : cred.Password;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Token'i Windows Credential Manager'dan siler.</summary>
    private static void DeleteTokenFromVault()
    {
        try
        {
            var vault = new PasswordVault();
            vault.Remove(vault.Retrieve(VaultResource, VaultUser));
        }
        catch { }
    }

    public TodoistWidgetProvider()
    {
        RecoverWidgets();
    }

    private static void RecoverWidgets()
    {
        try
        {
            var manager = WidgetManager.GetDefault();
            var infos = manager.GetWidgetInfos();
            Log($"RecoverWidgets: {infos.Length} widget bulundu");
            foreach (var info in infos)
            {
                var widgetId = info.WidgetContext.Id;
                var customState = info.CustomState;
                Log($"RecoverWidgets: widgetId={widgetId}, customStateLen={customState?.Length ?? 0}");
                lock (_lock)
                {
                    if (!_states.ContainsKey(widgetId))
                    {
                        var state = WidgetState.Deserialize(customState);
                        Log($"RecoverWidgets: recovered hasToken={state.HasToken}, cachedTasks={state.CachedTasks.Count}");
                        _states[widgetId] = state;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"RecoverWidgets HATA: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        var widgetId = widgetContext.Id;
        Log($"CreateWidget: widgetId={widgetId}");

        WidgetState state;
        lock (_lock)
        {
            // 1) Mevcut bir widget'tan token kopyala
            var existing = _states.Values.FirstOrDefault(s => s.HasToken);
            if (existing != null)
            {
                state = new WidgetState
                {
                    Token = existing.Token,
                    CachedTasks = new List<TodoistTask>(existing.CachedTasks),
                    LastFetchUtc = existing.LastFetchUtc
                };
                Log($"CreateWidget: token mevcut widget'tan kopyalandi");
            }
            else
            {
                // 2) Yerel dosyadan token oku
                state = new WidgetState();
                var fileToken = LoadTokenFromVault();
                if (fileToken != null)
                {
                    state.Token = fileToken;
                    Log($"CreateWidget: token dosyadan yuklendi");
                }
            }

            _states[widgetId] = state;
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
        Log($"Activate: widgetId={widgetId}");
        WidgetState state;

        lock (_lock)
        {
            if (!_states.TryGetValue(widgetId, out state!))
            {
                state = new WidgetState();
                _states[widgetId] = state;
                Log($"Activate: yeni state olusturuldu (token yok)");
            }
            else
            {
                Log($"Activate: mevcut state bulundu, hasToken={state.HasToken}, cachedTasks={state.CachedTasks.Count}");
            }

            // Token yoksa dosyadan yukle
            if (!state.HasToken)
            {
                var fileToken = LoadTokenFromVault();
                if (fileToken != null)
                {
                    state.Token = fileToken;
                    Log($"Activate: token dosyadan yuklendi");
                }
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

        Log($"OnActionInvoked: verb={verb}, data={data}");

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

            case "showSettings":
                ShowSettings(widgetId, state);
                break;

            case "showAddTask":
                ShowAddTask(widgetId, state);
                break;

            case "addTask":
                HandleAddTask(widgetId, state, data);
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
        Log($"HandleSaveToken: raw data = [{data}]");
        try
        {
            using var doc = JsonDocument.Parse(data);
            Log($"HandleSaveToken: parsed JSON properties = [{string.Join(", ", doc.RootElement.EnumerateObject().Select(p => $"{p.Name}={p.Value}"))}]");
            if (doc.RootElement.TryGetProperty("apiToken", out var tokenElement))
            {
                var token = tokenElement.GetString()?.Trim();
                if (string.IsNullOrWhiteSpace(token))
                {
                    ShowError(widgetId, state, "Token bos olamaz.");
                    return;
                }

                lock (_lock)
                {
                    state.Token = token;
                    state.LastError = null;
                }

                SaveTokenToVault(token);
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
        lock (_lock)
        {
            state.Token = null;
            state.UserName = null;
            state.AvatarUrl = null;
            state.Email = null;
            state.CachedTasks.Clear();
            state.LastError = null;
        }
        DeleteTokenFromVault();
        ShowSetup(widgetId, state);
    }

    // --- Async Islemler ---

    private async Task ValidateAndFetchAsync(string widgetId, WidgetState state)
    {
        try
        {
            Log($"ValidateAndFetchAsync: token length={state.Token?.Length}, first3={state.Token?[..Math.Min(3, state.Token?.Length ?? 0)]}...");
            var client = new TodoistApiClient(state.Token!);
            var isValid = await client.ValidateTokenAsync();
            Log($"ValidateAndFetchAsync: isValid={isValid}");

            if (!isValid)
            {
                lock (_lock)
                {
                    state.Token = null;
                }
                DeleteTokenFromVault();
                ShowError(widgetId, state, "Token gecersiz. Todoist ayarlarindan kontrol edin.");
                return;
            }

            // Kullanici bilgisini cek
            if (string.IsNullOrEmpty(state.UserName))
            {
                var (user, userError) = await client.GetUserAsync();
                if (user != null)
                {
                    lock (_lock) { state.UserName = user.FullName; state.AvatarUrl = user.AvatarSmall; state.Email = user.Email; }
                    Log($"ValidateAndFetchAsync: userName={user.FullName}");
                }
                else
                {
                    Log($"ValidateAndFetchAsync: user alinamadi: {userError}");
                }
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
            var client = new TodoistApiClient(state.Token!);

            // Kullanici adi yoksa cek (recover sonrasi)
            if (string.IsNullOrEmpty(state.UserName))
            {
                var (user, userError) = await client.GetUserAsync();
                if (user != null)
                {
                    lock (_lock) { state.UserName = user.FullName; state.AvatarUrl = user.AvatarSmall; state.Email = user.Email; }
                    Log($"RefreshTasksAsync: userName={user.FullName}");
                }
                else
                {
                    Log($"RefreshTasksAsync: user alinamadi: {userError}");
                }
            }

            var (error, tasks) = await client.GetTodayTasksAsync();
            Log($"RefreshTasksAsync: error={error}, taskCount={tasks.Count}");

            if (error != null)
            {
                lock (_lock)
                {
                    state.LastError = error;
                }

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

            lock (_lock)
            {
                state.CachedTasks = tasks;
                state.LastFetchUtc = DateTime.UtcNow.ToString("o");
                state.LastError = null;
            }

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
            lock (_lock)
            {
                state.CachedTasks.RemoveAll(t => t.Id == taskId);
            }

            if (state.CachedTasks.Count == 0)
                ShowEmpty(widgetId, state);
            else
                UpdateWidgetWithTasks(widgetId, state, state.CachedTasks);

            // Sonra API'ye gonder
            var client = new TodoistApiClient(state.Token!);
            var error = await client.CompleteTaskAsync(taskId);

            if (error != null)
            {
                lock (_lock)
                {
                    state.LastError = error;
                }
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
        lock (_lock)
        {
            state.LastError = message;
        }
        PushUpdate(widgetId, TemplateRenderer.GetErrorTemplate(), TemplateRenderer.GetErrorData(message), state);
    }

    private void ShowEmpty(string widgetId, WidgetState state)
    {
        PushUpdate(widgetId, TemplateRenderer.GetEmptyTemplate(), TemplateRenderer.GetEmptyData(), state);
    }

    private void ShowAddTask(string widgetId, WidgetState state)
    {
        PushUpdate(widgetId, TemplateRenderer.GetAddTaskTemplate(), TemplateRenderer.GetAddTaskData(), state);
    }

    private void HandleAddTask(string widgetId, WidgetState state, string data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            if (doc.RootElement.TryGetProperty("taskContent", out var contentEl))
            {
                var content = contentEl.GetString()?.Trim();
                if (!string.IsNullOrEmpty(content) && state.HasToken)
                {
                    _ = AddTaskAndRefreshAsync(widgetId, state, content);
                    return;
                }
            }
        }
        catch { }
        // Bos veya hata - gorev listesine don
        HandleRefresh(widgetId, state);
    }

    private async Task AddTaskAndRefreshAsync(string widgetId, WidgetState state, string content)
    {
        try
        {
            var client = new TodoistApiClient(state.Token!);
            var error = await client.AddTaskAsync(content);
            Log($"AddTaskAndRefreshAsync: error={error}");
            await RefreshTasksAsync(widgetId, state);
        }
        catch
        {
            await RefreshTasksAsync(widgetId, state);
        }
    }

    private void ShowSettings(string widgetId, WidgetState state)
    {
        PushUpdate(widgetId, TemplateRenderer.GetSettingsTemplate(), TemplateRenderer.GetSettingsData(state), state);
    }

    private void UpdateWidgetWithTasks(string widgetId, WidgetState state, List<TodoistTask> tasks, string? offlineNotice = null)
    {
        PushUpdate(widgetId, TemplateRenderer.GetTaskListTemplate(), TemplateRenderer.GetTaskListData(tasks, state.UserName, state.AvatarUrl, offlineNotice), state);
    }

    private static void PushUpdate(string widgetId, string template, string data, WidgetState state)
    {
        try
        {
            Log($"PushUpdate: widgetId={widgetId}, templateLen={template?.Length}, dataLen={data?.Length}");
            Log($"PushUpdate DATA: {data?[..Math.Min(500, data?.Length ?? 0)]}");

            var updateOptions = new WidgetUpdateRequestOptions(widgetId)
            {
                Template = template,
                Data = data,
                CustomState = state.Serialize()
            };

            WidgetManager.GetDefault().UpdateWidget(updateOptions);
            Log("PushUpdate: basarili");
        }
        catch (Exception ex)
        {
            Log($"PushUpdate HATA: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
