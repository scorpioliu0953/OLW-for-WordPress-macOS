using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OLWforWordPress.ViewModels;

namespace OLWforWordPress.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        ContentEditor.AddHandler(DragDrop.DropEvent, OnDrop);
        ContentEditor.AddHandler(DragDrop.DragOverEvent, OnDragOver);

        if (DataContext is MainWindowViewModel vm)
            await vm.LoadDataAsync();
    }

    // ── Toolbar: Post Actions ──

    private void OnNewPostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.NewPost();
    }

    private async void OnPublishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.PublishAsync();
    }

    // ── Toolbar: Formatting ──

    private void OnBoldClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<strong>", "</strong>");
    private void OnItalicClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<em>", "</em>");
    private void OnUnderlineClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<u>", "</u>");
    private void OnStrikeClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<s>", "</s>");
    private void OnH2Click(object? s, RoutedEventArgs e) => WrapSelectionWith("<h2>", "</h2>");
    private void OnH3Click(object? s, RoutedEventArgs e) => WrapSelectionWith("<h3>", "</h3>");
    private void OnBlockquoteClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<blockquote>", "</blockquote>");
    private void OnHrClick(object? s, RoutedEventArgs e) => InsertAtCursor("\n<hr />\n");
    private void OnReadMoreClick(object? s, RoutedEventArgs e) => InsertAtCursor("\n<!--more-->\n");

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new LinkDialog();
        var selStart = ContentEditor.SelectionStart;
        var selEnd = ContentEditor.SelectionEnd;
        if (selEnd > selStart)
        {
            var curText = ContentEditor.Text ?? string.Empty;
            dialog.DisplayText = curText.Substring(selStart, selEnd - selStart);
        }

        var result = await dialog.ShowDialog<LinkDialogResult?>(this);
        if (result != null)
        {
            var displayText = string.IsNullOrWhiteSpace(result.Text) ? result.Url : result.Text;
            var tag = $"<a href=\"{result.Url}\">{displayText}</a>";

            if (GetSelectionLength() > 0)
                ReplaceSelection(tag);
            else
                InsertAtCursor(tag);
        }
    }

    // ── Toolbar: Image Insert (multi-select) ──

    private async void OnInsertImageClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "選擇圖片",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("圖片檔案")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }
                }
            }
        });

        if (files.Count == 0) return;
        await UploadAndInsertImages(vm, files.Select(f => f.Path.LocalPath).ToList());
    }

    // ── Drag & Drop ──

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Files)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var storageItems = e.Data.GetFiles();
        if (storageItems == null) return;

        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

        var imagePaths = storageItems
            .Select(f => f.Path.LocalPath)
            .Where(p => imageExtensions.Contains(Path.GetExtension(p)))
            .ToList();

        if (imagePaths.Count == 0) return;
        await UploadAndInsertImages(vm, imagePaths);
    }

    // ── Sidebar ──

    private async void OnRecentPostSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not BlogPostListItem item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        listBox.SelectedItem = null;
        await vm.OpenPostAsync(item.Post);
    }

    private async void OnDeletePostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsEditing) return;
        await vm.DeleteCurrentPostAsync();
    }

    // ── Helpers ──

    private int GetSelectionLength()
    {
        return ContentEditor.SelectionEnd - ContentEditor.SelectionStart;
    }

    private async Task UploadAndInsertImages(MainWindowViewModel vm, List<string> imagePaths)
    {
        vm.IsBusy = true;
        var total = imagePaths.Count;
        var uploaded = 0;
        var imgTags = new List<string>();

        foreach (var path in imagePaths)
        {
            vm.StatusMessage = $"上傳圖片中... ({uploaded + 1}/{total})";
            var url = await vm.UploadImageAsync(path);
            if (url != null)
            {
                imgTags.Add($"<img src=\"{url}\" alt=\"{Path.GetFileNameWithoutExtension(path)}\" />");
                uploaded++;
            }
        }

        if (imgTags.Count > 0)
            InsertAtCursor("\n" + string.Join("\n", imgTags) + "\n");

        vm.IsBusy = false;
        vm.StatusMessage = uploaded == total
            ? $"已上傳 {uploaded} 張圖片"
            : $"已上傳 {uploaded}/{total} 張圖片";
    }

    private void WrapSelectionWith(string openTag, string closeTag)
    {
        var tb = ContentEditor;
        var start = tb.SelectionStart;
        var end = tb.SelectionEnd;
        var length = end - start;
        var text = tb.Text ?? string.Empty;

        if (length > 0)
        {
            var selected = text.Substring(start, length);
            var replacement = openTag + selected + closeTag;
            tb.Text = text.Remove(start, length).Insert(start, replacement);
            Dispatcher.UIThread.Post(() =>
            {
                tb.SelectionStart = start + replacement.Length;
                tb.SelectionEnd = tb.SelectionStart;
            });
        }
        else
        {
            var insertion = openTag + closeTag;
            tb.Text = text.Insert(start, insertion);
            Dispatcher.UIThread.Post(() =>
            {
                tb.SelectionStart = start + openTag.Length;
                tb.SelectionEnd = tb.SelectionStart;
            });
        }

        SyncContentToViewModel();
        tb.Focus();
    }

    private void InsertAtCursor(string content)
    {
        var tb = ContentEditor;
        var pos = tb.SelectionStart;
        var text = tb.Text ?? string.Empty;
        tb.Text = text.Insert(pos, content);
        Dispatcher.UIThread.Post(() =>
        {
            tb.SelectionStart = pos + content.Length;
            tb.SelectionEnd = tb.SelectionStart;
        });
        SyncContentToViewModel();
        tb.Focus();
    }

    private void ReplaceSelection(string content)
    {
        var tb = ContentEditor;
        var start = tb.SelectionStart;
        var length = tb.SelectionEnd - start;
        var text = tb.Text ?? string.Empty;
        tb.Text = text.Remove(start, length).Insert(start, content);
        Dispatcher.UIThread.Post(() =>
        {
            tb.SelectionStart = start + content.Length;
            tb.SelectionEnd = tb.SelectionStart;
        });
        SyncContentToViewModel();
        tb.Focus();
    }

    private void SyncContentToViewModel()
    {
        if (DataContext is MainWindowViewModel vm)
            vm.HtmlContent = ContentEditor.Text ?? string.Empty;
    }
}

// ── Link Dialog ──

public class LinkDialogResult
{
    public string Url { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
}

public class LinkDialog : Window
{
    private readonly TextBox _urlBox;
    private readonly TextBox _textBox;

    public string DisplayText
    {
        set => _textBox.Text = value;
    }

    public LinkDialog()
    {
        Title = "插入連結";
        Width = 400;
        Height = 200;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _urlBox = new TextBox { Watermark = "https://example.com" };
        _textBox = new TextBox { Watermark = "顯示文字（選填）" };

        var okBtn = new Button { Content = "確定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_urlBox.Text))
                Close(new LinkDialogResult { Url = _urlBox.Text, Text = _textBox.Text ?? "" });
            else
                Close(null);
        };

        var cancelBtn = new Button { Content = "取消", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        cancelBtn.Click += (_, _) => Close(null);

        var btnPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelBtn, okBtn }
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "網址", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                _urlBox,
                new TextBlock { Text = "顯示文字", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                _textBox,
                btnPanel
            }
        };
    }
}
