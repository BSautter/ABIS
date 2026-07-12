using Abis.Api.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>The test-recipient override is the safety guarantee: no email can reach a real recipient while
/// it's set. (Smtp.Host is left empty here so the sender logs instead of touching a real relay.)</summary>
public class EmailSenderTests
{
    private static SmtpEmailSender Sender(EmailOptions opt) =>
        new(Options.Create(opt), NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task Override_redirects_every_recipient_to_the_test_inbox()
    {
        var sut = Sender(new EmailOptions { OverrideRecipient = "cmattinson@albl.com" });
        var r = await sut.SendAsync(new EmailMessage(
            new[] { "partner@external.com", "qa@albl.com" }, "861 alert", "body") { Cc = new[] { "boss@albl.com" } },
            CancellationToken.None);

        Assert.Equal(new[] { "cmattinson@albl.com" }, r.ActualRecipients);   // real recipients + Cc collapsed away
        Assert.False(r.Sent);                                                 // no SMTP host → logged, not sent
        Assert.Contains("cmattinson@albl.com", r.Detail);
    }

    [Fact]
    public async Task Without_override_keeps_the_real_recipients()
    {
        var sut = Sender(new EmailOptions { OverrideRecipient = null });
        var r = await sut.SendAsync(new EmailMessage(new[] { "qa@albl.com" }, "s", "b"), CancellationToken.None);
        Assert.Equal(new[] { "qa@albl.com" }, r.ActualRecipients);
    }

    [Fact]
    public async Task Blank_override_is_treated_as_off()
    {
        var sut = Sender(new EmailOptions { OverrideRecipient = "   " });
        var r = await sut.SendAsync(new EmailMessage(new[] { "qa@albl.com" }, "s", "b"), CancellationToken.None);
        Assert.Equal(new[] { "qa@albl.com" }, r.ActualRecipients);
    }

    [Fact]
    public async Task No_recipients_and_no_override_is_a_safe_noop()
    {
        var sut = Sender(new EmailOptions { OverrideRecipient = "" });
        var r = await sut.SendAsync(new EmailMessage(System.Array.Empty<string>(), "s", "b"), CancellationToken.None);
        Assert.False(r.Sent);
        Assert.Empty(r.ActualRecipients);
    }
}
