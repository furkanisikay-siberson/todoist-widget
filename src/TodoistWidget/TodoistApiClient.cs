using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TodoistWidget.Models;

namespace TodoistWidget;

/// <summary>
/// Todoist API v1 istemcisi.
/// HttpClient singleton handler paylasiliyor (socket exhaustion onlemi).
/// </summary>
public sealed class TodoistApiClient
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    private const string BaseUrl = "https://api.todoist.com/api/v1/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public TodoistApiClient(string token)
    {
        _http = new HttpClient(SharedHandler, disposeHandler: false)
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = RequestTimeout
        };
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<(string? error, List<TodoistTask> tasks)> GetTodayTasksAsync()
    {
        try
        {
            // v1 API: /tasks degil, /tasks/filter?query= kullanilmali
            var response = await _http.GetAsync("tasks/filter?query=today%20%7C%20overdue");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return ("Token gecersiz. Todoist ayarlarindan kontrol edin.", []);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ("Erisim reddedildi. Token izinlerini kontrol edin.", []);

            if (response.StatusCode == (HttpStatusCode)429)
                return ("Cok fazla istek. Biraz bekleyin.", []);

            if (!response.IsSuccessStatusCode)
                return ($"API hatasi: {(int)response.StatusCode}", []);

            // API v1: {"results": [...]}
            var apiResponse = await response.Content.ReadFromJsonAsync(
                WidgetStateContext.Default.TodoistApiResponse);

            if (apiResponse?.Results == null || apiResponse.Results.Length == 0)
                return (null, []);

            var sorted = apiResponse.Results
                .Where(t => !t.Checked)
                .OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Due?.Date ?? "9999-12-31")
                .ThenBy(t => t.ChildOrder)
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

    public async Task<string?> CompleteTaskAsync(string taskId)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, $"tasks/{taskId}/close");
            request.Headers.Add("X-Request-Id", requestId);

            var response = await _http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return "Token gecersiz.";
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
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

    /// <summary>Yeni gorev olusturur.</summary>
    public async Task<string?> AddTaskAsync(string content)
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var request = new HttpRequestMessage(HttpMethod.Post, "tasks");
            request.Headers.Add("X-Request-Id", requestId);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { content }),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return $"Gorev eklenemedi: {(int)response.StatusCode}";

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

    public async Task<bool> ValidateTokenAsync()
    {
        try
        {
            var response = await _http.GetAsync("projects");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Todoist REST API v1 ile kullanici bilgisini ceker.</summary>
    public async Task<(TodoistUser? user, string? error)> GetUserAsync()
    {
        try
        {
            var response = await _http.GetAsync("user");

            if (!response.IsSuccessStatusCode)
                return (null, $"User API {(int)response.StatusCode}");

            var user = await response.Content.ReadFromJsonAsync(
                WidgetStateContext.Default.TodoistUser);

            return (user, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
