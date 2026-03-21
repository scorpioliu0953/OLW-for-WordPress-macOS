using System.Text.Json.Serialization;

namespace OLWforWordPress.Models;

public class BlogSettings
{
    [JsonPropertyName("siteUrl")]
    public string SiteUrl { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("appPassword")]
    public string AppPassword { get; set; } = string.Empty;

    [JsonPropertyName("blogName")]
    public string BlogName { get; set; } = string.Empty;

    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = string.Empty;
}
