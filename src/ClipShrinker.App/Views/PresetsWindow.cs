using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>Bitrate presets: name, Mbps and a hotkey (reference show_presets / _presets_render / _preset_edit).</summary>
public sealed class PresetsWindow : Window
{
    readonly MainViewModel vm;
    readonly Grid box = new() { Margin = new Thickness(16) };

    public PresetsWindow(MainViewModel vm)
    {
        this.vm = vm;
        Title = "Bitrate presets";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        Content = box;
        Render();
        vm.PresetsChanged += Render;
        Closed += (_, _) => vm.PresetsChanged -= Render;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center };
        t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        return t;
    }

    void Add(UIElement e, int row, int col, int span = 1)
    {
        while (box.RowDefinitions.Count <= row) box.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(e, row);
        Grid.SetColumn(e, col);
        Grid.SetColumnSpan(e, span);
        box.Children.Add(e);
    }

    void Render()
    {
        box.Children.Clear();
        box.RowDefinitions.Clear();
        box.ColumnDefinitions.Clear();
        for (int i = 0; i < 5; i++) box.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var head = new TextBlock { Text = "BITRATE PRESETS", Margin = new Thickness(0, 0, 0, 3) };
        head.SetResourceReference(StyleProperty, "SectionText");
        head.Margin = new Thickness(0, 0, 0, 3);
        Add(head, 0, 0, 5);
        var info = Muted("Pick one for a clip in its title bar, or press its hotkey on the active clip. Used with Fixed bitrate, when the clips are exported as separate files (or for a single clip).");
        info.TextWrapping = TextWrapping.Wrap;
        info.MaxWidth = 400;
        info.HorizontalAlignment = HorizontalAlignment.Left;
        info.Margin = new Thickness(0, 0, 0, 18);
        Add(info, 1, 0, 5);
        int r = 2;
        const double rowH = 30;
        for (int i = 0; i < vm.Presets.Count; i++)
        {
            var p = vm.Presets[i];
            int idx = i;
            if (i > 0)                                           // a thin line between the presets
            {
                var line = new System.Windows.Shapes.Rectangle { Height = 1, Margin = new Thickness(0, 8, 0, 8), SnapsToDevicePixels = true };
                line.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "CsBorder");
                Add(line, r++, 0, 5);
            }
            Add(new TextBlock { Text = p.Name, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center }, r, 0);
            Add(new TextBlock { Text = $"{Core.Py.Py.G(p.Mbps)} Mbps", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) }, r, 1);
            var key = new HotkeyButton { Chord = p.Key, EmptyText = "No hotkey", Margin = new Thickness(14, 0, 0, 0), ToolTip = "Click to change the hotkey" };
            System.Windows.Automation.AutomationProperties.SetHelpText(key, "hotkey of " + p.Name);
            key.Click += (_, _) =>
            {
                if (AskKey(this, p.Name, p.Key, idx) is { } k) vm.SetPresetKey(idx, k);
            };
            Add(key, r, 2);
            var edit = new Button
            {
                Content = "Edit", Margin = new Thickness(18, 0, 4, 0), Padding = new Thickness(12, 0, 12, 0), Height = rowH, MinHeight = 0,
                VerticalContentAlignment = VerticalAlignment.Center, Focusable = false,
            };
            edit.Click += (_, _) => Edit(idx);
            Add(edit, r, 3);
            // 2.9.1: the quiet bin of the Modules window and the clip title bars (red only when pointed at); it asks first
            var del = new Button { Content = "", FontSize = 14, Margin = new Thickness(4, 0, 0, 0), Width = rowH, Height = rowH,
                                   ToolTip = "Delete this preset (asks first)" };
            del.SetResourceReference(StyleProperty, "HeadQuietX");
            del.SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
            System.Windows.Automation.AutomationProperties.SetName(del, "Delete " + p.Name);
            del.Click += (_, _) => vm.DeletePreset(idx);
            Add(del, r, 4);
            r++;
        }
        if (vm.Presets.Count == 0) { var none = Muted("No presets yet."); none.Margin = new Thickness(0, 4, 0, 4); Add(none, r++, 0, 5); }
        // 2.9.1: "preset" everywhere (it said "profile"), and a Close button like the other windows (Esc still closes)
        var bottom = new DockPanel { Margin = new Thickness(0, 18, 0, 0), LastChildFill = false };
        var close = new Button { Content = "Close", Padding = new Thickness(18, 5, 18, 6), Focusable = false };
        close.SetResourceReference(StyleProperty, "AccentButtonStyle");
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Right);
        var add = new Button { Content = "+ Add preset", Focusable = false };
        add.Click += (_, _) => Edit(null);
        bottom.Children.Add(close);
        bottom.Children.Add(add);
        Add(bottom, r, 0, 5);
    }

    /// <summary>Records a preset hotkey (null = cancelled, "" = none); taken keys show why and cannot be saved.</summary>
    string? AskKey(Window owner, string name, string current, int? index)
    {
        var d = new HotkeyDialog(owner, name.Length > 0 ? $"Hotkey of \"{name}\"" : "Hotkey of the new preset", current,
                                 k => vm.PresetKeyConflict(k, index), resetTo: null, plainOnly: true);
        d.ShowDialog();
        return d.Result;
    }

    /// <summary>The New / Edit preset dialog; the hotkey is shown as keycaps, a click records a new one.</summary>
    void Edit(int? index)
    {
        var cur = index is { } i ? vm.Presets[i] : new Core.Settings.Preset("", 10.0, "");
        var d = new Window
        {
            Title = index is null ? "New preset" : "Edit preset", Owner = this, SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = Left + 30, Top = Top + 40,
        };
        d.SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
        var g = new Grid { Margin = new Thickness(16) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (int k = 0; k < 5; k++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement e, int row, int col, int span = 1) { Grid.SetRow(e, row); Grid.SetColumn(e, col); Grid.SetColumnSpan(e, span); g.Children.Add(e); }
        var name = new TextBox { Text = cur.Name, Width = 200, Margin = new Thickness(0, 3, 0, 3) };
        var mbps = new TextBox { Text = Core.Py.Py.G(cur.Mbps), Width = 90, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 3) };
        var key = new HotkeyButton { Chord = cur.Key, EmptyText = "No hotkey", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-4, 3, 0, 3),
                                     ToolTip = "Click to change the hotkey" };
        key.Click += (_, _) => { if (AskKey(d, name.Text.Trim(), key.Chord, index) is { } k) key.Chord = k; };
        Put(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, 0, 0);
        Put(name, 0, 1);
        Put(new TextBlock { Text = "Bitrate (Mbps):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, 1, 0);
        Put(mbps, 1, 1);
        Put(new TextBlock { Text = "Hotkey:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }, 2, 0);
        Put(key, 2, 1);
        Put(Muted("click the key to change it"), 3, 0, 2);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 8, 0), IsCancel = true, Focusable = false };
        var save = new Button { Content = "Save", Focusable = false };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        void Ok()
        {
            if (vm.SavePreset(index, name.Text, mbps.Text, key.Chord) is { } err)
                MessageBox.Show(d, err.Text, err.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            else d.Close();
        }
        save.Click += (_, _) => Ok();
        cancel.Click += (_, _) => d.Close();
        d.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { Ok(); e.Handled = true; }
            else if (e.Key == Key.Escape) { d.Close(); e.Handled = true; }
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Put(buttons, 4, 0, 2);
        d.Content = g;
        d.Loaded += (_, _) => name.Focus();
        d.ShowDialog();
    }
}
