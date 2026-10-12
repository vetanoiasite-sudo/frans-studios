using System.Text;
using FransStudios.Api.Configuration;
using FransStudios.Api.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace FransStudios.Api.Tests;

public sealed class PublicMediaStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fs-media-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Png_uploads_are_encoded_as_lossy_webp()
    {
        // ImageSharp keeps a PNG source's lossless format unless told otherwise, which made
        // poster WebPs several times larger than their JPEG fallbacks.
        using var png = new MemoryStream();
        using (var image = new Image<Rgb24>(900, 600))
        {
            image.ProcessPixelRows(rows =>
            {
                for (var y = 0; y < rows.Height; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++) row[x] = new Rgb24((byte)(x * 7 % 256), (byte)(y * 3 % 256), (byte)((x ^ y) % 256));
                }
            });
            await image.SaveAsPngAsync(png);
        }
        png.Position = 0;

        var store = new PublicMediaStore(Options.Create(new StorageOptions { PublicMediaPath = _root }), NullLogger<PublicMediaStore>.Instance);
        var asset = await store.SaveImageAsync(png, CancellationToken.None);

        var webp = await File.ReadAllBytesAsync(Path.Combine(_root, asset.Key, "900.webp"));
        Assert.Equal("WEBP", Encoding.ASCII.GetString(webp, 8, 4));
        Assert.Equal("VP8 ", Encoding.ASCII.GetString(webp, 12, 4)); // lossy; lossless would be "VP8L"
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
