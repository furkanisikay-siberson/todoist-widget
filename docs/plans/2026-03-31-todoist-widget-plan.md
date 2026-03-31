# Todoist Widget for Windows 11 - Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Windows 11 Widget Board'da Todoist gorevlerini gosteren ve tamamlama destekli bir widget.

**Architecture:** Headless C# COM server (no window). IWidgetProvider arayuzu ile Widget Board'a Adaptive Cards JSON gonderir. Todoist REST v2 API'ye HttpClient ile baglanir. Token ve cache CustomState'te persist edilir.

**Tech Stack:** .NET 8, Windows App SDK 1.6+, Adaptive Cards 1.6, System.Text.Json, MSIX packaging

---

## Task 1: Proje Iskeleti - .csproj ve Program.cs

**Files:**
- Create: `src/TodoistWidget/TodoistWidget.csproj`
- Create: `src/TodoistWidget/Program.cs`

**Step 1: .csproj olustur**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.22621.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22000.0</TargetPlatformMinVersion>
    <RootNamespace>TodoistWidget</RootNamespace>
    <Platforms>x64;ARM64</Platforms>
    <RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>
    <UseWinUI>false</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsPackageType>MSIX</WindowsPackageType>
    <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.*" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.*" />
  </ItemGroup>

  <ItemGroup>
    <Content Include="Templates\**\*.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>

  <ItemGroup Condition="'$(DisableMsixProjectCapabilityAddedByProject)'!='true' and '$(EnableMsixTooling)'=='true'">
    <ProjectCapability Include="Msix" />
  </ItemGroup>

  <PropertyGroup Condition="'$(DisableHasPackageAndPublishMenuAddedByProject)'!='true' and '$(EnableMsixTooling)'=='true'">
    <HasPackageAndPublishMenu>true</HasPackageAndPublishMenu>
  </PropertyGroup>
</Project>
```

**Step 2: Program.cs olustur**

```csharp
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using TodoistWidget;
using WinRT;

namespace TodoistWidget;

public static class Program
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    private const uint CLSCTX_LOCAL_SERVER = 0x4;
    private const uint REGCLS_MULTIPLEUSE = 0x1;

    [MTAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "-RegisterProcessAsComServer")
        {
            ComWrappersSupport.InitializeComWrappers();

            uint cookie;
            var clsid = Guid.Parse(TodoistWidgetProvider.ClsId);
            int hr = CoRegisterClassObject(
                clsid,
                new WidgetProviderFactory<TodoistWidgetProvider>(),
                CLSCTX_LOCAL_SERVER,
                REGCLS_MULTIPLEUSE,
                out cookie);

            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            var exitEvent = new ManualResetEvent(false);
            exitEvent.WaitOne();

            CoRevokeClassObject(cookie);
        }
    }
}
```

**Step 3: WidgetProviderFactory.cs olustur**

```csharp
using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace TodoistWidget;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00000001-0000-0000-C000-000000000046")]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
    [PreserveSig]
    int LockServer(bool fLock);
}

internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    private const int CLASS_E_NOAGGREGATION = -2147221232;
    private const int E_NOINTERFACE = -2147467262;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            Marshal.ThrowExceptionForHR(CLASS_E_NOAGGREGATION);
        }

        if (riid == typeof(IWidgetProvider).GUID || riid == Guid.Parse("00000000-0000-0000-C000-000000000046"))
        {
            ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
        }
        else
        {
            Marshal.ThrowExceptionForHR(E_NOINTERFACE);
        }

        return 0;
    }

    public int LockServer(bool fLock) => 0;
}
```

**Step 4: NuGet restore ve build**

Run: `dotnet restore src/TodoistWidget/TodoistWidget.csproj`
Expected: Basarili restore

**Step 5: Commit**

```bash
git add src/TodoistWidget/TodoistWidget.csproj src/TodoistWidget/Program.cs src/TodoistWidget/WidgetProviderFactory.cs
git commit -m "feat(widget): proje iskeleti ve COM server altyapisi"
```

---

## Task 2: Models - WidgetState ve TodoistTask

**Files:**
- Create: `src/TodoistWidget/Models/TodoistTask.cs`
- Create: `src/TodoistWidget/Models/WidgetState.cs`

**Step 1: TodoistTask.cs**

```csharp
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
```

**Step 2: WidgetState.cs**

```csharp
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
internal sealed partial class WidgetStateContext : JsonSerializerContext
{
}
```

**Step 3: Commit**

```bash
git add src/TodoistWidget/Models/
git commit -m "feat(widget): TodoistTask ve WidgetState modelleri"
```

---

## Task 3: TodoistApiClient

**Files:**
- Create: `src/TodoistWidget/TodoistApiClient.cs`

**Step 1: TodoistApiClient.cs**

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TodoistWidget.Models;

namespace TodoistWidget;

public sealed class TodoistApiClient : IDisposable
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private const string BaseUrl = "https://api.todoist.com/rest/v2";

    private readonly HttpClient _http;
    private bool _disposed;

    public TodoistApiClient(string token)
    {
        _http = new HttpClient
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = RequestTimeout
        };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Bugun ve geciken gorevleri getirir.
    /// Basarisiz olursa (null, tasks) tuple doner; hata varsa (error, []) doner.
    /// </summary>
    public async Task<(string? error, List<TodoistTask> tasks)> GetTodayTasksAsync()
    {
        try
        {
            // "today | overdue" filtresi bugunun gorevlerini ve gecikenleri getirir
            var response = await _http.GetAsync("/tasks?filter=today%20%7C%20overdue");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return ("Token gecersiz. Todoist ayarlarindan kontrol edin.", []);
            }

            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return ("Erisim reddedildi. Token izinlerini kontrol edin.", []);
            }

            if (response.StatusCode == (HttpStatusCode)429)
            {
                return ("Cok fazla istek. Biraz bekleyin.", []);
            }

            if (!response.IsSuccessStatusCode)
            {
                return ($"API hatasi: {(int)response.StatusCode}", []);
            }

            var tasks = await response.Content.ReadFromJsonAsync(
                WidgetStateContext.Default.TodoistTaskArray);

            if (tasks == null)
            {
                return ("API bos yanit dondurdu.", []);
            }

            // Oncelik siralama: P1 (priority=4) en uste, sonra due date, sonra order
            var sorted = tasks
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Due?.Date ?? "9999-12-31")
                .ThenBy(t => t.Order)
                .ToList();

            return (null, sorted);
        }
        catch (TaskCanceledException)
        {
            return ("Baglanti zaman asimina ugradi.", []);
        }
        catch (HttpRequestException ex)
        {
            return ($"Baglanti hatasi: {ex.Message}", []);
        }
        catch (JsonException)
        {
            return ("API yanitini okurken hata olustu.", []);
        }
    }

    /// <summary>
    /// Gorevi tamamlar. Basarili: (null), basarisiz: (error mesaji).
    /// </summary>
    public async Task<string?> CompleteTaskAsync(string taskId)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, $"/tasks/{taskId}/close");
            request.Headers.Add("X-Request-Id", requestId);

            var response = await _http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return "Token gecersiz.";
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null; // Zaten silinmis/tamamlanmis - sorun yok
            if (!response.IsSuccessStatusCode)
                return $"Gorev tamamlanamadi: {(int)response.StatusCode}";

            return null;
        }
        catch (TaskCanceledException)
        {
            return "Baglanti zaman asimina ugradi.";
        }
        catch (HttpRequestException ex)
        {
            return $"Baglanti hatasi: {ex.Message}";
        }
    }

    /// <summary>
    /// Token gecerli mi kontrol eder. True: gecerli, False: gecersiz.
    /// </summary>
    public async Task<bool> ValidateTokenAsync()
    {
        try
        {
            var response = await _http.GetAsync("/projects");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _http.Dispose();
            _disposed = true;
        }
    }
}
```

