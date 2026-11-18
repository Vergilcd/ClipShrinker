using System.Text.Json.Nodes;

namespace ClipShrinker.Core;

/// <summary>2.6: the update line of the About window. GitHub's "latest release" answer is read for its tag
/// (v2.6.0 -> 2.6.0) and compared with this version. Nothing is downloaded or installed; it is only shown.</summary>
public static class UpdateCheck
{
    public static string ApiUrl => C.RepoUrl.Replace("https://github.com/", "https://api.github.com/repos/") + "/releases/latest";
    public static string ReleasesUrl => C.RepoUrl + "/releases/latest";

    /// <summary>The version of the latest release ("2.6.0"), or null when the answer has none.</summary>
    public static string? LatestVersion(string json)
    {
        try
        {
            string? tag = JsonNode.Parse(json)?["tag_name"]?.GetValue<string>();
            tag = tag?.Trim().TrimStart('v', 'V');
            return string.IsNullOrEmpty(tag) || !char.IsDigit(tag[0]) ? null : tag;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>latest is a newer version than current (2.10.0 > 2.9.1).</summary>
    public static bool IsNewer(string latest, string current) => Modules.ModuleManifest.CompareVersions(latest, current) > 0;
}
