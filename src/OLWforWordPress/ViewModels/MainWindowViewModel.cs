using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OLWforWordPress.Models;
using OLWforWordPress.Services;

namespace OLWforWordPress.ViewModels;

public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly WordPressRestClient _client;
    private readonly BlogSettingsService _settingsService;
    private readonly BlogSettings _settings;

    private string _title = string.Empty;
    private string _htmlContent = string.Empty;
    private string _excerpt = string.Empty;
    private string _tagsText = string.Empty;
    private string _statusMessage = "就緒";
    private string _selectedStatus = "draft";
    private bool _isBusy;
    private int _currentPostId;
    private BlogPost? _currentPost;

    // Pending local images: key = unique id, value = (fileName, bytes)
    private readonly Dictionary<string, (string FileName, byte[] Bytes)> _pendingImages = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindowViewModel(BlogSettings settings, BlogSettingsService settingsService)
    {
        _settings = settings;
        _settingsService = settingsService;
        _client = new WordPressRestClient(settings);

        Categories = new ObservableCollection<CategoryItem>();
        RecentPosts = new ObservableCollection<BlogPostListItem>();
    }

    // ── Properties ──

    public string BlogName => _settings.BlogName;

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public string HtmlContent
    {
        get => _htmlContent;
        set { _htmlContent = value; OnPropertyChanged(); }
    }

    public string Excerpt
    {
        get => _excerpt;
        set { _excerpt = value; OnPropertyChanged(); }
    }

    public string TagsText
    {
        get => _tagsText;
        set { _tagsText = value; OnPropertyChanged(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; OnPropertyChanged(); }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set { _selectedStatus = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public int CurrentPostId
    {
        get => _currentPostId;
        set { _currentPostId = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEditing)); }
    }

    public bool IsEditing => CurrentPostId > 0;

    public ObservableCollection<CategoryItem> Categories { get; }
    public ObservableCollection<BlogPostListItem> RecentPosts { get; }

    public string[] StatusOptions => ["draft", "publish", "pending", "private"];

    public int PendingImageCount => _pendingImages.Count;

    // ── Local Image Management ──

    /// <summary>
    /// Add image to pending queue (local only, not uploaded yet).
    /// Returns the placeholder tag to insert into HTML content.
    /// </summary>
    public string AddLocalImage(byte[] bytes, string fileName)
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        _pendingImages[id] = (fileName, bytes);
        OnPropertyChanged(nameof(PendingImageCount));
        StatusMessage = $"已加入圖片: {fileName}（共 {_pendingImages.Count} 張待上傳）";
        return $"<img src=\"local://{id}\" alt=\"{Path.GetFileNameWithoutExtension(fileName)}\" data-filename=\"{fileName}\" />";
    }

    /// <summary>
    /// Upload all pending images and replace placeholders in content with real URLs.
    /// </summary>
    private async Task<string> UploadPendingImagesAsync(string content)
    {
        if (_pendingImages.Count == 0) return content;

        var total = _pendingImages.Count;
        var uploaded = 0;
        var failed = 0;

        foreach (var (id, (fileName, bytes)) in _pendingImages.ToList())
        {
            StatusMessage = $"上傳圖片 ({uploaded + 1}/{total}): {fileName}";
            try
            {
                var media = await _client.UploadMediaAsync(bytes, fileName);
                if (media != null)
                {
                    // Replace placeholder with real URL
                    content = content.Replace($"local://{id}", media.SourceUrl);
                    _pendingImages.Remove(id);
                    uploaded++;
                }
                else
                {
                    failed++;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"上傳 {fileName} 失敗: {ex.Message}";
                failed++;
            }
        }

        OnPropertyChanged(nameof(PendingImageCount));

        if (failed > 0)
            StatusMessage = $"已上傳 {uploaded}/{total} 張圖片，{failed} 張失敗";
        else
            StatusMessage = $"已上傳 {uploaded} 張圖片";

        return content;
    }

    // ── Commands ──

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        StatusMessage = "載入中...";
        try
        {
            var cats = await _client.GetCategoriesAsync();
            Categories.Clear();
            foreach (var c in cats)
                Categories.Add(new CategoryItem { Id = c.Id, Name = c.Name });

            var posts = await _client.GetPostsAsync(perPage: 10);
            RecentPosts.Clear();
            foreach (var p in posts)
                RecentPosts.Add(new BlogPostListItem(p));

            StatusMessage = "就緒";
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task PublishAsync()
    {
        if (string.IsNullOrWhiteSpace(Title))
        {
            StatusMessage = "請輸入文章標題";
            return;
        }

        IsBusy = true;
        StatusMessage = IsEditing ? "更新文章中..." : "發布文章中...";

        try
        {
            // Upload pending images first
            var finalContent = await UploadPendingImagesAsync(HtmlContent);

            var selectedCatIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToArray();
            int[]? tagIds = null;
            if (!string.IsNullOrWhiteSpace(TagsText))
            {
                var tagNames = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                tagIds = await _client.ResolveTagIdsAsync(tagNames);
            }

            StatusMessage = IsEditing ? "更新文章中..." : "發布文章中...";

            BlogPost? result;
            if (IsEditing)
            {
                result = await _client.UpdatePostAsync(CurrentPostId, Title, finalContent, SelectedStatus,
                    selectedCatIds.Length > 0 ? selectedCatIds : null,
                    tagIds,
                    string.IsNullOrWhiteSpace(Excerpt) ? null : Excerpt);
            }
            else
            {
                result = await _client.CreatePostAsync(Title, finalContent, SelectedStatus,
                    selectedCatIds.Length > 0 ? selectedCatIds : null,
                    tagIds,
                    string.IsNullOrWhiteSpace(Excerpt) ? null : Excerpt);
            }

            if (result != null)
            {
                // Update content with real URLs (replace local:// placeholders)
                HtmlContent = finalContent;
                CurrentPostId = result.Id;
                StatusMessage = SelectedStatus == "publish"
                    ? $"已發布！{result.Link}"
                    : $"已儲存為 {SelectedStatus}，文章 ID: {result.Id}";

                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void NewPost()
    {
        CurrentPostId = 0;
        _currentPost = null;
        Title = string.Empty;
        HtmlContent = string.Empty;
        Excerpt = string.Empty;
        TagsText = string.Empty;
        SelectedStatus = "draft";
        _pendingImages.Clear();
        foreach (var c in Categories) c.IsSelected = false;
        StatusMessage = "新文章";
        OnPropertyChanged(nameof(PendingImageCount));
    }

    public async Task OpenPostAsync(BlogPost post)
    {
        IsBusy = true;
        StatusMessage = "載入文章中...";
        try
        {
            var full = await _client.GetPostAsync(post.Id);
            if (full != null)
            {
                _currentPost = full;
                CurrentPostId = full.Id;
                Title = full.Title.Value;
                HtmlContent = full.Content.Value;
                Excerpt = full.Excerpt.Value;
                SelectedStatus = full.Status;
                _pendingImages.Clear();
                OnPropertyChanged(nameof(PendingImageCount));

                foreach (var c in Categories)
                    c.IsSelected = full.Categories.Contains(c.Id);

                StatusMessage = $"編輯中: {full.Title.Value}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeletePostAsync(BlogPost post)
    {
        IsBusy = true;
        try
        {
            await _client.DeletePostAsync(post.Id);
            if (CurrentPostId == post.Id) NewPost();
            await LoadDataAsync();
            StatusMessage = "文章已刪除";
        }
        catch (Exception ex)
        {
            StatusMessage = $"錯誤: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteCurrentPostAsync()
    {
        if (_currentPost == null) return;
        await DeletePostAsync(_currentPost);
    }

    public void Dispose() => _client.Dispose();

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class CategoryItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class BlogPostListItem
{
    public BlogPost Post { get; }

    public BlogPostListItem(BlogPost post)
    {
        Post = post;
    }

    public string DisplayTitle => string.IsNullOrWhiteSpace(Post.Title.Value) ? "(無標題)" : Post.Title.Value;
    public string Status => Post.Status;
    public string DateDisplay => Post.Date != null && DateTime.TryParse(Post.Date, out var dt)
        ? dt.ToString("yyyy-MM-dd")
        : "";
}