**Step 2: Commit**

```bash
git add src/TodoistWidget/TodoistApiClient.cs
git commit -m "feat(widget): Todoist REST v2 API istemcisi"
```

---

## Task 4: Adaptive Card Sablonlari

**Files:**
- Create: `src/TodoistWidget/Templates/SetupCard.json`
- Create: `src/TodoistWidget/Templates/TaskListCard.json`
- Create: `src/TodoistWidget/Templates/ErrorCard.json`
- Create: `src/TodoistWidget/Templates/EmptyCard.json`

**Step 1: SetupCard.json** (Token giris formu)

```json
{
    "type": "AdaptiveCard",
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "text": "Todoist Widget",
            "weight": "Bolder",
            "size": "Medium"
        },
        {
            "type": "TextBlock",
            "text": "API token'inizi girin. Todoist > Ayarlar > Entegrasyonlar > Gelistirici sayfasindan alabilirsiniz.",
            "wrap": true,
            "size": "Small",
            "isSubtle": true
        },
        {
            "type": "Input.Text",
            "id": "apiToken",
            "placeholder": "API Token",
            "style": "password"
        }
    ],
    "actions": [
        {
            "type": "Action.Execute",
            "title": "Kaydet",
            "verb": "saveToken",
            "style": "positive"
        }
    ]
}
```

**Step 2: TaskListCard.json** (Tek sablon, boyuta gore uyarlanir)

```json
{
    "type": "AdaptiveCard",
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "version": "1.6",
    "body": [
        {
            "type": "Container",
            "$when": "${overdueCount > 0}",
            "items": [
                {
                    "type": "TextBlock",
                    "text": "Geciken (${overdueCount})",
                    "weight": "Bolder",
                    "size": "Small",
                    "color": "Attention",
                    "spacing": "None"
                },
                {
                    "type": "ColumnSet",
                    "$data": "${overdueTasks}",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "auto",
                            "verticalContentAlignment": "Center",
                            "items": [
                                {
                                    "type": "TextBlock",
                                    "text": "${priorityIcon}",
                                    "size": "Small",
                                    "spacing": "None"
                                }
                            ]
                        },
                        {
                            "type": "Column",
                            "width": "stretch",
                            "items": [
                                {
                                    "type": "TextBlock",
                                    "text": "${content}",
                                    "wrap": false,
                                    "size": "Small",
                                    "spacing": "None"
                                },
                                {
                                    "type": "TextBlock",
                                    "text": "${projectName}",
                                    "isSubtle": true,
                                    "size": "Small",
                                    "spacing": "None",
                                    "$when": "${$host.widgetSize==\"large\" && projectName != ''}"
                                }
                            ]
                        },
                        {
                            "type": "Column",
                            "width": "auto",
                            "verticalContentAlignment": "Center",
                            "items": [
                                {
                                    "type": "ActionSet",
                                    "actions": [
                                        {
                                            "type": "Action.Execute",
                                            "title": "✓",
                                            "verb": "completeTask",
                                            "data": { "taskId": "${id}" }
                                        }
                                    ]
                                }
                            ]
                        }
                    ],
                    "spacing": "Small"
                }
            ]
        },
        {
            "type": "Container",
            "$when": "${todayCount > 0}",
            "items": [
                {
                    "type": "TextBlock",
                    "text": "Bugun (${todayCount})",
                    "weight": "Bolder",
                    "size": "Small",
                    "spacing": "Small"
                },
                {
                    "type": "ColumnSet",
                    "$data": "${todayTasks}",
                    "columns": [
                        {
                            "type": "Column",
                            "width": "auto",
                            "verticalContentAlignment": "Center",
                            "items": [
                                {
                                    "type": "TextBlock",
                                    "text": "${priorityIcon}",
                                    "size": "Small",
                                    "spacing": "None"
                                }
                            ]
                        },
                        {
                            "type": "Column",
                            "width": "stretch",
                            "items": [
                                {
                                    "type": "TextBlock",
                                    "text": "${content}",
                                    "wrap": false,
                                    "size": "Small",
                                    "spacing": "None"
                                },
                                {
                                    "type": "TextBlock",
                                    "text": "${projectName}",
                                    "isSubtle": true,
                                    "size": "Small",
                                    "spacing": "None",
                                    "$when": "${$host.widgetSize==\"large\" && projectName != ''}"
                                }
                            ]
                        },
                        {
                            "type": "Column",
                            "width": "auto",
                            "verticalContentAlignment": "Center",
                            "items": [
                                {
                                    "type": "ActionSet",
                                    "actions": [
                                        {
                                            "type": "Action.Execute",
                                            "title": "✓",
                                            "verb": "completeTask",
                                            "data": { "taskId": "${id}" }
                                        }
                                    ]
                                }
                            ]
                        }
                    ],
                    "spacing": "Small"
                }
            ]
        },
        {
            "type": "TextBlock",
            "$when": "${offlineNotice != ''}",
            "text": "${offlineNotice}",
            "size": "Small",
            "isSubtle": true,
            "color": "Warning",
            "spacing": "Small"
        }
    ],
    "actions": [
        {
            "type": "Action.Execute",
            "title": "Yenile",
            "verb": "refresh",
            "$when": "${$host.widgetSize!=\"small\"}"
        }
    ]
}
```

