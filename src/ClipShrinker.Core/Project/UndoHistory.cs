using static ClipShrinker.Core.Py.Py;

namespace ClipShrinker.Core.Project;

/// <summary>What undo restores of one clip: trims, speed sections, preset, zoom sections.</summary>
public sealed record ClipState(double Start, double End, IReadOnlyList<(double A, double B, double Speed)> Sections, string? Preset,
                               IReadOnlyList<(double A, double B, double X, double Y, double W)> Zooms)
{
    /// <summary>2.7: copies of the Sound track sounds (empty for clips without any).</summary>
    public IReadOnlyList<SoundBlock> Sounds { get; init; } = [];

    public static ClipState Capture(TimelineClip c) => new(c.Start, c.End,
        c.Sections.OrderBy(x => x.A).Select(x => (x.A, x.B, x.Speed)).ToList(), c.Preset,
        c.Zooms.OrderBy(z => z.A).Select(z => (z.A, z.B, z.X, z.Y, z.W)).ToList())
        { Sounds = c.Sounds.OrderBy(b => b.At).Select(b => b.Clone()).ToList() };

    /// <summary>Puts the state back (new section objects, like the reference set_state).</summary>
    public void Apply(TimelineClip c)
    {
        (c.Start, c.End, c.Preset) = (Start, End, Preset);
        c.Sections.Clear();
        c.Sections.AddRange(Sections.Select(s => new SpeedSection(s.A, s.B, s.Speed)));
        c.Zooms.Clear();
        c.Zooms.AddRange(Zooms.Select(z => new ZoomSection(z.A, z.B, z.X, z.Y, z.W)));
        c.Sounds.Clear();
        c.Sounds.AddRange(Sounds.Select(b => b.Clone()));
    }

    /// <summary>Rounded to milliseconds: tiny differences do not count as a change.</summary>
    public string Signature => string.Join("|",
        F(RoundTo(Start, 3), 3), F(RoundTo(End, 3), 3),
        string.Join(";", Sections.Select(s => $"{F(RoundTo(s.A, 3), 3)},{F(RoundTo(s.B, 3), 3)},{s.Speed:R}")), Preset ?? "\0",
        string.Join(";", Zooms.Select(z => $"{F(RoundTo(z.A, 3), 3)},{F(RoundTo(z.B, 3), 3)},{F(RoundTo(z.X, 3), 3)},{F(RoundTo(z.Y, 3), 3)},{F(RoundTo(z.W, 3), 3)}")),
        string.Join(";", Sounds.Select(b => $"{b.Path},{F(RoundTo(b.At, 3), 3)},{F(RoundTo(b.In, 3), 3)},{F(RoundTo(b.Len, 3), 3)},{b.VolumeDb},{F(RoundTo(b.Fade, 2), 2)}")));
}

/// <summary>The clip list at one moment: the clips in order (by identity), the active one and each clip's state.</summary>
public sealed record Snapshot<T>(IReadOnlyList<T> Rows, T? Active, IReadOnlyDictionary<T, ClipState> State) where T : class;

/// <summary>Undo / redo of the clip list (reference push_undo / undo / redo / _step / _sig). At most 60 steps; a step
/// that would change nothing is skipped.</summary>
public sealed class UndoHistory<T> where T : class
{
    public const int Max = 60;
    readonly List<(string Label, Snapshot<T> Snap)> undo = [], redo = [];

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public IEnumerable<Snapshot<T>> AllSnapshots => undo.Concat(redo).Select(e => e.Snap);

    /// <summary>Same clips (by identity) in the same order, same active clip, same rounded states (reference _sig).</summary>
    public static bool Same(Snapshot<T> a, Snapshot<T> b) =>
        a.Rows.Count == b.Rows.Count && a.Rows.Zip(b.Rows).All(p => ReferenceEquals(p.First, p.Second)) &&
        ReferenceEquals(a.Active, b.Active) &&
        a.Rows.All(r => (a.State.TryGetValue(r, out var x) ? x.Signature : "") == (b.State.TryGetValue(r, out var y) ? y.Signature : ""));

    /// <summary>Remembers the state before a change; a new change clears redo.</summary>
    public void Push(string label, Snapshot<T> before)
    {
        undo.Add((label, before));
        if (undo.Count > Max) undo.RemoveAt(0);
        redo.Clear();
    }

    /// <summary>Drops the last step (an "add clips" that added nothing).</summary>
    public void PopLast() { if (undo.Count > 0) undo.RemoveAt(undo.Count - 1); }

    /// <summary>Undo (or redo): the newest step that differs from now; now goes onto the other stack.
    /// Returns (label, snapshot to restore), or null when there is nothing to undo.</summary>
    public (string Label, Snapshot<T> Snap)? Step(bool isUndo, Snapshot<T> current)
    {
        var src = isUndo ? undo : redo;
        var dst = isUndo ? redo : undo;
        while (src.Count > 0)
        {
            var e = src[^1];
            src.RemoveAt(src.Count - 1);
            if (!Same(e.Snap, current))
            {
                dst.Add((e.Label, current));
                return e;
            }
        }
        return null;
    }
}
