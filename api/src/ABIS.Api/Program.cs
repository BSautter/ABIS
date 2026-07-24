using System.Threading.RateLimiting;
using Abis.Api.Data;
using Abis.Api.Endpoints;
using Abis.Api.Middleware;
using Abis.Api.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Native Linux service: integrate with systemd (Type=notify readiness, journald
// log formatting). This is a no-op when the process is not started by systemd, so
// the Docker container and `dotnet run` console paths are unchanged. See
// docs/INSTALL_PLAN.md.
builder.Host.UseSystemd();

// Behind the nginx reverse proxy (the native install terminates TLS at nginx and
// proxies to Kestrel on loopback), honour X-Forwarded-Proto/-For so the app sees
// the real client scheme + IP — needed for correct OIDC/redirect URLs and for the
// per-IP rate-limit fallback. nginx runs on the same host, so the default loopback
// trust covers it; ForwardLimit=1 since there is exactly one proxy hop.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
});

// Configuration: bind the Database section and register the data layer.
var dbOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()
                ?? new DatabaseOptions();
builder.Services.AddSingleton(dbOptions);
builder.Services.AddSingleton<IDbConnectionFactory, DbConnectionFactory>();
builder.Services.AddScoped<IAbisRepository, AbisRepository>();

// Secondary, read-only WinSPC (SQL Server) quality database. Inert unless WinSpc:Enabled=true
// with a connection string — CI and un-wired deployments get a disabled connector.
var winSpcOptions = builder.Configuration.GetSection(Abis.Api.Data.WinSpc.WinSpcOptions.SectionName)
                        .Get<Abis.Api.Data.WinSpc.WinSpcOptions>() ?? new Abis.Api.Data.WinSpc.WinSpcOptions();
builder.Services.AddSingleton(winSpcOptions);
builder.Services.AddSingleton<Abis.Api.Data.WinSpc.IWinSpcConnectionFactory, Abis.Api.Data.WinSpc.WinSpcConnectionFactory>();
builder.Services.AddScoped<Abis.Api.Data.WinSpc.IWinSpcRepository, Abis.Api.Data.WinSpc.WinSpcRepository>();

// Scheduler execution engine. Inert unless Scheduler:Enabled=true, and even then only the registered
// (allowlisted) operations run — there is no shell/legacy path, so the modern stack can never fire the
// legacy EDI/cron work (single-owner guardrail). Add new IScheduledOperation implementations to extend it.
var schedulerOptions = builder.Configuration.GetSection(Abis.Api.Scheduling.SchedulerOptions.SectionName)
                           .Get<Abis.Api.Scheduling.SchedulerOptions>() ?? new Abis.Api.Scheduling.SchedulerOptions();
builder.Services.AddSingleton(schedulerOptions);
builder.Services.AddSingleton<Abis.Api.Scheduling.IScheduledOperation, Abis.Api.Scheduling.NoopOperation>();
builder.Services.AddSingleton<Abis.Api.Scheduling.IScheduledOperation, Abis.Api.Scheduling.HeartbeatOperation>();
builder.Services.AddSingleton<Abis.Api.Scheduling.ScheduledOperationRegistry>();
builder.Services.AddScoped<Abis.Api.Scheduling.SchedulerService>();
builder.Services.AddHostedService<Abis.Api.Scheduling.SchedulerHostedService>();
// Bind large CLOB payloads (generated EDI X12) correctly on Oracle 11g — see ClobText.
Dapper.SqlMapper.AddTypeHandler(new Abis.Api.Data.ClobTextHandler());

// Audit middleware options: enabled by default; turn off (or it self-disables on
// the first write failure) when the target schema has no compatible audit table.
var auditOptions = builder.Configuration.GetSection(Abis.Api.Middleware.AuditOptions.SectionName)
                       .Get<Abis.Api.Middleware.AuditOptions>() ?? new Abis.Api.Middleware.AuditOptions();
builder.Services.AddSingleton(auditOptions);

// /api auth: the API key (machine clients, e.g. the edge service) plus optional
// JWT bearer (interactive users via OIDC). The default policy accepts a valid
// principal from EITHER scheme. apiKeyOptions is also reused for rate limiting +
// the Swagger security definition below.
var apiKeyOptions = builder.Configuration.GetSection(ApiKeyOptions.SectionName).Get<ApiKeyOptions>()
                    ?? new ApiKeyOptions();
builder.Services.AddSingleton(apiKeyOptions);
builder.AddAbisAuth();

// On-prem Active Directory sign-in (Auth:Ldap simple-bind). No-op when Auth:Ldap isn't configured —
// /auth/login then uses the local PBKDF2 credential store as before.
builder.AddAbisLdap();

