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
        EditorDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        EditorDropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);

        // Setup preview panel image resolver
        if (DataContext is MainWindowViewModel vm)
        {
            PreviewPanel.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            await vm.LoadDataAsync();
        }

        // Default to HTML tab for editing
        EditorTabs.SelectedIndex = 1;
    }

    // ── Tab switching ──

    private void OnEditorTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs.SelectedIndex == 0) // Preview tab
        {
            RefreshPreview();
        }
    }

    private void RefreshPreview()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            PreviewPanel.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            PreviewPanel.RenderHtml(vm.HtmlContent);
        }
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
    private void OnH4Click(object? s, RoutedEventArgs e) => WrapSelectionWith("<h4>", "</h4>");
    private void OnBlockquoteClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<blockquote>", "</blockquote>");
    private void OnCodeClick(object? s, RoutedEventArgs e) => WrapSelectionWith("<code>", "</code>");
    private void OnHrClick(object? s, RoutedEventArgs e) => InsertAtCursor("\n<hr />\n");
    private void OnReadMoreClick(object? s, RoutedEventArgs e) => InsertAtCursor("\n<!--more-->\n");

    private void OnUlClick(object? s, RoutedEventArgs e)
    {
        var sel = GetSelectedText();
        if (!string.IsNullOrEmpty(sel))
        {
            var lines = sel.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = string.Join("\n", lines.Select(l => $"  <li>{l.Trim()}</li>"));
            ReplaceSelection($"<ul>\n{items}\n</ul>");
        }
        else
        {
            InsertAtCursor("<ul>\n  <li></li>\n</ul>");
        }
    }

    private void OnOlClick(object? s, RoutedEventArgs e)
    {
        var sel = GetSelectedText();
        if (!string.IsNullOrEmpty(sel))
        {
            var lines = sel.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var items = string.Join("\n", lines.Select(l => $"  <li>{l.Trim()}</li>"));
            ReplaceSelection($"<ol>\n{items}\n</ol>");
        }
        else
        {
            InsertAtCursor("<ol>\n  <li></li>\n</ol>");
        }
    }

    private void OnFontSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedIndex <= 0) return;
        var item = FontSizeCombo.SelectedItem as ComboBoxItem;
        var size = item?.Tag?.ToString();
        if (size != null)
        {
            SwitchToHtmlTab();
            WrapSelectionWith($"<span style=\"font-size:{size}px\">", "</span>");
        }
        FontSizeCombo.SelectedIndex = 0;
    }

    private void OnTextColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TextColorCombo.SelectedIndex <= 0) return;
        var item = TextColorCombo.SelectedItem as ComboBoxItem;
        var color = item?.Tag?.ToString();
        if (color != null)
        {
            SwitchToHtmlTab();
            WrapSelectionWith($"<span style=\"color:{color}\">", "</span>");
        }
        TextColorCombo.SelectedIndex = 0;
    }

    private void OnBgColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BgColorCombo.SelectedIndex <= 0) return;
        var item = BgColorCombo.SelectedItem as ComboBoxItem;
        var color = item?.Tag?.ToString();
        if (color != null)
        {
            SwitchToHtmlTab();
            WrapSelectionWith($"<span style=\"background-color:{color}\">", "</span>");
        }
        BgColorCombo.SelectedIndex = 0;
    }

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
            SwitchToHtmlTab();
            var displayText = string.IsNullOrWhiteSpace(result.Text) ? result.Url : result.Text;
            var tag = $"<a href=\"{result.Url}\">{displayText}</a>";

            if (GetSelectionLength() > 0)
                ReplaceSelection(tag);
            else
                InsertAtCursor(tag);
        }
    }

    // ── Image Insert ──

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
        SwitchToHtmlTab();
        await AddLocalImages(vm, files.ToList());
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

        var imageFiles = new List<IStorageFile>();
        foreach (var item in storageItems)
        {
            if (item is IStorageFile sf && imageExtensions.Contains(Path.GetExtension(sf.Name)))
                imageFiles.Add(sf);
        }

        if (imageFiles.Count == 0) return;
        await AddLocalImages(vm, imageFiles);
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

    private void SwitchToHtmlTab()
    {
        EditorTabs.SelectedIndex = 1;
    }

    private int GetSelectionLength() => ContentEditor.SelectionEnd - ContentEditor.SelectionStart;

    private string GetSelectedText()
    {
        var start = ContentEditor.SelectionStart;
        var end = ContentEditor.SelectionEnd;
        if (end <= start) return "";
        return (ContentEditor.Text ?? "").Substring(start, end - start);
    }

    private async Task AddLocalImages(MainWindowViewModel vm, List<IStorageFile> files)
    {
        var imgTags = new List<string>();

        foreach (var file in files)
        {
            try
            {
                byte[] bytes;
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    bytes = await File.ReadAllBytesAsync(localPath);
                }
                else
                {
                    await using var stream = await file.OpenReadAsync();
                    using var ms = new MemoryStream();
                    await stream.CopyToAsync(ms);
                    bytes = ms.ToArray();
                }

                if (bytes.Length > 0)
                {
                    var tag = vm.AddLocalImage(bytes, file.Name);
                    imgTags.Add(tag);
                }
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"讀取 {file.Name} 失敗: {ex.Message}";
            }
        }

        if (imgTags.Count > 0)
            InsertAtCursor("\n" + string.Join("\n\n", imgTags) + "\n");
    }

    private void WrapSelectionWith(string openTag, string closeTag)
    {
        SwitchToHtmlTab();
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
        SwitchToHtmlTab();
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
        SwitchToHtmlTab();
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
