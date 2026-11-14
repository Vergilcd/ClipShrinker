using System.IO;
using ClipShrinker.Core;
using ClipShrinker.Core.Project;
using ClipShrinker.Core.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClipShrinker.App.ViewModels;

/// <summary>Undo / redo, saving projects, the last session, bitrate presets and key bindings
/// (reference push_undo / _step / _restore, save_project, _save_session, the presets window, _bind_row).</summary>
public sealed partial class MainViewModel
{
    // ------------------------------------------------------------------ undo / redo
    readonly UndoHistory<ClipItemViewModel> history = new();
    readonly HashSet<ClipItemViewModel> detached = [];        // removed clips that undo can bring back
    bool undoOff, reorderPushed;
    int undoSerial;

    [ObservableProperty] private bool canUndo;
    [ObservableProperty] private bool canRedo;

    Snapshot<ClipItemViewModel> Snap() => new(Clips.ToList(), ActiveClip is { } a && Clips.Contains(a) ? a : null,
                                              Clips.ToDictionary(c => c, c => ClipState.Capture(c.Row)));

    /// <summary>Remembers the clip list before a change. False while exporting or loading.</summary>
    public bool PushUndo(string label)
    {
        if (Exporting || undoOff) return false;
        undoSerial++;
        history.Push(label, Snap());
        GcRows();
        UpdateUndoButtons();
        return true;
    }

    void DropLastUndo()
    {
        history.PopLast();
        UpdateUndoButtons();
    }

    void UpdateUndoButtons() => (CanUndo, CanRedo) = (history.CanUndo, history.CanRedo);

    void DetachRow(ClipItemViewModel c)
    {
        c.Detach();
        detached.Add(c);
    }

    /// <summary>Removed clips that no undo / redo step refers to any more are let go.</summary>
    void GcRows()
    {
        var used = new HashSet<ClipItemViewModel>(Clips);
        foreach (var s in history.AllSnapshots) used.UnionWith(s.Rows);
        detached.RemoveWhere(r => !used.Contains(r));
    }

    /// <summary>A clip title bar is pressed: the next reorder is a new undo step.</summary>
    public void BeginReorder() => reorderPushed = false;

    [RelayCommand] public void Undo() => Step(true);
    [RelayCommand] public void Redo() => Step(false);

    void Step(bool isUndo)
    {
        if (Exporting) return;
        var e = history.Step(isUndo, Snap());
        if (e is not { } entry)
        {
            UpdateUndoButtons();
            Dialogs.Toast($"Nothing to {(isUndo ? "undo" : "redo")}");
            return;
        }
        Restore(entry.Snap);
        GcRows();
        UpdateUndoButtons();
        Dialogs.Toast($"{(isUndo ? "Undo" : "Redo")}: {entry.Label}");
    }

    void Restore(Snapshot<ClipItemViewModel> snap)
    {
        StopPlayback();
        var keep = new HashSet<ClipItemViewModel>(snap.Rows);
        foreach (var r in Clips.Where(r => !keep.Contains(r)).ToList()) DetachRow(r);
        undoOff = true;
        try
        {
            Clips.Clear();
            foreach (var r in snap.Rows)
            {
                Clips.Add(r);
                if (detached.Remove(r)) r.Reattach();
                if (snap.State.TryGetValue(r, out var st)) r.ApplyState(st);
            }
            var target = snap.Active is { } a && keep.Contains(a) ? a : Clips.FirstOrDefault();
            foreach (var r in Clips) r.IsActive = false;
            ActiveClip = null;
            selection.Clear();
            if (target is not null) RequestPreview(target, target.Row.Start);
            else { lastPreview = null; PreviewImage = null; }
            RefreshSelection();
        }
        finally { undoOff = false; }
    }

    // ------------------------------------------------------------------ projects
    [RelayCommand] void SaveProjectCmd() => SaveProject(false);
    [RelayCommand] void SaveProjectAs() => SaveProject(true);