// Browser OIDC client settings (Auth:Oidc), surfaced anonymously at /auth/config
// so the SPA can run a PKCE login flow. Empty/disabled → SPA uses the API-key field.
var oidcClientOptions = builder.Configuration.GetSection(OidcClientOptions.SectionName).Get<OidcClientOptions>()
                        ?? new OidcClientOptions();
builder.Services.AddSingleton(oidcClientOptions);

// Rate limiting: a fixed window partitioned per API key (fallback to remote IP),
// applied to the /api group. Shields the legacy DB from runaway callers; tunable
// via the RateLimiting section.
var rateLimitOptions = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
                       ?? new RateLimitOptions();
builder.Services.AddSingleton(rateLimitOptions);

// #7 server/service console (docs/SERVER_CONSOLE.md). OFF by default — inert until Admin:ServerConsole
// is enabled and (for restart / host-cron) the operator installs the sudoers allowlist / SSH channel.
var serverConsoleOptions = builder.Configuration.GetSection(Abis.Api.Admin.ServerConsoleOptions.SectionName)
                               .Get<Abis.Api.Admin.ServerConsoleOptions>() ?? new Abis.Api.Admin.ServerConsoleOptions();
builder.Services.AddSingleton(serverConsoleOptions);
builder.Services.AddSingleton<Abis.Api.Admin.IProcessRunner, Abis.Api.Admin.ProcessRunner>();
builder.Services.AddSingleton<Abis.Api.Admin.ServerConsoleService>();

// Report-not-triggered alert (outbound-EDI stall). OFF by default; thresholds tune it at deploy.
var reportStallOptions = builder.Configuration.GetSection(Abis.Api.Health.ReportStallOptions.SectionName)
                             .Get<Abis.Api.Health.ReportStallOptions>() ?? new Abis.Api.Health.ReportStallOptions();
builder.Services.AddSingleton(reportStallOptions);

// Outbound email. In the test phase Email:OverrideRecipient redirects EVERY email (automated / triggered /
// manual) to one inbox so nothing reaches a real recipient; no SMTP host = log-only. Enforced in SmtpEmailSender.
builder.Services.Configure<Abis.Api.Email.EmailOptions>(builder.Configuration.GetSection(Abis.Api.Email.EmailOptions.SectionName));
builder.Services.AddSingleton<Abis.Api.Email.IEmailSender, Abis.Api.Email.SmtpEmailSender>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitOptions.PolicyName, http =>
    {
        var partitionKey = http.Request.Headers[apiKeyOptions.HeaderName].ToString();
        if (string.IsNullOrEmpty(partitionKey))
            partitionKey = http.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = rateLimitOptions.PermitLimit,
            Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
            QueueLimit = 0
        });
    });
    // Stricter throttle for POST /auth/login — brute-force protection AND avoiding AD account
    // lockouts. Per client IP (honours X-Forwarded-For behind nginx); fixed at 10 attempts/minute.
    // Active only when RateLimiting:Enabled (UseRateLimiter is in the pipeline); harmless otherwise.
    options.AddPolicy("auth-login", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.Headers.RetryAfter = rateLimitOptions.WindowSeconds.ToString();
        await Results.Problem(
            statusCode: StatusCodes.Status429TooManyRequests,
            title: "Too many requests",
            detail: $"Rate limit exceeded. Retry after {rateLimitOptions.WindowSeconds}s.")
            .ExecuteAsync(context.HttpContext);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "ABIS API", Version = "v1",
        Description = "Read-first REST seam over the legacy ABIS database (modernization Phase 2)." });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = apiKeyOptions.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = $"API key sent in the {apiKeyOptions.HeaderName} header."
    });
    // /api accepts EITHER the API key OR a JWT bearer (when JWT is configured).
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT bearer token from your OIDC provider (when Auth:Jwt is configured)."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } }, Array.Empty<string>() },
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

builder.Services.AddProblemDetails(options =>
    options.CustomizeProblemDetails = ctx =>
    {
        if (Abis.Api.Middleware.RequestIdMiddleware.Current(ctx.HttpContext) is { } requestId)
            ctx.ProblemDetails.Extensions["requestId"] = requestId;
    });

// CORS for a future SPA: configure allowed origins via Cors:Origins. With none
// configured, Development allows any origin for convenience; other environments
// stay same-origin only.
var corsOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
builder.Services.AddCors(options => options.AddPolicy("Default", policy =>
{
    if (corsOrigins.Length > 0)
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod();
    else if (builder.Environment.IsDevelopment())
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
}));

var app = builder.Build();

