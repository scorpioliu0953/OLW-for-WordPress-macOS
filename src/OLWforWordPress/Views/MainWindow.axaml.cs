using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OLWforWordPress.ViewModels;

namespace OLWforWordPress.Views;

public partial class MainWindow : Window
{
    private DispatcherTimer? _previewTimer;
    private string _lastPreviewContent = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// In visual tab (0): toolbar inserts formatted HTML and refreshes preview.
    /// In HTML tab (1): toolbar works on ContentEditor TextBox selection.
    /// </summary>
    private bool IsVisualTab => EditorTabs?.SelectedIndex == 0;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Drag & drop on both tabs
        EditorDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        EditorDropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);
        HtmlDropZone.AddHandler(DragDrop.DragOverEvent, OnDragOver, RoutingStrategies.Tunnel);
        HtmlDropZone.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Tunnel);

        if (DataContext is MainWindowViewModel vm)
        {
            PreviewPanel.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            await vm.LoadDataAsync();
        }

        // Live preview auto-refresh
        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _previewTimer.Tick += (_, _) =>
        {
            if (DataContext is MainWindowViewModel v && v.HtmlContent != _lastPreviewContent)
            {
                _lastPreviewContent = v.HtmlContent;
                RefreshPreview();
            }
        };
        _previewTimer.Start();

        // Default to visual edit tab
        EditorTabs.SelectedIndex = 0;
        RefreshPreview();
    }

    // ── Tab switching ──

    private void OnEditorTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs == null) return;
        if (EditorTabs.SelectedIndex == 0)
            RefreshPreview();
    }

    private void RefreshPreview()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            PreviewPanel.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            PreviewPanel.RenderHtml(vm.HtmlContent);
        }
    }

    // ── Compose bar (visual tab) ──

    private void OnComposeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            InsertComposedText();
            e.Handled = true;
        }
    }

    private void OnComposeInsert(object? sender, RoutedEventArgs e)
    {
        InsertComposedText();
    }

    private void InsertComposedText()
    {
        var text = ComposeBox.Text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (DataContext is MainWindowViewModel vm)
        {
            vm.HtmlContent = (vm.HtmlContent ?? "").TrimEnd() + $"\n<p>{EscapeHtml(text)}</p>\n";
            ComposeBox.Text = string.Empty;
            _lastPreviewContent = vm.HtmlContent;
            RefreshPreview();
        }
    }

    private static string EscapeHtml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // ── Post Actions ──

    private void OnNewPostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NewPost();
            _lastPreviewContent = string.Empty;
            RefreshPreview();
        }
    }

    private async void OnPublishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm) await vm.PublishAsync();
    }

    // ── Formatting Toolbar ──
    // Visual tab: insert formatted block into HtmlContent + refresh preview.
    // HTML tab: wrap selection in ContentEditor TextBox.

    private void OnBoldClick(object? s, RoutedEventArgs e) => ApplyFormat("<strong>", "</strong>");
    private void OnItalicClick(object? s, RoutedEventArgs e) => ApplyFormat("<em>", "</em>");
    private void OnUnderlineClick(object? s, RoutedEventArgs e) => ApplyFormat("<u>", "</u>");
    private void OnStrikeClick(object? s, RoutedEventArgs e) => ApplyFormat("<s>", "</s>");
    private void OnCodeClick(object? s, RoutedEventArgs e) => ApplyFormat("<code>", "</code>");

    private void OnH2Click(object? s, RoutedEventArgs e) => ApplyBlockFormat("<h2>", "</h2>");
    private void OnH3Click(object? s, RoutedEventArgs e) => ApplyBlockFormat("<h3>", "</h3>");
    private void OnH4Click(object? s, RoutedEventArgs e) => ApplyBlockFormat("<h4>", "</h4>");
    private void OnBlockquoteClick(object? s, RoutedEventArgs e) => ApplyBlockFormat("<blockquote>", "</blockquote>");

    private void OnHrClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
            AppendToContent("\n<hr />\n");
        else
            InsertAtCursor("\n<hr />\n");
    }

    private void OnReadMoreClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
            AppendToContent("\n<!--more-->\n");
        else
            InsertAtCursor("\n<!--more-->\n");
    }

    /// <summary>
    /// Inline formatting: in visual tab wraps compose box selection;
    /// in HTML tab wraps ContentEditor selection.
    /// </summary>
    private void ApplyFormat(string openTag, string closeTag)
    {
        if (IsVisualTab)
        {
            // Wrap selected text in compose box
            var tb = ComposeBox;
            var start = tb.SelectionStart;
            var end = tb.SelectionEnd;
            var text = tb.Text ?? "";
            var length = end - start;

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
                tb.Text = (text ?? "").Insert(start, insertion);
                Dispatcher.UIThread.Post(() =>
                {
                    tb.SelectionStart = start + openTag.Length;
                    tb.SelectionEnd = tb.SelectionStart;
                });
            }
            tb.Focus();
        }
        else
        {
            WrapSelectionWith(openTag, closeTag);
        }
    }

    /// <summary>
    /// Block formatting: in visual tab wraps compose text as a block and inserts;
    /// in HTML tab wraps ContentEditor selection.
    /// </summary>
    private void ApplyBlockFormat(string openTag, string closeTag)
    {
        if (IsVisualTab)
        {
            var text = ComposeBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                if (DataContext is MainWindowViewModel vm)
                {
                    vm.HtmlContent = (vm.HtmlContent ?? "").TrimEnd() + $"\n{openTag}{EscapeHtml(text)}{closeTag}\n";
                    ComposeBox.Text = string.Empty;
                    _lastPreviewContent = vm.HtmlContent;
                    RefreshPreview();
                }
            }
            else
            {
                // Insert empty block
                AppendToContent($"\n{openTag}{closeTag}\n");
            }
        }
        else
        {
            WrapSelectionWith(openTag, closeTag);
        }
    }

    private void OnUlClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
        {
            var text = ComposeBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var items = string.Join("\n", lines.Select(l => $"  <li>{EscapeHtml(l.Trim())}</li>"));
                AppendToContent($"\n<ul>\n{items}\n</ul>\n");
                ComposeBox.Text = string.Empty;
            }
            else
            {
                AppendToContent("<ul>\n  <li></li>\n</ul>");
            }
        }
        else
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
    }

    private void OnOlClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
        {
            var text = ComposeBox.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
            {
                var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var items = string.Join("\n", lines.Select(l => $"  <li>{EscapeHtml(l.Trim())}</li>"));
                AppendToContent($"\n<ol>\n{items}\n</ol>\n");
                ComposeBox.Text = string.Empty;
            }
            else
            {
                AppendToContent("<ol>\n  <li></li>\n</ol>");
            }
        }
        else
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
    }

    private void OnFontSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo == null || FontSizeCombo.SelectedIndex <= 0) return;
        var item = FontSizeCombo.SelectedItem as ComboBoxItem;
        var size = item?.Tag?.ToString();
        if (size != null)
            ApplyFormat($"<span style=\"font-size:{size}px\">", "</span>");
        FontSizeCombo.SelectedIndex = 0;
    }

    private void OnTextColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TextColorCombo == null || TextColorCombo.SelectedIndex <= 0) return;
        var item = TextColorCombo.SelectedItem as ComboBoxItem;
        var color = item?.Tag?.ToString();
        if (color != null)
            ApplyFormat($"<span style=\"color:{color}\">", "</span>");
        TextColorCombo.SelectedIndex = 0;
    }

    private void OnBgColorChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (BgColorCombo == null || BgColorCombo.SelectedIndex <= 0) return;
        var item = BgColorCombo.SelectedItem as ComboBoxItem;
        var color = item?.Tag?.ToString();
        if (color != null)
            ApplyFormat($"<span style=\"background-color:{color}\">", "</span>");
        BgColorCombo.SelectedIndex = 0;
    }

    private async void OnLinkClick(object? sender, RoutedEventArgs e)
    {
        var dialog = new LinkDialog();

        if (IsVisualTab)
        {
            var selStart = ComposeBox.SelectionStart;
            var selEnd = ComposeBox.SelectionEnd;
            if (selEnd > selStart)
                dialog.DisplayText = (ComposeBox.Text ?? "").Substring(selStart, selEnd - selStart);
        }
        else
        {
            var selStart = ContentEditor.SelectionStart;
            var selEnd = ContentEditor.SelectionEnd;
            if (selEnd > selStart)
                dialog.DisplayText = (ContentEditor.Text ?? "").Substring(selStart, selEnd - selStart);
        }

        var result = await dialog.ShowDialog<LinkDialogResult?>(this);
        if (result == null) return;

        var displayText = string.IsNullOrWhiteSpace(result.Text) ? result.Url : result.Text;
        var tag = $"<a href=\"{result.Url}\">{displayText}</a>";

        if (IsVisualTab)
        {
            // Insert link into compose box or append to content
            if (!string.IsNullOrEmpty(ComposeBox.Text))
            {
                var tb = ComposeBox;
                var pos = tb.SelectionStart;
                var len = tb.SelectionEnd - pos;
                var text = tb.Text ?? "";
                if (len > 0)
                    tb.Text = text.Remove(pos, len).Insert(pos, tag);
                else
                    tb.Text = text.Insert(pos, tag);
            }
            else
            {
                AppendToContent($"\n<p>{tag}</p>\n");
            }
        }
        else
        {
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
        _lastPreviewContent = string.Empty;
        RefreshPreview();
    }

    private async void OnDeletePostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsEditing) return;
        await vm.DeleteCurrentPostAsync();
    }

    // ── Helpers ──

    /// <summary>Append HTML to the end of HtmlContent and refresh preview.</summary>
    private void AppendToContent(string html)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.HtmlContent = (vm.HtmlContent ?? "").TrimEnd() + html;
            _lastPreviewContent = vm.HtmlContent;
            RefreshPreview();
        }
    }

    /// <summary>Add local images - works from both tabs.</summary>
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
        {
            var html = "\n" + string.Join("\n\n", imgTags) + "\n";
            if (IsVisualTab)
                AppendToContent(html);
            else
                InsertAtCursor(html);
        }
    }

    // ── HTML tab TextBox helpers (for ContentEditor) ──

    private int GetSelectionLength() => ContentEditor.SelectionEnd - ContentEditor.SelectionStart;

    private string GetSelectedText()
    {
        var start = ContentEditor.SelectionStart;
        var end = ContentEditor.SelectionEnd;
        if (end <= start) return "";
        return (ContentEditor.Text ?? "").Substring(start, end - start);
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

        SyncHtmlEditorToViewModel();
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
        SyncHtmlEditorToViewModel();
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
        SyncHtmlEditorToViewModel();
        tb.Focus();
    }

    private void SyncHtmlEditorToViewModel()
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
