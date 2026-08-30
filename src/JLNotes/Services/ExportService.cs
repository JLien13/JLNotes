using System.IO;
using System.Windows.Media.Imaging;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JLNotes.Helpers;
using JLNotes.Models;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace JLNotes.Services;

public static class ExportService
{
    private const string FontName = "Segoe UI";
    private const string GrayHex = "6B7280";
    private const string LightGrayHex = "D1D5DB";
    private const string AccentBlueHex = "4A9EFF";
    private const string DarkHex = "1F2937";

    // getAttachmentsDir: NoteService.GetAttachmentsDir -- the app's single
    // authority for where a note's attachments live.
    public static void ExportToWord(IReadOnlyList<Note> notes, string outputPath,
        Func<Note, string> getAttachmentsDir)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        // Set default font for the document
        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(
                    new RunPropertiesBaseStyle(
                        new RunFonts { Ascii = FontName, HighAnsi = FontName },
                        new FontSize { Val = "22" },
                        new Color { Val = DarkHex })),
                new ParagraphPropertiesDefault(
                    new ParagraphPropertiesBaseStyle(
                        new SpacingBetweenLines { After = "80", Line = "276", LineRule = LineSpacingRuleValues.Auto }))));

        for (var i = 0; i < notes.Count; i++)
        {
            if (i > 0)
                AppendPageBreak(body);

            RenderNote(body, mainPart, notes[i], getAttachmentsDir);
        }
    }

    private static void RenderNote(Body body, MainDocumentPart mainPart, Note note,
        Func<Note, string> getAttachmentsDir)
    {
        // Title — large, bold, accent color, with bottom border
        var titlePara = CreateStyledParagraph(note.Title, 32, bold: true, color: DarkHex);
        titlePara.ParagraphProperties ??= new ParagraphProperties();
        titlePara.ParagraphProperties.AppendChild(new ParagraphBorders(
            new BottomBorder { Val = BorderValues.Single, Color = AccentBlueHex, Size = 8, Space = 4 }));
        titlePara.ParagraphProperties.AppendChild(new SpacingBetweenLines { After = "200" });
        body.AppendChild(titlePara);

        // Metadata table — clean 2-column layout
        AppendMetadataTable(body, note);

        // Tags as inline pills-style text
        if (note.Tags is { Count: > 0 })
        {
            body.AppendChild(new Paragraph()); // spacer
            var tagPara = new Paragraph();
            tagPara.ParagraphProperties = new ParagraphProperties(
                new SpacingBetweenLines { Before = "40", After = "40" });

            var labelRun = CreateRun("Tags:  ", 18, color: GrayHex);
            tagPara.AppendChild(labelRun);

            for (var i = 0; i < note.Tags.Count; i++)
            {
                var tagRun = CreateRun(note.Tags[i], 18, color: AccentBlueHex);
                tagPara.AppendChild(tagRun);
                if (i < note.Tags.Count - 1)
                    tagPara.AppendChild(CreateRun("  \u2022  ", 18, color: LightGrayHex));
            }
            body.AppendChild(tagPara);
        }

        // Horizontal rule before body
        var rulePara = new Paragraph();
        rulePara.ParagraphProperties = new ParagraphProperties(
            new ParagraphBorders(
                new BottomBorder { Val = BorderValues.Single, Color = LightGrayHex, Size = 4, Space = 6 }),
            new SpacingBetweenLines { Before = "200", After = "200" });
        body.AppendChild(rulePara);

        // Body content
        if (!string.IsNullOrEmpty(note.Body))
        {
            var lines = note.Body.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // {{attachment}} tokens (same grammar as the in-app renderer):
                // image files embed as pictures, anything else as its filename.
                var tokens = FlowDocumentHelper.AttachmentTokenRegex.Matches(line);
                if (tokens.Count > 0)
                {
                    var pos = 0;
                    foreach (System.Text.RegularExpressions.Match token in tokens)
                    {
                        var before = line[pos..token.Index].Trim();
                        if (before.Length > 0)
                            body.AppendChild(CreateStyledParagraph(before, 22));
                        AppendAttachment(body, mainPart, getAttachmentsDir(note), token.Groups[1].Value);
                        pos = token.Index + token.Length;
                    }
                    var after = line[pos..].Trim();
                    if (after.Length > 0)
                        body.AppendChild(CreateStyledParagraph(after, 22));
                    continue;
                }

                if (line.StartsWith("### "))
                    body.AppendChild(CreateHeading(line[4..], 24));
                else if (line.StartsWith("## "))
                    body.AppendChild(CreateHeading(line[3..], 28));
                else if (line.StartsWith("# "))
                    body.AppendChild(CreateHeading(line[2..], 32));
                else if (line.StartsWith("- "))
                    body.AppendChild(CreateBullet(line[2..]));
                else
                    body.AppendChild(CreateStyledParagraph(line, 22));
            }
        }
    }

    private static void AppendAttachment(Body body, MainDocumentPart mainPart, string attachmentsDir, string filename)
    {
        var path = Path.Combine(attachmentsDir, filename);

        if (File.Exists(path) && FlowDocumentHelper.IsImageFile(filename) &&
            TryGetImagePartType(filename, out var partType))
        {
            try
            {
                body.AppendChild(CreateImageParagraph(mainPart, path, partType));
                return;
            }
            catch
            {
                // Unreadable/corrupt image -- fall through to the filename style,
                // mirroring the in-app renderer's fallback.
            }
        }

        body.AppendChild(CreateStyledParagraph(filename, 18, color: AccentBlueHex));
    }

    private static bool TryGetImagePartType(string filename, out PartTypeInfo partType)
    {
        switch (Path.GetExtension(filename).ToLowerInvariant())
        {
            case ".png": partType = ImagePartType.Png; return true;
            case ".jpg" or ".jpeg": partType = ImagePartType.Jpeg; return true;
            case ".gif": partType = ImagePartType.Gif; return true;
            case ".bmp": partType = ImagePartType.Bmp; return true;
            default: partType = ImagePartType.Png; return false; // e.g. .webp -- Word can't host it
        }
    }

    // Page width inside the default margins is ~6.5"; cap images a bit under it.
    private const double MaxImageInches = 6.0;
    private const int EmusPerInch = 914400;

    private static Paragraph CreateImageParagraph(MainDocumentPart mainPart, string path, PartTypeInfo partType)
    {
        // Header-only decode for natural size in inches (same pattern as the
        // in-app thumbnail renderer).
        double widthIn, heightIn;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var frame = BitmapFrame.Create(fs, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var dpiX = frame.DpiX > 1 ? frame.DpiX : 96.0;
            var dpiY = frame.DpiY > 1 ? frame.DpiY : 96.0;
            widthIn = frame.PixelWidth / dpiX;
            heightIn = frame.PixelHeight / dpiY;
        }

        // Shrink-to-fit only, never upscale.
        if (widthIn > MaxImageInches)
        {
            heightIn *= MaxImageInches / widthIn;
            widthIn = MaxImageInches;
        }
        var widthEmu = (long)(widthIn * EmusPerInch);
        var heightEmu = (long)(heightIn * EmusPerInch);

        var imagePart = mainPart.AddImagePart(partType);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            imagePart.FeedData(fs);
        var relId = mainPart.GetIdOfPart(imagePart);

        var name = Path.GetFileName(path);
        var drawing = new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.DocProperties { Id = 1U, Name = name },
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U, DistanceFromBottom = 0U,
                DistanceFromLeft = 0U, DistanceFromRight = 0U
            });

        var para = new Paragraph(new Run(drawing));
        para.ParagraphProperties = new ParagraphProperties(
            new SpacingBetweenLines { Before = "80", After = "80" });
        return para;
    }

    private static void AppendMetadataTable(Body body, Note note)
    {
        var rows = new List<(string label, string value)>();
        if (!string.IsNullOrWhiteSpace(note.Project))
            rows.Add(("Project", note.Project));
        rows.Add(("Priority", note.Priority.ToString()));
        rows.Add(("Status", note.Status.ToString()));
        rows.Add(("Created", note.Created.ToString("MMMM d, yyyy")));
        if (!string.IsNullOrWhiteSpace(note.Branch))
            rows.Add(("Branch", note.Branch));

        var table = new Table();

        // Table properties — no visible borders, fixed width
        var tblProps = new TableProperties(
            new TableWidth { Width = "9000", Type = TableWidthUnitValues.Dxa },
            new TableBorders(
                new TopBorder { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder { Val = BorderValues.None },
                new RightBorder { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder { Val = BorderValues.None }),
            new TableCellMarginDefault(
                new TopMargin { Width = "40", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "40", Type = TableWidthUnitValues.Dxa }));
        table.AppendChild(tblProps);

        foreach (var (label, value) in rows)
        {
            var row = new TableRow();

            // Label cell
            var labelCell = new TableCell();
            labelCell.AppendChild(new TableCellProperties(
                new TableCellWidth { Width = "2000", Type = TableWidthUnitValues.Dxa }));
            var labelPara = CreateStyledParagraph(label, 18, bold: true, color: GrayHex);
            labelPara.ParagraphProperties ??= new ParagraphProperties();
            labelPara.ParagraphProperties.AppendChild(new SpacingBetweenLines { After = "0" });
            labelCell.AppendChild(labelPara);
            row.AppendChild(labelCell);

            // Value cell
            var valueCell = new TableCell();
            valueCell.AppendChild(new TableCellProperties(
                new TableCellWidth { Width = "7000", Type = TableWidthUnitValues.Dxa }));
            var valuePara = CreateStyledParagraph(value, 18, color: DarkHex);
            valuePara.ParagraphProperties ??= new ParagraphProperties();
            valuePara.ParagraphProperties.AppendChild(new SpacingBetweenLines { After = "0" });
            valueCell.AppendChild(valuePara);
            row.AppendChild(valueCell);

            table.AppendChild(row);
        }

        body.AppendChild(table);
    }

    private static Paragraph CreateHeading(string text, int fontSizeHalfPt)
    {
        var para = CreateStyledParagraph(text, fontSizeHalfPt, bold: true, color: DarkHex);
        para.ParagraphProperties ??= new ParagraphProperties();
        para.ParagraphProperties.AppendChild(new SpacingBetweenLines { Before = "200", After = "80" });
        return para;
    }

    private static Paragraph CreateStyledParagraph(string text, int fontSizeHalfPt,
        bool bold = false, string? color = null)
    {
        var run = CreateRun(text, fontSizeHalfPt, bold, color);
        return new Paragraph(run);
    }

    private static Run CreateRun(string text, int fontSizeHalfPt,
        bool bold = false, string? color = null)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var rp = new RunProperties();
        rp.AppendChild(new RunFonts { Ascii = FontName, HighAnsi = FontName });
        rp.AppendChild(new FontSize { Val = fontSizeHalfPt.ToString() });
        if (bold)
            rp.AppendChild(new Bold());
        if (color != null)
            rp.AppendChild(new Color { Val = color });
        run.PrependChild(rp);
        return run;
    }

    private static Paragraph CreateBullet(string text)
    {
        var bulletRun = CreateRun("\u2022  ", 22, color: AccentBlueHex);
        var textRun = CreateRun(text, 22, color: DarkHex);

        var para = new Paragraph();
        para.ParagraphProperties = new ParagraphProperties(
            new Indentation { Left = "540", Hanging = "270" },
            new SpacingBetweenLines { After = "40" });
        para.AppendChild(bulletRun);
        para.AppendChild(textRun);
        return para;
    }

    private static void AppendPageBreak(Body body)
    {
        body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
    }
}
