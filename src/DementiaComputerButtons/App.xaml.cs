using System.IO;
using System.Windows;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Services;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Infrastructure;
using DementiaComputerButtons.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace DementiaComputerButtons;

public partial class App : WpfApplication
{
    private IHost? _host;
    private Forms.NotifyIcon? _trayIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0].Equals("--call-event", StringComparison.OrdinalIgnoreCase))
        {
            var caller = e.Args.Length > 2 ? e.Args[2] : string.Empty;
            await CallEventBridge.TrySendAsync(e.Args[1], caller);
            Shutdown();
            return;
        }
        var builder = Host.CreateApplicationBuilder(e.Args);
        ConfigureServices(builder.Services);
        _host = builder.Build();
        await _host.StartAsync();
        await _host.Services.GetRequiredService<IConfigurationService>().LoadAsync();
        var startup = _host.Services.GetRequiredService<IStartupService>();
        try
        {
            if (_host.Services.GetRequiredService<IConfigurationService>().Current.Startup.StartWithWindows)
                startup.SetEnabled(true);
        }
        catch (Exception exception)
        {
            _host.Services.GetRequiredService<ILoggingService>().Error("startup_registration_failed", exception, exception.Message);
        }

        var arduino = _host.Services.GetRequiredService<IArduinoService>();
        var volume = _host.Services.GetRequiredService<IVolumeService>();
        var volumeOverlay = _host.Services.GetRequiredService<VolumeOverlayWindow>();
        var actions = _host.Services.GetRequiredService<IButtonActionService>();
        var logging = _host.Services.GetRequiredService<ILoggingService>();
        var calls = _host.Services.GetRequiredService<ICallService>();
        var vlc = _host.Services.GetRequiredService<IVlcService>();
        vlc.PlaybackEnded += (_, _) =>
        {
            var systemState = _host.Services.GetRequiredService<ISystemStateService>();
            if (systemState.Current == DadConsoleState.PlayingVideo)
                systemState.ReturnHome("video-finished");
        };
        calls.StatusChanged += async (_, snapshot) =>
        {
            try
            {
                if (snapshot.Status is CallStatus.Incoming or CallStatus.Calling or CallStatus.Ringing or CallStatus.Connecting or CallStatus.Connected)
                {
                    await _host.Services.GetRequiredService<IBrowserService>().CloseManagedContentAsync();
                    await _host.Services.GetRequiredService<IVlcService>().StopAsync();
                }
                var systemState = _host.Services.GetRequiredService<ISystemStateService>();
                if (snapshot.Status == CallStatus.Incoming) systemState.TransitionTo(DadConsoleState.IncomingCall, "incoming-call");
                else if (snapshot.Status == CallStatus.Connected) systemState.TransitionTo(DadConsoleState.InCall, "call-connected");
                else if (snapshot.Status == CallStatus.Ended && systemState.Current is DadConsoleState.IncomingCall or DadConsoleState.InCall)
                    systemState.ReturnHome("call-ended");
            }
            catch (Exception exception) { logging.Error("call_media_priority_failed", exception, exception.Message); }
        };
        arduino.ButtonChanged += async (_, button) =>
        {
            try { await actions.HandleAsync(button); }
            catch (Exception exception) { logging.Error("button_action_failed", exception, button.Button); }
        };
        var startupVolumeRead = true;
        volume.VolumeChanged += async (_, percent) =>
        {
            if (!startupVolumeRead) volumeOverlay.ShowVolume(percent, volume.IsMuted);
            if (!arduino.Connection.IsConnected) return;
            try { await arduino.SendCommandAsync($"MATRIX BAR {(int)Math.Round(percent)}"); }
            catch (Exception exception) { logging.Error("volume_matrix_sync_failed", exception, exception.Message); }
        };
        await arduino.StartAsync();
        _host.Services.GetRequiredService<CallEventBridge>().Start();
        _host.Services.GetRequiredService<PanelLightingService>().Start();
        _ = _host.Services.GetRequiredService<CallStatusWindow>();
        _host.Services.GetRequiredService<DisplayScheduleService>().Start();
        volume.Refresh();
        startupVolumeRead = false;
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        ConfigureTray(window);
        window.Show();
    }

    private void ConfigureTray(Window window)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Dementia Computer Buttons", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) =>
        {
            Dispatcher.Invoke(window.Close);
        });
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Dementia Computer Buttons",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        window.StateChanged += (_, _) =>
        {
            if (window.WindowState != WindowState.Minimized) return;
            window.ShowInTaskbar = false;
            window.Hide();
        };
    }

    private void RestoreFromTray()
    {
        Dispatcher.Invoke(() =>
        {
            if (MainWindow is null) return;
            MainWindow.ShowInTaskbar = true;
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.Activate();
        });
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ILoggingService, FileLoggingService>();
        services.AddSingleton<IConfigurationService>(provider =>
        {
            var configurationDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DementiaComputerButtons");
            Directory.CreateDirectory(configurationDirectory);
            var userConfiguration = Path.Combine(configurationDirectory, "appsettings.json");
            var bundledConfiguration = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(userConfiguration) && File.Exists(bundledConfiguration))
                File.Copy(bundledConfiguration, userConfiguration);
            return new ConfigurationService(userConfiguration, provider.GetRequiredService<ILoggingService>());
        });
        services.AddSingleton<ISerialPortProvider, SerialPortProvider>();
        services.AddSingleton<ISerialConnectionFactory, SerialConnectionFactory>();
        services.AddSingleton<IAsyncDelay, SystemAsyncDelay>();
        if (Environment.GetEnvironmentVariable("DCB_USE_MOCK") == "1")
            services.AddSingleton<IArduinoService, MockArduinoService>();
        else
            services.AddSingleton<IArduinoService, ArduinoService>();
        services.AddSingleton<IVolumeService, WindowsVolumeService>();
        services.AddSingleton<ISystemStateService, SystemStateService>();
        services.AddSingleton<IButtonActionService, ButtonActionService>();
        services.AddSingleton<IAudioRoutingService, WindowsAudioRoutingService>();
        services.AddSingleton<IVlcService, VlcService>();
        services.AddSingleton<IBrowserService, BrowserService>();
        services.AddSingleton<ICallService, CallService>();
        services.AddSingleton<IWindowsShellService, WindowsShellService>();
        services.AddSingleton<IStartupService, WindowsStartupService>();
        services.AddSingleton<IUpdateService, GitHubUpdateService>();
        services.AddSingleton<IApplicationManagerService, ApplicationManagerService>();
        services.AddSingleton<CallEventBridge>();
        services.AddSingleton<DisplayScheduleService>();
        services.AddSingleton<IIRService, IRService>();
        services.AddSingleton<PanelLightingService>();
        services.AddSingleton<IUserPromptService, UserPromptService>();
        services.AddSingleton<DiagnosticsViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<VolumeOverlayWindow>();
        services.AddSingleton<CallStatusWindow>();
        services.AddSingleton<BlackoutWindow>();
        services.AddSingleton<IOnScreenDisplayService>(provider => provider.GetRequiredService<VolumeOverlayWindow>());
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        if (_host is not null)
        {
            await _host.Services.GetRequiredService<IBrowserService>().CloseManagedContentAsync();
            await _host.Services.GetRequiredService<IVlcService>().StopAsync();
            await _host.Services.GetRequiredService<ICallService>().EndCallAsync();
            await _host.Services.GetRequiredService<CallEventBridge>().DisposeAsync();
            await _host.Services.GetRequiredService<IArduinoService>().StopAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
