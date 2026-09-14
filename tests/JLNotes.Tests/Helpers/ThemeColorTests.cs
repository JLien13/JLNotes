using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using JLNotes.Helpers;

namespace JLNotes.Tests.Helpers;

/// <summary>
/// Note body colors must come from the active theme dictionary, never from
/// hardcoded dark-theme hex values (which are unreadable on the light theme).
/// The RichTextBox host stands in for the app-level resource dictionary.
/// </summary>
public class ThemeColorTests
{
    private const string AttachDir = @"C:\nonexistent-attachments";

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    private static RichTextBox HostWithTheme(FlowDocument doc, Color text, Color accent, Color border)
    {
        var host = new RichTextBox();
        host.Resources["TextPrimaryBrush"] = new SolidColorBrush(text);
        host.Resources["AccentBlueBrush"] = new SolidColorBrush(accent);
        host.Resources["BorderSubtleBrush"] = new SolidColorBrush(border);
        host.Document = doc;
        return host;
    }

    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;

    [Fact]
    public void BodyText_UsesThemeTextPrimaryBrush()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("plain body text", AttachDir);
            HostWithTheme(doc, Colors.Red, Colors.Green, Colors.Blue);
            Assert.Equal(Colors.Red, ColorOf(doc.Foreground));
        });
    }

    [Fact]
    public void Hyperlink_UsesThemeAccentBrush()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("see https://example.com now", AttachDir);
            HostWithTheme(doc, Colors.Red, Colors.Green, Colors.Blue);
            var link = doc.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<Hyperlink>()).Single();
            Assert.Equal(Colors.Green, ColorOf(link.Foreground));
        });
    }

    [Fact]
    public void MissingAttachmentLink_UsesThemeAccentBrush()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("file {{report.pdf}} here", AttachDir);
            HostWithTheme(doc, Colors.Red, Colors.Green, Colors.Blue);
            var link = doc.Blocks.OfType<Paragraph>()
                .SelectMany(p => p.Inlines.OfType<InlineUIContainer>())
                .Select(c => c.Child).OfType<TextBlock>().Single();
            Assert.Equal(Colors.Green, ColorOf(link.Foreground));
        });
    }
}
