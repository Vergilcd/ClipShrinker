using ClipShrinker.Core;
using ClipShrinker.Core.Media;

namespace ClipShrinker.Tests;

/// <summary>2.6: the About window's update line and the top bar's GPU notice.</summary>
public class UpdateCheckTests
{
    [Theory]
    [InlineData("""{"tag_name":"v2.7.0","name":"x"}""", "2.7.0")]
    [InlineData("""{"tag_name":"2.6.1"}""", "2.6.1")]
    [InlineData("""{"tag_name":"nightly"}""", null)]
    [InlineData("""{"message":"Not Found"}""", null)]
    [InlineData("not json", null)]
    public void LatestVersionFromGitHub(string json, string? want) => Assert.Equal(want, UpdateCheck.LatestVersion(json));

    [Fact]
    public void NewerVersions()
    {
        Assert.True(UpdateCheck.IsNewer("2.7.0", "2.6.0"));
        Assert.True(UpdateCheck.IsNewer("2.10.0", "2.9.1"));
        Assert.False(UpdateCheck.IsNewer("2.6.0", "2.6.0"));
        Assert.False(UpdateCheck.IsNewer("2.5.0", "2.6.0"));
    }

    [Fact]
    public void UrlsPointAtTheRepo()
    {
        Assert.Equal("https://api.github.com/repos/Vergilcd/ClipShrinker/releases/latest", UpdateCheck.ApiUrl);
        Assert.Equal("https://github.com/Vergilcd/ClipShrinker/releases/latest", UpdateCheck.ReleasesUrl);
    }

    [Fact]
    public void CodecHintsPointAtTheBestOne()
    {
        string[] nvidia = ["h264_nvenc", "hevc_nvenc", "h264_mf_hw", "hevc_mf_hw"];
        Assert.Equal("hevc_nvenc", Encoders.Recommended(nvidia));
        Assert.Equal("recommended for your NVIDIA GPU", Encoders.Hint("hevc_nvenc", nvidia));
        Assert.Equal("NVIDIA GPU, plays on older devices", Encoders.Hint("h264_nvenc", nvidia));
        Assert.Equal("Windows encoder, lower quality", Encoders.Hint("hevc_mf_hw", nvidia));
        string[] amd = ["h264_amf", "hevc_amf"];
        Assert.Equal("recommended for your AMD GPU", Encoders.Hint("hevc_amf", amd));
        string[] windows = ["h264_mf_hw", "hevc_mf_hw"];
        Assert.Equal("recommended for this PC (Windows encoder)", Encoders.Hint("hevc_mf_hw", windows));
        Assert.Equal("Windows encoder, plays on older devices", Encoders.Hint("h264_mf_hw", windows));
        Assert.Equal("slow, no GPU encoder works here", Encoders.Hint("h264_mf", ["h264_mf"]));
    }

    [Fact]
    public void OnlyTheWindowsCpuEncoderIsNotGpu()
    {
        Assert.Equal(["h264_mf", "hevc_mf"], Encoders.All.Where(e => !e.Gpu).Select(e => e.Key));
    }
}
