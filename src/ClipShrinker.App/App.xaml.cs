using System.Runtime.InteropServices;
using System.Windows;
using ClipShrinker.App.ViewModels;
using ClipShrinker.App.Views;
using Microsoft.Win32;

namespace ClipShrinker.App;

public partial class App : Application
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { SetCurrentProcessExplicitAppUserModelID(Core.C.AppId); } catch (DllNotFoundException) { }   // own taskbar identity
        var vm = new MainViewModel();
        SetTheme(vm.Theme);
        var win = new MainWindow(vm);
        MainWindow = win;
        win.Show();
        _ = Start(vm, e.Args);
    }

    static async Task Start(MainViewModel vm, string[] args)
    {
        if (!await vm.AfterStart()) return;                     // no ffmpeg (and the download was declined / failed)
        // files given on the command line ("Open with", a file dropped on the exe): a project is opened, videos are added
        if (args.Length > 0) await vm.AddFiles(args.Select(System.IO.Path.GetFullPath).ToList());
        else await vm.RestoreLastSession();                     // "Restore the last session on start"
    }

    /// <summary>"dark" or "light" from the Windows app setting (dark when unknown), like the reference system_theme().</summary>
    public static string SystemTheme()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v != 0 ? "light" : "dark";
        }
        catch (System.Security.SecurityException) { return "dark"; }
    }

    /// <summary>Fluent dark / light plus the 1.0.8 palette (reference PALETTE): section headings, the summary panel,
    /// cards and the accent title bar keep the original colours. Light mode gets a light grey summary panel and clip title bars; buttons keep the Fluent light look.</summary>
    public static void SetTheme(string theme)
    {
        bool dark = theme != "light";
        Current.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
        void Res(string key, string dk, string lt)
        {
            var b = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(dark ? dk : lt));
            b.Freeze();
            Current.Resources[key] = b;
        }
        Res("CsSection", "#60CDFF", "#0067C0");     // section headings (accent_text)
        Res("CsFoot", "#141414", "#EEEEEE");        // summary / export panel at the bottom of the sidebar
        Res("CsFootLine", "#3A3A3A", "#D9D9D9");
        Res("CsCard", "#2B2B2B", "#FFFFFF");
        Res("CsHead", "#2B2B2B", "#E6E6E6");        // an inactive clip's title bar
        Res("CsHover", "#3A3A3A", "#E3E3E3");       // hotkey buttons on hover, the key recording area
        Res("CsErrorBg", "#442726", "#FDE7E9");      // "this key is taken" bar
        Res("CsErrorText", "#FF99A4", "#C42B1C");
        Res("CsSwitchOff", "#C5C5C5", "#5C5C5C");    // dark mode switch: track border and knob when off
        Res("CsHeadComboHover", "#262626", "#1A1A1A");   // the preset list hovered on the accent title bar (see-through)
        Res("CsBorder", "#3F3F3F", "#D0D0D0");
        Res("CsAccent", "#4CC2FF", "#0067C0");      // the active / selected clip's title bar and ring
        Res("CsOnAccent", "#06202F", "#FFFFFF");    // text on it
        Res("CsMuted", "#9D9D9D", "#6B6B6B");
        Res("CsGrip", "#5A5A5A", "#A8A8A8");        // the handle of the preview / clip list divider
        Res("CsProgress", "#4CC2FF", "#5AB4F0");       // light mode: light enough for the black caption
        Res("CsProgressText", "#FFFFFF", "#000000");   // the caption on the export progress bar
        Res("CsDone", "#22C55E", "#16A34A");
        Res("CsHeadX", "#FF5A52", "#C42B1C");          // the red cross of the remove button
        Res("CsChipBg", "#24FFFFFF", "#0F000000");      // 2.9.1 zoom chip in a clip's title bar
        Res("CsChipBorder", "#38FFFFFF", "#2E000000");
        Res("CsChipOnAccentBg", "#2406202F", "#29000000");   // ... on the active (accent) title bar
        Res("CsChipOnAccentBorder", "#4D06202F", "#73FFFFFF");
        Res("CsOkText", "#4ADE80", "#116B32");         // 2.9.1 SUMMARY result: the size limit holds (green)
        Res("CsOkBg", "#12301C", "#DDF5E4");
        Res("CsWarnText", "#F0A030", "#8A5000");       // 2.9.1 SUMMARY result: no size limit / value missing (amber)
        Res("CsWarnBg", "#3A2A10", "#FFF1DC");
    }
}
