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
        // Drag & drop on wrapper Border with Tunnel to intercept before TextBox
        EditorDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        EditorDropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);

        if (DataContext is MainWindowViewModel vm)
            await vm.LoadDataAsync();
    }

    // ── Post Actions ──

    private void OnNewPostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) vm.NewPost();
    }

    private async void OnPublishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.PublishAsync();
    }

    // ── Formatting Toolbar ──

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

    // ── Image Insert (multi-select, read via stream) ──

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
        await UploadStorageFiles(vm, files.ToList());
    }

    // ── Drag & Drop ──

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var storageItems = e.Data.GetFiles()?.ToList();
        if (storageItems == null || storageItems.Count == 0) return;

        e.Handled = true;

        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

        // Filter to image files only
        var imageFiles = new List<IStorageItem>();
        foreach (var item in storageItems)
        {
            var name = item.Name ?? "";
            if (imageExtensions.Contains(Path.GetExtension(name)))
                imageFiles.Add(item);
        }

        if (imageFiles.Count == 0) return;

        // Read files via stream (works on macOS sandbox)
        vm.IsBusy = true;
        var total = imageFiles.Count;
        var uploaded = 0;
        var imgTags = new List<string>();

        foreach (var item in imageFiles)
        {
            vm.StatusMessage = $"上傳圖片中... ({uploaded + 1}/{total})";
            try
            {
                // Try to get as IStorageFile to open stream
                if (item is IStorageFile storageFile)
                {
                    await using var stream = await storageFile.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    var bytes = ms.ToArray();
                    var fileName = storageFile.Name;

                    var url = await vm.UploadImageAsync(bytes, fileName);
                    if (url != null)
                    {
                        imgTags.Add($"<img src=\"{url}\" alt=\"{Path.GetFileNameWithoutExtension(fileName)}\" />");
                        uploaded++;
                    }
                }
            }
            catch
            {
                // Skip failed files
            }
        }

        if (imgTags.Count > 0)
            InsertAtCursor("\n" + string.Join("\n\n", imgTags) + "\n");

        vm.IsBusy = false;
        vm.StatusMessage = uploaded == total
            ? $"已上傳 {uploaded} 張圖片"
            : $"已上傳 {uploaded}/{total} 張圖片";
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

    private int GetSelectionLength() => ContentEditor.SelectionEnd - ContentEditor.SelectionStart;

    private async Task UploadStorageFiles(MainWindowViewModel vm, List<IStorageFile> files)
    {
        vm.IsBusy = true;
        var total = files.Count;
        var uploaded = 0;
        var imgTags = new List<string>();

        foreach (var file in files)
        {
            vm.StatusMessage = $"上傳圖片中... ({uploaded + 1}/{total})";
            try
            {
                await using var stream = await file.OpenReadAsync();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var bytes = ms.ToArray();
                var fileName = file.Name;

                var url = await vm.UploadImageAsync(bytes, fileName);
                if (url != null)
                {
                    imgTags.Add($"<img src=\"{url}\" alt=\"{Path.GetFileNameWithoutExtension(fileName)}\" />");
                    uploaded++;
                }
            }
            catch
            {
                // Skip failed files
            }
        }

        if (imgTags.Count > 0)
            InsertAtCursor("\n" + string.Join("\n\n", imgTags) + "\n");

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
        Width = 420;
        Height = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Media.Brushes.White;

        _urlBox = new TextBox { Watermark = "https://example.com", CornerRadius = new Avalonia.CornerRadius(6) };
        _textBox = new TextBox { Watermark = "顯示文字（選填）", CornerRadius = new Avalonia.CornerRadius(6) };

        var okBtn = new Button
        {
            Content = "插入",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Background = Avalonia.Media.Brush.Parse("#0066CC"),
            Foreground = Avalonia.Media.Brushes.White,
            Padding = new Avalonia.Thickness(20, 6),
            CornerRadius = new Avalonia.CornerRadius(6)
        };
        okBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_urlBox.Text))
                Close(new LinkDialogResult { Url = _urlBox.Text, Text = _textBox.Text ?? "" });
            else
                Close(null);
        };

        var cancelBtn = new Button
        {
            Content = "取消",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Padding = new Avalonia.Thickness(16, 6),
            CornerRadius = new Avalonia.CornerRadius(6)
        };
        cancelBtn.Click += (_, _) => Close(null);

        var btnPanel = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 4, 0, 0),
            Children = { cancelBtn, okBtn }
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "網址", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 13 },
                _urlBox,
                new TextBlock { Text = "顯示文字", FontWeight = Avalonia.Media.FontWeight.SemiBold, FontSize = 13 },
                _textBox,
                btnPanel
            }
        };
    }
}
