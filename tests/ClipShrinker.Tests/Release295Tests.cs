using ClipShrinker.Core.Settings;

namespace ClipShrinker.Tests;

/// <summary>2.9.5 review fixes: saves are whole or not at all, a damaged settings file is kept.</summary>
public class Release295Tests
{
    [Fact]
    public void SafeFileReplacesWholeAndLeavesNoTmp()
    {
        string dir = Directory.CreateTempSubdirectory("cs-safefile-").FullName;
        try
        {
            string f = Path.Combine(dir, "settings.json");
            File.WriteAllText(f, "old");
            SafeFile.WriteAllText(f, "{\"a\": \"é\"}");
            Assert.Equal("{\"a\": \"é\"}", File.ReadAllText(f));
            Assert.Equal(["settings.json"], Directory.GetFiles(dir).Select(Path.GetFileName));
            var bytes = File.ReadAllBytes(f);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);                     // no BOM, as File.WriteAllText

            // a write that cannot finish (folder gone) throws and leaves nothing half-written behind
            string gone = Path.Combine(dir, "missing", "x.json");
            Assert.ThrowsAny<IOException>(() => SafeFile.WriteAllText(gone, "x"));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void DamagedSettingsAreKeptAside()
    {
        string dir = Directory.CreateTempSubdirectory("cs-settings-").FullName;
        try
        {
            string f = Path.Combine(dir, "settings.json");
            File.WriteAllText(f, "{\"size\": \"25\", \"cut off her");
            Assert.Empty(SettingsStore.Load(f));
            Assert.Equal("{\"size\": \"25\", \"cut off her", File.ReadAllText(f + ".broken"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
