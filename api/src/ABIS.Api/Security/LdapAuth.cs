using Novell.Directory.Ldap;

namespace Abis.Api.Security;

/// <summary>On-prem Active Directory sign-in via an LDAP simple-bind, bound from <c>Auth:Ldap</c>.
/// When enabled, <c>POST /auth/login</c> verifies the password by binding to a domain controller with
/// the user's AD credential instead of the local PBKDF2 store — the ABIS <c>security_user</c> still
/// supplies identity + RBAC (so the AD sAMAccountName must equal the ABIS <c>login_id</c>). Off by
/// default; dev/local keeps using the credential store.</summary>
public sealed class LdapOptions
{
    public const string SectionName = "Auth:Ldap";

    public bool Enabled { get; set; }
    public string? Host { get; set; }                    // domain controller, e.g. dc01.abc.local
    public int Port { get; set; }                        // 0 = default (636 LDAPS, else 389)
    public bool UseSsl { get; set; } = true;             // LDAPS — recommended; the password is sent to the DC
    /// <summary>Accept any DC server certificate (for an internal self-signed/private-CA DC on a
    /// trusted LAN). Leave false and trust the DC's CA on the host for a real chain check.</summary>
    public bool AcceptAnyCertificate { get; set; }
    /// <summary>How the entered username becomes a bind identity, with <c>{0}</c> = the bare username.
    /// AD accepts a UPN (<c>{0}@abc.local</c>) or NetBIOS form (<c>ABC\{0}</c>). Required when enabled.</summary>
    public string? UserBindFormat { get; set; }
    public int TimeoutSeconds { get; set; } = 8;

    public int EffectivePort => Port > 0 ? Port : (UseSsl ? 636 : 389);

    /// <summary>Enabled AND the essentials are present — a half-filled section must not silently take
    /// over sign-in and then reject everyone.</summary>
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(UserBindFormat);
}

/// <summary>Validates a username/password against Active Directory. Abstracted so
/// <c>/auth/login</c> is unit-testable without a live domain controller.</summary>
public interface ILdapAuthenticator
{
    /// <summary>Whether AD-bind sign-in is configured and should be used by <c>/auth/login</c>.</summary>
    bool Enabled { get; }

    /// <summary>True iff an LDAP simple-bind with these credentials succeeds. The caller must not pass
    /// an empty password (an LDAP unauthenticated-bind succeeds without checking) — this also guards it.</summary>
    Task<bool> ValidateAsync(string username, string password, CancellationToken ct);
}

/// <summary>Novell.Directory.Ldap (pure-managed) implementation of <see cref="ILdapAuthenticator"/>.</summary>
public sealed class LdapAuthenticator(LdapOptions opt, ILogger<LdapAuthenticator> log) : ILdapAuthenticator
{
    public bool Enabled => opt.IsUsable;

    public Task<bool> ValidateAsync(string username, string password, CancellationToken ct)
    {
        // Never bind with an empty/whitespace password: an LDAP simple-bind with a DN and empty
        // password is an "unauthenticated bind" that SUCCEEDS without authenticating. Reject up front.
        if (!Enabled || string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return Task.FromResult(false);

        var bindDn = string.Format(opt.UserBindFormat!, username);

        // Novell's client is synchronous/blocking; run it off the request thread and honour cancellation.
        return Task.Run(() =>
        {
            try
            {
                var options = new LdapConnectionOptions();
                if (opt.UseSsl) options.UseSsl();
                if (opt.AcceptAnyCertificate) options.ConfigureRemoteCertificateValidationCallback((_, _, _, _) => true);

                using var conn = new LdapConnection(options) { ConnectionTimeout = opt.TimeoutSeconds * 1000 };
                conn.Connect(opt.Host, opt.EffectivePort);
                conn.Bind(bindDn, password);          // throws LdapException(49 InvalidCredentials) on a bad password
                var ok = conn.Bound;
                conn.Disconnect();
                return ok;
            }
            catch (LdapException ex)
            {
                // A rejected credential is the normal failure path — keep it quiet (no username/password logged).
                log.LogDebug("LDAP bind rejected for {BindDn}: result {Result}", bindDn, ex.ResultCode);
                return false;
            }
            catch (Exception ex)
            {
                // Connect/TLS/config problem — surface it (still fail closed).
                log.LogWarning(ex, "LDAP connect/bind error to {Host}:{Port}", opt.Host, opt.EffectivePort);
                return false;
            }
        }, ct);
    }
}

/// <summary>DI wiring for AD sign-in. Always registered (the authenticator reports
/// <see cref="ILdapAuthenticator.Enabled"/> = false when <c>Auth:Ldap</c> isn't configured).</summary>
public static class LdapSetup
{
    public static void AddAbisLdap(this WebApplicationBuilder builder)
    {
        var opt = builder.Configuration.GetSection(LdapOptions.SectionName).Get<LdapOptions>() ?? new LdapOptions();
        builder.Services.AddSingleton(opt);
        builder.Services.AddSingleton<ILdapAuthenticator, LdapAuthenticator>();
    }
}
