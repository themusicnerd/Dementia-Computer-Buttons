using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class ButtonActionServiceTests
{
    [Theory]
    [InlineData("LEFT", 45)]
    [InlineData("RIGHT", 55)]
    public async Task RoutesButtonsThroughConfiguredActions(string button, float expected)
    {
        var volume = new TestVolume();
        var configuration = new TestConfiguration(new AppConfiguration());
        var service = new ButtonActionService(volume, configuration,
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), new TestCalls(), new TestOsd(), new TestLog());
        await service.HandleAsync(new ButtonEvent(button, "DOWN", 1));
        Assert.Equal(expected, volume.CurrentVolumePercent);
    }

    [Fact]
    public async Task IgnoresButtonRelease()
    {
        var volume = new TestVolume();
        var service = new ButtonActionService(volume, new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), new TestCalls(), new TestOsd(), new TestLog());
        await service.HandleAsync(new ButtonEvent("LEFT", "UP", 2));
        Assert.Equal(50, volume.CurrentVolumePercent);
    }

    [Theory]
    [InlineData("LEFT", 48)]
    [InlineData("RIGHT", 52)]
    public async Task RepeatUsesSmallerHoldStep(string button, float expected)
    {
        var volume = new TestVolume();
        var configuration = new AppConfiguration { VolumeStepPercent = 5, VolumeHoldStepPercent = 2 };
        var service = new ButtonActionService(volume, new TestConfiguration(configuration),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), new TestCalls(), new TestOsd(), new TestLog());
        await service.HandleAsync(new ButtonEvent(button, "REPEAT", 600));
        Assert.Equal(expected, volume.CurrentVolumePercent);
    }

    [Fact]
    public async Task SpeakerButtonTogglesOnlyConfiguredAudioRoutingAction()
    {
        var mappings = new AppConfiguration();
        var audio = new TestAudioRouting();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(mappings),
            new SystemStateService(new TestLog()), audio, new TestVlc(), new TestBrowser(), new TestCalls(), new TestOsd(), new TestLog());
        await service.HandleAsync(new ButtonEvent("PANEL_8", "DOWN", 1));
        Assert.True(audio.SpeakersMuted);
    }

    [Fact]
    public async Task TvButtonStopsIncompatibleContentAndOpensConfiguredUrl()
    {
        var configuration = new AppConfiguration();
        configuration.Content.YouTubeUrl = "https://www.youtube.com/watch?v=test";
        var state = new SystemStateService(new TestLog());
        var vlc = new TestVlc();
        var browser = new TestBrowser();
        var calls = new TestCalls();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(configuration), state,
            new TestAudioRouting(), vlc, browser, calls, new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_5", "DOWN", 1));
        Assert.Null(browser.OpenedUrl);
        await service.HandleAsync(new ButtonEvent("PANEL_5", "UP", 2));

        Assert.Equal(configuration.Content.YouTubeUrl, browser.OpenedUrl);
        Assert.Equal(1, vlc.StopCount);
        Assert.Equal(0, calls.EndCount);
        Assert.Equal(DadConsoleState.WatchingTV, state.Current);
    }

    [Fact]
    public async Task HoldingTvButtonOpensSpotifyWithoutOpeningTvFirst()
    {
        var configuration = new AppConfiguration();
        configuration.Content.YouTubeUrl = "https://www.youtube.com/watch?v=test";
        configuration.Content.SpotifyUrl = "https://open.spotify.com/playlist/test";
        var browser = new TestBrowser();
        var state = new SystemStateService(new TestLog());
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(configuration), state,
            new TestAudioRouting(), new TestVlc(), browser, new TestCalls(), new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_5", "DOWN", 1));
        Assert.Null(browser.OpenedUrl);
        await service.HandleAsync(new ButtonEvent("PANEL_5", "LONG", 1501));
        await service.HandleAsync(new ButtonEvent("PANEL_5", "UP", 1600));

        Assert.Equal(configuration.Content.SpotifyUrl, browser.OpenedUrl);
        Assert.Equal(DadConsoleState.ListeningSpotify, state.Current);
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task StopHonoursVideoStopSetting(bool stopVideo, int expectedStops)
    {
        var configuration = new AppConfiguration();
        configuration.Content.StopVideoOnHome = stopVideo;
        var vlc = new TestVlc();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(configuration),
            new SystemStateService(new TestLog()), new TestAudioRouting(), vlc, new TestBrowser(), new TestCalls(), new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));

        Assert.Equal(expectedStops, vlc.StopCount);
    }

    [Theory]
    [InlineData("PANEL_3", "ADRIAN", DadConsoleState.CallingAdrian)]
    [InlineData("PANEL_4", "YVONNE", DadConsoleState.CallingMum)]
    public async Task ContactButtonsUseSharedConfigurableCallService(string button, string contact, DadConsoleState expectedState)
    {
        var calls = new TestCalls();
        var state = new SystemStateService(new TestLog());
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()), state,
            new TestAudioRouting(), new TestVlc(), new TestBrowser(), calls, new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent(button, "DOWN", 1));

        Assert.Equal(contact, calls.Contact);
        Assert.Equal(expectedState, state.Current);
    }

    [Fact]
    public async Task AnyNonStopButtonAnswersIncomingCallInsteadOfItsNormalAction()
    {
        var calls = new TestCalls();
        calls.HandleExternalEvent("INCOMING", "Adrian");
        var volume = new TestVolume();
        var service = new ButtonActionService(volume, new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), calls, new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_1", "DOWN", 1));

        Assert.Equal(1, calls.AnswerCount);
        Assert.Equal(50, volume.CurrentVolumePercent);
    }

    [Fact]
    public async Task StopButtonRejectsIncomingCall()
    {
        var calls = new TestCalls();
        calls.HandleExternalEvent("INCOMING", "Yvonne");
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), calls, new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));

        Assert.Equal(1, calls.RejectCount);
        Assert.Equal(0, calls.EndCount);
    }

    [Fact]
    public async Task StopOnlyEndsAnActiveCallOnce()
    {
        var calls = new TestCalls();
        calls.HandleExternalEvent("INCOMING", "Adrian");
        await calls.AnswerIncomingAsync();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(), calls, new TestOsd(), new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));
        await service.HandleAsync(new ButtonEvent("PANEL_7", "UP", 2));
        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 3));

        Assert.Equal(1, calls.EndCount);
        Assert.False(calls.IsActive);
    }

    [Fact]
    public async Task CallButtonIsIgnoredDuringQuietHours()
    {
        var calls = new TestCalls { IsCallingBlocked = true };
        var state = new SystemStateService(new TestLog());
        var osd = new TestOsd();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()), state,
            new TestAudioRouting(), new TestVlc(), new TestBrowser(), calls, osd, new TestLog());

        await service.HandleAsync(new ButtonEvent("PANEL_3", "DOWN", 1));

        Assert.Null(calls.Contact);
        Assert.Equal(DadConsoleState.Home, state.Current);
        Assert.Equal("CALLING UNAVAILABLE", osd.Headline);
    }

    [Fact]
    public async Task StopAsksWindowsShellToCloseStartMenu()
    {
        var shell = new TestWindowsShell();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(),
            new TestCalls(), new TestOsd(), new TestLog(), shell);

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));

        Assert.Equal(1, shell.CloseCount);
    }

    [Fact]
    public async Task StopRearmsTheDisplayBlackoutSchedule()
    {
        var display = new TestDisplaySchedule();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), new TestVlc(), new TestBrowser(),
            new TestCalls(), new TestOsd(), new TestLog(), null, display);

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));

        Assert.Equal(1, display.RearmCount);
    }

    [Fact]
    public async Task HoldingStopBlacksOutAfterStoppingImmediately()
    {
        var display = new TestDisplaySchedule();
        var vlc = new TestVlc();
        var service = new ButtonActionService(new TestVolume(), new TestConfiguration(new AppConfiguration()),
            new SystemStateService(new TestLog()), new TestAudioRouting(), vlc, new TestBrowser(),
            new TestCalls(), new TestOsd(), new TestLog(), null, display);

        await service.HandleAsync(new ButtonEvent("PANEL_7", "DOWN", 1));
        Assert.Equal(1, vlc.StopCount);
        Assert.Equal(1, display.RearmCount);

        await service.HandleAsync(new ButtonEvent("PANEL_7", "LONG", 1501));
        await service.HandleAsync(new ButtonEvent("PANEL_7", "UP", 1600));

        Assert.Equal(1, display.BlackoutCount);
        Assert.Equal(1, vlc.StopCount);
    }
}
