using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class ConfigurationServiceTests
{
    [Fact]
    public async Task LoadsAndValidatesConfiguration()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcb-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"volumeStepPercent":9,"controller":{"baudRate":9600,"protocol":"OLD"}}""");
            var service = new ConfigurationService(path, new TestLog());
            await service.LoadAsync();
            Assert.Equal(9, service.Current.VolumeStepPercent);
            Assert.Equal(115200, service.Current.Controller.BaudRate);
            Assert.Equal("DCB/1", service.Current.Controller.Protocol);
            Assert.Equal(2, service.Warnings.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task InvalidJsonFallsBackWithoutThrowing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcb-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, "not-json");
            var service = new ConfigurationService(path, new TestLog());
            await service.LoadAsync();
            Assert.Equal(5, service.Current.VolumeStepPercent);
            Assert.NotEmpty(service.Warnings);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SavesContentChoicesForNextStartup()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcb-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigurationService(path, new TestLog());
            service.Current.Content.YouTubeUrl = "https://www.youtube.com/watch?v=example";
            service.Current.Content.VideoPath = @"C:\Videos\family.mp4";
            service.Current.Content.StopVideoOnHome = false;
            service.Current.Calls.QuietHours.Enabled = true;
            service.Current.Calls.QuietHours.From = "21:30";
            service.Current.Calls.QuietHours.Until = "07:15";
            service.Current.Startup.StartWithWindows = true;
            service.Current.Updates.CheckAutomatically = false;
            await service.SaveAsync();

            var reloaded = new ConfigurationService(path, new TestLog());
            await reloaded.LoadAsync();
            Assert.Equal(service.Current.Content.YouTubeUrl, reloaded.Current.Content.YouTubeUrl);
            Assert.Equal(service.Current.Content.VideoPath, reloaded.Current.Content.VideoPath);
            Assert.False(reloaded.Current.Content.StopVideoOnHome);
            Assert.True(reloaded.Current.Calls.QuietHours.Enabled);
            Assert.Equal("21:30", reloaded.Current.Calls.QuietHours.From);
            Assert.Equal("07:15", reloaded.Current.Calls.QuietHours.Until);
            Assert.True(reloaded.Current.Startup.StartWithWindows);
            Assert.False(reloaded.Current.Updates.CheckAutomatically);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ExportsAndImportsPortableJsonSettings()
    {
        var activePath = Path.Combine(Path.GetTempPath(), $"dcb-active-{Guid.NewGuid():N}.json");
        var exportPath = Path.Combine(Path.GetTempPath(), $"dcb-export-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigurationService(activePath, new TestLog());
            service.Current.Content.YouTubeUrl = "https://example.test/tv";
            service.Current.Calls.Adrian.DisplayName = "Alex";
            await service.ExportAsync(exportPath);

            service.Current.Content.YouTubeUrl = "changed";
            await service.ImportAsync(exportPath);

            Assert.Equal("https://example.test/tv", service.Current.Content.YouTubeUrl);
            Assert.Equal("Alex", service.Current.Calls.Adrian.DisplayName);
            Assert.Equal("BLACKOUT", service.Current.LongPressButtonMappings["PANEL_7"]);
            Assert.True(File.Exists(activePath));
        }
        finally
        {
            File.Delete(activePath);
            File.Delete(exportPath);
        }
    }

    [Fact]
    public async Task InvalidImportDoesNotReplaceCurrentSettings()
    {
        var activePath = Path.Combine(Path.GetTempPath(), $"dcb-active-{Guid.NewGuid():N}.json");
        var importPath = Path.Combine(Path.GetTempPath(), $"dcb-invalid-{Guid.NewGuid():N}.json");
        try
        {
            var service = new ConfigurationService(activePath, new TestLog());
            service.Current.Content.YouTubeUrl = "keep-this";
            await File.WriteAllTextAsync(importPath, "not-json");

            await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(importPath));

            Assert.Equal("keep-this", service.Current.Content.YouTubeUrl);
        }
        finally
        {
            File.Delete(activePath);
            File.Delete(importPath);
        }
    }

    [Fact]
    public async Task LegacySettingsGainStopLongPressBlackoutMapping()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dcb-legacy-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, """{"longPressButtonMappings":{"PANEL_5":"OPEN_SPOTIFY"}}""");
            var service = new ConfigurationService(path, new TestLog());

            await service.LoadAsync();

            Assert.Equal("BLACKOUT", service.Current.LongPressButtonMappings["PANEL_7"]);
        }
        finally { File.Delete(path); }
    }
}
