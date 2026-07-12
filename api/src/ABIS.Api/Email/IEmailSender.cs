namespace Abis.Api.Email;

/// <summary>Options for outbound email (section <c>Email</c>). The critical field is
/// <see cref="OverrideRecipient"/>: while the plant is in the TEST phase (the app runs against the .230
/// sandbox, EDI doesn't transmit, etc.), it is set so that EVERY email — automated, triggered, or manual —
/// is redirected to a single test inbox instead of the real recipients. Clear it (empty) only at the
/// production cutover, deliberately. Same fail-safe spirit as the no-transmit EDI seam.</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>When set, ALL recipients (To/Cc) on every message are replaced with this single address and
    /// the original recipients are noted in the subject/body. Empty = send to the real recipients.</summary>
    public string? OverrideRecipient { get; set; }

    public string FromAddress { get; set; } = "abis@albl.com";
    public string FromName { get; set; } = "ABIS";

    /// <summary>When true, the app sends one diagnostic email through the full pipeline at startup — a
    /// deploy-time smoke test that the wiring + <see cref="OverrideRecipient"/> fire. With no
    /// <see cref="SmtpOptions.Host"/> it only logs the redirect (proves the path); once a relay is set it
    /// actually delivers on the next restart. Default off (don't email on every boot). Non-fatal.</summary>
    public bool SendTestOnStartup { get; set; }

    public SmtpOptions Smtp { get; set; } = new();
}

/// <summary>SMTP transport config. An empty <see cref="Host"/> means "don't actually send" — the sender
/// logs the message instead, so nothing breaks and the flow stays observable until a relay is configured.</summary>
public sealed class SmtpOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 25;
    public bool UseSsl { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
}

/// <summary>An email to send. Body is plain text unless <see cref="IsHtml"/>.</summary>
public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body)
{
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
    public bool IsHtml { get; init; }
}

/// <summary>The result of a send attempt.</summary>
/// <param name="Sent">True if it was handed to SMTP; false if redirected-to-log (no relay) or errored.</param>
/// <param name="ActualRecipients">Who it actually went to — the override address during testing.</param>
/// <param name="Detail">A human note for logs / the admin test endpoint.</param>
public sealed record EmailSendResult(bool Sent, IReadOnlyList<string> ActualRecipients, string Detail);

/// <summary>The one seam every email in ABIS goes through, so the test-recipient override is impossible to
/// bypass. Report generators, EDI alerts, QA/recovery notices, etc. all call this — never SmtpClient directly.</summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken ct);
}
