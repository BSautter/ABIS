using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Abis.Api.Security;

/// <summary>Optional JWT-bearer auth, bound from the <c>Auth:Jwt</c> section. Use
/// <see cref="Authority"/> for a standard OIDC provider (Entra ID, Keycloak,
/// Auth0, …), or <see cref="SigningKey"/> for a symmetric-key setup (also what
/// the tests use). When neither is set, JWT is disabled and only the API key
/// gates <c>/api</c> — fully backward compatible.</summary>
public sealed class JwtAuthOptions
{
    public const string SectionName = "Auth:Jwt";

    public string? Authority { get; set; }              // OIDC issuer (uses discovery)
    public string? Audience { get; set; }
    public string? Issuer { get; set; }                 // for the symmetric-key path
    public string? SigningKey { get; set; }             // symmetric key (alternative to Authority)
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>When true, a user with no password set in the ABIS credential store cannot sign in
    /// (POST /auth/login → 401). Default false = transitional: users without a credential still sign
    /// in passwordless (identity on the LAN) so passwords can be rolled out user-by-user; flip to
    /// true once every active user has been enrolled. Bound from <c>Auth:Jwt:RequirePassword</c>.</summary>
    public bool RequirePassword { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority) || !string.IsNullOrWhiteSpace(SigningKey);
}

/// <summary>Browser-side OIDC client settings, bound from <c>Auth:Oidc</c>, served
/// anonymously at <c>GET /auth/config</c> so the SPA knows whether to run an
/// Authorization-Code + PKCE login flow (and against which provider). Distinct from
/// <see cref="JwtAuthOptions"/>, which is the server's <i>token validation</i> config:
/// here <see cref="ClientId"/> is the public SPA client registered in the provider.
/// When unset, the SPA falls back to the API-key field.</summary>
public sealed class OidcClientOptions
{
    public const string SectionName = "Auth:Oidc";

    public string? Authority { get; set; }              // OIDC issuer (the SPA reads its discovery doc)
    public string? ClientId { get; set; }               // public SPA client id registered in the provider
    public string? Scope { get; set; }                  // e.g. "openid profile api://abis/.default"

    public bool Enabled => !string.IsNullOrWhiteSpace(Authority) && !string.IsNullOrWhiteSpace(ClientId);
}

/// <summary>Wires the <c>/api</c> auth: the API key (for machine clients such as
/// the edge service) plus optional JWT bearer (for interactive users via your
/// OIDC provider), with a default policy that accepts <b>either</b> scheme.</summary>
public static class AuthSetup
{
    public static void AddAbisAuth(this WebApplicationBuilder builder)
    {
        // ApiKeyOptions is registered by the caller (it's also reused for rate
        // limiting + the Swagger security definition).
        var jwt = builder.Configuration.GetSection(JwtAuthOptions.SectionName).Get<JwtAuthOptions>()
                  ?? new JwtAuthOptions();

        // If nothing is configured, fall back to an EPHEMERAL symmetric key so per-user sign-in
        // (POST /auth/login) works out of the box on an internal LAN deployment — no env config
        // required. The key lives only in this process, so tokens are invalidated on restart; set a
        // persistent Auth:Jwt:SigningKey (32+ chars) to keep sessions across restarts. A configured
        // OIDC Authority or explicit SigningKey always takes precedence over this fallback.
        if (!jwt.Enabled)
        {
            jwt.SigningKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
            if (string.IsNullOrWhiteSpace(jwt.Issuer)) jwt.Issuer = "abis";
            if (string.IsNullOrWhiteSpace(jwt.Audience)) jwt.Audience = "abis-ui";
        }

        // Registered so POST /auth/login can issue tokens signed with the same key the bearer
        // validation trusts (symmetric-key path).
        builder.Services.AddSingleton(jwt);

        var auth = builder.Services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, null);

        var schemes = new List<string> { ApiKeyAuthenticationHandler.SchemeName };
        if (jwt.Enabled)
        {
            auth.AddJwtBearer(o =>
            {
                o.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
                if (!string.IsNullOrWhiteSpace(jwt.Authority))
                {
                    // OIDC: signing keys + issuer come from the provider's discovery doc.
                    o.Authority = jwt.Authority;
                    if (!string.IsNullOrWhiteSpace(jwt.Audience)) o.Audience = jwt.Audience;
                    else o.TokenValidationParameters.ValidateAudience = false;
                }
                else
                {
                    // Symmetric signing key (no OIDC provider) — also the test path.
                    o.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = !string.IsNullOrWhiteSpace(jwt.Issuer),
                        ValidIssuer = jwt.Issuer,
                        ValidateAudience = !string.IsNullOrWhiteSpace(jwt.Audience),
                        ValidAudience = jwt.Audience,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!)),
                    };
                }
            });
            schemes.Add(JwtBearerDefaults.AuthenticationScheme);
        }

        // Default policy accepts a valid principal from EITHER scheme.
        builder.Services.AddAuthorization(options =>
            options.DefaultPolicy = new AuthorizationPolicyBuilder(schemes.ToArray())
                .RequireAuthenticatedUser()
                .Build());
    }
}
