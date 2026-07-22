using Abis.Api.Data;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>Guards the ABIS-owned schema seed list. <see cref="AbisSchema.BuildOwnedDdl"/> spreads generator
/// methods that read static data arrays (X12 code maps, edi_type descriptions). When the list was a static
/// FIELD initializer it ran during static construction — before those arrays were initialized — and threw a
/// NullReferenceException that aborted the entire Oracle startup seed (SQLite never touched it, so CI stayed
/// green while every Oracle deploy silently failed to seed). Building it here would throw again if that
/// regresses.</summary>
public class AbisSchemaSeedTests
{
    [Fact]
    public void BuildOwnedDdl_builds_without_throwing_and_is_non_empty()
    {
        var ddl = AbisSchema.BuildOwnedDdl();   // throws TypeInitializationException if static-init order regresses
        Assert.NotNull(ddl);
        Assert.NotEmpty(ddl);
    }

    [Fact]
    public void BuildOwnedDdl_includes_the_generator_backed_seeds_that_broke_static_init()
    {
        var ddl = AbisSchema.BuildOwnedDdl();

        // The 846 AISI code-map seeds (Edi846CodeMapSeeds, which reads the X12Coil array that was null at
        // static-init time) — the exact rows whose enumeration threw the NRE.
        Assert.Contains(ddl, s => s.Contains("INSERT INTO abis_x12_coil"));
        Assert.Contains(ddl, s => s.Contains("INSERT INTO abis_scrap_type_x12"));
        // The edi_type description seeds (EdiTypeSeeds).
        Assert.Contains(ddl, s => s.Contains("INSERT INTO edi_type") && s.Contains("Order Status Report"));
        // The partner-profile seeds that were rolled back on the live DB, incl. the new Constellium 870.
        Assert.Contains(ddl, s => s.Contains("'constellium'") && s.Contains("'870'"));
    }
}