**Step 3: ErrorCard.json**

```json
{
    "type": "AdaptiveCard",
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "text": "Todoist Widget",
            "weight": "Bolder",
            "size": "Medium"
        },
        {
            "type": "TextBlock",
            "text": "${errorMessage}",
            "wrap": true,
            "color": "Attention",
            "size": "Small"
        }
    ],
    "actions": [
        {
            "type": "Action.Execute",
            "title": "Tekrar Dene",
            "verb": "refresh"
        },
        {
            "type": "Action.Execute",
            "title": "Token Degistir",
            "verb": "resetToken"
        }
    ]
}
```

**Step 4: EmptyCard.json**

```json
{
    "type": "AdaptiveCard",
    "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
    "version": "1.6",
    "body": [
        {
            "type": "TextBlock",
            "text": "Todoist",
            "weight": "Bolder",
            "size": "Medium"
        },
        {
            "type": "TextBlock",
            "text": "Bugun gorev yok! 🎉",
            "wrap": true,
            "horizontalAlignment": "Center",
            "size": "Medium"
        }
    ],
    "actions": [
        {
            "type": "Action.Execute",
            "title": "Yenile",
            "verb": "refresh"
        }
    ]
}
```

**Step 5: Commit**

```bash
git add src/TodoistWidget/Templates/
git commit -m "feat(widget): Adaptive Card sablonlari"
```

---

## Task 5: TemplateRenderer - Sablon Yukleyici

**Files:**
- Create: `src/TodoistWidget/TemplateRenderer.cs`

**Step 1: TemplateRenderer.cs**

Bu sinif JSON sablonlarini diskten yukler, cache'ler ve data binding icin veri JSON'i uretir.

```csharp
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

    public static string GetTaskListData(List<TodoistTask> tasks, string? offlineNotice = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var overdue = new List<object>();
        var todayList = new List<object>();

        foreach (var task in tasks)
        {
            var isOverdue = task.Due?.IsOverdue == true;
            var item = new
            {
                id = task.Id,
                content = TruncateContent(task.Content, 40),
                priorityIcon = GetPriorityIcon(task.Priority),
                projectName = "" // Proje adi icin ayri API gerekir, simdilik bos
            };

            if (isOverdue)
                overdue.Add(item);
            else
                todayList.Add(item);
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
        4 => "🔴",  // P1 - Urgent
        3 => "🟠",  // P2 - High
        2 => "🔵",  // P3 - Medium
        _ => "⚪"   // P4 - Normal
    };

    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength) return content;
        return content[..(maxLength - 1)] + "…";
    }
}
```

**Step 2: Commit**

