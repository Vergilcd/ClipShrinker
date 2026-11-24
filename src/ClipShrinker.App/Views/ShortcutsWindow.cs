using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>Keyboard shortcuts and tips in two columns (reference show_shortcuts / _bind_row). Every keyboard
/// shortcut is shown as keycaps (PowerToys style); clicking one records a new key in a <see cref="HotkeyDialog"/>.</summary>
public sealed class ShortcutsWindow : Window
{
    readonly MainViewModel vm;
    readonly List<Action> refreshers = [];

    public ShortcutsWindow(MainViewModel vm)
    {
        this.vm = vm;
        Title = "Shortcuts";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        static (string, string) K(string act) => (Text.KeyText[act], "@" + act);   // a rebindable shortcut
        (string Desc, string Key)[] clips = [K("select_all"), K("delete"), K("clear_all"), ("Change the order by dragging a clip above or below another one.", "")];
        if (vm.PresetsModuleOn)
            clips = [.. clips, ("Bitrate presets module: apply a preset with its hotkey or by choosing it in the clip's title bar.", "")];
        var sections = new List<(string, (string Desc, string Key)[])>
        {
            ("GENERAL", [K("shortcuts"), K("export"), K("open"), K("save"), K("save_as"),
                         K("undo"), K("redo"), K("redo_alt")]),
            ("PREVIEW", [K("play"), K("mute"), K("fullscreen"),
                         ("With the playhead outside the trimmed part, Play plays to the end of the video once.", "")]),
            // ("text", "") = a plain tip line across the column (mouse actions, nothing to rebind)
            ("TRIMMING", [K("trim_start"), K("trim_end"),
                          ("Trim with the keys above or by dragging the trim handles.", ""),
                          ("Zoom the timeline with Ctrl + mouse wheel; the wheel alone scrolls it.", "")]),
            ("CLIPS", clips),
        };
        // module sections go last, and only while their module is on
        if (vm.EffectsModuleOn)
            sections.Add(("EFFECTS (module)", [K("effects"), K("speed"), K("slow"), K("pause"), K("zoom"), K("delete_section"),
                          ("Resize or move an effect section by dragging its handle bar or the whole section.", ""),
                          ("Remove a section by right-clicking it.", "")]));
        if (vm.SoundModuleOn)                                  // 2.7
            sections.Add(("SOUNDS (module)", [K("sound"),
                          ("Drop sound files on a clip's Sounds lane, or pick one in Sounds.", ""),
                          ("Drag a sound to move it, drag its ends to trim; right-click or Delete removes it.", "")]));
        var box = new Grid { Margin = new Thickness(26, 20, 26, 20) };
        box.ColumnDefinitions.Add(new ColumnDefinition());
        box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        box.ColumnDefinitions.Add(new ColumnDefinition());
        box.RowDefinitions.Add(new RowDefinition());
        box.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var sep = new Separator { Margin = new Thickness(28, 0, 28, 0), LayoutTransform = new System.Windows.Media.RotateTransform(90) };
        Grid.SetColumn(sep, 1);
        box.Children.Add(sep);
        for (int c = 0; c < 2; c++)
        {
            var col = new Grid { VerticalAlignment = VerticalAlignment.Top };
            // 2.9.1: one column; each row lays out its text and keycaps itself (the keycaps share a width per column)
            col.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(540) });
            col.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
            int r = 0;
            // 2.9.1: General, Preview and Trimming left, the rest right (it left a large empty block on the left)
            Grid.SetIsSharedSizeScope(col, true);
            foreach (var (title, items) in c == 0 ? sections.Take(3) : sections.Skip(3))
            {
                if (r > 0) Add(col, new Separator { Margin = new Thickness(0, 14, 0, 0) }, r++, 0, 2);
                var head = new TextBlock { Text = title };
                head.SetResourceReference(StyleProperty, "SectionText");
                head.Margin = new Thickness(0, r == 0 ? 0 : 12, 0, 6);
                Add(col, head, r++, 0, 2);
                bool firstTip = true;
                foreach (var (desc, key) in items)
                {
                    if (key.Length == 0)                         // a tip: one muted line, no right column
                    {
                        var tip = new TextBlock { Text = desc, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, firstTip ? 8 : 2, 0, 2) };
                        tip.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
                        Add(col, tip, r++, 0, 2);
                        firstTip = false;
                        continue;
                    }
                    Add(col, Row(desc, key[1..]), r++, 0, 2);
                }
            }
            Grid.SetColumn(col, c * 2);
            box.Children.Add(col);
        }
        var note = new TextBlock
        {
            Text = "Click a shortcut to change it. Esc, Enter and Tab keep their usual job.",
            Margin = new Thickness(0, 18, 0, 0),
        };
        note.SetResourceReference(StyleProperty, "MutedText");
        Grid.SetRow(note, 1);
        Grid.SetColumnSpan(note, 2);
        box.Children.Add(note);
        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0), Padding = new Thickness(18, 5, 18, 6) };
        close.SetResourceReference(StyleProperty, "AccentButtonStyle");   // 2.9.1: like the other windows
        close.Click += (_, _) => Close();
        Grid.SetRow(close, 1);
        Grid.SetColumn(close, 2);
        box.Children.Add(close);
        Content = box;
        PreviewKeyDown += OnKey;
    }

    /// <summary>The effect buttons' colours: a square before their rows instead of "(blue)" in the text.</summary>
    static readonly Dictionary<string, string> EffectColour = new() { ["speed"] = "#3B82F6", ["slow"] = "#EC4899", ["pause"] = "#F97316", ["zoom"] = "#F4F4F5" };

    /// <summary>2.9.1: one shortcut row: the text (with its effect's colour square), the keycaps, and a pencil that shows
    /// with a light highlight while the row is under the mouse. A click anywhere on the row changes the key.</summary>
    UIElement Row(string desc, string act)
    {
        var hb = BindRow(act);
        var text = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
        if (EffectColour.TryGetValue(act, out var hex))
        {
            var sq = new System.Windows.Shapes.Rectangle { Width = 10, Height = 10, RadiusX = 3, RadiusY = 3, Margin = new Thickness(0, 1, 8, 0),
                                                           Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)) };
            if (act == "zoom") { sq.Stroke = System.Windows.Media.Brushes.Gray; sq.StrokeThickness = 1; }
            text.Children.Add(sq);
            desc = System.Text.RegularExpressions.Regex.Replace(desc, @" \((blue|pink|orange|white)\)", "");
        }
        text.Children.Add(new TextBlock { Text = desc, VerticalAlignment = VerticalAlignment.Center });
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "Keys" });
        Grid.SetColumn(hb, 1);
        g.Children.Add(text);
        g.Children.Add(hb);
        var rowBox = new Border { Child = g, CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 0, 4, 0), Margin = new Thickness(-8, 0, -4, 0),
                                  Background = System.Windows.Media.Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = "Click to change this shortcut" };
        rowBox.MouseEnter += (_, _) => { rowBox.SetResourceReference(Border.BackgroundProperty, "CsHover"); hb.SetHot(true); };
        rowBox.MouseLeave += (_, _) => { rowBox.Background = System.Windows.Media.Brushes.Transparent; hb.SetHot(false); };
        rowBox.MouseLeftButtonUp += (_, _) => hb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, hb));
        return rowBox;
    }

    /// <summary>One rebindable shortcut: its keys as keycaps with a pencil; a click opens the recording dialog
    /// (Save / Reset to the original / Cancel).</summary>
    HotkeyButton BindRow(string act)
    {
        var hb = new HotkeyButton { Margin = new Thickness(0, 1, 0, 1), InRow = true };
        AutomationProperties.SetHelpText(hb, act);
        void Refresh() => hb.Chord = vm.Keys[act];
        hb.Click += (_, _) =>
        {
            string def = C.KeyDefaults[act];
            var presetKeys = vm.Presets.Select(p => p.Key).ToHashSet();
            var d = new HotkeyDialog(this, Text.KeyText[act], vm.Keys[act], ch =>
            {
                if (ch == vm.Keys[act]) return null;
                var why = vm.KeyConflict(ch, act);
                // back to the original key: another shortcut holding it gets its own original back
                return why is not null && ch == def && !presetKeys.Contains(ch) && !C.FixedKeys.Contains(ch) ? null : why;
            }, def, plainOnly: false);
            d.ShowDialog();
            if (d.Result is not { } ch || ch == vm.Keys[act]) return;
            if (ch == def) vm.ResetKey(act); else vm.SetKey(act, ch);
            vm.Dialogs.Toast($"{Text.KeyText[act]}: {Text.ChordLabel(ch)}");
            RefreshAll();
        };
        refreshers.Add(Refresh);
        Refresh();
        return hb;
    }

    void RefreshAll()
    {
        foreach (var r in refreshers) r();
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    static void Add(Grid g, UIElement e, int row, int col, int span = 1)
    {
        while (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        Grid.SetColumnSpan(e, span);
        g.Children.Add(e);
    }
}
