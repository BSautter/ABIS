using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Truck appointment scheduling (abis_truck_appointment): schedule → gate check-in →
/// check-out, status transitions, list filters, and the direction guard.</summary>
public sealed class TruckAppointmentTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_truck_{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Schedule_check_in_and_check_out_a_truck()
    {
        using var f = new Factory();
        var c = Client(f);

        var create = await c.PostAsJsonAsync("/api/truck-appointments", new
        {
            direction = "OUTBOUND", carrierId = 7001, carrierName = "Acme Freight", dock = "D-3",
            scheduledStart = "2026-02-01T14:00:00Z", scheduledEnd = "2026-02-01T15:00:00Z",
            refType = "SHIPMENT", refId = "6001", driverName = "T. Nguyen", tractorNum = "TR-1",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var appt = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = appt.GetProperty("appointmentId").GetInt64();
        Assert.Equal(0, appt.GetProperty("truckStatus").GetInt32());   // Scheduled

        // Gate check-in → status 1, checkinTime stamped.
        var checkedIn = await (await c.PostAsync($"/api/truck-appointments/{id}/check-in", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, checkedIn.GetProperty("truckStatus").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, checkedIn.GetProperty("checkinTime").ValueKind);

        // At dock via status PATCH.
        var atDock = await (await c.PatchAsJsonAsync($"/api/truck-appointments/{id}/status", new { status = 2 })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, atDock.GetProperty("truckStatus").GetInt32());

        // Gate check-out → status 3, checkoutTime stamped.
        var checkedOut = await (await c.PostAsync($"/api/truck-appointments/{id}/check-out", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, checkedOut.GetProperty("truckStatus").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, checkedOut.GetProperty("checkoutTime").ValueKind);
    }

    [Fact]
    public async Task List_filters_by_direction_and_status()
    {
        using var f = new Factory();
        var c = Client(f);
        // Seed has 1 OUTBOUND (status 0) + 1 INBOUND (status 1).
        var inbound = await c.GetFromJsonAsync<JsonElement>("/api/truck-appointments?direction=INBOUND");
        Assert.True(inbound.GetProperty("totalCount").GetInt32() >= 1);
        foreach (var a in inbound.GetProperty("items").EnumerateArray())
            Assert.Equal("INBOUND", a.GetProperty("direction").GetString());

        var scheduled = await c.GetFromJsonAsync<JsonElement>("/api/truck-appointments?status=0");
        foreach (var a in scheduled.GetProperty("items").EnumerateArray())
            Assert.Equal(0, a.GetProperty("truckStatus").GetInt32());
    }

    [Fact]
    public async Task Direction_is_validated()
    {
        using var f = new Factory();
        var resp = await Client(f).PostAsJsonAsync("/api/truck-appointments", new { direction = "SIDEWAYS", dock = "D-9" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Check_in_a_missing_appointment_is_404()
    {
        using var f = new Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).PostAsync("/api/truck-appointments/99999999/check-in", null)).StatusCode);
    }
}
