using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Shipment / BOL close-out: the guided close endpoint (marks a shipment shipped + stamps
/// the sent/actual dates) and the truck→BOL link (an outbound truck signing out at the gate closes
/// its linked shipment). Calls run as the API-key service account.</summary>
public sealed class ShipmentCloseTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"abis_shipclose_{Guid.NewGuid():N}.db");
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

    // Create an OPEN shipment (status 3, no dates) and return its packing-list number.
    private static async Task<long> NewOpenShipment(HttpClient c)
    {
        var create = await c.PostAsJsonAsync("/api/shipments",
            new { carrierId = 1201, customerId = 4001, vehicleId = "TRK-CLOSE", shipmentStatus = 3, shipmentNotes = "open" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        return (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("packingList").GetInt64();
    }

    [Fact]
    public async Task Close_marks_shipped_and_stamps_dates()
    {
        using var f = new Factory();
        var c = Client(f);
        var pl = await NewOpenShipment(c);

        var closed = await c.PostAsync($"/api/shipments/{pl}/close", null);
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        var s = await closed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, s.GetProperty("shipmentStatus").GetInt32());                 // ClosedShipmentStatus
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("dateSent").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("shipmentActualedDateTime").ValueKind);
    }

    [Fact]
    public async Task Close_of_a_missing_shipment_is_404()
    {
        using var f = new Factory();
        Assert.Equal(HttpStatusCode.NotFound, (await Client(f).PostAsync("/api/shipments/99999999/close", null)).StatusCode);
    }

    [Fact]
    public async Task Outbound_truck_sign_out_closes_the_linked_bol()
    {
        using var f = new Factory();
        var c = Client(f);
        var pl = await NewOpenShipment(c);

        // An outbound truck appointment linked to that shipment (refType SHIPMENT, refId = packing list).
        var create = await c.PostAsJsonAsync("/api/truck-appointments", new
        {
            direction = "OUTBOUND", carrierId = 7001, carrierName = "Acme Freight",
            scheduledStart = "2026-02-01T14:00:00Z", scheduledEnd = "2026-02-01T15:00:00Z",
            refType = "SHIPMENT", refId = pl.ToString(),
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appointmentId").GetInt64();

        await c.PostAsync($"/api/truck-appointments/{id}/check-in", null);
        var checkedOut = await c.PostAsync($"/api/truck-appointments/{id}/check-out", null);
        Assert.Equal(HttpStatusCode.OK, checkedOut.StatusCode);

        // The gate sign-out closed the linked shipment.
        var s = await c.GetFromJsonAsync<JsonElement>($"/api/shipments/{pl}");
        Assert.Equal(0, s.GetProperty("shipmentStatus").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, s.GetProperty("dateSent").ValueKind);
    }

    [Fact]
    public async Task Inbound_truck_sign_out_does_not_touch_shipments()
    {
        using var f = new Factory();
        var c = Client(f);
        var pl = await NewOpenShipment(c);   // stays open — an inbound truck must not close it

        var create = await c.PostAsJsonAsync("/api/truck-appointments", new
        {
            direction = "INBOUND", carrierId = 7001, carrierName = "Acme Freight",
            scheduledStart = "2026-02-01T14:00:00Z", scheduledEnd = "2026-02-01T15:00:00Z",
            refType = "SHIPMENT", refId = pl.ToString(),   // even if mis-linked, inbound must not close
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("appointmentId").GetInt64();
        await c.PostAsync($"/api/truck-appointments/{id}/check-out", null);

        var s = await c.GetFromJsonAsync<JsonElement>($"/api/shipments/{pl}");
        Assert.Equal(3, s.GetProperty("shipmentStatus").GetInt32());   // still open
    }
}
