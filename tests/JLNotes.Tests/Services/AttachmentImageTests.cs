using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JLNotes.Models;
using JLNotes.Services;

namespace JLNotes.Tests.Services;

public class AttachmentImageTests : IDisposable
{
    private readonly string _testDir;
    private readonly NoteService _service;

    public AttachmentImageTests()
    {
        // Nest notes/ one level down so the attachments base dir
        // (sibling of the notes dir) stays inside the per-test folder.
        _testDir = Path.Combine(Path.GetTempPath(), $"jlnotes-img-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_testDir, "notes"));
        _service = new NoteService(Path.Combine(_testDir, "notes"));
    }

    public void Dispose()
    {
        _service.Dispose();
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, true);
    }

    private static BitmapSource CreateTestBitmap()
    {
        var pixels = new byte[4 * 4 * 4]; // 4x4 BGRA
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0xFF;     // B
            pixels[i + 2] = 0xFF; // R
            pixels[i + 3] = 0xFF; // A
        }
        return BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 4 * 4);
    }

    private Note CreateSavedNote()
    {
        var note = new Note { Title = "Image host note", Created = new DateTime(2026, 6, 12) };
        _service.Save(note);
        return note;
    }

    [Fact]
    public void AddAttachmentFromImage_WritesDecodablePng()
    {
        var note = CreateSavedNote();

        var fileName = _service.AddAttachmentFromImage(note, CreateTestBitmap());

        Assert.EndsWith(".png", fileName);
        var path = Path.Combine(_service.GetAttachmentsDir(note), fileName);
        Assert.True(File.Exists(path));

        using var fs = File.OpenRead(path);
        var decoded = BitmapFrame.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        Assert.Equal(4, decoded.PixelWidth);
        Assert.Equal(4, decoded.PixelHeight);
    }

    [Fact]
    public void AddAttachmentFromImage_RepeatedPastes_GetDistinctFilenames()
    {
        var note = CreateSavedNote();

        var first = _service.AddAttachmentFromImage(note, CreateTestBitmap());
        var second = _service.AddAttachmentFromImage(note, CreateTestBitmap());

        Assert.NotEqual(first, second);
        var dir = _service.GetAttachmentsDir(note);
        Assert.True(File.Exists(Path.Combine(dir, first)));
        Assert.True(File.Exists(Path.Combine(dir, second)));
    }

    [Fact]
    public void ExportToWord_EmbedsImageAttachment()
    {
        var note = CreateSavedNote();
        var fileName = _service.AddAttachmentFromImage(note, CreateTestBitmap());
        note.Body = "before\n{{" + fileName + "}}\nafter";
        note.Attachments = [fileName]; // Save() deletes files not listed here
        _service.Save(note);

        var outPath = Path.Combine(_testDir, "export.docx");
        ExportService.ExportToWord([note], outPath, _service.GetAttachmentsDir);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(outPath, false);
        var main = doc.MainDocumentPart!;
        Assert.Single(main.ImageParts);
        Assert.NotEmpty(main.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>());
        Assert.DoesNotContain("{{", main.Document.InnerText);

        // Schema-validate the new picture markup so it's provably Word-openable.
        // (Scoped to the drawing: the legacy metadata table has pre-existing,
        // Word-tolerated schema quirks that are out of scope here.)
        var validator = new DocumentFormat.OpenXml.Validation.OpenXmlValidator();
        var drawing = main.Document.Descendants<DocumentFormat.OpenXml.Wordprocessing.Drawing>().First();
        var errs = validator.Validate(drawing).Select(e => e.Description + " | " + e.Path?.XPath).ToList();
        Assert.True(errs.Count == 0, string.Join(Environment.NewLine, errs));
    }

    [Fact]
    public void ExportToWord_MissingAttachment_FallsBackToFilenameText()
    {
        var note = CreateSavedNote();
        note.Body = "see {{gone.png}} here";
        _service.Save(note);

        var outPath = Path.Combine(_testDir, "export-missing.docx");
        ExportService.ExportToWord([note], outPath, _service.GetAttachmentsDir);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(outPath, false);
        var main = doc.MainDocumentPart!;
        Assert.Empty(main.ImageParts);
        Assert.Contains("gone.png", main.Document.InnerText);
        Assert.DoesNotContain("{{", main.Document.InnerText);
    }
}
