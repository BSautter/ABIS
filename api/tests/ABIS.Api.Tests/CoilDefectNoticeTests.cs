using Abis.Api.Email;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The coil-received-with-defect notification — legacy <c>coil_receiving_12.pl</c> calling
/// <c>P_SEND_EMAIL_COIL_DEFECT</c>, a stored procedure that opens UTL_SMTP itself.
/// </summary>
public class CoilDefectNoticeTests
{
    private static readonly DateTime At = new(2026, 8, 20, 14, 5, 0);

    /// <summary>The three body lines, word for word from the procedure — including its
    /// <c>MM/DD/YYYY, HH24:MI</c> timestamp. These go to six people who will compare them against what
    /// legacy used to send.</summary>
    [Fact]
    public void The_body_is_the_procedures_wording()
    {
        var body = CoilDefectNotice.Body("814-33670", At);
        var lines = body.Split(Environment.NewLine);

        Assert.Equal(3, lines.Length);
        Assert.Equal("Coil Received With Defect Notification: Customer Coil # 814-33670.", lines[0]);
        Assert.Equal("Please follow up accordingly.", lines[1]);
        Assert.Equal("Notification Time: 08/20/2026, 14:05.", lines[2]);
    }

    /// <summary>Legacy's format string is <c>MM/DD/YYYY, HH24:MI</c> — 24-hour, zero-padded, with that
    /// comma. Pinned because a plausible-looking "improvement" to a local format would change what six
    /// people read every time a damaged coil arrives.</summary>
    [Theory]
    [InlineData(2026, 1, 2, 3, 4, "01/02/2026, 03:04")]
    [InlineData(2026, 12, 31, 23, 59, "12/31/2026, 23:59")]
    [InlineData(2026, 8, 20, 0, 0, "08/20/2026, 00:00")]
    public void The_timestamp_is_24_hour_and_zero_padded(int y, int mo, int d, int h, int mi, string expected)
    {
        Assert.Equal(expected, CoilDefectNotice.FormatTime(new DateTime(y, mo, d, h, mi, 0)));
    }

    /// <summary>The one deliberate addition. The procedure opens the SMTP data section and writes the
    /// body straight in, never writing a <c>Subject:</c> header — so every one of these arrived with an
    /// empty subject and six people had to open it to learn which coil. That is a defect, not a
    /// convention worth preserving.</summary>
    [Fact]
    public void A_subject_is_added_and_it_names_the_coil()
    {
        Assert.Contains("814-33670", CoilDefectNotice.Subject("814-33670"));
    }

    /// <summary>The default list is legacy's six hard-coded addresses, so behaviour is unchanged out of
    /// the box — but it is configuration now, because in legacy changing it means a DBA and a proc
    /// recompile.</summary>
    [Fact]
    public void The_default_recipients_are_the_procedures_six()
    {
        var opts = new CoilDefectNoticeOptions();
        Assert.Equal(6, opts.Recipients.Count);
        Assert.Contains("vhuang@albl.com", opts.Recipients);
        Assert.Contains("celliott@albl.com", opts.Recipients);
    }

    /// <summary>Off unless someone turns it on. Nothing in a port should start mailing six people the
    /// first time it is deployed.</summary>
    [Fact]
    public void It_is_disabled_by_default()
    {
        Assert.False(new CoilDefectNoticeOptions().Enabled);
    }

    [Fact]
    public void Build_addresses_the_configured_list()
    {
        var opts = new CoilDefectNoticeOptions();
        var msg = CoilDefectNotice.Build("C-1", opts.Recipients, At);
        Assert.Equal(opts.Recipients, msg.To);
        Assert.False(msg.IsHtml);
    }
}
