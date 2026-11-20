using System.Windows.Controls;
using System.Windows.Documents;
using JLNotes.Helpers;

namespace JLNotes.Tests.Helpers;

public class LinkAndCheckboxTests
{
    private const string AttachDir = @"C:\nonexistent-attachments";

    // CheckBox / ContextMenu construction requires STA; xUnit runs MTA.
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception e) { failure = e; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    private static string RoundTrip(string body) =>
        FlowDocumentHelper.SerializeDocument(FlowDocumentHelper.BuildDocument(body, AttachDir));

    private static List<CheckBox> CheckboxesOf(FlowDocument doc) =>
        doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<InlineUIContainer>())
            .Select(c => c.Child).OfType<CheckBox>().ToList();

    private static List<Hyperlink> HyperlinksOf(FlowDocument doc) =>
        doc.Blocks.OfType<Paragraph>()
            .SelectMany(p => p.Inlines.OfType<Hyperlink>()).ToList();

    [Fact]
    public void PlainText_RoundTripsUnchanged()
    {
        Sta(() =>
        {
            const string body = "line one\nline two\n\nline four";
            Assert.Equal(body, RoundTrip(body));
        });
    }

    [Fact]
    public void Url_BecomesHyperlink_AndRoundTrips()
    {
        Sta(() =>
        {
            const string body = "see https://github.com/JLien13/JLNotes for code";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);

            var links = HyperlinksOf(doc);
            Assert.Single(links);
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void Url_TrailingPunctuation_StaysOutsideLink()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("go to https://example.com/x.", AttachDir);
            var link = Assert.Single(HyperlinksOf(doc));
            Assert.Equal("https://example.com/x", new TextRange(link.ContentStart, link.ContentEnd).Text);
            Assert.Equal("go to https://example.com/x.", FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void Url_InParens_KeepsBalancedParenOnly()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("(see https://en.wikipedia.org/wiki/Foo_(bar))", AttachDir);
            var link = Assert.Single(HyperlinksOf(doc));
            Assert.Equal("https://en.wikipedia.org/wiki/Foo_(bar)",
                new TextRange(link.ContentStart, link.ContentEnd).Text);
        });
    }

    [Fact]
    public void BareWwwDomain_BecomesHyperlink_AndRoundTrips()
    {
        Sta(() =>
        {
            const string body = "visit www.corvascular.com today";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);
            var link = Assert.Single(HyperlinksOf(doc));
            Assert.Equal("www.corvascular.com", new TextRange(link.ContentStart, link.ContentEnd).Text);
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void CheckboxLines_RenderAsCheckboxes_AndRoundTrip()
    {
        Sta(() =>
        {
            const string body = "- [ ] open task\n- [x] done task\nplain line";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);

            var boxes = CheckboxesOf(doc);
            Assert.Equal(2, boxes.Count);
            Assert.False(boxes[0].IsChecked);
            Assert.True(boxes[1].IsChecked);
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void CheckboxToggle_WritesBackToMarkdown()
    {
        Sta(() =>
        {
            var doc = FlowDocumentHelper.BuildDocument("- [ ] task", AttachDir);
            CheckboxesOf(doc)[0].IsChecked = true;
            Assert.Equal("- [x] task", FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void IndentedCheckbox_PreservesIndent()
    {
        Sta(() =>
        {
            const string body = "  - [ ] nested task";
            Assert.Equal(body, RoundTrip(body));
        });
    }

    [Fact]
    public void CheckboxSyntax_MidLine_StaysPlainText()
    {
        Sta(() =>
        {
            const string body = "not a task - [ ] mid-line";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);
            Assert.Empty(CheckboxesOf(doc));
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void TokenWithNoFile_IsPlainText_AndRoundTrips()
    {
        Sta(() =>
        {
            const string body = "Dear {{companyName}}, see {{{path}} and {{ opacity: 1 }}";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);
            Assert.Empty(doc.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines.OfType<InlineUIContainer>()));
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    private static JLNotes.Services.NoteIndex IndexOf(params string[] fileNames)
    {
        var index = new JLNotes.Services.NoteIndex();
        index.Refresh(fileNames.Select(f => new JLNotes.Models.Note { Title = f, FilePath = @"C:\notes\" + f + ".md" }).ToList());
        return index;
    }

    [Fact]
    public void NoteLink_BecomesHyperlink_AndRoundTripsUnchanged()
    {
        Sta(() =>
        {
            const string body = "see [[2026-01-02-b]] and [[2026-01-03-c|C]] plus https://example.com";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir, IndexOf("2026-01-02-b", "2026-01-03-c"));

            Assert.Equal(3, HyperlinksOf(doc).Count);
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void NoteLink_WithoutAnIndex_StaysText()
    {
        Sta(() =>
        {
            const string body = "see [[2026-01-02-b]]";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);

            Assert.Empty(HyperlinksOf(doc));
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }

    [Fact]
    public void NoteLink_Click_AsksForThatNote()
    {
        Sta(() =>
        {
            var index = IndexOf("2026-01-02-b");
            var opened = new List<string>();
            index.OpenRequested += n => opened.Add(n.Title);
            var doc = FlowDocumentHelper.BuildDocument("[[2026-01-02-b]]", AttachDir, index);

            HyperlinksOf(doc).Single().RaiseEvent(new System.Windows.RoutedEventArgs(Hyperlink.ClickEvent));

            Assert.Equal(["2026-01-02-b"], opened);
        });
    }

    [Fact]
    public void UrlOnCheckboxLine_GetsBoth()
    {
        Sta(() =>
        {
            const string body = "- [ ] read https://example.com/doc";
            var doc = FlowDocumentHelper.BuildDocument(body, AttachDir);
            Assert.Single(CheckboxesOf(doc));
            Assert.Single(HyperlinksOf(doc));
            Assert.Equal(body, FlowDocumentHelper.SerializeDocument(doc));
        });
    }
}
