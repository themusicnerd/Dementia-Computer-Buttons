using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Infrastructure;

public sealed class GitHubUpdateService(IConfigurationService configuration, ILoggingService logging) : IUpdateService
{
    private static readonly HttpClient Client = CreateClient();
    public Version CurrentVersion { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public async Task<UpdateRelease?> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = configuration.Current.Updates;
        if (!IsSafeName(settings.GitHubOwner) || !IsSafeName(settings.GitHubRepository))
            throw new InvalidOperationException("The GitHub update owner or repository setting is invalid.");
        var endpoint = $"https://api.github.com/repos/{settings.GitHubOwner}/{settings.GitHubRepository}/releases/latest";
        var release = await Client.GetFromJsonAsync<GitHubRelease>(endpoint, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("GitHub returned an empty release response.");
        if (release.Draft || release.Prerelease || !ReleaseVersion.TryParseTag(release.TagName, out var version) || version <= CurrentVersion)
            return null;
        var msiName = $"DementiaComputerButtons-{version.ToString(3)}-x64.msi";
        var msi = release.Assets.FirstOrDefault(asset => asset.Name.Equals(msiName, StringComparison.OrdinalIgnoreCase));
        var checksum = release.Assets.FirstOrDefault(asset => asset.Name.Equals(msiName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (msi is null || checksum is null || !IsGitHubDownload(msi.DownloadUrl) || !IsGitHubDownload(checksum.DownloadUrl))
            throw new InvalidOperationException("The latest release does not contain a matching MSI and SHA-256 checksum.");
        logging.Information("update_available", $"current={CurrentVersion}; available={version}; tag={release.TagName}");
        return new UpdateRelease(version, release.TagName, msi.DownloadUrl, checksum.DownloadUrl);
    }

    public async Task DownloadAndInstallAsync(UpdateRelease release, CancellationToken cancellationToken = default)
    {
        if (!IsGitHubDownload(release.MsiUrl) || !IsGitHubDownload(release.ChecksumUrl))
            throw new InvalidOperationException("The update download address is not a trusted GitHub HTTPS address.");
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DementiaComputerButtons", "updates", release.Version.ToString(3));
        Directory.CreateDirectory(directory);
        var msiPath = Path.Combine(directory, $"DementiaComputerButtons-{release.Version.ToString(3)}-x64.msi");
        var checksumText = await Client.GetStringAsync(release.ChecksumUrl, cancellationToken).ConfigureAwait(false);
        var expected = checksumText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (expected is null || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidOperationException("The release checksum file is invalid.");
        await using (var source = await Client.GetStreamAsync(release.MsiUrl, cancellationToken).ConfigureAwait(false))
        await using (var destination = new FileStream(msiPath, FileMode.Create, FileAccess.Write, FileShare.None))
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await using var verify = File.OpenRead(msiPath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, cancellationToken).ConfigureAwait(false));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(msiPath);
            throw new InvalidOperationException("The downloaded update failed SHA-256 verification.");
        }
        logging.Information("update_verified", $"version={release.Version}; sha256={actual}");
        var start = new ProcessStartInfo("msiexec.exe")
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"/i \"{msiPath}\""
        };
        _ = Process.Start(start) ?? throw new InvalidOperationException("Windows Installer could not be started.");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DementiaComputerButtons-Updater/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static bool IsSafeName(string value) => !string.IsNullOrWhiteSpace(value) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static bool IsGitHubDownload(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("assets")] GitHubAsset[] Assets);

    private sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string DownloadUrl);
}
