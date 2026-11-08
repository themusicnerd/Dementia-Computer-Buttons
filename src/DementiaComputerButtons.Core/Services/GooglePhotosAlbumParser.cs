using System.Text.Json;
using System.Text.RegularExpressions;

namespace DementiaComputerButtons.Core.Services;

public sealed record GooglePhotosAlbumPhoto(
    string Id,
    Uri ImageUrl,
    DateTimeOffset? Taken,
    DateTimeOffset? Added,
    int AlbumIndex);

public static partial class GooglePhotosAlbumParser
{
    [GeneratedRegex(@"\bdata\s*:\s*\[", RegexOptions.CultureInvariant)]
    private static partial Regex DataArrayStart();

    public static Uri ValidateAlbumUrl(string value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            throw new ArgumentException("Use an HTTPS Google Photos shared-album link.", nameof(value));
        var allowed = uri.Host.Equals("photos.app.goo.gl", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath.Length > 2 ||
                      uri.Host.Equals("photos.google.com", StringComparison.OrdinalIgnoreCase) &&
                      uri.AbsolutePath.StartsWith("/share/", StringComparison.OrdinalIgnoreCase);
        if (!allowed) throw new ArgumentException("Use a Google Photos shared-album link.", nameof(value));
        return uri;
    }

    public static IReadOnlyList<GooglePhotosAlbumPhoto> ExtractPhotos(string html, DateTimeOffset? now = null)
    {
        var photos = new Dictionary<string, GooglePhotosAlbumPhoto>(StringComparer.Ordinal);
        var albumIndex = 0;
        foreach (var json in ExtractDataArrays(html))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                Visit(document.RootElement, photos, ref albumIndex, now ?? DateTimeOffset.UtcNow);
            }
            catch (JsonException) { }
        }
        return photos.Values.OrderBy(photo => photo.AlbumIndex).ToArray();
    }

    public static GooglePhotosAlbumPhoto? ChooseLatest(IEnumerable<GooglePhotosAlbumPhoto> photos) =>
        photos.Where(photo => photo.Added is not null)
            .OrderByDescending(photo => photo.Added)
            .ThenByDescending(photo => photo.Taken)
            .ThenByDescending(photo => photo.AlbumIndex)
            .FirstOrDefault();

    public static string ExtractDescription(string html, string mediaId)
    {
        foreach (var json in ExtractDataArrays(html))
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 5 ||
                    root[0].ValueKind != JsonValueKind.Array || root[0].GetArrayLength() == 0 ||
                    root[0][0].ValueKind != JsonValueKind.String || root[0][0].GetString() != mediaId ||
                    root[4].ValueKind != JsonValueKind.Object || !root[4].TryGetProperty("11", out var description) ||
                    description.ValueKind != JsonValueKind.String) continue;
                var value = description.GetString()?.Trim() ?? string.Empty;
                return value.Length <= 12000 ? value : value[..12000];
            }
            catch (JsonException) { }
        }
        return string.Empty;
    }

    private static IEnumerable<string> ExtractDataArrays(string html)
    {
        if (string.IsNullOrEmpty(html)) yield break;
        foreach (Match match in DataArrayStart().Matches(html))
        {
            var start = match.Index + match.Length - 1;
            var depth = 0;
            var quoted = false;
            var escaped = false;
            for (var index = start; index < html.Length; index++)
            {
                var character = html[index];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') quoted = false;
                }
                else if (character == '"') quoted = true;
                else if (character == '[') depth++;
                else if (character == ']' && --depth == 0)
                {
                    yield return html[start..(index + 1)];
                    break;
                }
            }
        }
    }

    private static void Visit(JsonElement value, IDictionary<string, GooglePhotosAlbumPhoto> photos,
        ref int albumIndex, DateTimeOffset now)
    {
        if (value.ValueKind != JsonValueKind.Array) return;
        if (value.GetArrayLength() >= 3 && value[0].ValueKind == JsonValueKind.String &&
            value[1].ValueKind == JsonValueKind.Array && value[1].GetArrayLength() > 0 &&
            value[1][0].ValueKind == JsonValueKind.String &&
            Uri.TryCreate(value[1][0].GetString(), UriKind.Absolute, out var imageUri) && IsImageHost(imageUri))
        {
            var id = value[0].GetString()!;
            var taken = ReadTimestamp(value[2], now);
            var added = value.GetArrayLength() > 5 ? ReadTimestamp(value[5], now) : null;
            photos[id] = new(id, imageUri, taken, added, albumIndex++);
        }
        foreach (var child in value.EnumerateArray())
            if (child.ValueKind == JsonValueKind.Array) Visit(child, photos, ref albumIndex, now);
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement value, DateTimeOffset now)
    {
        double milliseconds;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) milliseconds = number;
        else if (value.ValueKind == JsonValueKind.String &&
                 double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                     System.Globalization.CultureInfo.InvariantCulture, out number)) milliseconds = number;
        else return null;
        var minimum = DateTimeOffset.UnixEpoch.AddYears(30).ToUnixTimeMilliseconds();
        var maximum = now.AddDays(1).ToUnixTimeMilliseconds();
        if (!double.IsFinite(milliseconds) || milliseconds <= minimum || milliseconds >= maximum) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds((long)milliseconds);
    }

    public static bool IsImageHost(Uri uri) => uri.Scheme == Uri.UriSchemeHttps &&
        new[] { "lh3.googleusercontent.com", "lh4.googleusercontent.com", "lh5.googleusercontent.com", "lh6.googleusercontent.com" }
            .Contains(uri.Host, StringComparer.OrdinalIgnoreCase);
}
