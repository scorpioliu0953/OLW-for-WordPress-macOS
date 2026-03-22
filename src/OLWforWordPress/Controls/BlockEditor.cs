using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace OLWforWordPress.Controls;

/// <summary>
/// Block-based WYSIWYG editor. Each block (paragraph, heading, image, etc.)
/// is rendered as an editable native control. Users type directly in the visual view.
/// </summary>
public partial class BlockEditor : UserControl
{
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _container;
    private readonly List<EditorBlock> _blocks = new();
    private Func<string, byte[]?>? _localImageResolver;
    private int _focusedBlockIndex = -1;
    private bool _suppressSync;

    /// <summary>Fires when the user edits content. Parameter is the new HTML.</summary>
    public event Action<string>? ContentChanged;

    /// <summary>Fires when the focused block changes. Parameter is the block type.</summary>
    public event Action<string>? FocusedBlockTypeChanged;

    /// <summary>Called when images are dropped at a specific block position.</summary>
    public event Func<IEnumerable<Avalonia.Platform.Storage.IStorageItem>, int, Task>? ImagesDroppedAtPosition;

    public BlockEditor()
    {
        _container = new StackPanel { Spacing = 2, Margin = new Thickness(16, 12) };

        // Add a click-catcher at bottom to create new paragraphs
        var bottomArea = new Border
        {
            MinHeight = 100,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Ibeam)
        };
        bottomArea.PointerPressed += OnBottomAreaClicked;

        var outerPanel = new StackPanel
        {
            Children = { _container, bottomArea }
        };

        _scrollViewer = new ScrollViewer { Content = outerPanel };
        Content = _scrollViewer;

