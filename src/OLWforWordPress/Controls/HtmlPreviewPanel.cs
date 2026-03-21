using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace OLWforWordPress.Controls;

/// <summary>
/// Simple HTML preview renderer using native Avalonia controls.
/// Handles common blog HTML: headings, paragraphs, images, lists, blockquotes, hr, etc.
/// </summary>
public partial class HtmlPreviewPanel : UserControl
{
    private readonly ScrollViewer _scrollViewer;
    private readonly StackPanel _container;
    private Func<string, byte[]?>? _localImageResolver;

    public HtmlPreviewPanel()
    {
        _container = new StackPanel { Spacing = 8, Margin = new Thickness(16, 12) };
        _scrollViewer = new ScrollViewer { Content = _container };
        Content = _scrollViewer;
    }

    public void SetLocalImageResolver(Func<string, byte[]?> resolver)
    {
        _localImageResolver = resolver;
    }

    public void RenderHtml(string? html)
    {
        _container.Children.Clear();

        if (string.IsNullOrWhiteSpace(html))
        {
            _container.Children.Add(new TextBlock
            {
                Text = "（尚無內容）",
                Foreground = Brushes.Gray,
                FontStyle = FontStyle.Italic,
                Margin = new Thickness(0, 20)
            });
            return;
        }

        // Normalize line breaks
        html = html.Replace("\r\n", "\n").Replace("\r", "\n");

        // Split by block-level elements
        var blocks = SplitIntoBlocks(html);
        foreach (var block in blocks)
        {
            var control = RenderBlock(block);
            if (control != null)
                _container.Children.Add(control);
        }
    }

    private List<HtmlBlock> SplitIntoBlocks(string html)
    {
        var blocks = new List<HtmlBlock>();
        var remaining = html.Trim();

        while (remaining.Length > 0)
        {
            remaining = remaining.TrimStart('\n', ' ');
            if (remaining.Length == 0) break;

            // Check for block elements
            var match = BlockRegex().Match(remaining);
            if (match.Success && match.Index == 0)
            {
                var tag = match.Groups["tag"].Value.ToLower();
                var content = match.Groups["content"].Value;
                blocks.Add(new HtmlBlock(tag, content));
                remaining = remaining[match.Length..].TrimStart('\n', ' ');
                continue;
            }

            // Check for self-closing / void elements
            var imgMatch = ImgRegex().Match(remaining);
            if (imgMatch.Success && imgMatch.Index == 0)
            {
                blocks.Add(new HtmlBlock("img", imgMatch.Value));
                remaining = remaining[imgMatch.Length..].TrimStart('\n', ' ');
                continue;
            }

            var hrMatch = HrRegex().Match(remaining);
            if (hrMatch.Success && hrMatch.Index == 0)
            {
                blocks.Add(new HtmlBlock("hr", ""));
                remaining = remaining[hrMatch.Length..].TrimStart('\n', ' ');
                continue;
            }

            // Check for <!--more-->
            if (remaining.StartsWith("<!--more-->", StringComparison.OrdinalIgnoreCase))
            {
                blocks.Add(new HtmlBlock("more", ""));
                remaining = remaining[11..].TrimStart('\n', ' ');
                continue;
            }

            // Plain text until next block element
            var nextBlock = NextBlockStart().Match(remaining, 1);
            if (nextBlock.Success)
            {
                var text = remaining[..nextBlock.Index].Trim();
                if (text.Length > 0)
                    blocks.Add(new HtmlBlock("p", text));
                remaining = remaining[nextBlock.Index..];
            }
            else
            {
                if (remaining.Trim().Length > 0)
                    blocks.Add(new HtmlBlock("p", remaining.Trim()));
                break;
            }
        }

        return blocks;
    }

    private Control? RenderBlock(HtmlBlock block)
    {
        return block.Tag switch
        {
            "h1" => CreateHeading(block.Content, 28, FontWeight.Bold),
            "h2" => CreateHeading(block.Content, 24, FontWeight.Bold),
            "h3" => CreateHeading(block.Content, 20, FontWeight.SemiBold),
            "h4" => CreateHeading(block.Content, 17, FontWeight.SemiBold),
            "blockquote" => CreateBlockquote(block.Content),
            "ul" => CreateList(block.Content, ordered: false),
            "ol" => CreateList(block.Content, ordered: true),
            "img" => CreateImage(block.Content),
            "hr" => CreateHr(),
            "more" => CreateMoreTag(),
            "p" or _ => CreateParagraph(block.Content),
        };
    }

    private Control CreateHeading(string content, double fontSize, FontWeight weight)
    {
        var tb = CreateFormattedTextBlock(StripTags(content));
        tb.FontSize = fontSize;
        tb.FontWeight = weight;
        tb.Margin = new Thickness(0, 8, 0, 4);
        return tb;
    }

