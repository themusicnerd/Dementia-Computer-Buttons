namespace DementiaComputerButtons.Core.Services;

public static class ReleaseVersion
{
    public static bool TryParseTag(string tag, out Version version)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var prerelease = value.IndexOfAny(['-', '+']);
        if (prerelease >= 0) value = value[..prerelease];
        return Version.TryParse(value, out version!);
    }
}
