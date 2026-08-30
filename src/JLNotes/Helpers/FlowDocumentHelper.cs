using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JLNotes.Helpers;

public static class FlowDocumentHelper
{
    private static readonly Regex AttachmentTokenRegex = new(@"\{\{(.+?)\}\}", RegexOptions.Compiled);
    // Scheme'd URLs plus bare www. domains ("www.corvascular.com").
    private static readonly Regex UrlRegex = new(@"(?:https?://|www\.)\S+", RegexOptions.Compiled);
    // Task-list line: optional indent, "- [ ]" / "- [x]", optional trailing space.
    private static readonly Regex CheckboxLineRegex = new(@"^(\s*)- \[( |x|X)\] ?", RegexOptions.Compiled);
    private static readonly BrushConverter BrushConverter = new();
    private static readonly Brush AccentBlueBrush = (Brush)BrushConverter.ConvertFromString("#4a9eff")!;
    private static readonly Brush ForegroundBrush = (Brush)BrushConverter.ConvertFromString("#e0e0e0")!;
    private static readonly Brush ThumbnailBorderBrush = (Brush)BrushConverter.ConvertFromString("#2a2a4a")!;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    // Display cap for inline thumbnails (DIP); decode at up to 3x for hi-DPI crispness.
    private const double ThumbMaxWidth = 300;
    private const double ThumbMaxHeight = 220;
    private const int ThumbDecodeWidth = 900;

    public static FlowDocument BuildDocument(string bodyText, string attachmentsDir)
    {
        var doc = new FlowDocument
        {
            Background = Brushes.Transparent,
            Foreground = ForegroundBrush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            PagePadding = new Thickness(0)
        };

        if (string.IsNullOrEmpty(bodyText))
        {
            doc.Blocks.Add(new Paragraph());
            return doc;
        }

        // Split the body text by attachment tokens, keeping the captures
        var parts = AttachmentTokenRegex.Split(bodyText);
        var matches = AttachmentTokenRegex.Matches(bodyText);

        // parts[0] is text before first match, parts[1] is first capture group,
        // parts[2] is text between first and second match, etc.
        // Even indices are plain text, odd indices are captured filenames.

        // We need to build paragraphs. Plain text may contain \n which means new paragraphs.
        // Attachment tokens are inline elements within the current paragraph.

        var currentParagraph = new Paragraph();
        doc.Blocks.Add(currentParagraph);

        for (int i = 0; i < parts.Length; i++)
        {
            if (i % 2 == 0)
            {
                // Plain text segment — split on newlines for paragraph breaks
                var textLines = parts[i].Split('\n');
                for (int j = 0; j < textLines.Length; j++)
                {
                    if (j > 0)
                    {
                        // New paragraph for each \n
                        currentParagraph = new Paragraph();
                        doc.Blocks.Add(currentParagraph);
                    }

                    if (textLines[j].Length > 0)
                    {
                        AddTextLine(currentParagraph, textLines[j]);
                    }
                }
            }
            else
            {
                // Attachment filename — create inline UI element
                var filename = parts[i];
                var container = CreateAttachmentInline(filename, attachmentsDir);
                currentParagraph.Inlines.Add(container);
            }
        }

        return doc;
    }

