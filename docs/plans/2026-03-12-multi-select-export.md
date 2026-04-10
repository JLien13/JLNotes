# Multi-Select & Word Export Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add multi-select mode with selection checkboxes, move the done checkbox to the right side, and enable exporting notes (single or batch) to Word documents via save dialog.

**Architecture:** Selection state lives on `NoteItemViewModel` (`IsSelected` bool), orchestrated by `MainViewModel` (`IsSelectMode` toggle). A single `ExportService` handles all Word doc generation for both single and batch export. The XAML note card template rearranges columns: priority dot + selection checkbox (left), title (center), done checkbox + timestamp (right). A floating action bar appears at the bottom of the note list when in select mode.

**Tech Stack:** WPF/.NET 10, CommunityToolkit.Mvvm, Open XML SDK (`DocumentFormat.OpenXml` NuGet) for .docx generation

---

## Task 1: Add Open XML SDK dependency

**Files:**
- Modify: `src/JLNotes/JLNotes.csproj`

**Step 1: Add NuGet package**

Run: `dotnet add src/JLNotes/JLNotes.csproj package DocumentFormat.OpenXml`

**Step 2: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: add Open XML SDK for Word export
```

---

## Task 2: Create ExportService (single source of truth for docx generation)

**Files:**
- Create: `src/JLNotes/Services/ExportService.cs`

This service handles all Word document generation. Both single-note right-click export and batch export from select mode call into this one service.

**Step 1: Create ExportService**

```csharp
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using JLNotes.Models;

namespace JLNotes.Services;

public static class ExportService
{
    /// <summary>
    /// Export one or more notes to a single .docx file.
    /// Multiple notes are separated by page breaks.
    /// </summary>
    public static void ExportToWord(IReadOnlyList<Note> notes, string outputPath)
    {
        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        for (int i = 0; i < notes.Count; i++)
        {
            if (i > 0)
                AppendPageBreak(body);

            AppendNote(body, notes[i]);
        }
    }

    private static void AppendNote(Body body, Note note)
    {
        // Title (Heading 1)
        body.AppendChild(CreateHeading(note.Title, "Heading1", 28));

        // Metadata line: project | priority | status | created
        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(note.Project))
            metaParts.Add($"Project: {note.Project}");
        metaParts.Add($"Priority: {note.Priority}");
        metaParts.Add($"Status: {note.Status}");
        metaParts.Add($"Created: {note.Created:yyyy-MM-dd}");
        if (!string.IsNullOrEmpty(note.Branch))
            metaParts.Add($"Branch: {note.Branch}");

        body.AppendChild(CreateParagraph(string.Join("  |  ", metaParts), "8899AA", 9));

        // Tags
        if (note.Tags.Count > 0)
            body.AppendChild(CreateParagraph($"Tags: {string.Join(", ", note.Tags)}", "8899AA", 9));

        // Spacer
        body.AppendChild(new Paragraph());

