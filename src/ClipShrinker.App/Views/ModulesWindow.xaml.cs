using System.Windows;
using System.Windows.Input;
using ClipShrinker.App.ViewModels;

namespace ClipShrinker.App.Views;

/// <summary>The module list: on / off switch, version, author, changelog, delete and restore (MainViewModel.Modules.cs).</summary>
public partial class ModulesWindow : Window
{
    public ModulesWindow(MainViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
