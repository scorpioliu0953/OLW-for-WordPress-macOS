using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
    private string _statusMessage = "Ready";
    private string _selectedStatus = "draft";
    private bool _isBusy;
    private int _currentPostId;
    private BlogPost? _currentPost;

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

    // ── Commands ──

    public async Task LoadDataAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading...";
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

            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
            StatusMessage = "Title is required.";
            return;
        }

        IsBusy = true;
        StatusMessage = IsEditing ? "Updating post..." : "Publishing...";

        try
        {
            var selectedCatIds = Categories.Where(c => c.IsSelected).Select(c => c.Id).ToArray();
            int[]? tagIds = null;
            if (!string.IsNullOrWhiteSpace(TagsText))
            {
                var tagNames = TagsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                tagIds = await _client.ResolveTagIdsAsync(tagNames);
            }

            BlogPost? result;
            if (IsEditing)
            {
                result = await _client.UpdatePostAsync(CurrentPostId, Title, HtmlContent, SelectedStatus,
                    selectedCatIds.Length > 0 ? selectedCatIds : null,
                    tagIds,
                    string.IsNullOrWhiteSpace(Excerpt) ? null : Excerpt);
            }
            else
            {
                result = await _client.CreatePostAsync(Title, HtmlContent, SelectedStatus,
                    selectedCatIds.Length > 0 ? selectedCatIds : null,
                    tagIds,
                    string.IsNullOrWhiteSpace(Excerpt) ? null : Excerpt);
            }

            if (result != null)
            {
                CurrentPostId = result.Id;
                StatusMessage = SelectedStatus == "publish"
                    ? $"Published! {result.Link}"
                    : $"Saved as {SelectedStatus}. Post ID: {result.Id}";

                await LoadDataAsync();
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string?> UploadImageAsync(string filePath)
    {
        try
        {
            StatusMessage = "上傳圖片中...";
            var media = await _client.UploadMediaAsync(filePath);
            StatusMessage = media != null ? "圖片已上傳" : "上傳失敗";
            return media?.SourceUrl;
        }
        catch (Exception ex)
        {
            StatusMessage = $"上傳錯誤: {ex.Message}";
            return null;
        }
    }

    public async Task<string?> UploadImageAsync(byte[] fileBytes, string fileName)
    {
        try
        {
            StatusMessage = "上傳圖片中...";
            var media = await _client.UploadMediaAsync(fileBytes, fileName);
            StatusMessage = media != null ? "圖片已上傳" : "上傳失敗";
            return media?.SourceUrl;
        }
        catch (Exception ex)
        {
            StatusMessage = $"上傳錯誤: {ex.Message}";
            return null;
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
        foreach (var c in Categories) c.IsSelected = false;
        StatusMessage = "New post";
    }

    public async Task OpenPostAsync(BlogPost post)
    {
        IsBusy = true;
        StatusMessage = "Loading post...";
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

                foreach (var c in Categories)
                    c.IsSelected = full.Categories.Contains(c.Id);

                StatusMessage = $"Editing: {full.Title.Value}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
            StatusMessage = "Post deleted.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
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
