using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.1.0", 0, 1, 0)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V2.0.0-beta.1", 2, 0, 0)]
    [InlineData("3.4.5+build7", 3, 4, 5)]
    public void ParsesGitHubReleaseTags(string tag, int major, int minor, int build)
    {
        Assert.True(ReleaseVersion.TryParseTag(tag, out var version));
        Assert.Equal(new Version(major, minor, build), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1")]
    public void RejectsInvalidReleaseTags(string tag)
    {
        Assert.False(ReleaseVersion.TryParseTag(tag, out _));
    }
}