    public static string SerializeDocument(FlowDocument doc)
    {
        var sb = new StringBuilder();
        var isFirstParagraph = true;

        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                if (!isFirstParagraph)
                    sb.Append('\n');
                isFirstParagraph = false;

                foreach (var inline in paragraph.Inlines)
                    AppendInlineText(sb, inline);
            }
        }

        return sb.ToString();
    }

    private static void AppendInlineText(StringBuilder sb, Inline inline)
    {
        if (inline is Run run)
        {
            sb.Append(run.Text);
        }
        else if (inline is InlineUIContainer container)
        {
            if (container.Child is CheckBox checkBox)
            {
                // Task checkbox: live IsChecked state writes back as markdown.
                sb.Append(checkBox.Tag as string ?? "");
                sb.Append(checkBox.IsChecked == true ? "- [x] " : "- [ ] ");
            }
            else if (container.Child is FrameworkElement element && element.Tag is string tagValue)
            {
                sb.Append("{{");
                sb.Append(tagValue);
                sb.Append("}}");
            }
        }
        else if (inline is Span span)
        {
            // Hyperlinks (and any other Span) serialize as their plain text.
            foreach (var child in span.Inlines)
                AppendInlineText(sb, child);
        }
    }

    public static List<string> GetAttachmentFilenames(FlowDocument doc)
    {
        var filenames = new List<string>();

        foreach (var block in doc.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    // CheckBox containers carry a string Tag too (their indent) --
                    // they are task checkboxes, not attachments.
                    if (inline is InlineUIContainer container &&
                        container.Child is FrameworkElement element &&
                        element is not CheckBox &&
                        element.Tag is string tagValue)
                    {
                        filenames.Add(tagValue);
                    }
                }
            }
        }

        return filenames;
    }

    public static void InsertAttachment(RichTextBox richTextBox, string filename, string attachmentsDir)
    {
        var container = CreateAttachmentInline(filename, attachmentsDir);
        var caretPosition = richTextBox.CaretPosition;

        // Insert the InlineUIContainer at the caret position
        if (caretPosition.Parent is Run run)
        {
            // Split the run at the caret position
            var inlineCollection = ((Paragraph)run.Parent!).Inlines;
            var textBefore = new TextRange(run.ContentStart, caretPosition).Text;
            var textAfter = new TextRange(caretPosition, run.ContentEnd).Text;

            inlineCollection.InsertBefore(run, new Run(textBefore));
            inlineCollection.InsertBefore(run, container);
            inlineCollection.InsertBefore(run, new Run(textAfter));
            inlineCollection.Remove(run);
        }
        else if (caretPosition.Parent is Paragraph paragraph)
        {
            paragraph.Inlines.Add(container);
        }
        else
        {
            // Fallback: insert at the end of the document
            var lastBlock = richTextBox.Document.Blocks.LastBlock;
            if (lastBlock is Paragraph lastParagraph)
            {
                lastParagraph.Inlines.Add(container);
            }
            else
            {
                var newParagraph = new Paragraph();
                newParagraph.Inlines.Add(container);
                richTextBox.Document.Blocks.Add(newParagraph);
            }
        }

        // Move caret after the inserted element
        richTextBox.CaretPosition = container.ElementEnd;
    }

    /// <summary>One text line into a paragraph: a leading "- [ ]"/"- [x]" becomes a
    /// live checkbox (only at true line start), and URLs become Ctrl+Click hyperlinks.</summary>
    private static void AddTextLine(Paragraph paragraph, string text)
    {
        if (paragraph.Inlines.Count == 0)
        {
            var m = CheckboxLineRegex.Match(text);
            if (m.Success)
            {
                var isChecked = m.Groups[2].Value is "x" or "X";
                paragraph.Inlines.Add(CreateCheckboxInline(m.Groups[1].Value, isChecked));
                text = text[m.Length..];
            }
        }
        AddTextWithLinks(paragraph, text);
    }

    private static InlineUIContainer CreateCheckboxInline(string indent, bool isChecked)
    {
        var checkBox = new CheckBox
        {
            IsChecked = isChecked,
            Tag = indent, // original leading whitespace, restored on serialize
            Margin = new Thickness(indent.Length * 7, 0, 5, 0),
            Cursor = Cursors.Hand
        };
        return new InlineUIContainer(checkBox) { BaselineAlignment = BaselineAlignment.Center };
    }

    private static void AddTextWithLinks(Paragraph paragraph, string text)
    {
        if (text.Length == 0) return;

        var pos = 0;
        foreach (Match m in UrlRegex.Matches(text))
        {
            var url = TrimUrlEnd(m.Value);
            if (url.Length == 0) continue;
            if (m.Index > pos)
                paragraph.Inlines.Add(new Run(text[pos..m.Index]));
            paragraph.Inlines.Add(CreateHyperlink(url));
            pos = m.Index + url.Length;
        }
        if (pos < text.Length)
            paragraph.Inlines.Add(new Run(text[pos..]));
    }

    // \S+ grabs trailing punctuation ("see https://x.com." / "(https://x.com)") --
    // peel it off so the link target is the bare URL. A ')' stays only while it
    // balances a '(' inside the URL (e.g. wikipedia's "Foo_(bar)").
    private static string TrimUrlEnd(string url)
    {
        while (url.Length > 0)
        {
            var last = url[^1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '\'' or '"' or '>' ||
                (last == ')' && url.Count(c => c == ')') > url.Count(c => c == '(')))
                url = url[..^1];
            else
                break;
        }
        return url;
    }

    private static Hyperlink CreateHyperlink(string url)
    {
        var link = new Hyperlink(new Run(url))
        {
            Foreground = AccentBlueBrush,
            ToolTip = url + Environment.NewLine + "Ctrl+Click to open",
            Cursor = Cursors.Hand
        };
        // Fires on Ctrl+Click inside an editable RichTextBox (IsDocumentEnabled);
        // a plain click keeps placing the caret for editing.
        link.Click += (_, _) => OpenUrl(url);

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open link" };
        open.Click += (_, _) => OpenUrl(url);
        var copy = new MenuItem { Header = "Copy link" };
        copy.Click += (_, _) => { try { Clipboard.SetText(url); } catch { } };
        menu.Items.Add(open);
        menu.Items.Add(copy);
        link.ContextMenu = menu;
        return link;
    }

    private static void OpenUrl(string url)
    {
        if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            url = "https://" + url;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private static InlineUIContainer CreateAttachmentInline(string filename, string attachmentsDir)
    {
        var filePath = Path.Combine(attachmentsDir, filename);

        if (File.Exists(filePath) && ImageExtensions.Contains(Path.GetExtension(filename)))
        {
            try
            {
                return new InlineUIContainer(CreateThumbnail(filename, filePath));
            }
            catch
            {
                // Unreadable/corrupt image — fall through to the link style
            }
        }

        return new InlineUIContainer(CreateLink(filename));
    }

    private static Border CreateThumbnail(string filename, string filePath)
    {
        // Header-only read of the natural width so small images aren't upscaled by DecodePixelWidth.
        int decodeWidth;
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            decodeWidth = Math.Min(frame.PixelWidth, ThumbDecodeWidth);
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad; // release the file handle so save/cleanup can delete it
        bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
        bitmap.DecodePixelWidth = decodeWidth;
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            MaxWidth = ThumbMaxWidth,
            MaxHeight = ThumbMaxHeight,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        // Tooltip and click are handled at the RichTextBox level (RichTextBoxBehavior)
        // because RichTextBox in edit mode intercepts mouse events before they reach inline UIElements.
        // The Tag property is what marks this as an attachment element.
        return new Border
        {
            Child = image,
            Tag = filename,
            ToolTip = filename,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 4, 0, 2),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            BorderBrush = ThumbnailBorderBrush,
            Background = Brushes.Transparent
        };
    }

    private static TextBlock CreateLink(string filename)
    {
        return new TextBlock
        {
            Text = filename,
            Foreground = AccentBlueBrush,
            TextDecorations = TextDecorations.Underline,
            Cursor = Cursors.Hand,
            FontSize = 12,
            FontFamily = new FontFamily("Segoe UI"),
            Tag = filename
        };
    }
}
