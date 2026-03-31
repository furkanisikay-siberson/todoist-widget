using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TodoistWidget.Models;

namespace TodoistWidget;

/// <summary>
/// Todoist REST v2 API istemcisi.
/// HttpClient singleton olarak paylasiliyor (socket exhaustion onlemi).
/// Token degistiginde yeni instance olusturulmali.
/// </summary>
public sealed class TodoistApiClient
{
    private static readonly SocketsHttpHandler SharedHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5)
    };

    private const string BaseUrl = "https://api.todoist.com/rest/v2/";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    public TodoistApiClient(string token)
    {
        // HttpClient handler paylasiliyor, dispose etmeye gerek yok
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
            var response = await _http.GetAsync("tasks?filter=today%20%7C%20overdue");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
                return ("Token gecersiz. Todoist ayarlarindan kontrol edin.", []);

            if (response.StatusCode == HttpStatusCode.Forbidden)
                return ("Erisim reddedildi. Token izinlerini kontrol edin.", []);

            if (response.StatusCode == (HttpStatusCode)429)
                return ("Cok fazla istek. Biraz bekleyin.", []);

            if (!response.IsSuccessStatusCode)
                return ($"API hatasi: {(int)response.StatusCode}", []);

            var tasks = await response.Content.ReadFromJsonAsync(
                WidgetStateContext.Default.TodoistTaskArray);

            if (tasks == null)
                return ("API bos yanit dondurdu.", []);

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
}
