using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DementiaComputerButtons.Core.Abstractions;
using DementiaComputerButtons.Core.Models;
using DementiaComputerButtons.Core.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;

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
    private readonly HttpClient _http = new();
    private readonly string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DementiaComputerButtons", "wallpaper");
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _rotationTimer;
    private System.Threading.Timer? _rescanTimer;
    private System.Threading.Timer? _availabilityTimer;
    private bool _disposed;
    private DateTime _lastGeocodeRequestUtc = DateTime.MinValue;

    public WindowsWallpaperService(IConfigurationService configuration, ILoggingService logging)
    {
        _configuration = configuration;
        _logging = logging;
        _http.Timeout = TimeSpan.FromSeconds(12);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DementiaComputerButtons/0.1.11 (+https://github.com/themusicnerd/Dementia-Computer-Buttons)");
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
        var location = settings.ShowLocation && metadata.Latitude is not null && metadata.Longitude is not null
            ? await GetLocationOverlayAsync(metadata.Latitude.Value, metadata.Longitude.Value, cancellationToken).ConfigureAwait(false)
            : null;
        var dispatcher = WpfApplication.Current?.Dispatcher ?? throw new InvalidOperationException("The Windows dispatcher is not available.");
        await dispatcher.InvokeAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var source = File.Open(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource bitmap = decoder.Frames[0];
            bitmap.Freeze();
            bitmap = ApplyExifOrientation(bitmap, metadata.Orientation);
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
                DrawOverlay(drawing, width, height, metadata, settings, location);
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

    private static void DrawOverlay(DrawingContext drawing, int width, int height, PhotoMetadata metadata,
        WallpaperConfiguration settings, LocationOverlay? location)
    {
        if (settings.ShowCaptureDate)
        {
            string date, time;
            try { date = metadata.TakenAt.ToString(settings.DateFormat, CultureInfo.CurrentCulture); }
            catch (FormatException) { date = metadata.TakenAt.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture); }
            try { time = metadata.TakenAt.ToString(settings.TimeFormat, CultureInfo.CurrentCulture); }
            catch (FormatException) { time = metadata.TakenAt.ToString("h:mm tt", CultureInfo.CurrentCulture); }
            DrawShadowedText(drawing, $"{metadata.TakenAt:dddd}\n{date}\n{time}", width - 48, 36, 48, TextAlignment.Right);
        }

        if (location is null) return;
        const double mapSize = 300;
        var mapLeft = width - mapSize - 48;
        var mapTop = height - mapSize - 112;
        var mapRect = new Rect(mapLeft, mapTop, mapSize, mapSize);
        drawing.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(205, 245, 245, 245)),
            new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)), 3), mapRect, 8, 8);
        var innerLeft = mapLeft + 3;
        var innerTop = mapTop + 3;
        var innerSize = mapSize - 6;
        var tileScale = innerSize / LocationOverlay.ViewportPixels;
        var drewMap = false;
        drawing.PushClip(new RectangleGeometry(new Rect(innerLeft, innerTop, innerSize, innerSize)));
        foreach (var tile in location.Tiles)
        {
            var map = tile.GetMap();
            if (map is null) continue;
            drawing.DrawImage(map, new Rect(innerLeft + tile.OffsetX * tileScale,
                innerTop + tile.OffsetY * tileScale, 256 * tileScale, 256 * tileScale));
            drewMap = true;
        }
        drawing.Pop();
        if (!drewMap)
            DrawShadowedText(drawing, "MAP UNAVAILABLE", mapLeft + mapSize / 2, mapTop + mapSize / 2 - 14,
                20, TextAlignment.Center);

        var pinX = mapLeft + mapSize / 2;
        var pinY = mapTop + mapSize / 2;
        var pinBrush = new SolidColorBrush(Color.FromRgb(220, 28, 28));
        var pinShadow = new StreamGeometry();
        using (var context = pinShadow.Open())
        {
            context.BeginFigure(new Point(pinX - 15, pinY - 22), true, true);
            context.LineTo(new Point(pinX + 15, pinY - 22), true, false);
            context.LineTo(new Point(pinX, pinY + 3), true, false);
        }
        pinShadow.Freeze();
        drawing.DrawGeometry(Brushes.Black, null, pinShadow);
        drawing.DrawEllipse(Brushes.Black, null, new Point(pinX + 3, pinY - 27), 23, 23);
        drawing.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 2), pinShadow);
        drawing.DrawEllipse(Brushes.White, new Pen(Brushes.Black, 2), new Point(pinX, pinY - 30), 21, 21);
        var pin = new StreamGeometry();
        using (var context = pin.Open())
        {
            context.BeginFigure(new Point(pinX - 11, pinY - 22), true, true);
            context.LineTo(new Point(pinX + 11, pinY - 22), true, false);
            context.LineTo(new Point(pinX, pinY), true, false);
        }
        pin.Freeze();
        drawing.DrawGeometry(pinBrush, null, pin);
        drawing.DrawEllipse(pinBrush, null, new Point(pinX, pinY - 30), 16, 16);
        drawing.DrawEllipse(Brushes.White, null, new Point(pinX, pinY - 30), 6, 6);
        DrawOutlinedSmallText(drawing, "© OpenStreetMap contributors", mapLeft + 8, mapTop + mapSize - 24);
        DrawShadowedText(drawing, location.DisplayName, width - 48, height - 88, 35, TextAlignment.Right);
    }

    private static void DrawShadowedText(DrawingContext drawing, string content, double anchorX, double y,
        double fontSize, TextAlignment alignment)
    {
        var family = new System.Windows.Media.FontFamily(
            new Uri("pack://application:,,,/"), "./Assets/Fonts/#Google Sans Flex");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var origin = new Point(anchorX, y);
        var glow = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0));
        var shadow = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        glow.Freeze();
        shadow.Freeze();
        for (var radius = 3; radius <= 12; radius += 3)
            for (var angle = 0; angle < 360; angle += 30)
            {
                var radians = angle * Math.PI / 180d;
                var blurred = CreateText(content, typeface, fontSize, glow);
                blurred.TextAlignment = alignment;
                drawing.DrawText(blurred, origin + new Vector(Math.Cos(radians) * radius, Math.Sin(radians) * radius));
            }
        var drop = CreateText(content, typeface, fontSize, shadow);
        drop.TextAlignment = alignment;
        drawing.DrawText(drop, origin + new Vector(7, 9));
        var text = CreateText(content, typeface, fontSize, Brushes.White);
        text.TextAlignment = alignment;
        drawing.DrawText(text, origin);
    }

    private static void DrawOutlinedSmallText(DrawingContext drawing, string content, double x, double y)
    {
        var typeface = new Typeface("Segoe UI");
        var dark = CreateText(content, typeface, 12, Brushes.Black);
        var light = CreateText(content, typeface, 12, Brushes.White);
        dark.TextAlignment = TextAlignment.Left;
        light.TextAlignment = TextAlignment.Left;
        foreach (var offset in new[] { new Vector(-1, 0), new Vector(1, 0), new Vector(0, -1), new Vector(0, 1) })
            drawing.DrawText(dark, new Point(x, y) + offset);
        drawing.DrawText(light, new Point(x, y));
    }

    private static FormattedText CreateText(string content, Typeface typeface, double fontSize, System.Windows.Media.Brush brush) =>
        new(content, CultureInfo.CurrentCulture, System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0)
        { TextAlignment = TextAlignment.Right, LineHeight = fontSize * 1.28 };

    private async Task<LocationOverlay> GetLocationOverlayAsync(double latitude, double longitude,
        CancellationToken cancellationToken)
    {
        var cache = Path.Combine(_outputDirectory, "map-cache");
        Directory.CreateDirectory(cache);
        var key = $"{latitude:F4}_{longitude:F4}".Replace('-', 'm').Replace('.', '_');
        var namePath = Path.Combine(cache, key + ".txt");
        var displayName = File.Exists(namePath)
            ? (await File.ReadAllTextAsync(namePath, cancellationToken).ConfigureAwait(false)).Trim()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(displayName))
        {
            try
            {
                var remaining = TimeSpan.FromSeconds(1) - (DateTime.UtcNow - _lastGeocodeRequestUtc);
                if (remaining > TimeSpan.Zero) await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                _lastGeocodeRequestUtc = DateTime.UtcNow;
                var url = FormattableString.Invariant(
                    $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={latitude:F6}&lon={longitude:F6}&zoom=14&addressdetails=1");
                using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
                displayName = ReadSuburb(json.RootElement) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(displayName))
                    await File.WriteAllTextAsync(namePath, displayName, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logging.Warning("wallpaper_geocode_failed", exception.Message);
            }
        }
        if (string.IsNullOrWhiteSpace(displayName)) displayName = $"{latitude:0.0000}, {longitude:0.0000}";

        const int zoom = 14;
        var n = Math.Pow(2, zoom);
        var safeLatitude = Math.Clamp(latitude, -85.05112878, 85.05112878);
        var worldX = (longitude + 180d) / 360d * n;
        var radians = safeLatitude * Math.PI / 180d;
        var worldY = (1d - Math.Log(Math.Tan(radians) + 1d / Math.Cos(radians)) / Math.PI) / 2d * n;
        var worldPixelX = worldX * 256d;
        var worldPixelY = worldY * 256d;
        var viewportLeft = worldPixelX - LocationOverlay.ViewportPixels / 2d;
        var viewportTop = worldPixelY - LocationOverlay.ViewportPixels / 2d;
        var tiles = new List<MapTile>();
        var firstTileX = (int)Math.Floor(viewportLeft / 256d);
        var lastTileX = (int)Math.Floor((viewportLeft + LocationOverlay.ViewportPixels - 1) / 256d);
        var firstTileY = (int)Math.Floor(viewportTop / 256d);
        var lastTileY = (int)Math.Floor((viewportTop + LocationOverlay.ViewportPixels - 1) / 256d);
        for (var sourceTileY = firstTileY; sourceTileY <= lastTileY; sourceTileY++)
        for (var sourceTileX = firstTileX; sourceTileX <= lastTileX; sourceTileX++)
        {
            var tileX = ((sourceTileX % (int)n) + (int)n) % (int)n;
            var tileY = Math.Clamp(sourceTileY, 0, (int)n - 1);
            var tilePath = Path.Combine(cache, $"osm_{zoom}_{tileX}_{tileY}.png");
            byte[]? bytes = null;
            try
            {
                if (!File.Exists(tilePath) || DateTime.UtcNow - File.GetLastWriteTimeUtc(tilePath) > TimeSpan.FromDays(30))
                {
                    bytes = await _http.GetByteArrayAsync(
                        $"https://tile.openstreetmap.org/{zoom}/{tileX}/{tileY}.png", cancellationToken).ConfigureAwait(false);
                    await File.WriteAllBytesAsync(tilePath, bytes, cancellationToken).ConfigureAwait(false);
                }
                else bytes = await File.ReadAllBytesAsync(tilePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logging.Warning("wallpaper_map_failed", exception.Message);
                if (File.Exists(tilePath))
                    bytes = await File.ReadAllBytesAsync(tilePath, cancellationToken).ConfigureAwait(false);
            }
            tiles.Add(new(bytes, sourceTileX * 256d - viewportLeft, sourceTileY * 256d - viewportTop));
        }
        return new(displayName, tiles);
    }

    private static string? ReadSuburb(JsonElement root)
    {
        if (!root.TryGetProperty("address", out var address)) return null;
        foreach (var field in new[] { "suburb", "neighbourhood", "quarter", "city_district", "town", "village", "city", "municipality", "county" })
            if (address.TryGetProperty(field, out var value) && !string.IsNullOrWhiteSpace(value.GetString()))
                return value.GetString();
        return null;
    }

    private static PhotoMetadata ReadMetadata(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        var metadata = frame.Metadata as BitmapMetadata;
        var takenAt = ParseDate(metadata?.DateTaken) ?? File.GetLastWriteTime(path);
        return new(takenAt, ReadGps(metadata, "/app1/ifd/gps/{ushort=2}", "/app1/ifd/gps/{ushort=1}"),
            ReadGps(metadata, "/app1/ifd/gps/{ushort=4}", "/app1/ifd/gps/{ushort=3}"), ReadOrientation(metadata));
    }

    private static ushort ReadOrientation(BitmapMetadata? metadata)
    {
        try { return Convert.ToUInt16(metadata?.GetQuery("/app1/ifd/{ushort=274}") ?? 1, CultureInfo.InvariantCulture); }
        catch { return 1; }
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource source, ushort orientation)
    {
        var matrix = orientation switch
        {
            2 => new Matrix(-1, 0, 0, 1, 0, 0),
            3 => new Matrix(-1, 0, 0, -1, 0, 0),
            4 => new Matrix(1, 0, 0, -1, 0, 0),
            5 => new Matrix(0, 1, 1, 0, 0, 0),
            6 => new Matrix(0, 1, -1, 0, 0, 0),
            7 => new Matrix(0, -1, -1, 0, 0, 0),
            8 => new Matrix(0, -1, 1, 0, 0, 0),
            _ => Matrix.Identity
        };
        if (matrix.IsIdentity) return source;
        var transformed = new TransformedBitmap(source, new MatrixTransform(matrix));
        transformed.Freeze();
        return transformed;
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
        _http.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record PhotoMetadata(DateTime TakenAt, double? Latitude, double? Longitude, ushort Orientation);
    private sealed record LocationOverlay(string DisplayName, IReadOnlyList<MapTile> Tiles)
    {
        public const double ViewportPixels = 300;
    }

    private sealed record MapTile(byte[]? Bytes, double OffsetX, double OffsetY)
    {
        public BitmapSource? GetMap()
        {
            if (Bytes is null) return null;
            using var stream = new MemoryStream(Bytes);
            var bitmap = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmap.Freeze();
            return bitmap;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(int action, int parameter, string value, int flags);
}
