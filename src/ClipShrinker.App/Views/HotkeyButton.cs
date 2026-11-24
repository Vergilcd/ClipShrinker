using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>A shortcut shown as accent keycaps plus a pencil (the PowerToys look). Hovering it lights up a rounded
/// background; clicking it raises Click, and the owner opens a <see cref="HotkeyDialog"/>.</summary>
public sealed class HotkeyButton : Button
{
    public static readonly DependencyProperty ChordProperty = DependencyProperty.Register(
        nameof(Chord), typeof(string), typeof(HotkeyButton), new PropertyMetadata("", (d, _) => ((HotkeyButton)d).Build()));

    /// <summary>The key in the saved form ("ctrl+shift+s", "f2", "q"); empty = no hotkey.</summary>
    public string Chord { get => (string)GetValue(ChordProperty); set => SetValue(ChordProperty, value); }

    /// <summary>Shown instead of keycaps when there is no hotkey.</summary>
    public string EmptyText { get => emptyText; init { emptyText = value; Build(); } }
    string emptyText = "Not set";

    readonly StackPanel row = new() { Orientation = Orientation.Horizontal };
    TextBlock? pen;

    /// <summary>2.9.1 (Shortcuts window): the whole row around it lights up instead of the button, and the pencil
    /// shows only while the row is under the mouse (<see cref="SetHot"/>).</summary>
    public bool InRow { get => inRow; init { inRow = value; Build(); } }
    bool inRow, hot;

    /// <summary>The row under the mouse (InRow): show the pencil.</summary>
    public void SetHot(bool on)
    {
        hot = on;
        if (pen is not null) pen.Opacity = on ? 1 : 0;
    }

    public HotkeyButton()
    {
        Focusable = false;
        Cursor = Cursors.Hand;
        Padding = new Thickness(4);
        HorizontalAlignment = HorizontalAlignment.Right;
        Background = Brushes.Transparent;
        Template = MakeTemplate();
        Content = row;
        MouseEnter += (_, _) => { if (!inRow) SetResourceReference(BackgroundProperty, "CsHover"); };
        MouseLeave += (_, _) => Background = Brushes.Transparent;
        Build();
    }

    /// <summary>Screen readers and UI automation see one button named after the shortcut, not each keycap.</summary>
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed class Peer(Button owner) : System.Windows.Automation.Peers.ButtonAutomationPeer(owner)
    {
        protected override List<System.Windows.Automation.Peers.AutomationPeer>? GetChildrenCore() => null;
    }

    static ControlTemplate MakeTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        bd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        t.VisualTree = bd;
        return t;
    }

    void Build()
    {
        row.Children.Clear();
        if (string.IsNullOrEmpty(Chord))
        {
            var none = new TextBlock { Text = EmptyText, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) };
            none.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var holder = new Border { Height = 30, Child = none };              // same height as the keycaps
            row.Children.Add(holder);
        }
        else foreach (var part in Parts(Chord)) row.Children.Add(Keycap(part, false));
        pen = new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0),
        };
        pen.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        if (inRow) pen.Opacity = hot ? 1 : 0;
        row.Children.Add(pen);
        AutomationProperties.SetName(this, string.IsNullOrEmpty(Chord) ? EmptyText : Text.ChordLabel(Chord));
    }

    /// <summary>"ctrl+shift+s" -> Ctrl, Shift, S (the labels of the Shortcuts window).</summary>
    public static string[] Parts(string chord) => Text.ChordLabel(chord).Split(" + ");

    /// <summary>One key: an accent rounded box with the key's name.</summary>
    public static Border Keycap(string label, bool large)
    {
        var tb = new TextBlock
        {
            Text = label, FontWeight = FontWeights.SemiBold, FontSize = large ? 16 : 13,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "CsOnAccent");
        var b = new Border
        {
            CornerRadius = new CornerRadius(large ? 6 : 4), MinWidth = large ? 48 : 34, Height = large ? 44 : 30,
            Padding = new Thickness(large ? 12 : 8, 0, large ? 12 : 8, 0), Margin = new Thickness(large ? 4 : 2, 0, large ? 4 : 2, 0), Child = tb,
        };
        b.SetResourceReference(Border.BackgroundProperty, "CsAccent");
        return b;
    }
}

