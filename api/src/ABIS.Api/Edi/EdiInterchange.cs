using System.Globalization;
using Abis.Api.Models;

namespace Abis.Api.Edi;

/// <summary>
/// The shared front of every EDI document: the sender identity (always Aluminum Blanking Co.) and the
/// envelope opener that writes the ISA / GS / ST headers from a trading-partner <see cref="EdiPartnerProfile"/>.
/// Each per-set generator (861, 870, 846, …) calls <see cref="Open"/> to start the interchange, then appends
/// its own body on the returned <see cref="X12Writer"/> and calls <c>Close()</c>. Generation only — nothing
/// here transmits. This is the going-forward standard: a new document conforms by opening here and persisting
/// through the shared EDI sink (see <c>AbisRepository.InsertEdiTransactionAsync</c>).
/// </summary>
public static class EdiInterchange
{
    /// <summary>ISA05/GS/interchange sender qualifier — Aluminum Blanking Co.</summary>
    public const string SenderQualifier = "01";
    /// <summary>ISA06 + GS02 sender id (the ABCo EDI id).</summary>
    public const string SenderId = "039630926T";
    /// <summary>The ABCo party DUNS — the N1*OU id + <c>outbound_edi_transaction.duns_from</c>.</summary>
    public const string SenderParty = "039630926";

    /// <summary>Open an X12 interchange for <paramref name="setId"/> using the partner profile's envelope: its
    /// component separator, segment suffix, receiver qualifier/id, envelope version, and GS functional code
    /// (each falling back to the per-set default when the profile leaves it blank). ISA13 = GS06 =
    /// <paramref name="groupControl"/>; ST02 = <paramref name="setControl"/> (the modern engine passes the same
    /// edi_file_id for both). Returns a writer positioned after ST — the caller appends the body then Close()s.</summary>
    public static X12Writer Open(
        EdiPartnerProfile profile, string setId, string gsFunctionalDefault, string versionDefault,
        long groupControl, long setControl, DateTime timestamp)
    {
        var options = new X12Options
        {
            ComponentSeparator = profile.ComponentSeparator ?? "",
            SegmentSuffix = profile.SegmentSuffix ?? "",
        };
        var w = new X12Writer(options);
        var receiverQualifier = string.IsNullOrEmpty(profile.ReceiverQualifier) ? "" : profile.ReceiverQualifier!;
        var receiverId = string.IsNullOrEmpty(profile.ReceiverId) ? "" : profile.ReceiverId!;
        var version = string.IsNullOrEmpty(profile.EnvelopeVersion) ? versionDefault : profile.EnvelopeVersion!;
        var gsFunc = string.IsNullOrEmpty(profile.GsFunctionalCode) ? gsFunctionalDefault : profile.GsFunctionalCode!;
        // GS02/GS03 usually equal the ISA sender/receiver; some partners assign their own (Arconic 861 GS02 =
        // R0P7ATN; the Novelis 870 GS03 = 001504935001 vs ISA08 0015049350011G).
        var gsSender = string.IsNullOrEmpty(profile.GsSenderCode) ? SenderId : profile.GsSenderCode!;
        var gsReceiver = string.IsNullOrEmpty(profile.GsReceiverCode) ? receiverId : profile.GsReceiverCode!;

        var gs = groupControl.ToString(CultureInfo.InvariantCulture);
        var yyMMdd = timestamp.ToString("yyMMdd", CultureInfo.InvariantCulture);
        var yyyyMMdd = timestamp.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var hhmm = timestamp.ToString("HHmm", CultureInfo.InvariantCulture);

        w.Isa("00", "", "00", "", SenderQualifier, SenderId, receiverQualifier, receiverId,
            yyMMdd, hhmm, "U", version, gs, "0", "P");
        w.Gs(gsFunc, gsSender, gsReceiver, yyyyMMdd, hhmm, gs, "X", "004010");
        w.St(setId, setControl.ToString(CultureInfo.InvariantCulture));
        return w;
    }

    /// <summary>The output EDI file name from the partner's file prefix (legacy <c>edi_file_prefix</c>),
    /// falling back to <paramref name="defaultPrefix"/> when the profile leaves it blank.</summary>
    public static string FileName(EdiPartnerProfile profile, long ediFileId, string defaultPrefix) =>
        $"{(string.IsNullOrEmpty(profile.FilePrefix) ? defaultPrefix : profile.FilePrefix)}{ediFileId.ToString(CultureInfo.InvariantCulture)}.edi";
}
