using System.Globalization;

namespace Abis.Api.Email;

/// <summary>
/// Config for the coil-received-with-defect notification (<c>Notifications:CoilDefect</c>) — the handheld
/// receiving station's "email" action, legacy <c>coil_receiving_12.pl</c> → <c>P_SEND_EMAIL_COIL_DEFECT</c>.
///
/// <para>The recipient list is configuration rather than a constant because in legacy it is six addresses
/// hard-coded inside a stored procedure: changing who gets told a defective coil arrived means a DBA and a
/// proc recompile. The legacy six are the default here, so behaviour is unchanged out of the box, but the
/// plant can edit the list without a deploy.</para>
///
/// <para>OFF by default, like every other notification in this app. Nothing about the port should start
/// mailing six people the first time it is deployed.</para>
/// </summary>
public sealed class CoilDefectNoticeOptions
{
    public const string SectionName = "Notifications:CoilDefect";

    /// <summary>Master switch. When false the endpoint reports the notice as not sent and mails nobody.</summary>
    public bool Enabled { get; set; }

    /// <summary>Who is told. Defaults to the six addresses hard-coded in
    /// <c>P_SEND_EMAIL_COIL_DEFECT</c>.</summary>
    public List<string> Recipients { get; set; } =
    [
        "vhuang@albl.com", "rsedam@albl.com", "dpolking@albl.com",
        "cbeamer@albl.com", "mmillis@albl.com", "celliott@albl.com",
    ];
}

/// <summary>
/// The message legacy sends, reproduced.
///
/// <para><b>One deliberate addition.</b> The stored procedure opens the SMTP data section and writes the
/// three body lines straight in — it never writes a <c>Subject:</c> header, so every one of these arrives
/// with an empty subject. That is a defect, not a convention worth preserving: six people receive an
/// unsubjected mail and have to open it to learn which coil. A subject naming the coil is added; the body
/// is otherwise word-for-word, including its timestamp format.</para>
/// </summary>
public static class CoilDefectNotice
{
    /// <summary>Legacy's <c>TO_CHAR(SYSDATE, 'MM/DD/YYYY, HH24:MI')</c>.</summary>
    public static string FormatTime(DateTime at) =>
        at.ToString("MM/dd/yyyy, HH:mm", CultureInfo.InvariantCulture);

    public static string Subject(string coilOrgNum) =>
        $"Coil received with defect: customer coil # {coilOrgNum}";

    /// <summary>The three body lines, verbatim from the procedure.</summary>
    public static string Body(string coilOrgNum, DateTime at) =>
        string.Join(Environment.NewLine,
            $"Coil Received With Defect Notification: Customer Coil # {coilOrgNum}.",
            "Please follow up accordingly.",
            $"Notification Time: {FormatTime(at)}.");

    public static EmailMessage Build(string coilOrgNum, IReadOnlyList<string> to, DateTime at) =>
        new(to, Subject(coilOrgNum), Body(coilOrgNum, at));
}
