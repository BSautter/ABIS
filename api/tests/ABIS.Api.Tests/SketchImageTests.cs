using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The part drawing — the one column in this schema that can come back <b>quietly wrong</b> in two
/// independent ways.
///
/// <para><b>1. From the wrong table.</b> There are two identically-shaped drawing tables and
/// <c>ab_job.sketch_id</c> keys the JPEG one; legacy moved in 2016 and every live consumer followed.
/// They were re-keyed rather than copied, so reading <c>sketch</c> does not merely miss images — on
/// the live database 3,420 jobs get back a drawing of a <b>different part</b> (id 125 is "AB-2-5"
/// there and "JL FENDER" in <c>sketch_jpg</c>, across 1,203 jobs). The fixture reproduces that
/// collision on purpose, which is what makes the tests below able to tell the tables apart.</para>
///
/// <para><b>2. Truncated.</b> <c>sketch_view</c> is a <c>LONG RAW</c>, and ODP.NET truncates a LONG to
/// <c>InitialLONGFetchSize</c> <b>silently</b> when it is too small — there is no global switch (the
/// property lives on <c>OracleCommand</c>, and this repository builds commands through Dapper). A
/// truncated JPEG still carries a valid header, so it renders as a partial drawing rather than
/// failing — on a shape the shop floor cuts metal from. What makes that testable is the
/// <c>FF D9</c> end-of-image marker: it is the last thing in the file, so a short read loses it.</para>
/// </summary>
public class SketchImageTests
{
    /// <summary>Whether the payload carries JPEG's end-of-image marker as its final two bytes.</summary>
    private static bool EndsAtEoi(byte[] jpeg) =>
        jpeg.Length >= 2 && jpeg[^2] == 0xFF && jpeg[^1] == 0xD9;

    // ---- Which table the drawing came from ------------------------------------------------

    [Fact]
    public async Task The_drawing_is_the_JPEG_one_because_that_is_the_table_jobs_key_into()
    {
        // Both tables hold an image under id 1. Reading the retired one would return a BMP here, and
        // on real data would return someone else's part.
        using var f = new Factory();
        var res = await Client(f).GetAsync("/api/sketches/1/image");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.NotEqual((byte)'B', bytes[0]);   // 'BM' would mean the retired BMP table answered
    }

    [Fact]
    public async Task The_sketch_HEADER_also_comes_from_the_live_table()
    {
        // The name matters as much as the image: the folder screen prints "Sketch #" beside the
        // drawing, and the retired table names a different part under the same id.
        using var f = new Factory();
        var sketch = await Client(f).GetFromJsonAsync<SketchDto>("/api/sketches/1");
        Assert.Equal("BRKT-A rev1", sketch!.SketchName);
        Assert.DoesNotContain("RETIRED", sketch.SketchName!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_media_type_is_read_from_the_bytes_rather_than_declared()
    {
        // Sniffed, not hard-coded — a mislabelled image renders as a broken box with no clue why, and
        // this is the exact place the two formats have already been confused once.
        using var f = new Factory();
        var res = await Client(f).GetAsync("/api/sketches/1/image");
        Assert.Equal("image/jpeg", res.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, "image/jpeg")]
    [InlineData(new byte[] { 0x42, 0x4D, 0x46, 0x00 }, "image/bmp")]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 }, "application/octet-stream")]
    [InlineData(new byte[] { 0xFF }, "application/octet-stream")]
    public void An_unrecognised_payload_is_not_guessed_at(byte[] head, string expected) =>
        Assert.Equal(expected, Abis.Api.Endpoints.ApiEndpoints.SniffImage(head).Media);

    // ---- Whether it came back whole ---------------------------------------------------------

    [Fact]
    public async Task The_drawing_comes_back_whole()
    {
        using var f = new Factory();
        var bytes = await Client(f).GetByteArrayAsync("/api/sketches/1/image");

        // The load-bearing assertion. A truncated LONG RAW read keeps the header and loses the tail,
        // so the file still looks like a JPEG and still decodes to a partial drawing.
        Assert.True(EndsAtEoi(bytes), "the drawing does not end at JPEG's FF D9 marker — the LONG RAW read was cut short");
    }

    // ---- Absence ------------------------------------------------------------------------------

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
        // Fetched by the job screen and the DAS console over the plant LAN. Without a cache lifetime
        // every visit re-fetches the whole image.
        using var f = new Factory();
        var res = await Client(f).GetAsync("/api/sketches/1/image");
        Assert.True(res.Headers.CacheControl?.MaxAge > TimeSpan.Zero,
            "the sketch image should carry a cache lifetime");
    }

    private sealed record SketchDto(long SketchId, string? SketchName);

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