        // Body — split on newlines, render each line as a paragraph
        // Detect markdown headings (## etc.)
        foreach (var line in note.Body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("### "))
                body.AppendChild(CreateHeading(trimmed[4..], "Heading3", 14));
            else if (trimmed.StartsWith("## "))
                body.AppendChild(CreateHeading(trimmed[3..], "Heading2", 16));
            else if (trimmed.StartsWith("# "))
                body.AppendChild(CreateHeading(trimmed[2..], "Heading1", 20));
            else if (trimmed.StartsWith("- "))
                body.AppendChild(CreateBullet(trimmed[2..]));
            else
                body.AppendChild(CreateParagraph(trimmed));
        }
    }

    private static Paragraph CreateHeading(string text, string styleId, int fontSizeHalfPt)
    {
        var run = new Run(new Text(text));
        run.RunProperties = new RunProperties
        {
            Bold = new Bold(),
            FontSize = new FontSize { Val = (fontSizeHalfPt * 2).ToString() }
        };
        var para = new Paragraph(run);
        para.ParagraphProperties = new ParagraphProperties
        {
            SpacingBetweenLines = new SpacingBetweenLines { After = "120" }
        };
        return para;
    }

    private static Paragraph CreateParagraph(string text, string? colorHex = null, int? fontSizeHalfPt = null)
    {
        var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        var rp = new RunProperties();
        if (colorHex != null)
            rp.Color = new Color { Val = colorHex };
        if (fontSizeHalfPt != null)
            rp.FontSize = new FontSize { Val = (fontSizeHalfPt.Value * 2).ToString() };
        if (rp.HasChildren)
            run.RunProperties = rp;
        return new Paragraph(run);
    }

    private static Paragraph CreateBullet(string text)
    {
        var run = new Run(new Text($"\u2022  {text}") { Space = SpaceProcessingModeValues.Preserve });
        var para = new Paragraph(run);
        para.ParagraphProperties = new ParagraphProperties
        {
            Indentation = new Indentation { Left = "360" }
        };
        return para;
    }

    private static void AppendPageBreak(Body body)
    {
        body.AppendChild(new Paragraph(
            new Run(new Break { Type = BreakValues.Page })));
    }
}
```

**Step 2: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: add ExportService for Word document generation
```

---

## Task 3: Add single-note "Export to Word" via right-click context menu

**Files:**
- Modify: `src/JLNotes/ViewModels/NoteItemViewModel.cs` — add `ExportToWord` command
- Modify: `src/JLNotes/App.xaml` — add context menu item

**Step 1: Add ExportToWord command to NoteItemViewModel**

Add after the `OpenInClaude` method:

```csharp
[RelayCommand]
private void ExportToWord()
{
    var dialog = new Microsoft.Win32.SaveFileDialog
    {
        Title = "Export Note to Word",
        Filter = "Word Document|*.docx",
        FileName = $"{_note.GetSlug()}.docx"
    };
    if (dialog.ShowDialog() == true)
    {
        ExportService.ExportToWord([_note], dialog.FileName);
    }
}
```

Add `using JLNotes.Services;` if not already present (it should be).

**Step 2: Add context menu item in App.xaml**

In the `NoteItemTemplate` ContextMenu, add before the Delete separator:

```xml
<MenuItem Header="Export to Word" Command="{Binding ExportToWordCommand}" />
```

Place it after "Open in Claude" and before the final Separator + Delete.

**Step 3: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
feat: add single-note Export to Word via right-click
```

---

## Task 4: Add selection state to NoteItemViewModel

**Files:**
- Modify: `src/JLNotes/ViewModels/NoteItemViewModel.cs`

**Step 1: Add IsSelected property**

Add to the `[ObservableProperty]` block at the top of the class:

```csharp
[ObservableProperty] private bool _isSelected;
```

**Step 2: Add IsSelectMode property**

This is driven by the parent MainViewModel. Add a settable property:

```csharp
[ObservableProperty] private bool _isSelectMode;
```

**Step 3: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
feat: add selection state properties to NoteItemViewModel
```

---

## Task 5: Add select mode to MainViewModel

**Files:**
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs`

**Step 1: Add IsSelectMode property and SelectedCount**

```csharp
[ObservableProperty]
private bool _isSelectMode;
```

Add computed property:

```csharp
public int SelectedCount => AllNoteViewModels.Count(vm => vm.IsSelected);

private IEnumerable<NoteItemViewModel> AllNoteViewModels =>
    HighPriority.Concat(MediumPriority).Concat(LowPriority);
