using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace DementiaComputerButtons.Infrastructure;

public sealed class WindowsWallpaperService : IWallpaperService
{
    private const int SpiSetDesktopWallpaper = 0x0014;
    private const int SpifUpdateIniFile = 0x01;
    private const int SpifSendChange = 0x02;
    private static readonly string[] SupportedExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff"];
    private readonly IConfigurationService _configuration;
    private readonly ILoggingService _logging;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DementiaComputerButtons", "wallpaper");
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _rotationTimer;
    private System.Threading.Timer? _rescanTimer;
    private System.Threading.Timer? _availabilityTimer;
    private bool _disposed;

    public WindowsWallpaperService(IConfigurationService configuration, ILoggingService logging)
    {
        _configuration = configuration;
        _logging = logging;
        Current = new(false, "Wallpaper rotation is disabled", null, null, 0);
    }

    public WallpaperSnapshot Current { get; private set; }
    public event EventHandler<WallpaperSnapshot>? StatusChanged;

    public void Start() => ApplyConfiguration();

    public void ApplyConfiguration()
    {
        DisposeScheduling();
        if (_disposed) return;
        var settings = _configuration.Current.Wallpaper;
        if (!settings.Enabled)
        {
            Update(new(false, "Wallpaper rotation is disabled", Current.CurrentPhoto, Current.LatestDay, Current.PhotosOnLatestDay));
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.SourceFolder) || !Directory.Exists(settings.SourceFolder))
        {
            Update(new(false, "Google Drive photo folder is not available", Current.CurrentPhoto, null, 0));
            _availabilityTimer = new System.Threading.Timer(_ =>
            {
                if (!_disposed && Directory.Exists(_configuration.Current.Wallpaper.SourceFolder))
                    ApplyConfiguration();
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(settings.SourceFolder)
            {
                IncludeSubdirectories = settings.IncludeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += FolderChanged;
            _watcher.Changed += FolderChanged;
            _watcher.Deleted += FolderChanged;
            _watcher.Renamed += FolderChanged;
            _watcher.Error += (_, args) =>
            {
                _logging.Warning("wallpaper_watcher_error", args.GetException().Message);
                ApplyConfiguration();
            };
        }
        catch (Exception exception)
        {
            DisposeScheduling();
            Update(new(false, $"Google Drive photo folder cannot be watched: {exception.Message}", Current.CurrentPhoto, null, 0));
            _logging.Error("wallpaper_watcher_start_failed", exception, settings.SourceFolder);
            return;
        }
        var minutes = Math.Clamp(settings.RotationMinutes, 1, 1440);
        _rotationTimer = new System.Threading.Timer(_ => _ = ShowNextSafeAsync(), null, TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        Update(new(true, "Scanning the Google Drive photo folder", Current.CurrentPhoto, Current.LatestDay, Current.PhotosOnLatestDay));
        _ = RefreshSafeAsync();
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        SelectAndDisplayAsync(advance: false, cancellationToken);

    public Task ShowNextAsync(CancellationToken cancellationToken = default) =>
        SelectAndDisplayAsync(advance: true, cancellationToken);

    private async Task SelectAndDisplayAsync(bool advance, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _configuration.Current.Wallpaper;
            if (!settings.Enabled || !Directory.Exists(settings.SourceFolder)) return;
            var enumeration = new EnumerationOptions
            {
                RecurseSubdirectories = settings.IncludeSubfolders,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };
            var photos = new List<PhotoCandidate>();
            foreach (var path in Directory.EnumerateFiles(settings.SourceFolder, "*", enumeration)
                         .Where(IsSupportedImage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { photos.Add(new(path, ReadMetadata(path).TakenAt)); }
                catch (Exception exception)
                {
                    _logging.Warning("wallpaper_photo_skipped", $"{path}; {exception.Message}");
                }
            }

            var latest = LatestDayPhotoSelector.Select(photos);
            if (latest.Count == 0)
            {
                Update(new(true, "No supported photos were found", null, null, 0));
                return;
            }
            var index = advance ? LatestDayPhotoSelector.NextIndex(latest, Current.CurrentPhoto) :
                Math.Max(0, latest.ToList().FindIndex(photo => photo.Path.Equals(Current.CurrentPhoto, StringComparison.OrdinalIgnoreCase)));
            if (!advance && (Current.LatestDay != DateOnly.FromDateTime(latest[0].TakenAt) || index < 0)) index = 0;
            var selected = latest[index];
            var metadata = ReadMetadata(selected.Path);
            var outputPath = await RenderAndSetAsync(selected.Path, metadata, settings, cancellationToken).ConfigureAwait(false);
            var day = DateOnly.FromDateTime(selected.TakenAt);
            Update(new(true, $"Showing {Path.GetFileName(selected.Path)}", selected.Path, day, latest.Count));
            _logging.Information("wallpaper_changed", $"source={selected.Path}; output={outputPath}; latestDay={day:yyyy-MM-dd}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Update(new(true, $"Wallpaper update failed: {exception.Message}", Current.CurrentPhoto, Current.LatestDay, Current.PhotosOnLatestDay));
            _logging.Error("wallpaper_update_failed", exception, exception.Message);
        }
        finally { _gate.Release(); }
    }

    private async Task<string> RenderAndSetAsync(string sourcePath, PhotoMetadata metadata,
        WallpaperConfiguration settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_outputDirectory);
        var outputPath = Path.Combine(_outputDirectory, "current-wallpaper.jpg");
        var dispatcher = WpfApplication.Current?.Dispatcher ?? throw new InvalidOperationException("The Windows dispatcher is not available.");
        await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            bitmap.Freeze();
            var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            var width = Math.Max(800, bounds.Width);
            var height = Math.Max(600, bounds.Height);
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(System.Windows.Media.Brushes.Black, null, new Rect(0, 0, width, height));
                var scale = Math.Max(width / (double)bitmap.PixelWidth, height / (double)bitmap.PixelHeight);
                var drawWidth = bitmap.PixelWidth * scale;
                var drawHeight = bitmap.PixelHeight * scale;
                drawing.DrawImage(bitmap, new Rect((width - drawWidth) / 2, (height - drawHeight) / 2, drawWidth, drawHeight));
                DrawOverlay(drawing, width, metadata, settings);
            }
            var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
            encoder.Frames.Add(BitmapFrame.Create(rendered));
            using var output = File.Create(outputPath);
            encoder.Save(output);
        });

        using (var desktop = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true))
        {
            desktop?.SetValue("WallpaperStyle", "10");
            desktop?.SetValue("TileWallpaper", "0");
        }
        if (!SystemParametersInfo(SpiSetDesktopWallpaper, 0, outputPath, SpifUpdateIniFile | SpifSendChange))
            throw new InvalidOperationException($"Windows rejected the wallpaper change ({Marshal.GetLastWin32Error()}).");
        return outputPath;
    }

