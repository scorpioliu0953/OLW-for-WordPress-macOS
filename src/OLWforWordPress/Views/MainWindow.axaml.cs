using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OLWforWordPress.Controls;
using OLWforWordPress.ViewModels;

namespace OLWforWordPress.Views;

public partial class MainWindow : Window
{
    private bool _syncingToEditor;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private bool IsVisualTab => EditorTabs?.SelectedIndex == 0;

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // HTML tab drag & drop
        HtmlDropZone.AddHandler(DragDrop.DragOverEvent, OnHtmlDragOver, RoutingStrategies.Tunnel);
        HtmlDropZone.AddHandler(DragDrop.DropEvent, OnHtmlDrop, RoutingStrategies.Tunnel);

        if (DataContext is MainWindowViewModel vm)
        {
            // Wire up block editor
            VisualEditor.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            VisualEditor.ContentChanged += OnBlockEditorContentChanged;
            VisualEditor.ImagesDroppedAtPosition += OnImagesDroppedAtPosition;

            await vm.LoadDataAsync();
            LoadVisualEditor();
        }

        EditorTabs.SelectedIndex = 0;
    }

    // ── Block editor ↔ ViewModel sync ──

    private void OnBlockEditorContentChanged(string html)
    {
        if (_syncingToEditor) return;
        if (DataContext is MainWindowViewModel vm)
        {
            _syncingToEditor = true;
            vm.HtmlContent = html;
            _syncingToEditor = false;
        }
    }

    private void LoadVisualEditor()
    {
        if (DataContext is MainWindowViewModel vm)
        {
            _syncingToEditor = true;
            VisualEditor.SetLocalImageResolver(id => vm.GetPendingImageBytes(id));
            VisualEditor.LoadHtml(vm.HtmlContent);
            _syncingToEditor = false;
        }
    }

    // ── Tab switching ──

    private void OnEditorTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (EditorTabs == null) return;
        if (EditorTabs.SelectedIndex == 0)
        {
            // Switching to visual → reload from HTML
            LoadVisualEditor();
        }
        else
        {
            // Switching to HTML → sync from block editor
            if (DataContext is MainWindowViewModel vm)
                vm.HtmlContent = VisualEditor.ToHtml();
        }
    }

    // ── Post Actions ──

    private void OnNewPostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NewPost();
            LoadVisualEditor();
        }
    }

    private async void OnPublishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            // Sync visual editor to ViewModel before publishing
            if (IsVisualTab)
                vm.HtmlContent = VisualEditor.ToHtml();
            await vm.PublishAsync();
        }
    }

    // ── Formatting Toolbar ──
    // Visual tab: operates on block editor focused block
    // HTML tab: operates on ContentEditor TextBox

    private void OnBoldClick(object? s, RoutedEventArgs e) => ApplyFormat("<strong>", "</strong>");
    private void OnItalicClick(object? s, RoutedEventArgs e) => ApplyFormat("<em>", "</em>");
    private void OnUnderlineClick(object? s, RoutedEventArgs e) => ApplyFormat("<u>", "</u>");
    private void OnStrikeClick(object? s, RoutedEventArgs e) => ApplyFormat("<s>", "</s>");
    private void OnCodeClick(object? s, RoutedEventArgs e) => ApplyFormat("<code>", "</code>");

    private void OnH2Click(object? s, RoutedEventArgs e) => ChangeBlockType("h2");
    private void OnH3Click(object? s, RoutedEventArgs e) => ChangeBlockType("h3");
    private void OnH4Click(object? s, RoutedEventArgs e) => ChangeBlockType("h4");
    private void OnBlockquoteClick(object? s, RoutedEventArgs e) => ChangeBlockType("blockquote");

    private void OnHrClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
        {
            var idx = VisualEditor.FocusedBlockIndex;
            VisualEditor.InsertHtmlBlock("hr", "", idx >= 0 ? idx + 1 : -1);
        }
        else
            InsertAtCursor("\n<hr />\n");
    }

    private void OnReadMoreClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
        {
            var idx = VisualEditor.FocusedBlockIndex;
            VisualEditor.InsertHtmlBlock("more", "", idx >= 0 ? idx + 1 : -1);
        }
        else
            InsertAtCursor("\n<!--more-->\n");
    }

    private void ApplyFormat(string openTag, string closeTag)
    {
        if (IsVisualTab)
        {
            VisualEditor.ApplyInlineFormat(openTag, closeTag);
        }
        else
        {
            WrapSelectionWith(openTag, closeTag);
        }
    }

    private void ChangeBlockType(string newTag)
    {
        if (IsVisualTab)
        {
            var currentType = VisualEditor.GetFocusedBlockType();
            if (currentType == newTag)
            {
                // Toggle back to paragraph
                VisualEditor.ChangeBlockType("p");
            }
            else
            {
                VisualEditor.ChangeBlockType(newTag);
            }
        }
        else
        {
            // HTML tab: wrap selection
            var tag = newTag;
            WrapSelectionWith($"<{tag}>", $"</{tag}>");
        }
    }

    private void OnUlClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
            VisualEditor.ChangeBlockType("ul");
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
                InsertAtCursor("<ul>\n  <li></li>\n</ul>");
        }
    }

    private void OnOlClick(object? s, RoutedEventArgs e)
    {
        if (IsVisualTab)
            VisualEditor.ChangeBlockType("ol");
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
                InsertAtCursor("<ol>\n  <li></li>\n</ol>");
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
            var tb = VisualEditor.GetFocusedTextBox();
            if (tb != null && tb.SelectionEnd > tb.SelectionStart)
                dialog.DisplayText = (tb.Text ?? "").Substring(tb.SelectionStart, tb.SelectionEnd - tb.SelectionStart);
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
            // In visual mode, insert link text at cursor in focused block
            var tb = VisualEditor.GetFocusedTextBox();
            if (tb != null)
            {
                var pos = tb.SelectionStart;
                var len = tb.SelectionEnd - pos;
                var text = tb.Text ?? "";
                if (len > 0)
                    tb.Text = text.Remove(pos, len).Insert(pos, displayText);
                else
                    tb.Text = text.Insert(pos, displayText);
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

        if (IsVisualTab)
        {
            var insertIdx = VisualEditor.FocusedBlockIndex;
            await AddImagesToVisualEditor(vm, files.ToList(), insertIdx >= 0 ? insertIdx + 1 : -1);
        }
        else
        {
            await AddImagesToHtmlEditor(vm, files.ToList());
        }
    }

    private async Task OnImagesDroppedAtPosition(IEnumerable<IStorageItem> items, int insertIndex)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".webp" };

        var imageFiles = new List<IStorageFile>();
        foreach (var item in items)
        {
            if (item is IStorageFile sf && imageExtensions.Contains(Path.GetExtension(sf.Name)))
                imageFiles.Add(sf);
        }

        if (imageFiles.Count > 0)
            await AddImagesToVisualEditor(vm, imageFiles, insertIndex);
    }

    private async Task AddImagesToVisualEditor(MainWindowViewModel vm, List<IStorageFile> files, int insertAt)
    {
        var offset = 0;
        foreach (var file in files)
        {
            try
            {
                var bytes = await ReadFileBytes(file);
                if (bytes.Length > 0)
                {
                    var imgTag = vm.AddLocalImage(bytes, file.Name);
                    VisualEditor.InsertImageBlock(imgTag, insertAt >= 0 ? insertAt + offset : -1);
                    offset++;
                }
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"讀取 {file.Name} 失敗: {ex.Message}";
            }
        }
    }

    // ── HTML tab drag & drop ──

    private void OnHtmlDragOver(object? sender, DragEventArgs e)
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

    private async void OnHtmlDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        var storageItems = e.Data.GetFiles()?.ToList();
        if (storageItems == null || storageItems.Count == 0) return;
        e.Handled = true;

        var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
        var imageFiles = storageItems
            .OfType<IStorageFile>()
            .Where(sf => imageExtensions.Contains(Path.GetExtension(sf.Name)))
            .ToList();

        if (imageFiles.Count > 0)
            await AddImagesToHtmlEditor(vm, imageFiles);
    }

    private async Task AddImagesToHtmlEditor(MainWindowViewModel vm, List<IStorageFile> files)
    {
        var imgTags = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var bytes = await ReadFileBytes(file);
                if (bytes.Length > 0)
                    imgTags.Add(vm.AddLocalImage(bytes, file.Name));
            }
            catch (Exception ex)
            {
                vm.StatusMessage = $"讀取 {file.Name} 失敗: {ex.Message}";
            }
        }
        if (imgTags.Count > 0)
            InsertAtCursor("\n" + string.Join("\n\n", imgTags) + "\n");
    }

    // ── Sidebar ──

    private async void OnRecentPostSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (listBox.SelectedItem is not BlogPostListItem item) return;
        if (DataContext is not MainWindowViewModel vm) return;

        listBox.SelectedItem = null;
        await vm.OpenPostAsync(item.Post);
        LoadVisualEditor();
    }

    private async void OnDeletePostClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.IsEditing) return;
        await vm.DeleteCurrentPostAsync();
        LoadVisualEditor();
    }

    // ── HTML tab TextBox helpers ──

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
        tb.Focus();
    }

    private static async Task<byte[]> ReadFileBytes(IStorageFile file)
    {
        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            return await File.ReadAllBytesAsync(localPath);

        await using var stream = await file.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return ms.ToArray();
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
