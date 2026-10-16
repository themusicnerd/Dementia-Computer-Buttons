namespace DementiaComputerButtons.Core.Services;

public sealed record PhotoCandidate(string Path, DateTime TakenAt);

public static class LatestDayPhotoSelector
{
    public static IReadOnlyList<PhotoCandidate> Select(IEnumerable<PhotoCandidate> candidates) =>
        candidates
            .GroupBy(photo => DateOnly.FromDateTime(photo.TakenAt))
            .OrderByDescending(group => group.Key)
            .FirstOrDefault()?
            .OrderBy(photo => photo.TakenAt)
            .ThenBy(photo => photo.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

    public static int NextIndex(IReadOnlyList<PhotoCandidate> photos, string? currentPath)
    {
        if (photos.Count == 0) return -1;
        var current = photos.Select((photo, index) => (photo, index))
            .FirstOrDefault(item => item.photo.Path.Equals(currentPath, StringComparison.OrdinalIgnoreCase));
        return current.photo is null ? 0 : (current.index + 1) % photos.Count;
    }
}