    private static void DrawOverlay(DrawingContext drawing, int width, PhotoMetadata metadata, WallpaperConfiguration settings)
    {
        var lines = new List<string>();
        if (settings.ShowCaptureDate)
        {
            string date;
            try { date = metadata.TakenAt.ToString(settings.DateFormat, CultureInfo.CurrentCulture); }
            catch (FormatException) { date = metadata.TakenAt.ToString("dddd, dd MMMM yyyy  h:mm tt", CultureInfo.CurrentCulture); }
            lines.Add(date);
        }
        if (settings.ShowLocation && metadata.Latitude is not null && metadata.Longitude is not null)
            lines.Add($"📍  {FormatCoordinate(metadata.Latitude.Value, 'N', 'S')}  {FormatCoordinate(metadata.Longitude.Value, 'E', 'W')}");
        if (lines.Count == 0) return;

        var family = new System.Windows.Media.FontFamily(
            new Uri("pack://application:,,,/"), "./Assets/Fonts/#Google Sans Flex");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var fontSize = Math.Clamp(width / 45d, 28d, 48d);
        var content = string.Join(Environment.NewLine, lines);
        var text = CreateText(content, typeface, fontSize, System.Windows.Media.Brushes.White);
        var origin = new System.Windows.Point(width - 48 - text.WidthIncludingTrailingWhitespace, 42);
        var glow = new SolidColorBrush(System.Windows.Media.Color.FromArgb(125, 0, 0, 0));
        glow.Freeze();
        foreach (var offset in new[] { new Vector(-2, 0), new Vector(2, 0), new Vector(0, -2), new Vector(0, 2), new Vector(-2, -2), new Vector(2, 2) })
            drawing.DrawText(CreateText(content, typeface, fontSize, glow), origin + offset);
        var shadow = new SolidColorBrush(System.Windows.Media.Color.FromArgb(210, 0, 0, 0));
        shadow.Freeze();
        drawing.DrawText(CreateText(content, typeface, fontSize, shadow), origin + new Vector(5, 7));
        drawing.DrawText(text, origin);
    }

