using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OLWforWordPress.Models;

namespace OLWforWordPress.Services;

public class WordPressRestClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _apiBase;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    public WordPressRestClient(BlogSettings settings)
    {
        _apiBase = settings.ApiBaseUrl.TrimEnd('/');
        _http = new HttpClient();
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.Username}:{settings.AppPassword}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OLWforWordPress/1.0");
    }

    // ── Blog Info ──

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var resp = await _http.GetAsync($"{_apiBase}/users/me?context=edit");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    // ── Posts ──

    public async Task<BlogPost[]> GetPostsAsync(int page = 1, int perPage = 20, string status = "any")
    {
        var url = $"{_apiBase}/posts?context=edit&page={page}&per_page={perPage}&status={status}&orderby=date&order=desc";
        return await GetJsonAsync<BlogPost[]>(url) ?? [];
    }

    public async Task<BlogPost?> GetPostAsync(int id)
    {
        return await GetJsonAsync<BlogPost>($"{_apiBase}/posts/{id}?context=edit");
    }

    public async Task<BlogPost?> CreatePostAsync(string title, string content, string status = "draft",
        int[]? categories = null, int[]? tags = null, string? excerpt = null)
    {
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["content"] = content,
            ["status"] = status
        };
        if (categories != null) body["categories"] = categories;
        if (tags != null) body["tags"] = tags;
        if (excerpt != null) body["excerpt"] = excerpt;

        return await PostJsonAsync<BlogPost>($"{_apiBase}/posts", body);
    }

    public async Task<BlogPost?> UpdatePostAsync(int id, string title, string content, string status,
        int[]? categories = null, int[]? tags = null, string? excerpt = null)
    {
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["content"] = content,
            ["status"] = status
        };
        if (categories != null) body["categories"] = categories;
        if (tags != null) body["tags"] = tags;
        if (excerpt != null) body["excerpt"] = excerpt;

        return await PostJsonAsync<BlogPost>($"{_apiBase}/posts/{id}", body, "PUT");
    }

    public async Task<bool> DeletePostAsync(int id)
    {
        var resp = await _http.DeleteAsync($"{_apiBase}/posts/{id}");
        return resp.IsSuccessStatusCode;
    }

    // ── Pages ──

    public async Task<BlogPost[]> GetPagesAsync(int page = 1, int perPage = 20)
    {
        return await GetJsonAsync<BlogPost[]>($"{_apiBase}/pages?context=edit&page={page}&per_page={perPage}") ?? [];
    }

    public async Task<BlogPost?> CreatePageAsync(string title, string content, string status = "draft")
    {
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["content"] = content,
            ["status"] = status
        };
        return await PostJsonAsync<BlogPost>($"{_apiBase}/pages", body);
    }

    public async Task<BlogPost?> UpdatePageAsync(int id, string title, string content, string status)
    {
        var body = new Dictionary<string, object>
        {
            ["title"] = title,
            ["content"] = content,
            ["status"] = status
        };
        return await PostJsonAsync<BlogPost>($"{_apiBase}/pages/{id}", body, "PUT");
    }

    // ── Categories ──

    public async Task<BlogCategory[]> GetCategoriesAsync()
    {
        return await GetAllPagesAsync<BlogCategory>($"{_apiBase}/categories") ?? [];
    }

    public async Task<BlogCategory?> CreateCategoryAsync(string name, int parent = 0)
    {
        var body = new Dictionary<string, object> { ["name"] = name, ["parent"] = parent };
        return await PostJsonAsync<BlogCategory>($"{_apiBase}/categories", body);
    }

    // ── Tags ──

    public async Task<BlogTag[]> GetTagsAsync(string? search = null)
    {
        var url = $"{_apiBase}/tags?per_page=100";
        if (!string.IsNullOrEmpty(search)) url += $"&search={Uri.EscapeDataString(search)}";
        return await GetJsonAsync<BlogTag[]>(url) ?? [];
    }

    public async Task<BlogTag?> CreateTagAsync(string name)
    {
        var body = new Dictionary<string, object> { ["name"] = name };
        return await PostJsonAsync<BlogTag>($"{_apiBase}/tags", body);
    }

    public async Task<int[]> ResolveTagIdsAsync(string[] tagNames)
    {
        var ids = new List<int>();
        foreach (var name in tagNames)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var existing = await GetTagsAsync(name.Trim());
            var match = existing?.FirstOrDefault(t => t.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                ids.Add(match.Id);
            }
            else
            {
                var created = await CreateTagAsync(name.Trim());
                if (created != null) ids.Add(created.Id);
            }
        }
        return ids.ToArray();
    }

    // ── Media ──

    public async Task<MediaItem?> UploadMediaAsync(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var fileBytes = await File.ReadAllBytesAsync(filePath);
        return await UploadMediaAsync(fileBytes, fileName);
    }

    public async Task<MediaItem?> UploadMediaAsync(byte[] fileBytes, string fileName)
    {
        var mimeType = GetMimeType(fileName);

        using var content = new ByteArrayContent(fileBytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
        {
            FileName = fileName
        };

        var resp = await _http.PostAsync($"{_apiBase}/media", content);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<MediaItem>(JsonOpts);
    }

    // ── Detection ──

    public static async Task<string?> DetectApiBaseUrlAsync(string siteUrl)
    {
        siteUrl = siteUrl.TrimEnd('/');
        var candidates = new[]
        {
            $"{siteUrl}/wp-json/wp/v2",
            $"{siteUrl}/index.php?rest_route=/wp/v2"
        };

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        foreach (var url in candidates)
        {
            try
            {
                var resp = await http.GetAsync($"{url}/types");
                if (resp.IsSuccessStatusCode) return url;
            }
            catch { }
        }
        return null;
    }

    // ── Helpers ──

    private async Task<T?> GetJsonAsync<T>(string url) where T : class
    {
        var resp = await _http.GetAsync(url);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private async Task<T[]?> GetAllPagesAsync<T>(string baseUrl) where T : class
    {
        var all = new List<T>();
        int page = 1;
        while (true)
        {
            var sep = baseUrl.Contains('?') ? '&' : '?';
            var resp = await _http.GetAsync($"{baseUrl}{sep}per_page=100&page={page}");
            if (!resp.IsSuccessStatusCode) break;
            var items = await resp.Content.ReadFromJsonAsync<T[]>(JsonOpts);
            if (items == null || items.Length == 0) break;
            all.AddRange(items);
            if (!resp.Headers.TryGetValues("X-WP-TotalPages", out var vals)) break;
            if (!int.TryParse(vals.FirstOrDefault(), out var totalPages) || page >= totalPages) break;
            page++;
        }
        return all.ToArray();
    }

    private async Task<T?> PostJsonAsync<T>(string url, object body, string method = "POST") where T : class
    {
        var json = JsonSerializer.Serialize(body, JsonOpts);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage resp;
        if (method == "PUT")
        {
            resp = await _http.PutAsync(url, content);
        }
        else
        {
            resp = await _http.PostAsync(url, content);
        }

        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<T>(JsonOpts);
    }

    private static string GetMimeType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".pdf" => "application/pdf",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            _ => "application/octet-stream"
        };
    }

    public void Dispose() => _http.Dispose();
}