```bash
git add src/TodoistWidget/TemplateRenderer.cs
git commit -m "feat(widget): sablon yukleyici ve veri baglama katmani"
```

---

## Task 6: TodoistWidgetProvider - Ana IWidgetProvider Implementasyonu

**Files:**
- Create: `src/TodoistWidget/TodoistWidgetProvider.cs`

Bu en kritik dosya. Tum widget yasam dongusu burada yonetilir.

**Step 1: TodoistWidgetProvider.cs**

```csharp
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

    // Her widget instance icin state
    // Key: widgetId, Value: state
    private static readonly Dictionary<string, WidgetState> _states = new();
    private static readonly object _lock = new();

    public TodoistWidgetProvider()
    {
        // Crash/restart sonrasi mevcut widgetlari kurtar
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

        // Token yoksa setup kartin goster
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
            // Widget gorunur oldu, gorevleri yenile
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
        // Boyut degisikligi - UI'i yeniden ciz (sablon zaten $when ile adapte olur)
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
        // data: Action.Execute'dan gelen JSON (Input.Text degerleri dahil)
        // {"apiToken": "kullanicinin_girdigi_token"}
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

                // Token gecerliligini kontrol et ve gorevleri cek
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

    // --- Async Islemler (fire-and-forget, IWidgetProvider senkron) ---

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

                // Cache varsa onu goster + cevrimdisi uyarisi
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

            // Basarili - cache'i guncelle
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
            // Son care: cache varsa goster
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
            // Once UI'dan gorevi kaldir (aninda geri bildirim)
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
                // API basarisiz oldu ama UI zaten guncellendi
                // Bir sonraki refresh dogrusunu gosterecek
                state.LastError = error;
            }

            // Tam listeyi yenile (tekrarlayan gorevler guncellenebilir)
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
            // Bir sonraki Activate'te tekrar denenecek
        }
    }
}
```

**Step 2: Commit**

```bash
git add src/TodoistWidget/TodoistWidgetProvider.cs
git commit -m "feat(widget): IWidgetProvider implementasyonu - tum yasam dongusu"
```

---

## Task 7: Package.appxmanifest ve Assets

**Files:**
- Create: `src/TodoistWidget/Package.appxmanifest`
- Create: `src/TodoistWidget/Assets/` (icon dosyalari)
- Create: `src/TodoistWidget/Properties/launchSettings.json`

**Step 1: Package.appxmanifest**

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10"
  IgnorableNamespaces="uap uap3 rescap">

  <Identity
    Name="TodoistWidget"
    Publisher="CN=TodoistWidgetDev"
    Version="1.0.0.0" />

  <Properties>
    <DisplayName>Todoist Widget</DisplayName>
    <PublisherDisplayName>TodoistWidgetDev</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22000.0" MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="x-generate" />
  </Resources>

  <Applications>
    <Application Id="App"
      Executable="$targetnametoken$.exe"
      EntryPoint="$targetentrypoint$">

      <uap:VisualElements
        DisplayName="Todoist Widget"
        Description="Windows 11 widget for Todoist tasks"
        BackgroundColor="transparent"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
        <uap:SplashScreen Image="Assets\SplashScreen.png" />
      </uap:VisualElements>

      <Extensions>
        <!-- COM Server -->
        <com:Extension Category="windows.comServer">
          <com:ComServer>
            <com:ExeServer
              Executable="TodoistWidget.exe"
              Arguments="-RegisterProcessAsComServer"
              DisplayName="Todoist Widget Provider">
              <com:Class Id="E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70"
                DisplayName="TodoistWidgetProvider" />
            </com:ExeServer>
          </com:ComServer>
        </com:Extension>

        <!-- Widget Declaration -->
        <uap3:Extension Category="windows.appExtension">
          <uap3:AppExtension
            Name="com.microsoft.windows.widgets"
            DisplayName="Todoist Widget"
            Id="TodoistWidgetApp"
            PublicFolder="Public">
            <uap3:Properties>
              <WidgetProvider>
                <ProviderIcons>
                  <Icon Path="Assets\StoreLogo.png" />
                </ProviderIcons>
                <Activation>
                  <CreateInstance ClassId="E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70" />
                </Activation>
                <Definitions>
                  <Definition
                    Id="Todoist_Tasks_Widget"
                    DisplayName="Todoist Gorevlerim"
                    Description="Bugunun gorevleri ve geciken gorevler">
                    <Capabilities>
                      <Capability>
                        <Size Name="medium" />
                      </Capability>
                      <Capability>
                        <Size Name="large" />
                      </Capability>
                    </Capabilities>
                    <ThemeResources>
                      <Icons>
                        <Icon Path="Assets\WidgetIcon.png" />
                      </Icons>
                      <Screenshots>
                        <Screenshot
                          Path="Assets\WidgetScreenshot.png"
                          DisplayAltText="Todoist gorev listesi widget onizlemesi" />
                      </Screenshots>
                      <DarkMode />
                      <LightMode />
                    </ThemeResources>
                  </Definition>
                </Definitions>
              </WidgetProvider>
            </uap3:Properties>
          </uap3:AppExtension>
        </uap3:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <Capability Name="internetClient" />
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