    /// <summary>Saves the clips and the export settings (reference save_project). False = not saved.</summary>
    public bool SaveProject(bool saveAs)
    {
        if (Clips.Count == 0) { Dialogs.Toast("Nothing to save - add some clips first"); return false; }
        string? path = ProjectPath;
        if (saveAs || path is null)
        {
            string name = ProjectPath is { } p ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(Clips[0].Path) + C.ProjectExt;
            path = Dialogs.SaveProjectFile(name, Path.GetDirectoryName(Clips[0].Path));
            if (path is null) return false;
        }
        try
        {
            int active = ActiveClip is { } a ? Math.Max(0, Clips.IndexOf(a)) : 0;
            ProjectSave.Save(path, ProjectSave.Data(Clips.Select(c => c.Row).ToList(), active, S, path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dialogs.Error("Could not save the project", e.Message);
            return false;
        }
        ProjectPath = path;
        OnPropertyChanged(nameof(WindowTitle));
        Dialogs.Toast($"Project saved: {path}");
        return true;
    }

    string SessionFile => Path.Combine(AppPaths.LibraryDir, "last_session.json");

    /// <summary>On close: the open clips are kept for the next start ("Restore the last session"), or the old file goes.</summary>
    public void SaveSession()
    {
        try
        {
            if (RestoreSession && Clips.Count > 0)
            {
                Directory.CreateDirectory(AppPaths.LibraryDir);
                int active = ActiveClip is { } a ? Math.Max(0, Clips.IndexOf(a)) : 0;
                ProjectSave.Save(SessionFile, ProjectSave.Data(Clips.Select(c => c.Row).ToList(), active, S, null));
            }
            else if (!RestoreSession && File.Exists(SessionFile)) File.Delete(SessionFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>At start-up, when nothing else was opened.</summary>
    public async Task RestoreLastSession()
    {
        if (RestoreSession && File.Exists(SessionFile) && Clips.Count == 0) await OpenProject(SessionFile, quiet: true);
    }

    // ------------------------------------------------------------------ bitrate presets
    public IReadOnlyList<Preset> Presets => presets;
    public event Action? PresetsChanged;

    /// <summary>Adds (index null) or replaces a preset. Returns the error box text, or null when it was saved.</summary>
    public (string Title, string Text)? SavePreset(int? index, string name, string mbps, string key)
    {
        var (p, title, err) = PresetStore.Validate(presets, index, name, mbps, key, PresetReserved);
        if (p is null) return (title!, err!);
        var list = presets.ToList();
        if (index is { } i)
        {
            string old = list[i].Name;
            list[i] = p;
            foreach (var c in Clips.Concat(detached)) c.PresetRenamed(old, p.Name);      // renamed: clips keep it
        }
        else list.Add(p);
        SetPresets(list);
        return null;
    }

    /// <summary>Why a preset (index, null = a new one) cannot have this hotkey, or null.</summary>
    public string? PresetKeyConflict(string key, int? index)
    {
        if (key.Length == 0) return null;
        string label = Text.ChordLabel(key);
        if (C.FixedKeys.Contains(key)) return $"{label} is used by the program (confirm, cancel, next field, close).";
        if (Keys.FirstOrDefault(kv => kv.Value == key).Key is { } act)
            return $"{label} is already used: {Text.KeyText[act].ToLowerInvariant()}.";
        if (presets.Where((q, i) => i != index).FirstOrDefault(q => q.Key == key) is { } clash)
            return $"{label} is already used by the preset \"{clash.Name}\".";
        return null;
    }

    /// <summary>Gives preset index a new hotkey ("" = none).</summary>
    public void SetPresetKey(int index, string key)
    {
        if (index < 0 || index >= presets.Count || PresetKeyConflict(key, index) is not null) return;
        var list = presets.ToList();
        list[index] = list[index] with { Key = key };
        SetPresets(list);
    }

    public void DeletePreset(int index)
    {
        if (index < 0 || index >= presets.Count) return;
        var list = presets.ToList();
        list.RemoveAt(index);
        SetPresets(list);
    }

    void SetPresets(List<Preset> list)
    {
        presets = list;
        Directory.CreateDirectory(AppPaths.LibraryDir);
        PresetStore.Save(AppPaths.PresetsFile, presets);
        foreach (var c in Clips.Concat(detached)) c.SetPresets(presets);
        OnPropertyChanged(nameof(Presets));
        OnPropertyChanged(nameof(AllPresetOptions));
        OnPropertyChanged(nameof(AllPreset));
        PresetsChanged?.Invoke();
    }

    // ------------------------------------------------------------------ key bindings
    public event Action? KeysChanged;

    /// <summary>Why ch cannot be used for act, or null (reference key_conflict).</summary>
    public string? KeyConflict(string ch, string act) => Text.KeyConflict(ch, act, Keys, presets.Select(p => p.Key));

    public void SetKey(string act, string ch)
    {
        Keys[act] = ch;
        KeysChanged?.Invoke();
    }

    /// <summary>"Default": back to the original key; another action that took it gets its own default back.</summary>
    public void ResetKey(string act)
    {
        var clash = Keys.FirstOrDefault(kv => kv.Value == C.KeyDefaults[act] && kv.Key != act).Key;
        if (clash is not null) Keys[clash] = C.KeyDefaults[clash];
        SetKey(act, C.KeyDefaults[act]);
    }
}
