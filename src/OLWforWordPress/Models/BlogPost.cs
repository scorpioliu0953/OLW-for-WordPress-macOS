using System.Text.Json.Serialization;

namespace OLWforWordPress.Models;

public class BlogPost
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public BlogPostField Title { get; set; } = new();

    [JsonPropertyName("content")]
    public BlogPostField Content { get; set; } = new();

    [JsonPropertyName("excerpt")]
    public BlogPostField Excerpt { get; set; } = new();

    [JsonPropertyName("status")]
    public string Status { get; set; } = "draft";

    [JsonPropertyName("slug")]
    public string Slug { get; set; } = string.Empty;

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("categories")]
    public int[] Categories { get; set; } = [];

    [JsonPropertyName("tags")]
    public int[] Tags { get; set; } = [];

    [JsonPropertyName("featured_media")]
    public int FeaturedMedia { get; set; }

    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
}

public class BlogPostField
{
    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("rendered")]
    public string Rendered { get; set; } = string.Empty;

    [JsonIgnore]
    public string Value
    {
        get => Raw ?? Rendered;
        set => Raw = value;
    }
}

public class BlogCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("parent")]
    public int Parent { get; set; }
}

public class BlogTag
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class MediaItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("source_url")]
    public string SourceUrl { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public BlogPostField Title { get; set; } = new();
}
