using System.ComponentModel;
using System.Runtime.CompilerServices;
using OLWforWordPress.Models;
using OLWforWordPress.Services;

namespace OLWforWordPress.ViewModels;

public class SetupWizardViewModel : INotifyPropertyChanged
{
    private readonly BlogSettingsService _settingsService;

    private string _siteUrl = string.Empty;
    private string _username = string.Empty;
    private string _appPassword = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isConnecting;

    public event EventHandler<BlogSettings>? SetupCompleted;
    public event PropertyChangedEventHandler? PropertyChanged;

    public SetupWizardViewModel(BlogSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public string SiteUrl
    {
        get => _siteUrl;
        set { _siteUrl = value; OnPropertyChanged(); }
    }

    public string Username
    {
        get => _username;
        set { _username = value; OnPropertyChanged(); }
    }

    public string AppPassword
    {
        get => _appPassword;
        set { _appPassword = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set { _isConnecting = value; OnPropertyChanged(); }
    }

    public async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(SiteUrl) || string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(AppPassword))
        {
            StatusMessage = "Please fill in all fields.";
            return;
        }

        IsConnecting = true;
        StatusMessage = "Detecting WordPress REST API...";

        try
        {
            var url = SiteUrl.Trim();
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                url = "https://" + url;

            var apiBase = await WordPressRestClient.DetectApiBaseUrlAsync(url);
            if (apiBase == null)
            {
                StatusMessage = "Could not detect WordPress REST API. Please check the URL.";
                return;
            }

            StatusMessage = "Verifying credentials...";

            var settings = new BlogSettings
            {
                SiteUrl = url,
                Username = Username.Trim(),
                AppPassword = AppPassword.Trim(),
                ApiBaseUrl = apiBase
            };

            using var client = new WordPressRestClient(settings);
            var ok = await client.TestConnectionAsync();
            if (!ok)
            {
                StatusMessage = "Authentication failed. Please check username and application password.";
                return;
            }

            StatusMessage = "Connected!";
            settings.BlogName = new Uri(url).Host;
            _settingsService.Save(settings);
            SetupCompleted?.Invoke(this, settings);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