**Step 2: Placeholder asset dosyalari olustur**

Asagidaki dosyalar icin 1x1 transparan PNG olustur (gercek iconlar sonra eklenir):
- `Assets/StoreLogo.png` (50x50)
- `Assets/Square150x150Logo.png` (150x150)
- `Assets/Square44x44Logo.png` (44x44)
- `Assets/Wide310x150Logo.png` (310x150)
- `Assets/SplashScreen.png` (620x300)
- `Assets/WidgetIcon.png` (64x64)
- `Assets/WidgetScreenshot.png` (400x400)

Olusturma komutu (PowerShell ile minimal PNG):

```powershell
# Basit bir script ile placeholder PNG olustur
$pngBytes = [byte[]]@(0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A,0x00,0x00,0x00,0x0D,0x49,0x48,0x44,0x52,0x00,0x00,0x00,0x01,0x00,0x00,0x00,0x01,0x08,0x06,0x00,0x00,0x00,0x1F,0x15,0xC4,0x89,0x00,0x00,0x00,0x0A,0x49,0x44,0x41,0x54,0x78,0x9C,0x62,0x00,0x00,0x00,0x02,0x00,0x01,0xE5,0x27,0xDE,0xFC,0x00,0x00,0x00,0x00,0x49,0x45,0x4E,0x44,0xAE,0x42,0x60,0x82)
$assets = @("StoreLogo.png","Square150x150Logo.png","Square44x44Logo.png","Wide310x150Logo.png","SplashScreen.png","WidgetIcon.png","WidgetScreenshot.png")
foreach ($a in $assets) { [IO.File]::WriteAllBytes("src\TodoistWidget\Assets\$a", $pngBytes) }
```

**Step 3: Properties/launchSettings.json**

```json
{
  "profiles": {
    "TodoistWidget": {
      "commandName": "Project",
      "commandLineArgs": "-RegisterProcessAsComServer"
    }
  }
}
```

**Step 4: Public klasoru olustur** (widget extension icin gerekli)

```bash
mkdir -p src/TodoistWidget/Public
```

**Step 5: .csproj'a asset referanslari ekle**

`TodoistWidget.csproj` dosyasina eklenecek ItemGroup:

```xml
<ItemGroup>
  <Content Include="Assets\**">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
  <Content Include="Properties\launchSettings.json" />
  <None Include="Package.appxmanifest" />
</ItemGroup>
```

**Step 6: Commit**

```bash
git add src/TodoistWidget/Package.appxmanifest src/TodoistWidget/Assets/ src/TodoistWidget/Properties/ src/TodoistWidget/Public/
git commit -m "feat(widget): MSIX manifest, asset'ler ve launch ayarlari"
```

---

## Task 8: Build, Deploy ve Test

**Step 1: Solution dosyasi olustur**