    private static FormattedText CreateText(string content, Typeface typeface, double fontSize, System.Windows.Media.Brush brush) =>
        new(content, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0)
        { TextAlignment = TextAlignment.Right, LineHeight = fontSize * 1.28 };

    private static string FormatCoordinate(double value, char positive, char negative) =>
        $"{Math.Abs(value):0.0000}° {(value >= 0 ? positive : negative)}";

    private static PhotoMetadata ReadMetadata(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var metadata = frame.Metadata as BitmapMetadata;
        var takenAt = ParseDate(metadata?.DateTaken) ?? File.GetLastWriteTime(path);
        return new(takenAt, ReadGps(metadata, "/app1/ifd/gps/{ushort=2}", "/app1/ifd/gps/{ushort=1}"),
            ReadGps(metadata, "/app1/ifd/gps/{ushort=4}", "/app1/ifd/gps/{ushort=3}"));
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var formats = new[] { "yyyy:MM:dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss" };
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsed) ? parsed :
            DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out parsed) ? parsed : null;
    }

    private static double? ReadGps(BitmapMetadata? metadata, string coordinateQuery, string referenceQuery)
    {
        if (metadata is null) return null;
        try
        {
            if (metadata.GetQuery(coordinateQuery) is not ulong[] values || values.Length < 3) return null;
            var coordinate = ExifGpsConverter.Rational(values[0]) + ExifGpsConverter.Rational(values[1]) / 60d + ExifGpsConverter.Rational(values[2]) / 3600d;
            var reference = Convert.ToString(metadata.GetQuery(referenceQuery), CultureInfo.InvariantCulture)?.Trim('\0', ' ');
            return reference is "S" or "W" ? -coordinate : coordinate;
        }
        catch { return null; }
    }

    private static bool IsSupportedImage(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private void FolderChanged(object sender, FileSystemEventArgs args)
    {
        if (IsSupportedImage(args.FullPath)) ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        _rescanTimer?.Dispose();
        _rescanTimer = new System.Threading.Timer(_ => _ = RefreshSafeAsync(), null, TimeSpan.FromSeconds(3), Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshSafeAsync()
    {
        try { await RefreshAsync().ConfigureAwait(false); }
        catch (Exception exception) { _logging.Error("wallpaper_refresh_failed", exception, exception.Message); }
    }

    private async Task ShowNextSafeAsync()
    {
        try { await ShowNextAsync().ConfigureAwait(false); }
        catch (Exception exception) { _logging.Error("wallpaper_rotation_failed", exception, exception.Message); }
    }

    private void Update(WallpaperSnapshot snapshot)
    {
        Current = snapshot;
        StatusChanged?.Invoke(this, snapshot);
    }

    private void DisposeScheduling()
    {
        _watcher?.Dispose();
        _watcher = null;
        _rotationTimer?.Dispose();
        _rotationTimer = null;
        _rescanTimer?.Dispose();
        _rescanTimer = null;
        _availabilityTimer?.Dispose();
        _availabilityTimer = null;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        DisposeScheduling();
        return ValueTask.CompletedTask;
    }

    private sealed record PhotoMetadata(DateTime TakenAt, double? Latitude, double? Longitude);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(int action, int parameter, string value, int flags);
}
