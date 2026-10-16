using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class LatestDayPhotoSelectorTests
{
    [Fact]
    public void SelectsEveryPhotoFromNewestCaptureDayInTimeOrder()
    {
        PhotoCandidate[] candidates =
        [
            new("older.jpg", new DateTime(2026, 10, 14, 17, 0, 0)),
            new("latest-evening.jpg", new DateTime(2026, 10, 16, 20, 0, 0)),
            new("latest-morning.jpg", new DateTime(2026, 10, 16, 8, 0, 0)),
            new("middle.jpg", new DateTime(2026, 10, 15, 12, 0, 0))
        ];

        var selected = LatestDayPhotoSelector.Select(candidates);

        Assert.Equal(["latest-morning.jpg", "latest-evening.jpg"], selected.Select(photo => photo.Path));
    }

    [Fact]
    public void NextIndexWrapsAndStartsAtFirstForUnknownPhoto()
    {
        PhotoCandidate[] candidates =
        [
            new("one.jpg", DateTime.Today),
            new("two.jpg", DateTime.Today.AddMinutes(1))
        ];

        Assert.Equal(1, LatestDayPhotoSelector.NextIndex(candidates, "one.jpg"));
        Assert.Equal(0, LatestDayPhotoSelector.NextIndex(candidates, "two.jpg"));
        Assert.Equal(0, LatestDayPhotoSelector.NextIndex(candidates, "missing.jpg"));
        Assert.Equal(-1, LatestDayPhotoSelector.NextIndex([], null));
    }
}
