using System.Text.Json;
using OLWforWordPress.Models;

namespace OLWforWordPress.Services;

public class BlogSettingsService
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OLWforWordPress");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public BlogSettings? Load()
    {
        if (!File.Exists(SettingsPath)) return null;
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<BlogSettings>(json);
        }
        catch
        {
            return null;
        }
    }

    public void Save(BlogSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(SettingsPath, json);
    }

    public void Delete()
    {
        if (File.Exists(SettingsPath))
            File.Delete(SettingsPath);
    }
}