// Dev/CI only: build and seed the local SQLite fixture so the API has data.
if (dbOptions.Seed && dbOptions.Dialect == SqlDialect.Sqlite)
{
    SqliteFixture.EnsureCreatedAndSeeded(dbOptions.ConnectionString);
    app.Logger.LogInformation("Seeded SQLite fixture at {ConnectionString}", dbOptions.ConnectionString);
}

// Production (Oracle): idempotently ensure the ABIS-owned tables exist. They are new
// (not part of the legacy DBO schema), so nothing else creates them — a deploy
// self-provisions them here, with no manual DDL step. This ONLY creates abis_* tables;
// it never touches the legacy schema and never fires a scheduled job. Non-fatal: a
// failure is logged and the dependent admin endpoints 500 until it succeeds, while the
// rest of the app serves normally.
if (dbOptions.Dialect == SqlDialect.Oracle)
{
    try
    {
        await AbisSchema.EnsureOwnedTablesAsync(
            app.Services.GetRequiredService<IDbConnectionFactory>(), app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "ABIS-owned schema ensure failed; admin scheduler endpoints will 500 until it succeeds.");
    }

    // Self-heal any id sequences left behind their table max (a Data Pump refresh leaves them drifted,
    // which breaks every id-minting write with ORA-00001). Doing it on startup means a redeploy fixes
    // the drift with no manual step. Idempotent — a no-op when the sequences are already ahead.
    if (dbOptions.ResyncSequencesOnStartup)
    {
        try
        {
            await AbisSchema.ResyncSequencesAsync(
                app.Services.GetRequiredService<IDbConnectionFactory>(), app.Logger);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Startup sequence re-sync failed; id-minting writes may hit ORA-00001 until it succeeds.");
        }
    }
}

// Opt-in deploy-time email smoke test (Email:SendTestOnStartup). Sends ONE message through the real
// IEmailSender pipeline so a deploy proves the wiring + the test-recipient override fire. With no
// Smtp.Host it only logs the redirect; once a relay is set it actually delivers on the next restart.
// Non-fatal: a failure is logged and never blocks startup.
{
    var emailOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Abis.Api.Email.EmailOptions>>().Value;
    if (emailOpts.SendTestOnStartup)
    {
        try
        {
            var sender = app.Services.GetRequiredService<Abis.Api.Email.IEmailSender>();
            string to = string.IsNullOrWhiteSpace(emailOpts.OverrideRecipient) ? "qa@albl.com" : emailOpts.OverrideRecipient!;
            var result = await sender.SendAsync(new Abis.Api.Email.EmailMessage(
                new List<string> { to },
                "ABIS startup email test",
                $"This is the ABIS startup email smoke test, sent at {DateTime.Now:yyyy-MM-dd HH:mm} server time. " +
                "If you received this, the email pipeline and the test-recipient override are working."),
                CancellationToken.None);
            app.Logger.LogInformation(
                "Startup email test: sent={Sent}, recipients=[{Recipients}]. {Detail}",
                result.Sent, string.Join(", ", result.ActualRecipients), result.Detail);
        }
        catch (Exception ex)
        {
            app.Logger.LogError(ex, "Startup email test failed (non-fatal).");
        }
    }
}

// Very first: apply X-Forwarded-* from the nginx proxy so every downstream
// component (rate limiter, auth, URL generation) sees the real scheme + client IP.
app.UseForwardedHeaders();

// Then: assign/propagate a correlation id available to everything downstream.
app.UseMiddleware<RequestIdMiddleware>();

// Outermost audit: observe the final status (incl. exception-handler output) and audit it.
app.UseMiddleware<AuditMiddleware>();

// Baseline security headers on every response. Set via OnStarting so they are
// applied right before the response is sent — which also covers responses that
// UseExceptionHandler re-executes (it clears directly-set headers; OnStarting
// callbacks survive), keeping the headers on 500s too.
app.Use((context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        return Task.CompletedTask;
    });
    return next(context);
});

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseCors("Default");

// Serve the static UI (wwwroot/ui/) — anonymous; its data calls still carry the API key.
// The app bundles (.js/.html/.css) keep stable filenames but change on every deploy, so serve them
// with `no-cache` (revalidate before use; ETag/Last-Modified still yield cheap 304s when unchanged).
// Without this the browser serves a stale cached bundle after a redeploy until a manual hard refresh.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        if (name.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    },
});

app.UseAuthentication();
app.UseAuthorization();

// After routing + auth so the selected endpoint's RequireRateLimiting policy applies.
if (rateLimitOptions.Enabled)
    app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Conditional-GET caching for /api reads (wraps endpoint execution).
app.UseMiddleware<ETagMiddleware>();

app.MapAbisApi();

app.Run();

// Exposed so the integration test project can host the app via WebApplicationFactory.
public partial class Program { }