```

**Step 2: Add OnIsSelectModeChanged handler**

When select mode is turned off, clear all selections and propagate IsSelectMode to all note VMs:

```csharp
partial void OnIsSelectModeChanged(bool value)
{
    foreach (var vm in AllNoteViewModels)
    {
        vm.IsSelectMode = value;
        if (!value) vm.IsSelected = false;
    }
    // Also propagate to project group VMs
    foreach (var pg in ProjectGroups)
        foreach (var vm in pg.Notes)
        {
            vm.IsSelectMode = value;
            if (!value) vm.IsSelected = false;
        }
    OnPropertyChanged(nameof(SelectedCount));
}
```

**Step 3: Propagate IsSelectMode when building note VMs**

In `RebuildGroup`, after creating the VM, add:
```csharp
vm.IsSelectMode = IsSelectMode;
vm.PropertyChanged += (s, e) =>
{
    if (e.PropertyName == nameof(NoteItemViewModel.IsSelected))
        OnPropertyChanged(nameof(SelectedCount));
};
```

Do the same in `RebuildProjectGroups` where VMs are created.

**Step 4: Add ExportSelected command**

```csharp
[RelayCommand]
private void ExportSelected()
{
    var selected = AllNoteViewModels.Where(vm => vm.IsSelected).Select(vm => vm.Note).ToList();
    // Also check project groups (they may hold different VM instances)
    if (GroupByProject)
        selected = ProjectGroups.SelectMany(pg => pg.Notes).Where(vm => vm.IsSelected).Select(vm => vm.Note).ToList();

    if (selected.Count == 0) return;

    var dialog = new Microsoft.Win32.SaveFileDialog
    {
        Title = "Export Selected Notes to Word",
        Filter = "Word Document|*.docx",
        FileName = $"jlnotes-export-{DateTime.Now:yyyy-MM-dd}.docx"
    };
    if (dialog.ShowDialog() == true)
    {
        ExportService.ExportToWord(selected, dialog.FileName);
        IsSelectMode = false;
    }
}
```

Add `using JLNotes.Services;` at top.

**Step 5: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 6: Commit**

```
feat: add select mode and batch export to MainViewModel
```

---

## Task 6: Rearrange note card layout — move done checkbox to right

**Files:**
- Modify: `src/JLNotes/App.xaml` — NoteItemTemplate title row

**Step 1: Update the title row Grid columns**

Replace the current title row Grid (columns 0-3) with this new layout:

```
Col 0: Selection checkbox (visible only in select mode)
Col 1: Priority dot
Col 2: Title (fills remaining space)
Col 3: Done checkbox
Col 4: Timestamp
```

New XAML for the title row:

```xml
<Grid>
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="*" />
        <ColumnDefinition Width="Auto" />
        <ColumnDefinition Width="Auto" />
    </Grid.ColumnDefinitions>
    <!-- Selection checkbox (select mode only) -->
    <CheckBox Grid.Column="0" Style="{StaticResource DarkCheckBox}"
              IsChecked="{Binding IsSelected}"
              Visibility="{Binding IsSelectMode, Converter={StaticResource BoolToVisibility}}" />
    <Ellipse Grid.Column="1" Style="{StaticResource PriorityDot}"
             Fill="{Binding PriorityBrush}" />
    <TextBlock Grid.Column="2" Text="{Binding Title}"
               Style="{StaticResource BaseText}"
               TextTrimming="CharacterEllipsis"
               VerticalAlignment="Center" />
    <!-- Done checkbox (right side) -->
    <CheckBox Grid.Column="3" Style="{StaticResource DarkCheckBox}"
              IsChecked="{Binding IsDone}"
              Margin="6,0,0,0" />
    <!-- Timestamp -->
    <TextBlock Grid.Column="4" Text="{Binding TimeAgo}"
               Style="{StaticResource SecondaryText}"
               FontSize="10"
               VerticalAlignment="Center"
               Margin="0,0,0,0" />