    private Control CreateParagraph(string content)
    {
        content = content.Trim();
        if (string.IsNullOrEmpty(content)) return new Border { Height = 8 };

        // Check if it's just an img tag
        if (ImgRegex().IsMatch(content) && StripTags(content).Trim().Length == 0)
            return CreateImage(content);

        var tb = CreateFormattedTextBlock(content);
        tb.FontSize = 15;
        tb.LineHeight = 24;
        tb.Margin = new Thickness(0, 2);
        return tb;
    }

    private Control CreateBlockquote(string content)
    {
        var tb = CreateFormattedTextBlock(StripTags(content));
        tb.FontSize = 15;
        tb.FontStyle = FontStyle.Italic;
        tb.Foreground = new SolidColorBrush(Color.Parse("#555"));
        tb.LineHeight = 24;

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#ddd")),
            BorderThickness = new Thickness(4, 0, 0, 0),
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 4),
            Background = new SolidColorBrush(Color.Parse("#fafafa")),
            Child = tb
        };
    }

    private Control CreateList(string content, bool ordered)
    {
        var items = ListItemRegex().Matches(content);
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(16, 4) };
        var index = 1;

        foreach (Match item in items)
        {
            var text = StripTags(item.Groups["content"].Value).Trim();
            var bullet = ordered ? $"{index}." : "•";
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = bullet, FontSize = 15, Width = 20 },
                    new TextBlock { Text = text, FontSize = 15, TextWrapping = TextWrapping.Wrap }
                }
            };
            panel.Children.Add(row);
            index++;
        }

        if (panel.Children.Count == 0)
        {
            // No <li> found, just show as text
            return CreateParagraph(content);
        }

        return panel;
    }

    private Control? CreateImage(string imgHtml)
    {
        var srcMatch = SrcRegex().Match(imgHtml);
        if (!srcMatch.Success) return null;

        var src = srcMatch.Groups["url"].Value;
        var altMatch = AltRegex().Match(imgHtml);
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
                    container.Children.Add(new TextBlock
                    {
                        Text = $"[圖片載入失敗: {alt}]",
                        Foreground = Brushes.Red,
                        FontSize = 13
                    });
                }
            }
            else
            {
                container.Children.Add(CreateImagePlaceholder(alt, "待上傳"));
            }
        }
        else if (src.StartsWith("http"))
        {
            // For remote images, show a placeholder with URL
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
                    new TextBlock { Text = "🖼", FontSize = 24, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = string.IsNullOrEmpty(alt) ? "圖片" : alt, FontSize = 13, Foreground = Brushes.Gray,
                        HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = info, FontSize = 11, Foreground = new SolidColorBrush(Color.Parse("#aaa")),
                        HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 360 }
                }
            }
        };
    }

    private static Control CreateHr()
    {
        return new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.Parse("#ddd")),
            Margin = new Thickness(0, 12)
        };
    }

    private static Control CreateMoreTag()
    {
        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#ccc")),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 12),
            Padding = new Thickness(0, 4, 0, 0),
            Child = new TextBlock
            {
                Text = "── 繼續閱讀 ──",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#aaa")),
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
    }

    private static TextBlock CreateFormattedTextBlock(string content)
    {
        // Strip inline HTML for simple display
        content = content
            .Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n")
            .Replace("&nbsp;", " ").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"");

        content = StripInlineTags(content);

        return new TextBlock
        {
            Text = content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#333"))
        };
    }

    private static string StripTags(string html)
    {
        return TagStripRegex().Replace(html, "")
            .Replace("&nbsp;", " ").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"");
    }

    private static string StripInlineTags(string html)
    {
        return InlineTagRegex().Replace(html, "");
    }

    // ── Regex patterns ──

    [GeneratedRegex(@"<(?<tag>h[1-6]|p|blockquote|ul|ol|div|pre|table)[^>]*>(?<content>[\s\S]*?)</\k<tag>\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<img\s[^>]*?/?>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex(@"<hr\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex HrRegex();

    [GeneratedRegex(@"(?=<(?:h[1-6]|p|blockquote|ul|ol|div|pre|hr|img|<!--))", RegexOptions.IgnoreCase)]
    private static partial Regex NextBlockStart();

    [GeneratedRegex(@"<li[^>]*>(?<content>[\s\S]*?)</li>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"src=""(?<url>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex SrcRegex();

    [GeneratedRegex(@"alt=""(?<alt>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex AltRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagStripRegex();

    [GeneratedRegex(@"</?(?:strong|em|b|i|u|s|span|a|code|mark|sub|sup)[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex InlineTagRegex();

    private record HtmlBlock(string Tag, string Content);
}
