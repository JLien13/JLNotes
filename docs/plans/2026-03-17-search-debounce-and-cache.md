# Search Debounce & In-Memory Cache Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Eliminate search lag by debouncing keystroke input and caching notes in memory instead of re-reading disk on every character typed.

**Architecture:** Add a 250ms `DispatcherTimer` debounce so rapid keystrokes collapse into a single filter pass. Cache `LoadAll()` results and project info in `MainViewModel` fields, refreshed only when `FileSystemWatcher` fires or a non-search filter changes (project selector, show-completed toggle, group-by toggle). Search text changes filter against the cache — zero disk I/O.

**Tech Stack:** WPF, CommunityToolkit.Mvvm, DispatcherTimer

---

### Task 1: Add debounce timer to search text changes

**Files:**
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:1-8` (add using)
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:10-15` (add field)
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:48-63` (init timer in constructor)
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:234` (debounce instead of direct call)

**Step 1: Add the timer field and using**

Add `using System.Windows.Threading;` to the top of the file alongside existing usings.

Add a private field to `MainViewModel`:

```csharp
private DispatcherTimer? _searchDebounce;
```

**Step 2: Initialize the timer in the constructor**

At the end of the constructor (after `_noteService.StartWatching();`), add:

```csharp
_searchDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
_searchDebounce.Tick += (_, _) =>
{
    _searchDebounce.Stop();
    RefreshFromCache();
};
```

(`RefreshFromCache` doesn't exist yet — that's Task 2. For now this won't compile; that's fine, we're building incrementally.)

**Step 3: Change `OnSearchTextChanged` to debounce**

Replace line 234:

```csharp
partial void OnSearchTextChanged(string value) => RefreshNotes();
```

with:

```csharp
partial void OnSearchTextChanged(string value)
{
    _searchDebounce!.Stop();
    _searchDebounce.Start();
}
```

This resets the 250ms timer on every keystroke. Only the final keystroke (after the user pauses) triggers the actual filter.

**Step 4: Commit**

```
feat: debounce search input with 250ms timer
```

---

### Task 2: Cache notes in memory, filter from cache

**Files:**
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:10-15` (add cache fields)
- Modify: `src/JLNotes/ViewModels/MainViewModel.cs:93-111` (split RefreshNotes into load vs filter)

**Step 1: Add cache fields**

Add two private fields to `MainViewModel`:

```csharp
private List<Note> _cachedNotes = [];
private List<ProjectInfo> _cachedProjects = [];
```

**Step 2: Split `RefreshNotes()` into two methods**

The existing `RefreshNotes()` does two things: load from disk, then filter + rebuild UI. Split it:

**`RefreshNotes()`** — full reload from disk + filter (called by FileSystemWatcher, project/filter changes):

```csharp
public void RefreshNotes()
{
    _cachedNotes = _noteService.LoadAll();
    _cachedProjects = _projectService.Load();
    LoadProjects(_cachedNotes);
    RefreshFromCache();
}
```

**`RefreshFromCache()`** — filter cached data + rebuild UI (called by search debounce):

```csharp
private void RefreshFromCache()
{
    var filtered = _cachedNotes.Where(n =>
        (SelectedProject == "Projects" || n.Project == SelectedProject) &&
        (ShowCompleted || n.Status != NoteStatus.Done) &&
        (string.IsNullOrEmpty(SearchText) ||
         n.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
         n.Body.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
         n.Tags.Any(t => t.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
    ).ToList();

    RebuildGroup(HighPriority, filtered.Where(n => n.Priority == NotePriority.High));
    RebuildGroup(MediumPriority, filtered.Where(n => n.Priority == NotePriority.Medium));
    RebuildGroup(LowPriority, filtered.Where(n => n.Priority == NotePriority.Low));
    RebuildProjectGroups(filtered);
}
```

**Step 3: Update `RebuildProjectGroups` to use cached projects**

In `RebuildProjectGroups`, replace line 118:

```csharp
var projectInfos = _projectService.Load();
```

with:

```csharp
var projectInfos = _cachedProjects;
```

This eliminates the redundant disk read of `projects.json`.

**Step 4: Build and verify**

Run: `dotnet build src/JLNotes/JLNotes.csproj`
Expected: Build succeeds with no errors.

**Step 5: Commit**

```
perf: cache notes in memory, filter from cache on search
```

---

### Task 3: Manual smoke test

**Step 1: Run the app**

```
dotnet run --project src/JLNotes/JLNotes.csproj
```

**Step 2: Verify these behaviors**

- [ ] Typing in search bar is smooth and responsive (no lag between keystrokes)
- [ ] Search results appear ~250ms after you stop typing (slight delay is expected and correct)
- [ ] Clearing the search box restores all notes
- [ ] Changing project dropdown still filters correctly (full reload path)
- [ ] Toggling "Show completed" still works (full reload path)
- [ ] Toggling "Group by project" still works (full reload path)
- [ ] Adding a new note (via + button) appears in the list (FileSystemWatcher → full reload)
- [ ] Editing and saving a note updates the list (FileSystemWatcher → full reload)
- [ ] Deleting a note removes it from the list (FileSystemWatcher → full reload)

**Step 3: Commit (if any adjustments were needed)**

---

## Summary of changes

| File | Change |
|------|--------|
| `MainViewModel.cs` | Add `_searchDebounce` timer, `_cachedNotes`/`_cachedProjects` fields. Split `RefreshNotes` into disk-load + `RefreshFromCache`. Wire debounce to `RefreshFromCache`. |
| `RebuildProjectGroups` | Use `_cachedProjects` instead of `_projectService.Load()`. |

**No new files. No XAML changes. No new dependencies.**
