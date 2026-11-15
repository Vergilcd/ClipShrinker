using System.Windows;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>The plugin list: on / off switch, version, author, changelog, delete and restore (MainViewModel.Plugins.cs).</summary>
public partial class PluginsWindow : Window
{
    public PluginsWindow(MainViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