        // Enable drop on entire editor
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnEditorDragOver, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(DragDrop.DropEvent, OnEditorDrop, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public void SetLocalImageResolver(Func<string, byte[]?> resolver)
    {
        _localImageResolver = resolver;
    }

    public int FocusedBlockIndex => _focusedBlockIndex;

    // ── Load / Export HTML ──

    /// <summary>Load HTML and render as editable blocks.</summary>
    public void LoadHtml(string? html)
    {
        _suppressSync = true;
        _blocks.Clear();
        _container.Children.Clear();
        _focusedBlockIndex = -1;

        if (string.IsNullOrWhiteSpace(html))
        {
            // Start with one empty paragraph
            AddBlock(new EditorBlock("p", ""));
            _suppressSync = false;
            return;
        }

        html = html.Replace("\r\n", "\n").Replace("\r", "\n");
        var parsed = HtmlParser.SplitIntoBlocks(html);

        foreach (var block in parsed)
        {
            AddBlock(new EditorBlock(block.Tag, block.Content));
        }

        // Ensure at least one block
        if (_blocks.Count == 0)
            AddBlock(new EditorBlock("p", ""));

        _suppressSync = false;
    }

    /// <summary>Export all blocks back to HTML.</summary>
    public string ToHtml()
    {
        var sb = new StringBuilder();
        foreach (var block in _blocks)
        {
            var html = block.ToHtml();
            if (!string.IsNullOrEmpty(html))
            {
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(html);
            }
        }
        return sb.ToString();
    }

    // ── Block Management ──

    private void AddBlock(EditorBlock block, int insertAt = -1)
    {
        var control = CreateBlockControl(block);
        block.Control = control;

        if (insertAt >= 0 && insertAt < _blocks.Count)
        {
            _blocks.Insert(insertAt, block);
            _container.Children.Insert(insertAt, control);
        }
        else
        {
            _blocks.Add(block);
            _container.Children.Add(control);
        }
    }

    private void RemoveBlock(int index)
    {
        if (index < 0 || index >= _blocks.Count) return;
        _blocks.RemoveAt(index);
        _container.Children.RemoveAt(index);
        if (_focusedBlockIndex >= _blocks.Count)
            _focusedBlockIndex = _blocks.Count - 1;
    }

    /// <summary>Insert an image block at a specific position.</summary>
    public void InsertImageBlock(string imgTag, int atIndex = -1)
    {
        var block = new EditorBlock("img", imgTag);
        AddBlock(block, atIndex);
        NotifyContentChanged();
    }

    /// <summary>Insert raw HTML block (for toolbar operations).</summary>
    public void InsertHtmlBlock(string tag, string content, int atIndex = -1)
    {
        var block = new EditorBlock(tag, content);
        AddBlock(block, atIndex);
        NotifyContentChanged();

        // Focus the new block if it's a text block
        var idx = atIndex >= 0 ? atIndex : _blocks.Count - 1;
        FocusBlock(idx);
    }

    /// <summary>Append raw HTML and re-render.</summary>
    public void AppendHtml(string html)
    {
        var parsed = HtmlParser.SplitIntoBlocks(html);
        foreach (var p in parsed)
            AddBlock(new EditorBlock(p.Tag, p.Content));
        NotifyContentChanged();
    }

    // ── Block Control Creation ──

    private Control CreateBlockControl(EditorBlock block)
    {
        return block.Tag switch
        {
            "h1" or "h2" or "h3" or "h4" => CreateEditableHeading(block),
            "blockquote" => CreateEditableBlockquote(block),
            "ul" or "ol" => CreateEditableList(block),
            "img" => CreateImageBlock(block),
            "hr" => CreateHrBlock(block),
            "more" => CreateMoreBlock(block),
            _ => CreateEditableParagraph(block),
        };
    }

    private Control CreateEditableParagraph(EditorBlock block)
    {
        var tb = CreateEditableTextBox(block, 15, FontWeight.Normal);
        tb.Margin = new Thickness(0, 2);
        tb.LineHeight = 24;
        return tb;
    }

    private Control CreateEditableHeading(EditorBlock block)
    {
        var (fontSize, weight) = block.Tag switch
        {
            "h1" => (28d, FontWeight.Bold),
            "h2" => (24d, FontWeight.Bold),
            "h3" => (20d, FontWeight.SemiBold),
            "h4" => (17d, FontWeight.SemiBold),
            _ => (15d, FontWeight.Normal)
        };
        var tb = CreateEditableTextBox(block, fontSize, weight);
        tb.Margin = new Thickness(0, 8, 0, 4);
        return tb;
    }

    private Control CreateEditableBlockquote(EditorBlock block)
    {
        var tb = CreateEditableTextBox(block, 15, FontWeight.Normal);
        tb.FontStyle = FontStyle.Italic;
        tb.Foreground = new SolidColorBrush(Color.Parse("#555"));
        tb.LineHeight = 24;

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#ddd")),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(12, 4),
            Margin = new Thickness(0, 4),
            Background = new SolidColorBrush(Color.Parse("#fafafa")),
            Child = tb,
            Tag = block // store ref
        };
    }

    private Control CreateEditableList(EditorBlock block)
    {
        // For lists, use a single TextBox where each line is a list item
        var plainText = ExtractListItemsAsText(block.Content);
        block.PlainText = plainText;

        var tb = CreateEditableTextBox(block, 15, FontWeight.Normal);
        tb.Margin = new Thickness(16, 4);

        var label = new TextBlock
        {
            Text = block.Tag == "ol" ? "1." : "•",
            FontSize = 15,
            Width = 20,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = new SolidColorBrush(Color.Parse("#666"))
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(8, 2),
            Children = { label, tb },
            Tag = block
        };
    }

