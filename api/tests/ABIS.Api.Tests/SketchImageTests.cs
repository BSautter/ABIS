using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The sketch drawing is the one column in this schema that can come back <b>quietly wrong</b>.
/// <para><c>sketch.sketch_view</c> is a <c>LONG RAW</c> on Oracle holding an uncompressed BMP — every
/// one of the 128 sketches on live <c>.230</c> is 417,078 bytes. ODP.NET truncates a LONG to
/// <c>InitialLONGFetchSize</c> <b>silently</b> when it is too small, and there is no global switch for
/// it (the property lives on <c>OracleCommand</c>, and this repository builds commands through Dapper).
/// A truncated BMP still carries a valid header, so it renders as a partial drawing rather than
/// failing — on a shape the shop floor cuts metal from.</para>
/// <para>What makes it testable is that a BMP <b>declares its own total length</b> in bytes 2–5. The
/// live images satisfy that property (checked against .230: header says 417,078, byte count is
/// 417,078), so asserting it here catches a truncated read whatever the driver does.</para>
/// </summary>
public class SketchImageTests
{
    private static int DeclaredLength(byte[] bmp) => BitConverter.ToInt32(bmp, 2);

    [Fact]
    public async Task The_drawing_comes_back_whole_and_is_a_BMP()
    {
        using var f = new Factory();
        var res = await Client(f).GetAsync("/api/sketches/1/image");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("image/bmp", res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal((byte)'B', bytes[0]);
        Assert.Equal((byte)'M', bytes[1]);

        // The load-bearing assertion: the image says how long it is, and it is that long. This is what
        // a truncated LONG RAW read would fail — the header would still say 417,078 while the payload
        // stopped short.
        Assert.Equal(bytes.Length, DeclaredLength(bytes));
    }

    [Fact]
    public async Task A_sketch_with_no_drawing_is_404_not_an_empty_image()
    {
        // Sketch 2 exists but carries no image. Returning 200 with zero bytes would put a broken
        // image on the page and look like a rendering fault rather than missing data.
        using var f = new Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).GetAsync("/api/sketches/2/image")).StatusCode);
    }

    [Fact]
    public async Task An_unknown_sketch_is_404()
    {
        using var f = new Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).GetAsync("/api/sketches/999999/image")).StatusCode);
    }

    [Fact]
    public async Task The_drawing_is_cacheable_because_it_is_large_and_does_not_change()
    {
        // 417 KB per sketch on the real data, fetched by the job screen and the DAS console over the
        // plant LAN. Without a cache lifetime every visit re-fetches the whole bitmap.
        using var f = new Factory();
        var res = await Client(f).GetAsync("/api/sketches/1/image");
        Assert.True(res.Headers.CacheControl?.MaxAge > TimeSpan.Zero,
            "the sketch image should carry a cache lifetime");
    }

    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_sketch_{Guid.NewGuid():N}.db");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.UseSetting("Database:ConnectionString", $"Data Source={_dbPath}");
            builder.UseSetting("Database:Seed", "true");
            builder.UseSetting("ApiKeys:Enabled", "true");
            builder.UseSetting("ApiKeys:Keys:0", "test-key");
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
        }
    }

    private static HttpClient Client(WebApplicationFactory<Program> f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        return c;
    }
}
