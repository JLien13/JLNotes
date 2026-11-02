using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JLNotes.Models;
using JLNotes.Services;
using JLNotes.ViewModels;

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
    public void ExportToWord_TokenWithNoFile_StaysAsWritten()
    {
        var note = CreateSavedNote();
        note.Body = "Dear {{companyName}} here";
        _service.Save(note);

        var outPath = Path.Combine(_testDir, "export-missing.docx");
        ExportService.ExportToWord([note], outPath, _service.GetAttachmentsDir);

        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(outPath, false);
        Assert.Contains("Dear {{companyName}} here", doc.MainDocumentPart!.Document!.InnerText);
    }

    [Fact]
    public void Save_LeavingTheBodyAlone_KeepsAnImageNotYetInTheBody()
    {
        var note = CreateSavedNote();
        var fileName = _service.AddAttachmentFromImage(note, CreateTestBitmap());

        note.Priority = NotePriority.High;
        _service.Save(note);

        Assert.True(File.Exists(Path.Combine(_service.GetAttachmentsDir(note), fileName)));
    }

    [Fact]
    public void Save_RemovingTheTokenFromTheBody_DeletesTheImage()
    {
        var note = CreateSavedNote();
        var fileName = _service.AddAttachmentFromImage(note, CreateTestBitmap());
        note.Body = "{{" + fileName + "}}";
        _service.Save(note);
        Assert.True(File.Exists(Path.Combine(_service.GetAttachmentsDir(note), fileName)));

        note.Body = "no image now";
        _service.Save(note);

        Assert.False(File.Exists(Path.Combine(_service.GetAttachmentsDir(note), fileName)));
    }

    [Fact]
    public void PasteThenChangePriority_KeepsTheImageAndItsToken()
    {
        var failure = default(Exception);
        var thread = new Thread(() =>
        {
            try
            {
                var note = CreateSavedNote();
                var vm = new NoteItemViewModel(note, _service, new SettingsService(_testDir), new ProjectCatalog());
                vm.LoadEditor();
                var editor = new System.Windows.Controls.RichTextBox { Document = vm.EditDocument };

                vm.HandleImagePaste(CreateTestBitmap(), editor);
                vm.SetPriorityHighCommand.Execute(null);

                var token = Assert.Single(_service.LoadAll().Single().GetAttachmentTokens());
                Assert.True(File.Exists(Path.Combine(_service.GetAttachmentsDir(note), token)));
            }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
    }

    [Fact]
    public void NotesWithTheSameTitle_CannotTouchEachOthersImages()
    {
        var monday = new Note { Title = "Weekly sync", Created = new DateTime(2026, 1, 5) };
        var nextMonday = new Note { Title = "Weekly sync", Created = new DateTime(2026, 1, 12) };
        _service.Save(monday);
        _service.Save(nextMonday);
        var image = _service.AddAttachmentFromImage(monday, CreateTestBitmap());
        monday.Body = "{{" + image + "}}";
        _service.Save(monday);
        var imagePath = Path.Combine(_service.GetAttachmentsDir(monday), image);

        nextMonday.Body = "edited";
        _service.Save(nextMonday);
        Assert.True(File.Exists(imagePath));

        _service.Delete(nextMonday);
        Assert.True(File.Exists(imagePath));
    }

    [Fact]
    public void Migration_GivesEachNoteItsOwnFolder_AndLeavesOtherFoldersAlone()
    {
        var root = Path.Combine(_testDir, "attachments");
        Directory.CreateDirectory(Path.Combine(root, "weekly-sync"));
        Directory.CreateDirectory(Path.Combine(root, "2026-09-21-hand-made"));
        File.WriteAllText(Path.Combine(root, "weekly-sync", "a.png"), "a");
        File.WriteAllText(Path.Combine(root, "weekly-sync", "shared.png"), "s");
        File.WriteAllText(Path.Combine(root, "2026-09-21-hand-made", "mine.md"), "m");
        var first = new Note { Title = "Weekly sync", Body = "{{a.png}} {{shared.png}} {{companyName}}", Created = new DateTime(2026, 1, 5) };
        var second = new Note { Title = "Weekly sync", Body = "{{shared.png}}", Created = new DateTime(2026, 1, 12) };
        _service.Save(first);
        _service.Save(second);

        _service.MigrateAttachmentFolders();

        Assert.True(File.Exists(Path.Combine(root, "2026-01-05-weekly-sync", "a.png")));
        Assert.True(File.Exists(Path.Combine(root, "2026-01-05-weekly-sync", "shared.png")));
        Assert.True(File.Exists(Path.Combine(root, "2026-01-12-weekly-sync", "shared.png")));
        Assert.False(Directory.Exists(Path.Combine(root, "weekly-sync")));
        Assert.True(File.Exists(Path.Combine(root, "2026-09-21-hand-made", "mine.md")));

        _service.MigrateAttachmentFolders(); // nothing left to do
        Assert.True(File.Exists(Path.Combine(root, "2026-01-05-weekly-sync", "a.png")));
    }
}
