using DementiaComputerButtons.Core.Services;

namespace DementiaComputerButtons.Tests;

public sealed class GooglePhotosAlbumParserTests
{
    [Theory]
    [InlineData("https://photos.app.goo.gl/abc123")]
    [InlineData("https://photos.google.com/share/example?key=secret")]
    public void AcceptsSharedAlbumUrls(string value) =>
        Assert.Equal(value, GooglePhotosAlbumParser.ValidateAlbumUrl(value).AbsoluteUri);

    [Theory]
    [InlineData("http://photos.app.goo.gl/abc123")]
    [InlineData("https://example.com/album")]
    [InlineData("https://photos.google.com/not-shared")]
    public void RejectsOtherUrls(string value) =>
        Assert.Throws<ArgumentException>(() => GooglePhotosAlbumParser.ValidateAlbumUrl(value));

    [Fact]
    public void ParsesDatedRecordsWithoutExecutingScripts()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        var taken = now.AddHours(-2).ToUnixTimeMilliseconds();
        var added = now.AddHours(-1).ToUnixTimeMilliseconds();
        var html = $"<script>AF_initDataCallback({{data:[[[\"id1\",[\"https://lh3.googleusercontent.com/pw/example\",1200,900],{taken},null,null,{added}],[\"bad\",[\"https://evil.example/x\"],{taken}]]],sideChannel:{{}}}})</script>";

        var photo = Assert.Single(GooglePhotosAlbumParser.ExtractPhotos(html, now));

        Assert.Equal("id1", photo.Id);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(taken), photo.Taken);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(added), photo.Added);
        Assert.Empty(GooglePhotosAlbumParser.ExtractPhotos("unfamiliar page format", now));
    }

    [Fact]
    public void NewestCaptureBreaksEqualAdditionTimeTie()
    {
        var added = DateTimeOffset.Parse("2026-11-06T13:27:06Z");
        var earlier = new GooglePhotosAlbumPhoto("earlier", new("https://lh3.googleusercontent.com/a"),
            DateTimeOffset.Parse("2026-11-06T02:20:09Z"), added, 0);
        var later = new GooglePhotosAlbumPhoto("later", new("https://lh3.googleusercontent.com/b"),
            DateTimeOffset.Parse("2026-11-06T02:44:40Z"), added, 1);

        Assert.Equal("later", GooglePhotosAlbumParser.ChooseLatest([earlier, later])?.Id);
        Assert.Equal("later", GooglePhotosAlbumParser.ChooseLatest([later, earlier])?.Id);
    }

    [Fact]
    public void DescriptionComesOnlyFromMatchingMediaRecord()
    {
        const string html = "AF_initDataCallback({data:[[\"photo-id\"],null,null,null,{\"11\":\"  A family picnic.  \"}]})";

        Assert.Equal("A family picnic.", GooglePhotosAlbumParser.ExtractDescription(html, "photo-id"));
        Assert.Empty(GooglePhotosAlbumParser.ExtractDescription(html, "other-id"));
    }
}
