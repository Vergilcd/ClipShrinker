namespace ClipShrinker.App.ViewModels;

/// <summary>Message boxes and file dialogs; the main window implements them so the view-model stays testable.</summary>
public interface IDialogs
{
    void Info(string title, string text);
    void Warn(string title, string text);
    void Error(string title, string text);
    bool YesNo(string title, string text);
    /// <summary>Own button labels; returns the index of the pressed button, or cancel when closed.</summary>
    int Choice(string title, string text, IReadOnlyList<string> buttons, int defaultIndex, int cancelIndex);
    string[] OpenVideos();
    string? OpenProject();
    string? SaveFile(string title, string fileName, string? folder);
    string? SaveProjectFile(string fileName, string? folder);
    string? PickFolder(string title, string? folder);
    void Toast(string text);
}
