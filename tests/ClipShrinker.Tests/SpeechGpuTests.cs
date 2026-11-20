using ClipShrinker.Core.Analysis;

namespace ClipShrinker.Tests;

/// <summary>2.8.0-beta2: speech to text asks for the graphics card first; a failed GPU run is run again on the CPU,
/// and the CPU is used from then on (Speech.GpuFailed).</summary>
[Collection("SpeechGpu")]
public class SpeechGpuTests : IDisposable
{
    readonly string dir = Directory.CreateTempSubdirectory("cs_speechgpu_").FullName;
    readonly bool before = Speech.GpuFailed;

    public void Dispose()
    {
        Speech.GpuFailed = before;
        try { Directory.Delete(dir, true); } catch (IOException) { }
    }

    /// <summary>A stand-in for ffmpeg: notes each call's use_gpu value, fails when it is 1 (and failGpu).</summary>
    string FakeFfmpeg(bool failGpu)
    {
        string f = Path.Combine(dir, "ffmpeg.cmd");
        File.WriteAllText(f, "@echo off\r\n" +
            "echo %* | findstr /c:\"use_gpu=1\" >nul && (echo gpu>>\"%~dp0calls.txt\" & " + (failGpu ? "echo vulkan error 1>&2 & exit /b 3" : "exit /b 0") + ")\r\n" +
            "echo cpu>>\"%~dp0calls.txt\"\r\nexit /b 0\r\n");
        return f;
    }

    string[] Calls() => File.Exists(Path.Combine(dir, "calls.txt")) ? File.ReadAllLines(Path.Combine(dir, "calls.txt")).Select(s => s.Trim()).ToArray() : [];

    [Fact]
    public void AFailedGpuRunIsRunAgainOnTheCpuAndTheCpuStays()
    {
        Speech.GpuFailed = false;
        string ff = FakeFfmpeg(failGpu: true);
        Assert.Empty(Speech.Transcribe(ff, dir, "clip.mp4", 1, 0, 5, "auto"));
        Assert.Equal(["gpu", "cpu"], Calls());
        Assert.True(Speech.GpuFailed);
        Speech.Transcribe(ff, dir, "clip.mp4", 1, 0, 5, "auto");
        Assert.Equal(["gpu", "cpu", "cpu"], Calls());
    }

    [Fact]
    public void AWorkingGpuRunIsNotRepeated()
    {
        Speech.GpuFailed = false;
        string ff = FakeFfmpeg(failGpu: false);
        Speech.Transcribe(ff, dir, "clip.mp4", 1, 0, 5, "auto");
        Assert.Equal(["gpu"], Calls());
        Assert.False(Speech.GpuFailed);
    }
}