</Grid>
```

**Step 2: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 3: Commit**

```
feat: rearrange note card — selection left, done checkbox right
```

---

## Task 7: Add select mode toggle and floating action bar to MainPanelWindow

**Files:**
- Modify: `src/JLNotes/Views/MainPanelWindow.xaml`

**Step 1: Add select mode toggle button**

In the toolbar `StackPanel` (Grid.Column="1"), add a third toggle button:

```xml
<ToggleButton IsChecked="{Binding IsSelectMode}"
              ToolTip="Select notes"
              Style="{StaticResource SubtleToggleButton}"
              Content="&#x2610;"
              FontSize="14"
              Margin="4,0,0,0"
              Padding="4,2" />
```

**Step 2: Add floating action bar at bottom of the Grid**

Add a new row to the main Grid:

```xml
<RowDefinition Height="Auto" />
```

Then add the action bar (in new Grid.Row="3"):

```xml
<!-- Selection Action Bar -->
<Border Grid.Row="3"
        Padding="12,8"
        Background="{DynamicResource BgSecondaryBrush}"
        BorderBrush="{DynamicResource BorderSubtleBrush}"
        BorderThickness="0,1,0,0"
        Visibility="{Binding IsSelectMode, Converter={StaticResource BoolToVisibility}}">
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*" />
            <ColumnDefinition Width="Auto" />
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Column="0"
                   Style="{StaticResource SecondaryText}"
                   VerticalAlignment="Center">
            <Run Text="{Binding SelectedCount, Mode=OneWay}" />
            <Run Text=" selected" />
        </TextBlock>
        <Button Grid.Column="1"
                Content="Export to Word"
                Style="{StaticResource SubtleButton}"
                Foreground="{DynamicResource AccentBlueBrush}"
                Command="{Binding ExportSelectedCommand}" />
    </Grid>
</Border>
```

**Step 3: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
feat: add select mode toggle and floating export action bar
```

---

## Task 8: Update CollapseAll/SaveExpanded to include ProjectGroups

**Files:**
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs`

The `CollapseAll` and `SaveExpanded` methods currently only iterate priority groups. They should also cover `ProjectGroups` VMs.

**Step 1: Update the AllNoteViewModels helper**

Replace the simple `AllNoteViewModels` with one that covers both views:

```csharp
private IEnumerable<NoteItemViewModel> AllNoteViewModels =>
    GroupByProject
        ? ProjectGroups.SelectMany(pg => pg.Notes)
        : HighPriority.Concat(MediumPriority).Concat(LowPriority);
```

**Step 2: Update CollapseAll and SaveExpanded to use AllNoteViewModels**

```csharp
public void CollapseAll()
{
    foreach (var vm in AllNoteViewModels)
        if (vm.IsExpanded) vm.CancelEditsCommand.Execute(null);
}

public void SaveExpanded()
{
    foreach (var vm in AllNoteViewModels)
        if (vm.IsExpanded) vm.SaveEditsCommand.Execute(null);
}
```

**Step 3: Verify build**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeded

**Step 4: Commit**

```
refactor: CollapseAll/SaveExpanded use AllNoteViewModels
```

---

## Task 9: Build Debug + Release and manual test

**Step 1: Build both configurations**

Run: `dotnet build src/JLNotes/JLNotes.csproj -c Debug && dotnet build src/JLNotes/JLNotes.csproj -c Release`
Expected: Both succeed

**Step 2: Manual test checklist**

- [ ] Default view: done checkbox appears on right side of note card
- [ ] Selection checkboxes are hidden by default
- [ ] Click select toggle (checkbox icon in toolbar) → selection checkboxes appear on left of each note
- [ ] Check some notes → "N selected" count updates in action bar
- [ ] Click "Export to Word" → save dialog appears → saves .docx with all selected notes separated by page breaks
- [ ] Toggle select mode off → selections clear, checkboxes disappear, action bar hides
- [ ] Right-click a note → "Export to Word" → save dialog → exports single note .docx
- [ ] Open exported .docx → title, metadata, tags, body, headings, bullets all render correctly
- [ ] Project group view + select mode works together

**Step 3: Commit**

```
chore: verify multi-select and word export feature complete
```
