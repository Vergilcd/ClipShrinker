using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ClipShrinker.Core;

namespace ClipShrinker.App.Views;

/// <summary>Version, changelog, licenses and readme, read from the text files next to the program (reference show_about).</summary>
public sealed class AboutWindow : Window
{
    public AboutWindow()
    {
        Title = $"About {C.AppName}";
        Width = 820;
        Height = 640;
        MinWidth = 560;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = $"{C.AppName}  v{C.AppVersion}", FontSize = 22, FontWeight = FontWeights.Bold });
        var by = new TextBlock { Text = "Made with Claude (Anthropic) by Vergil. Free and open source (MIT license).", Margin = new Thickness(0, 4, 0, 2) };
        by.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        head.Children.Add(by);
        var link = new Hyperlink(new Run(C.RepoUrl)) { NavigateUri = new Uri(C.RepoUrl) };
        link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        head.Children.Add(new TextBlock(link) { Margin = new Thickness(0, 0, 0, 12), ToolTip = "Open the project page on GitHub (source code, releases, bug reports)" });
        head.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(16, 5, 16, 6) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        string docs = AppPaths.DocsDir, lib = AppPaths.DocsLibraryDir;
        var tabs = new TabControl();
        tabs.Items.Add(Tab("Changelog", Read(Path.Combine(docs, "CHANGELOG.txt")) ?? "CHANGELOG.txt was not found."));
        var parts = new List<string>();
        foreach (var (title, path) in new[]
                 {
                     (C.AppName, Path.Combine(docs, "LICENSE.txt")),
                     ("ffmpeg / ffprobe", Path.Combine(docs, "ffmpeg-LICENSE.txt")),
                     ("RIFE ncnn Vulkan", Path.Combine(lib, "rife", "LICENSE.txt")),
                     ("Third-party notices", Path.Combine(docs, "THIRD-PARTY-NOTICES.txt")),
                 })
            if (Read(path) is { } t) parts.Add($"{new string('=', 70)}\n{title}\n{new string('=', 70)}\n\n{t}");
        tabs.Items.Add(Tab("Licenses", parts.Count > 0 ? string.Join("\n\n\n", parts) : "No license files were found."));
        tabs.Items.Add(Tab("Readme", Read(Path.Combine(docs, "README.txt")) ?? "README.txt was not found."));
        root.Children.Add(tabs);
        Content = root;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    static string? Read(string path)
    {
        try { return File.ReadAllText(path).Trim(); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static TabItem Tab(string title, string text)
    {
        var tb = new TextBox
        {
            Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, BorderThickness = new Thickness(0), Padding = new Thickness(12, 10, 12, 10),
        };
        return new TabItem { Header = title, Content = tb };
    }
}
