using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    private readonly HttpClient _googleHttp = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly string _outputDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DementiaComputerButtons", "wallpaper");
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _rotationTimer;
    private System.Threading.Timer? _rescanTimer;
    private System.Threading.Timer? _availabilityTimer;
    private bool _disposed;
    private DateTime _lastGeocodeRequestUtc = DateTime.MinValue;
    private int _googleFailureCount;
    private DateTimeOffset _nextGoogleAttemptUtc = DateTimeOffset.MinValue;

    public WindowsWallpaperService(IConfigurationService configuration, ILoggingService logging)
    {
        _configuration = configuration;
        _logging = logging;
        _http.Timeout = TimeSpan.FromSeconds(12);
        _googleHttp.Timeout = TimeSpan.FromSeconds(15);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DementiaComputerButtons/0.1.14 (+https://github.com/themusicnerd/Dementia-Computer-Buttons)");
        _googleHttp.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DementiaComputerButtons/0.1.14 (+https://github.com/themusicnerd/Dementia-Computer-Buttons)");
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
        if (settings.SourceType == "GooglePhotos")
        {
            try { GooglePhotosAlbumParser.ValidateAlbumUrl(settings.GooglePhotosAlbumUrl); }
            catch (ArgumentException exception)
            {
                Update(new(false, exception.Message, Current.CurrentPhoto, null, 0));
                return;
            }
            _googleFailureCount = 0;
            _nextGoogleAttemptUtc = DateTimeOffset.MinValue;
            var checkMinutes = Math.Clamp(settings.RotationMinutes, 15, 1440);
            _rotationTimer = new System.Threading.Timer(_ => _ = RefreshSafeAsync(), null,
                TimeSpan.FromMinutes(checkMinutes), TimeSpan.FromMinutes(checkMinutes));
            Update(new(true, $"Checking Google Photos every {checkMinutes} minutes", Current.CurrentPhoto,
                Current.LatestDay, Current.PhotosOnLatestDay));
            _ = RefreshSafeAsync();
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

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_configuration.Current.Wallpaper.SourceType == "GooglePhotos")
        {
            _googleFailureCount = 0;
            _nextGoogleAttemptUtc = DateTimeOffset.MinValue;
        }
        return SelectAndDisplayAsync(advance: false, cancellationToken);
    }

    public Task ShowNextAsync(CancellationToken cancellationToken = default) =>
        SelectAndDisplayAsync(advance: true, cancellationToken);

    private async Task SelectAndDisplayAsync(bool advance, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = _configuration.Current.Wallpaper;
            if (!settings.Enabled) return;
            if (settings.SourceType == "GooglePhotos")
            {
                await SelectGooglePhotosAsync(settings, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (!Directory.Exists(settings.SourceFolder)) return;
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

    private async Task SelectGooglePhotosAsync(WallpaperConfiguration settings, CancellationToken cancellationToken)
    {
        var cacheDirectory = Path.Combine(_outputDirectory, "google-photos");
        var manifestPath = Path.Combine(cacheDirectory, "current.json");
        Directory.CreateDirectory(cacheDirectory);
        var cached = await ReadGoogleManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        if (DateTimeOffset.UtcNow < _nextGoogleAttemptUtc)
        {
            Update(new(true, $"Google Photos retry deferred until {_nextGoogleAttemptUtc.ToLocalTime():t}",
                cached?.ImagePath, cached?.Taken is null ? null : DateOnly.FromDateTime(cached.Taken.Value.LocalDateTime),
                cached is null ? 0 : 1));
            return;
        }
        try
        {
            var albumUri = GooglePhotosAlbumParser.ValidateAlbumUrl(settings.GooglePhotosAlbumUrl);
            using var albumResponse = await GetGooglePageAsync(albumUri, cancellationToken).ConfigureAwait(false);
            albumResponse.EnsureSuccessStatusCode();
            var resolvedAlbumUri = albumResponse.RequestMessage?.RequestUri ?? albumUri;
            GooglePhotosAlbumParser.ValidateAlbumUrl(resolvedAlbumUri.AbsoluteUri);
            var albumHtml = await ReadLimitedTextAsync(albumResponse.Content, 12_000_000, cancellationToken)
                .ConfigureAwait(false);
            var selected = GooglePhotosAlbumParser.ChooseLatest(GooglePhotosAlbumParser.ExtractPhotos(albumHtml))
                ?? throw new InvalidOperationException("No dated photo records were found in the shared album.");

            var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(selected.Id)))[..24];
            var imagePath = Path.Combine(cacheDirectory, cacheKey + ".image");
            var description = cached?.Id == selected.Id ? cached.Description : string.Empty;
            var lastDescriptionCheck = cached?.Id == selected.Id ? cached.DescriptionCheckedUtc : null;
            if (cached?.Id != selected.Id || !File.Exists(imagePath))
                await DownloadGooglePhotoAsync(selected.ImageUrl, imagePath, cancellationToken).ConfigureAwait(false);

            if (cached?.Id != selected.Id || lastDescriptionCheck is null ||
                DateTimeOffset.UtcNow - lastDescriptionCheck > TimeSpan.FromHours(1))
            {
                try
                {
                    var detailBuilder = new UriBuilder(resolvedAlbumUri)
                    {
                        Path = resolvedAlbumUri.AbsolutePath.TrimEnd('/') + "/photo/" + Uri.EscapeDataString(selected.Id)
                    };
                    using var detailResponse = await GetGooglePageAsync(detailBuilder.Uri, cancellationToken)
                        .ConfigureAwait(false);
                    detailResponse.EnsureSuccessStatusCode();
                    var detailHtml = await ReadLimitedTextAsync(detailResponse.Content, 8_000_000, cancellationToken)
                        .ConfigureAwait(false);
                    description = GooglePhotosAlbumParser.ExtractDescription(detailHtml, selected.Id);
                    lastDescriptionCheck = DateTimeOffset.UtcNow;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logging.Warning("google_photos_description_failed", exception.Message);
                }
            }

            var manifest = new GooglePhotosCacheManifest(selected.Id, imagePath, selected.Taken, selected.Added,
                description, lastDescriptionCheck);
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest), cancellationToken)
                .ConfigureAwait(false);
            _googleFailureCount = 0;
            _nextGoogleAttemptUtc = DateTimeOffset.MinValue;
            await RenderGooglePhotoAsync(manifest, settings, fromCache: false, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logging.Warning("google_photos_check_failed", exception.Message);
            _googleFailureCount = Math.Min(_googleFailureCount + 1, 6);
            var delayMinutes = Math.Min(15 * (1 << (_googleFailureCount - 1)), 360);
            _nextGoogleAttemptUtc = DateTimeOffset.UtcNow.AddMinutes(delayMinutes);
            if (cached is not null && File.Exists(cached.ImagePath))
            {
                await RenderGooglePhotoAsync(cached, settings, fromCache: true, cancellationToken).ConfigureAwait(false);
                Update(Current with { Status = $"Google Photos unavailable; cached photo shown; retry after {_nextGoogleAttemptUtc.ToLocalTime():t}" });
                return;
            }
            throw new InvalidOperationException("Google Photos could not be read and no cached photo is available.", exception);
        }
    }

    private async Task RenderGooglePhotoAsync(GooglePhotosCacheManifest manifest, WallpaperConfiguration settings,
        bool fromCache, CancellationToken cancellationToken)
    {
        var metadata = ReadMetadata(manifest.ImagePath) with
        {
            TakenAt = (manifest.Taken ?? manifest.Added ?? File.GetLastWriteTime(manifest.ImagePath)).LocalDateTime,
            Description = manifest.Description
        };
        var outputPath = await RenderAndSetAsync(manifest.ImagePath, metadata, settings, cancellationToken)
            .ConfigureAwait(false);
        var day = DateOnly.FromDateTime(metadata.TakenAt);
        var status = fromCache ? "Google Photos unavailable; showing cached photo" : "Showing latest Google Photos album photo";
        Update(new(true, status, manifest.ImagePath, day, 1));
        _logging.Information("wallpaper_changed",
            $"source=GooglePhotos; mediaIdHash={Path.GetFileNameWithoutExtension(manifest.ImagePath)}; output={outputPath}; captureDay={day:yyyy-MM-dd}; cached={fromCache}");
    }

    private async Task DownloadGooglePhotoAsync(Uri original, string destination, CancellationToken cancellationToken)
    {
        if (!GooglePhotosAlbumParser.IsImageHost(original))
            throw new InvalidOperationException("Google Photos returned an unexpected image host.");
        var value = original.GetLeftPart(UriPartial.Path);
        var equals = value.LastIndexOf('=');
        if (equals > value.LastIndexOf('/')) value = value[..equals];
        var imageUri = new Uri(value + "=w1920-h1080");
        using var response = await _googleHttp.GetAsync(imageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var finalUri = response.RequestMessage?.RequestUri ?? imageUri;
        if (!GooglePhotosAlbumParser.IsImageHost(finalUri))
            throw new InvalidOperationException("Google Photos redirected the image to an unexpected host.");
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Google Photos did not return an image.");
        var temporary = destination + ".tmp";
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var output = File.Create(temporary))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > 25_000_000) throw new InvalidOperationException("Google Photos image exceeded 25 MB.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        File.Move(temporary, destination, true);
    }

    private static async Task<string> ReadLimitedTextAsync(HttpContent content, int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (output.Length + read > maximumBytes) throw new InvalidOperationException("Google Photos page was unexpectedly large.");
            output.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private async Task<HttpResponseMessage> GetGooglePageAsync(Uri initial, CancellationToken cancellationToken)
    {
        var current = GooglePhotosAlbumParser.ValidateAlbumUrl(initial.AbsoluteUri);
        for (var redirect = 0; redirect <= 5; redirect++)
        {
            var response = await _googleHttp.GetAsync(current, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is not (HttpStatusCode.Moved or HttpStatusCode.Redirect or
                HttpStatusCode.RedirectMethod or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect))
                return response;
            var location = response.Headers.Location;
            response.Dispose();
            if (location is null) throw new InvalidOperationException("Google Photos returned an invalid redirect.");
            current = GooglePhotosAlbumParser.ValidateAlbumUrl(new Uri(current, location).AbsoluteUri);
        }
        throw new InvalidOperationException("Google Photos returned too many redirects.");
    }

    private static async Task<GooglePhotosCacheManifest?> ReadGoogleManifestAsync(string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<GooglePhotosCacheManifest>(
                await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { throw; }
        catch { return null; }
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

        if (settings.ShowDescription && !string.IsNullOrWhiteSpace(metadata.Description))
            DrawDescription(drawing, metadata.Description, width, height, location is not null);

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

    private static void DrawDescription(DrawingContext drawing, string description, int width, int height,
        bool mapVisible)
    {
        var content = description.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var family = new System.Windows.Media.FontFamily(
            new Uri("pack://application:,,,/"), "./Assets/Fonts/#Google Sans Flex");
        var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);
        var fontSize = Math.Clamp(width / 85d, 20d, 27d);
        var maximumWidth = Math.Max(320, mapVisible ? width - 470 : width - 96);
        FormattedText Make(System.Windows.Media.Brush brush) => new(content, CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, typeface, fontSize, brush, 1.0)
        {
            TextAlignment = TextAlignment.Left,
            LineHeight = fontSize * 1.22,
            MaxTextWidth = maximumWidth,
            Trimming = TextTrimming.None
        };
        var text = Make(Brushes.White);
        var maximumHeight = Math.Max(160, height - 96);
        while (text.Height > maximumHeight && fontSize > 12)
        {
            fontSize -= 1;
            text = Make(Brushes.White);
        }
        var origin = new Point(48, height - 48 - text.Height);
        var glow = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
        glow.Freeze();
        foreach (var offset in new[] { new Vector(-3, 0), new Vector(3, 0), new Vector(0, -3), new Vector(0, 3),
                     new Vector(-2, -2), new Vector(2, -2), new Vector(-2, 2), new Vector(2, 2), new Vector(5, 6) })
            drawing.DrawText(Make(glow), origin + offset);
        drawing.DrawText(text, origin);
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
            ReadGps(metadata, "/app1/ifd/gps/{ushort=4}", "/app1/ifd/gps/{ushort=3}"), ReadOrientation(metadata), null);
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
        try { await SelectAndDisplayAsync(advance: false, CancellationToken.None).ConfigureAwait(false); }
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
        _googleHttp.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record PhotoMetadata(DateTime TakenAt, double? Latitude, double? Longitude, ushort Orientation,
        string? Description);
    private sealed record GooglePhotosCacheManifest(string Id, string ImagePath, DateTimeOffset? Taken,
        DateTimeOffset? Added, string Description, DateTimeOffset? DescriptionCheckedUtc);
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