```bash
cd src
dotnet new sln -n TodoistWidget
dotnet sln TodoistWidget.sln add TodoistWidget/TodoistWidget.csproj
```

**Step 2: Build**

```bash
dotnet build src/TodoistWidget.sln -c Debug -r win-x64
```

Expected: Basarili build, hata yok.

**Step 3: Developer Mode aktif mi kontrol et**

Windows Ayarlar > Guncellestirme ve Guvenlik > Gelistirici icin > Gelistirici Modu ACIK olmali.

**Step 4: MSIX deploy (Visual Studio veya dotnet)**

Visual Studio'dan F5 ile deploy edilir. Komut satirindan:

```bash
dotnet publish src/TodoistWidget/TodoistWidget.csproj -c Debug -r win-x64
```

Sonra cikan MSIX paketini sideload et.

**Step 5: Widget Board'dan test**

1. Win+W ile Widget Board'u ac
2. "+" butonuna tikla
3. "Todoist Gorevlerim" widget'ini bul
4. Pin et
5. Token giris ekrani gelmeli
6. Todoist API token'ini gir
7. Gorev listesi gorunmeli
8. Bir gorevi tamamla (✓ butonu)
9. Liste yenilenmeli

**Step 6: Commit**

```bash
git add src/TodoistWidget.sln
git commit -m "feat(widget): solution dosyasi ve build yapilandirmasi"
```

---

## Task 9: Son Kontrol ve Edge Case Test

**Test senaryolari:**

| Senaryo | Beklenen |
|---------|----------|
| Bos token giris | "Token bos olamaz" hatasi |
| Yanlis token | "Token gecersiz" hatasi + yeniden giris |
| Gecerli token, gorev var | Gorev listesi gosterilir |
| Gecerli token, gorev yok | "Bugun gorev yok!" mesaji |
| Internet kapatma (cache var) | Cache gosterilir + "Baglanti hatasi" uyarisi |
| Internet kapatma (cache yok) | "Gorevler alinamadi" hatasi |
| Widget unpin + repin | Kaydedilmis token korunur |
| Gorev tamamlama | Gorev listeden kalkar, liste yenilenir |
| Widget boyut degistirme (M->L) | Large'da proje adi gosterilir |
| Uygulama restart | RecoverWidgets ile state korunur |

**Step 1: Her senaryoyu test et**

**Step 2: Bulunan sorunlari duzelt**

**Step 3: Final commit**

```bash
git add -A
git commit -m "fix(widget): edge case duzeltmeleri ve son iyilestirmeler"
```

---

## Kritik Notlar

### GUID Eslesmesi (En Sik Hata Kaynagi)
Asagidaki 3 yerdeki GUID **birebir ayni** olmali:
1. `TodoistWidgetProvider.cs` -> `[Guid("E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70")]`
2. `Package.appxmanifest` -> `<com:Class Id="E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70">`
3. `Package.appxmanifest` -> `<CreateInstance ClassId="E7A4B53C-1F8D-4D2A-9C6E-3B5A8D2F1E70">`

### Definition ID Eslesmesi
`Package.appxmanifest`'teki `<Definition Id="Todoist_Tasks_Widget">` ile `TodoistWidgetProvider.cs`'teki `DefinitionId = "Todoist_Tasks_Widget"` **birebir ayni** olmali.

### Exe Ismi Eslesmesi
`Package.appxmanifest`'teki `<com:ExeServer Executable="TodoistWidget.exe">` ile csproj'dan uretilen exe ismi **birebir ayni** olmali.

### Async Dikkat
IWidgetProvider metodlari senkron. Async islemleri `_ = SomeAsync()` (fire-and-forget) ile baslatiyoruz. Bu, widget board'un donmasini engeller. Hata durumunda catch bloklari UI'i korur.

### CustomState Limiti
CustomState string olarak saklanir. Cok buyuk veri saklamaktan kacin. Gorev cache'i makul boyutta tutulmali (max ~50 gorev).
