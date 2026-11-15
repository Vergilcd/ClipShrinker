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
        var linkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        linkRow.Children.Add(new TextBlock(link) { VerticalAlignment = VerticalAlignment.Center, ToolTip = "Open the project page on GitHub (source code, releases, bug reports)" });
        linkRow.Children.Add(UpdateLine());
        head.Children.Add(linkRow);
        head.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 12) });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var close = new Button { Content = "Close", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), Padding = new Thickness(16, 5, 16, 6) };
        close.Click += (_, _) => Close();
        DockPanel.SetDock(close, Dock.Bottom);
        root.Children.Add(close);

        string docs = AppPaths.DocsDir;
        var tabs = new TabControl();
        tabs.Items.Add(Tab("Changelog", Read(Path.Combine(docs, "CHANGELOG.txt")) ?? "CHANGELOG.txt was not found."));
        var parts = new List<string>();
        foreach (var (title, path) in new[]
                 {
                     (C.AppName, Path.Combine(docs, "LICENSE.txt")),
                     ("ffmpeg / ffprobe", Path.Combine(docs, "ffmpeg-LICENSE.txt")),
                     ("RIFE ncnn Vulkan (Timeline effects plugin)", AppPaths.RifeLicense),
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

    // ------------------------------------------------------------------ update check (2.6)
    static readonly System.Net.Http.HttpClient http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>A dot and a short text after the GitHub link: blinking green while it asks GitHub, then steady green
    /// "You have the latest version", blinking green "Version x is available" (a link to the release page), or grey
    /// when GitHub could not be reached. Only shown here; nothing pops up and nothing is downloaded.</summary>
    static FrameworkElement UpdateLine()
    {
        var green = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50));
        var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = green, Margin = new Thickness(18, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock { Text = "Checking for updates...", VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        System.Windows.Automation.AutomationProperties.SetName(row, "Update check");
        row.Children.Add(dot);
        row.Children.Add(text);

        var blink = new System.Windows.Media.Animation.DoubleAnimation(1, 0.2, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
        };
        void Blink(bool on) => dot.BeginAnimation(OpacityProperty, on ? blink : null);
        Blink(true);

        _ = Check();
        return row;

        async Task Check()
        {
            string? latest = null;
            try
            {
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, UpdateCheck.ApiUrl);
                req.Headers.UserAgent.ParseAdd($"ClipShrinker/{C.AppVersion}");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = await http.SendAsync(req);
                if (resp.IsSuccessStatusCode) latest = UpdateCheck.LatestVersion(await resp.Content.ReadAsStringAsync());
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or TaskCanceledException or IOException) { }

            if (latest is null)
            {
                Blink(false);
                dot.Fill = Brushes.Gray;
                text.Text = "Could not check for updates (offline?)";
            }
            else if (UpdateCheck.IsNewer(latest, C.AppVersion))
            {
                var link = new Hyperlink(new Run($"Version {latest} is available")) { NavigateUri = new Uri(UpdateCheck.ReleasesUrl) };
                link.RequestNavigate += (_, e) => Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                text.Inlines.Clear();
                text.Inlines.Add(link);
                text.ToolTip = "Open the release page on GitHub (download and changelog). Updating is up to you.";
            }
            else
            {
                Blink(false);
                text.Text = "You have the latest version";
            }
        }
    }
}
