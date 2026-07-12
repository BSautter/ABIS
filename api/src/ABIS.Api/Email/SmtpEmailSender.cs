using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Abis.Api.Email;

/// <summary>
/// The SMTP email sender — and the enforcement point for the test-recipient override. Every message:
/// <list type="number">
/// <item>has its recipients rewritten to <see cref="EmailOptions.OverrideRecipient"/> when that is set
///   (the real To/Cc are preserved in the subject prefix + a body header so testers can see who it was for);</item>
/// <item>is sent via the configured SMTP relay — or, if no <see cref="SmtpOptions.Host"/> is configured,
///   is LOGGED instead of sent, so a missing relay never breaks a caller and the flow stays observable.</item>
/// </list>
/// A send failure is caught + logged (returns <c>Sent=false</c>) — email is best-effort; it must never
/// take down the report/EDI path that triggered it.
/// </summary>
public sealed class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> log) : IEmailSender
{
    private readonly EmailOptions _opt = options.Value;

    public async Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct)
    {
        var overridden = !string.IsNullOrWhiteSpace(_opt.OverrideRecipient);
        var actualTo = overridden ? new[] { _opt.OverrideRecipient!.Trim() } : message.To.ToArray();
        if (actualTo.Length == 0) return new EmailSendResult(false, actualTo, "No recipients.");

        var origWho = $"To: {string.Join(", ", message.To)}"
            + (message.Cc.Count > 0 ? $"; Cc: {string.Join(", ", message.Cc)}" : "");
        var subject = overridden ? $"[ABIS TEST → {_opt.OverrideRecipient!.Trim()}] {message.Subject}" : message.Subject;
        var body = overridden && !message.IsHtml
            ? $"*** TEST REDIRECT — originally addressed to {origWho} ***\n\n{message.Body}"
            : message.Body;

        // No relay configured → log-only. Still applies the override, so the "would-send" is safe + visible.
        if (string.IsNullOrWhiteSpace(_opt.Smtp.Host))
        {
            log.LogInformation("EMAIL (no SMTP host — logged, not sent) → {To} · subject: {Subject} ({Orig})",
                string.Join(", ", actualTo), subject, origWho);
            return new EmailSendResult(false, actualTo,
                $"Logged (no SMTP host configured). Would send to {string.Join(", ", actualTo)}.");
        }

        try
        {
            using var mail = new MailMessage { From = new MailAddress(_opt.FromAddress, _opt.FromName), Subject = subject, Body = body, IsBodyHtml = message.IsHtml };
            foreach (var a in actualTo) mail.To.Add(a);
            // Cc is dropped under override (everything collapses to the single test address).
            if (!overridden) foreach (var a in message.Cc) mail.CC.Add(a);

            using var client = new SmtpClient(_opt.Smtp.Host, _opt.Smtp.Port) { EnableSsl = _opt.Smtp.UseSsl };
            if (!string.IsNullOrEmpty(_opt.Smtp.User))
                client.Credentials = new NetworkCredential(_opt.Smtp.User, _opt.Smtp.Password);

            await client.SendMailAsync(mail, ct);
            log.LogInformation("EMAIL sent → {To} · subject: {Subject}", string.Join(", ", actualTo), subject);
            return new EmailSendResult(true, actualTo, $"Sent to {string.Join(", ", actualTo)}.");
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "EMAIL send failed → {To} · subject: {Subject}", string.Join(", ", actualTo), subject);
            return new EmailSendResult(false, actualTo, $"Send failed: {ex.Message}");
        }
    }
}