/// <summary>Records a new key for a shortcut or a bitrate preset (the PowerToys "activation shortcut" dialog):
/// the pressed keys appear as keycaps; a key that is taken shows the reason and cannot be saved.</summary>
public sealed class HotkeyDialog : Window
{
    readonly Func<string, string?> check;
    readonly bool plainOnly;
    readonly WrapPanel caps = new() { HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border errorBar = new() { CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock errorText = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button save;
    string? pending;

    /// <summary>The chosen key ("" = cleared), or null when the dialog was cancelled.</summary>
    public string? Result { get; private set; }

    /// <param name="check">Why a key cannot be used (null = fine).</param>
    /// <param name="resetTo">The "Reset" button's key (the default); null = a "Clear" button (no hotkey) instead.</param>
    /// <param name="plainOnly">A single key without Ctrl / Alt / Shift: a letter, a digit or F1 - F24 (preset hotkeys).</param>
    public HotkeyDialog(Window owner, string heading, string current, Func<string, string?> check, string? resetTo, bool plainOnly)
    {
        this.check = check;
        this.plainOnly = plainOnly;
        Owner = owner;
        Title = "Change shortcut";
        SizeToContent = SizeToContent.Height;
        Width = 440;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");

        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
        root.Children.Add(new TextBlock { Text = heading, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        var hint = new TextBlock
        {
            Text = plainOnly ? "Press a key: a letter, a digit or F1 - F24." : "Press a key or a combination. Ctrl, Alt and Shift can be added to any key.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0),
        };
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        root.Children.Add(hint);

        var stage = new Border { CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(12), MinHeight = 76, Child = caps };
        stage.SetResourceReference(Border.BackgroundProperty, "CsHover");
        caps.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(stage);

        errorBar.SetResourceReference(Border.BackgroundProperty, "CsErrorBg");
        errorText.SetResourceReference(TextBlock.ForegroundProperty, "CsErrorText");
        var errRow = new DockPanel();
        var icon = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(TextBlock.ForegroundProperty, "CsErrorText");
        errRow.Children.Add(icon);
        errRow.Children.Add(errorText);
        errorBar.Child = errRow;
        root.Children.Add(errorBar);

        var buttons = new Grid { Margin = new Thickness(0, 20, 0, 0) };
        for (int i = 0; i < 3; i++) buttons.ColumnDefinitions.Add(new ColumnDefinition());
        save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 4, 0), Focusable = false };
        save.SetResourceReference(StyleProperty, "AccentButtonStyle");
        var reset = new Button { Content = resetTo is null ? "Clear" : "Reset", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 4, 0), Focusable = false,
                                 ToolTip = resetTo is null ? "No hotkey" : "Back to the original key: " + Text.ChordLabel(resetTo) };
        var cancel = new Button { Content = "Cancel", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        Grid.SetColumn(reset, 1);
        Grid.SetColumn(cancel, 2);
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);
        Content = root;

        save.Click += (_, _) => Save();
        reset.Click += (_, _) => Set(resetTo ?? "");
        cancel.Click += (_, _) => Close();
        PreviewKeyDown += OnKey;
        PreviewKeyUp += (_, _) => { if (pending is null) ShowCaps(HeldModifiers(), false); };
        Set(current, initial: true);
    }

    static List<string> HeldModifiers()
    {
        var m = Keyboard.Modifiers;
        var l = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) l.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) l.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) l.Add("Shift");
        return l;
    }

    void OnKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        bool noMods = Keyboard.Modifiers == ModifierKeys.None;
        if (k == Key.Escape && noMods) { Close(); return; }
        if (k == Key.Enter && noMods) { Save(); return; }
        string? ch = KeyNames.Chord(e);
        if (ch is null)                                          // only Ctrl / Alt / Shift so far: show them live
        {
            pending = null;
            save.IsEnabled = false;
            errorBar.Visibility = Visibility.Collapsed;
            ShowCaps(HeldModifiers(), false);
            return;
        }
        Set(ch);
    }

    void Set(string ch, bool initial = false)
    {
        pending = ch;
        ShowCaps(ch.Length == 0 ? [] : HotkeyButton.Parts(ch).ToList(), ch.Length == 0);
        string? err = ch.Length == 0 ? null
            : plainOnly && !IsPlainKey(ch) ? "A preset hotkey is a single key without Ctrl / Alt / Shift: a letter, a digit or F1 - F24."
            : check(ch);
        errorText.Text = err ?? "";
        errorBar.Visibility = err is null ? Visibility.Collapsed : Visibility.Visible;
        save.IsEnabled = err is null && !initial;
    }

    static bool IsPlainKey(string ch) =>
        !ch.Contains('+') && ((ch.Length == 1 && char.IsLetterOrDigit(ch[0])) || (ch.Length > 1 && ch[0] == 'f' && ch[1..].All(char.IsDigit)));

    void ShowCaps(List<string> parts, bool cleared)
    {
        caps.Children.Clear();
        if (parts.Count == 0)
        {
            var t = new TextBlock { Text = cleared ? "No hotkey" : "Press a key...", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 0, 12) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            caps.Children.Add(t);
        }
        foreach (var p in parts) caps.Children.Add(HotkeyButton.Keycap(p, true));
    }

    void Save()
    {
        if (pending is null || !save.IsEnabled) return;
        Result = pending;
        Close();
    }
}