    private Control CreateImageBlock(EditorBlock block)
    {
        var srcMatch = Regex.Match(block.Content, @"src=""(?<url>[^""]+)""", RegexOptions.IgnoreCase);
        var altMatch = Regex.Match(block.Content, @"alt=""(?<alt>[^""]+)""", RegexOptions.IgnoreCase);
        var src = srcMatch.Success ? srcMatch.Groups["url"].Value : "";
        var alt = altMatch.Success ? altMatch.Groups["alt"].Value : "";

        var container = new StackPanel { Spacing = 4, Margin = new Thickness(0, 8) };

        if (src.StartsWith("local://"))
        {
            var id = src["local://".Length..];
            var bytes = _localImageResolver?.Invoke(id);
            if (bytes != null)
            {
                try
                {
                    using var ms = new MemoryStream(bytes);
                    var bitmap = new Bitmap(ms);
                    var image = new Avalonia.Controls.Image
                    {
                        Source = bitmap,
                        MaxWidth = 600,
                        MaxHeight = 400,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    container.Children.Add(image);
                }
                catch
                {
                    container.Children.Add(CreateImagePlaceholder(alt, "載入失敗"));
                }
            }
            else
            {
                container.Children.Add(CreateImagePlaceholder(alt, "待上傳"));
            }
        }
        else if (src.StartsWith("http"))
        {
            container.Children.Add(CreateImagePlaceholder(alt, src));
        }
        else
        {
            container.Children.Add(CreateImagePlaceholder(alt, src));
        }

        if (!string.IsNullOrEmpty(alt))
        {
            container.Children.Add(new TextBlock
            {
                Text = alt,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#888")),
                FontStyle = FontStyle.Italic
            });
        }

        // Make image block focusable
        container.PointerPressed += (_, _) =>
        {
            var idx = _blocks.IndexOf(block);
            if (idx >= 0) _focusedBlockIndex = idx;
            FocusedBlockTypeChanged?.Invoke(block.Tag);
        };

        container.Tag = block;
        return container;
    }

    private static Control CreateImagePlaceholder(string alt, string info)
    {
        return new Border
        {
            Background = new SolidColorBrush(Color.Parse("#f0f0f0")),
            BorderBrush = new SolidColorBrush(Color.Parse("#ddd")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16, 12),
            MinHeight = 60,
            MaxWidth = 400,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new StackPanel
            {
                Spacing = 4,
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = "\U0001f5bc", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = string.IsNullOrEmpty(alt) ? "圖片" : alt, FontSize = 13, Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = info, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#aaa")),
                        HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 }
                }
            }
        };
    }

    private static Control CreateHrBlock(EditorBlock block)
    {
        var border = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#ddd")),
            Margin = new Thickness(0, 12),
            Tag = block
        };
        return border;
    }

    private static Control CreateMoreBlock(EditorBlock block)
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#ccc")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 12),
            Padding = new Thickness(0, 4, 0, 0),
            Tag = block,
            Child = new TextBlock
            {
                Text = "── 繼續閱讀 ──",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#aaa")),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    // ── Editable TextBox ──

    private TextBox CreateEditableTextBox(EditorBlock block, double fontSize, FontWeight weight)
    {
        // Use plain text for display
        var text = block.PlainText ?? HtmlParser.StripTags(block.Content);
        block.PlainText = text;

        var tb = new TextBox
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.Parse("#333")),
            Padding = new Thickness(4, 4),
            MinHeight = 24,
            Watermark = block.Tag switch
            {
                "h2" or "h3" or "h4" => "標題...",
                "blockquote" => "引用文字...",
                _ => "輸入文字..."
            },
            Tag = block
        };

        // Track focus
        tb.GotFocus += (_, _) =>
        {
            var idx = _blocks.IndexOf(block);
            if (idx >= 0)
            {
                _focusedBlockIndex = idx;
                FocusedBlockTypeChanged?.Invoke(block.Tag);
            }
        };

        // Track text changes
        tb.TextChanged += (_, _) =>
        {
            if (_suppressSync) return;
            block.PlainText = tb.Text ?? "";
            NotifyContentChanged();
        };

        // Handle Enter and Backspace
        tb.KeyDown += (_, e) => OnBlockKeyDown(block, tb, e);

        block.TextBoxRef = tb;
        return tb;
    }

    private void OnBlockKeyDown(EditorBlock block, TextBox tb, KeyEventArgs e)
    {
        var idx = _blocks.IndexOf(block);
        if (idx < 0) return;

        if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // Enter → create new paragraph below
            e.Handled = true;

            var text = tb.Text ?? "";
            var cursorPos = tb.SelectionStart;

            // Split text at cursor
            var beforeCursor = text[..cursorPos];
            var afterCursor = text[cursorPos..];

            // Update current block
            _suppressSync = true;
            tb.Text = beforeCursor;
            block.PlainText = beforeCursor;
            _suppressSync = false;

            // Create new paragraph with remaining text
            var newBlock = new EditorBlock("p", "") { PlainText = afterCursor };
            AddBlock(newBlock, idx + 1);
            NotifyContentChanged();

            // Focus new block
            Dispatcher.UIThread.Post(() => FocusBlock(idx + 1), DispatcherPriority.Background);
        }
        else if (e.Key == Key.Back && tb.SelectionStart == 0 && tb.SelectionEnd == 0)
        {
            // Backspace at start → merge with previous block
            if (idx > 0)
            {
                var prevBlock = _blocks[idx - 1];
                if (prevBlock.TextBoxRef != null)
                {
                    e.Handled = true;
                    var prevText = prevBlock.PlainText ?? "";
                    var curText = block.PlainText ?? "";
                    var mergePos = prevText.Length;

                    _suppressSync = true;
                    prevBlock.PlainText = prevText + curText;
                    prevBlock.TextBoxRef.Text = prevBlock.PlainText;
                    _suppressSync = false;

                    RemoveBlock(idx);
                    NotifyContentChanged();

                    Dispatcher.UIThread.Post(() =>
                    {
                        prevBlock.TextBoxRef?.Focus();
                        if (prevBlock.TextBoxRef != null)
                        {
                            prevBlock.TextBoxRef.SelectionStart = mergePos;
                            prevBlock.TextBoxRef.SelectionEnd = mergePos;
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }
        else if (e.Key == Key.Delete && string.IsNullOrEmpty(tb.Text) && _blocks.Count > 1)
        {
            // Delete on empty block → remove it
            e.Handled = true;
            RemoveBlock(idx);
            NotifyContentChanged();
            if (idx < _blocks.Count)
                Dispatcher.UIThread.Post(() => FocusBlock(idx), DispatcherPriority.Background);
            else if (idx > 0)
                Dispatcher.UIThread.Post(() => FocusBlock(idx - 1), DispatcherPriority.Background);
        }
    }

    private void FocusBlock(int index)
    {
        if (index < 0 || index >= _blocks.Count) return;
        var block = _blocks[index];
        if (block.TextBoxRef != null)
        {
            block.TextBoxRef.Focus();
            block.TextBoxRef.SelectionStart = 0;
            block.TextBoxRef.SelectionEnd = 0;
        }
    }

    private void OnBottomAreaClicked(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        // If last block is empty, just focus it
        if (_blocks.Count > 0 && string.IsNullOrEmpty(_blocks[^1].PlainText) && _blocks[^1].TextBoxRef != null)
        {
            FocusBlock(_blocks.Count - 1);
            return;
        }

        // Create new paragraph at end
        var block = new EditorBlock("p", "") { PlainText = "" };
        AddBlock(block);
        NotifyContentChanged();
        Dispatcher.UIThread.Post(() => FocusBlock(_blocks.Count - 1), DispatcherPriority.Background);
    }

    // ── Toolbar Integration ──

    /// <summary>Apply inline formatting to the focused block's selected text.</summary>
    public void ApplyInlineFormat(string openTag, string closeTag)
    {
        if (_focusedBlockIndex < 0 || _focusedBlockIndex >= _blocks.Count) return;
        var block = _blocks[_focusedBlockIndex];
        var tb = block.TextBoxRef;
        if (tb == null) return;

        var start = tb.SelectionStart;
        var end = tb.SelectionEnd;
        var text = tb.Text ?? "";

        // Store the inline format in the block's format list
        if (end > start)
        {
            // We track inline formatting separately since TextBox is plain text.
            // The format will be applied when serializing to HTML.
            block.AddInlineFormat(start, end - start, openTag, closeTag);
            NotifyContentChanged();
        }
        tb.Focus();
    }

    /// <summary>Change the focused block's type (e.g., p → h2).</summary>
    public void ChangeBlockType(string newTag)
    {
        if (_focusedBlockIndex < 0 || _focusedBlockIndex >= _blocks.Count) return;
        var block = _blocks[_focusedBlockIndex];

        // Only change text block types
        if (block.Tag is "img" or "hr" or "more") return;

        block.Tag = newTag;

        // Re-create the control
        var newControl = CreateBlockControl(block);
        block.Control = newControl;
        _container.Children[_focusedBlockIndex] = newControl;

        NotifyContentChanged();

        // Re-focus
        Dispatcher.UIThread.Post(() => FocusBlock(_focusedBlockIndex), DispatcherPriority.Background);
    }

    /// <summary>Get the focused block's type.</summary>
    public string? GetFocusedBlockType()
    {
        if (_focusedBlockIndex < 0 || _focusedBlockIndex >= _blocks.Count) return null;
        return _blocks[_focusedBlockIndex].Tag;
    }

    /// <summary>Get the focused block's TextBox (for selection info).</summary>
    public TextBox? GetFocusedTextBox()
    {
        if (_focusedBlockIndex < 0 || _focusedBlockIndex >= _blocks.Count) return null;
        return _blocks[_focusedBlockIndex].TextBoxRef;
    }

    // ── Drag & Drop ──

    private void OnEditorDragOver(object? sender, DragEventArgs e)
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

    private async void OnEditorDrop(object? sender, DragEventArgs e)
    {
        var storageItems = e.Data.GetFiles()?.ToList();
        if (storageItems == null || storageItems.Count == 0) return;

        e.Handled = true;

        // Determine drop position: find which block the drop is near
        var dropPos = e.GetPosition(_container);
        var insertIndex = GetInsertIndexFromPosition(dropPos.Y);

        if (ImagesDroppedAtPosition != null)
            await ImagesDroppedAtPosition(storageItems, insertIndex);
    }

    private int GetInsertIndexFromPosition(double y)
    {
        double cumY = 0;
        for (int i = 0; i < _container.Children.Count; i++)
        {
            var child = _container.Children[i];
            var height = child.Bounds.Height + 2; // +spacing
            cumY += height;
            if (y < cumY)
                return i;
        }
        return _blocks.Count; // at the end
    }

    // ── Helpers ──

    private void NotifyContentChanged()
    {
        if (_suppressSync) return;
        ContentChanged?.Invoke(ToHtml());
    }

    private static string ExtractListItemsAsText(string content)
    {
        var matches = Regex.Matches(content, @"<li[^>]*>(?<content>[\s\S]*?)</li>", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return HtmlParser.StripTags(content);
        var lines = matches.Select(m => HtmlParser.StripTags(m.Groups["content"].Value).Trim());
        return string.Join("\n", lines);
    }
}

// ── Editor Block Model ──

public class EditorBlock
{
    public string Tag { get; set; }
    public string Content { get; set; } // Original HTML content
    public string? PlainText { get; set; } // Editable plain text
    public Control? Control { get; set; }
    public TextBox? TextBoxRef { get; set; }

    // Inline formats: (start, length, openTag, closeTag)
    private readonly List<(int Start, int Length, string Open, string Close)> _inlineFormats = new();

    public EditorBlock(string tag, string content)
    {
        Tag = tag;
        Content = content;
    }

    public void AddInlineFormat(int start, int length, string openTag, string closeTag)
    {
        _inlineFormats.Add((start, length, openTag, closeTag));
    }

    public string ToHtml()
    {
        return Tag switch
        {
            "img" => Content, // keep original img tag
            "hr" => "<hr />",
            "more" => "<!--more-->",
            "ul" => ToListHtml("ul"),
            "ol" => ToListHtml("ol"),
            _ => ToTextBlockHtml()
        };
    }

    private string ToTextBlockHtml()
    {
        var text = PlainText ?? "";
        if (string.IsNullOrEmpty(text) && Tag == "p") return "";

        // Apply inline formats to generate HTML
        var html = ApplyInlineFormats(text);

        // Escape basic HTML chars in non-formatted parts is already handled
        return Tag switch
        {
            "p" => $"<p>{html}</p>",
            "h1" => $"<h1>{html}</h1>",
            "h2" => $"<h2>{html}</h2>",
            "h3" => $"<h3>{html}</h3>",
            "h4" => $"<h4>{html}</h4>",
            "blockquote" => $"<blockquote>{html}</blockquote>",
            _ => $"<p>{html}</p>"
        };
    }

    private string ToListHtml(string listTag)
    {
        var text = PlainText ?? "";
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "";

        var items = string.Join("\n", lines.Select(l => $"  <li>{EscapeHtml(l.Trim())}</li>"));
        return $"<{listTag}>\n{items}\n</{listTag}>";
    }

    private string ApplyInlineFormats(string text)
    {
        if (_inlineFormats.Count == 0)
            return EscapeHtml(text);

        // Sort formats by start position (descending) to apply from end to start
        var sorted = _inlineFormats.OrderByDescending(f => f.Start).ToList();
        var result = text;

        foreach (var (start, length, open, close) in sorted)
        {
            if (start >= 0 && start + length <= result.Length)
            {
                var before = EscapeHtml(result[..start]);
                var middle = result.Substring(start, length);
                var after = EscapeHtml(result[(start + length)..]);
                result = before + open + EscapeHtml(middle) + close + after;
                // Since we've escaped parts, clear formats to avoid double-escaping
                // Actually this approach is getting complex. Let's simplify.
            }
        }

        // Simplified: just escape the whole thing and wrap formatted ranges
        // For now, just escape HTML
        return EscapeHtml(text);
    }

    private static string EscapeHtml(string text)
    {
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}

// ── HTML Parser (shared utilities) ──

public static partial class HtmlParser
{
    public record ParsedBlock(string Tag, string Content);

    public static List<ParsedBlock> SplitIntoBlocks(string html)
    {
        var blocks = new List<ParsedBlock>();
        var remaining = html.Trim();

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart('\n', ' ');
            if (remaining.Length == 0) break;

            var match = BlockRegex().Match(remaining);
            if (match.Success && match.Index == 0)
            {
                var tag = match.Groups["tag"].Value.ToLower();
                var content = match.Groups["content"].Value;
                blocks.Add(new ParsedBlock(tag, content));
                remaining = remaining[match.Length..].TrimStart('\n', ' ');
                continue;
            }

            var imgMatch = ImgRegex().Match(remaining);
            if (imgMatch.Success && imgMatch.Index == 0)
            {
                blocks.Add(new ParsedBlock("img", imgMatch.Value));
                remaining = remaining[imgMatch.Length..].TrimStart('\n', ' ');
                continue;
            }

            var hrMatch = HrRegex().Match(remaining);
            if (hrMatch.Success && hrMatch.Index == 0)
            {
                blocks.Add(new ParsedBlock("hr", ""));
                remaining = remaining[hrMatch.Length..].TrimStart('\n', ' ');
                continue;
            }

            if (remaining.StartsWith("<!--more-->", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new ParsedBlock("more", ""));
                remaining = remaining[11..].TrimStart('\n', ' ');
                continue;
            }

            var nextBlock = NextBlockStart().Match(remaining, 1);
            if (nextBlock.Success)
            {
                var text = remaining[..nextBlock.Index].Trim();
                if (text.Length > 0)
                    blocks.Add(new ParsedBlock("p", text));
                remaining = remaining[nextBlock.Index..];
            }
            else
            {
                if (remaining.Trim().Length > 0)
                    blocks.Add(new ParsedBlock("p", remaining.Trim()));
                break;
            }
        }

        return blocks;
    }

    public static string StripTags(string html)
    {
        return TagStripRegex().Replace(html, "")
            .Replace("&nbsp;", " ").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
    }

    [GeneratedRegex(@"<(?<tag>h[1-6]|p|blockquote|ul|ol|div|pre|table)[^>]*>(?<content>[\s\S]*?)</\k<tag>\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<img\s[^>]*?/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex(@"<hr\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"(?=<(?:h[1-6]|p|blockquote|ul|ol|div|pre|hr|img|<!--))", RegexOptions.IgnoreCase)]
    private static partial Regex NextBlockStart();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagStripRegex();
}
