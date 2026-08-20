namespace Abis.Api.Edi;

/// <summary>
/// Reference data for the <b>Cleveland-Cliffs Steel — Outside Processing</b> EDI program (customer 3061).
/// See <c>docs/EDI_CLIFFS.md</c> for the full program map, the certification test plan, and the open decisions.
///
/// <para>Cliffs is an <b>onboarding</b> partner, not a running one: as of 2026-08-20 customer 3061 on the live
/// database is a name-and-DUNS shell with <b>zero</b> orders and <b>zero</b> coils, the legacy cron entries that
/// would run the 846 are commented out and marked "TEST ONLY", and every archived Cleveland-Cliffs 846 on disk is
/// the empty "Nothing to report." placeholder. There is therefore <b>no golden file</b> for any Cliffs document
/// and there cannot be one until Cliffs material physically arrives. The published guides are the spec.</para>
/// </summary>
public static class CliffsOutsideProcessing
{
    /// <summary>
    /// The <c>N1*MF</c> (Steel Producer) DUNS per Cliffs works, from the guides' N104 code list. The 861/870/846/856
    /// all carry one of these as the material owner, and <b>which one depends on the works the material came from</b>
    /// — it is not a single value for "Cliffs".
    ///
    /// <para><b>OPEN — do not guess.</b> The DUNS stored against customer 3061 is <c>606072130</c>
    /// (<c>customer.customer_duns_number_string</c>; the numeric <c>customer_duns_number</c> is NULL), which matches
    /// <b>none</b> of these four. It is also what the legacy proc hardcodes as the receiver and what the partner
    /// profile carries as ISA08. Until the plant confirms whether 606072130 is a VAN mailbox id (an envelope
    /// address) or a stale party DUNS (a body value), the two must not be conflated.</para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SteelProducerDuns = new Dictionary<string, string>
    {
        ["Indiana Harbor"] = "005159199",
        ["Kote"] = "613460476",
        ["Burns Harbor"] = "003913423",
        ["Cleveland Works"] = "122373918",
    };

    /// <summary>
    /// Whether the trailing <c>PID07</c> source subqualifier (the AISI table number — <c>***67</c>, <c>***70</c>,
    /// <c>***68</c>) is emitted on <c>PID*S*MAC</c> and <c>PID*S*MA</c>.
    ///
    /// <para><b>False, deliberately, and it overrides the guide.</b> Every published Cliffs example shows the table
    /// number in PID07. The live proc emits it in a commented-out line directly above the live one, under this
    /// comment, repeated at all three of its loops:</para>
    /// <code>
    /// --Email from Lisa received on Mon 5/18/2026 2:14 PM
    /// --Remove PID06 from PID*S*MA and *MAC segments
    /// </code>
    /// <para>A dated instruction from the partner's own analyst, received during this onboarding, outranks a guide
    /// published in February 2021. Do not "fix" these segments back to the guide shape without a newer instruction
    /// from Cliffs — and if one arrives, change it here, not in each generator.</para>
    /// </summary>
    public static readonly bool EmitPidTableSubqualifier = false;
}
