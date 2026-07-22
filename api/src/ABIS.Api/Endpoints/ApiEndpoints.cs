using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Abis.Api.Admin;
using Abis.Api.Data;
using Abis.Api.Documents;
using Abis.Api.Middleware;
using Abis.Api.Models;
using Abis.Api.Security;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

namespace Abis.Api.Endpoints;

/// <summary>Maps the ABIS REST endpoints. Routes are grouped under <c>/api</c>;
/// collections are paginated (<c>page</c>, <c>pageSize</c>) and sortable
/// (<c>sort</c>, <c>dir</c>). Every endpoint declares its response types via
/// <c>.Produces&lt;T&gt;()</c> so the generated OpenAPI contract is fully typed
/// and client codegen (NSwag / openapi-generator) produces real models.</summary>
public static class ApiEndpoints
{
    /// <summary>Maps a modern endpoint tag → the legacy security feature name that
    /// <c>f_security_door</c> checks (see <c>legacy/src/security/f_security_door.srf</c>).
    /// A <b>mutating</b> request (POST/PUT/PATCH/DELETE) under a mapped tag requires the
    /// caller to hold Write (level 1) on that feature — mirroring the legacy screens, which
    /// gate writes with <c>IF f_security_door("…") = 1</c>. Only tags that map 1:1 to a
    /// single legacy feature are listed; ambiguous tags (Shipments, Dies, Sketches, Sales,
    /// Accounting, Downtime, Jobs, Stacker, ScanLog, Carriers, ProdFolder) are intentionally
    /// left ungated pending live <c>security_application</c> verification — see NEXT_STEPS.
    /// Security-admin writes keep their own inline "User Control"/"User Group Control" gates.</summary>
    private static readonly IReadOnlyDictionary<string, string> FeatureByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Orders"] = "Order Entry",
        ["OrderItems"] = "Order Entry",
        ["Customers"] = "Order Entry",          // customer master is edited from the order-entry module
        ["Parts"] = "Part Number",
        ["Coils"] = "Inventory(Coil)",
        ["Skids"] = "Inventory(Skid)",
        ["Warehouse"] = "Warehouse",
        ["Receiving"] = "Shipment(Receiving)",
        ["CoilEval"] = "Quality Control",
        ["Quality"] = "Quality Control",
        ["Shifts"] = "Shift Control",
        ["Maintenance"] = "Maintenance_logs",
    };

    public static IEndpointRouteBuilder MapAbisApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/", (IHostEnvironment env) => Results.Ok(new
        {
            name = "ABIS API",
            version = typeof(ApiEndpoints).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            environment = env.EnvironmentName,
            docs = "/swagger",
            health = "/health",
            ready = "/health/ready",
            ui = "/ui/index.html"
        })).WithTags("Meta").WithName("Root").WithSummary("Service info, version, and links.");

        // Liveness: the process is up. No dependencies touched (cheap; safe to poll).
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
           .WithTags("Meta").WithName("Health").WithSummary("Liveness probe — the process is up.");

        // Readiness: the database is reachable. Returns 503 when it is not, so an
        // orchestrator can hold traffic until the data path is actually serving.
        app.MapGet("/health/ready", async (IAbisRepository repo, CancellationToken ct) =>
            {
                try
                {
                    return await repo.PingAsync(ct)
                        ? Results.Ok(new { status = "ready" })
                        : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
                }
                catch (Exception ex)
                {
                    return Results.Json(new { status = "unavailable", error = ex.Message },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            })
           .WithTags("Meta").WithName("Ready")
           .WithSummary("Readiness probe — verifies database connectivity (503 when unreachable).")
           .Produces(StatusCodes.Status200OK)
           .Produces(StatusCodes.Status503ServiceUnavailable);

        // Report-not-triggered signal: is the outbound-EDI pipeline stalled? The notification bell polls
        // this. OFF by default (Notifications:EdiStall) — inert until the plant's cadence/thresholds are
        // set; only alarms inside the business window so a quiet evening/weekend never cries wolf.
        app.MapGet("/health/report-stall", async (IAbisRepository repo, Abis.Api.Health.ReportStallOptions opts, CancellationToken ct) =>
            {
                DateTime? last = null;
                if (opts.Enabled) { try { last = await repo.GetLatestEdiActivityAsync(ct); } catch { /* DB down = the DB alert covers it */ } }
                return Results.Ok(Abis.Api.Health.ReportStall.Evaluate(last, DateTime.Now, opts));
            })
           .WithTags("Meta").WithName("ReportStall")
           .WithSummary("Report-not-triggered check — whether outbound EDI looks stalled during business hours (config-gated).")
           .Produces(StatusCodes.Status200OK);

        // Anonymous: tells the browser SPA whether to run an OIDC login flow and,
        // if so, which provider/client/scope to use (Authorization Code + PKCE).
        // When OIDC isn't configured, returns { oidc: false } and the SPA uses the
        // API-key field. Safe to expose: ClientId is a public value, no secrets.
        app.MapGet("/auth/config", (OidcClientOptions oidc) => Results.Ok(oidc.Enabled
                ? new { oidc = true, authority = oidc.Authority, clientId = oidc.ClientId,
                        scope = oidc.Scope ?? "openid profile" }
                : (object)new { oidc = false }))
           .WithTags("Meta").WithName("AuthConfig")
           .WithSummary("Browser OIDC client config (or { oidc:false } to use the API-key field).");

        // Anonymous per-user sign-in: validates the login against security_user and issues a
        // bearer token the SPA sends on subsequent calls (so ResolveLogin resolves the real user +
        // their grants). Passwordless on the LAN for now — identity, not a secret — until a password
        // layer or OIDC lands. Requires Auth:Jwt:SigningKey configured (the token is signed with the
        // same symmetric key the bearer validation trusts).
        app.MapPost("/auth/login", async (LoginRequest body, JwtAuthOptions jwt, ILdapAuthenticator ldap, IAbisRepository repo, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(jwt.SigningKey))
                    return Results.Problem(statusCode: StatusCodes.Status501NotImplemented, title: "Sign-in not configured",
                        detail: "Set Auth:Jwt:SigningKey (and Issuer/Audience) on the server to enable user login.");
                var raw = body.Login?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["login"] = ["A username is required."] });
                // With AD sign-in, accept DOMAIN\user or user@domain and reduce to the bare sAMAccountName
                // — which must equal the ABIS login_id (the security_user row supplies identity + RBAC).
                var login = ldap.Enabled ? StripAdDomain(raw) : raw;
                var user = await repo.GetSecurityUserByLoginAsync(login, ct);
                if (user is null)
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unknown user",
                        detail: $"'{login}' is not in the ABIS user directory.");
                if (user.UserStatus == 0)
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Inactive user",
                        detail: "This ABIS account is not active.");

                var mustChangePassword = false;
                bool passwordSet;
                if (ldap.Enabled)
                {
                    // AD-backed. Reject an empty password before any bind (an LDAP simple-bind with an
                    // empty password is an "unauthenticated bind" that succeeds) — closes the blank-password
                    // sign-in. Bind to a DC; if AD rejects OR every DC is unreachable, fall back to a
                    // BREAK-GLASS local password — but ONLY for an account with an admin-set credential
                    // (never passwordless), so a local admin can still get in when AD/the DC is down.
                    if (string.IsNullOrEmpty(body.Password))
                        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials",
                            detail: "The username or password is incorrect.");
                    if (await ldap.ValidateAsync(login, body.Password, ct))
                    {
                        passwordSet = true;                          // authenticated against AD
                    }
                    else
                    {
                        var cred = await repo.GetUserCredentialAsync(user.LoginId ?? login, ct);
                        if (cred is null || !PasswordHashing.Verify(body.Password, cred.PasswordHash))
                            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials",
                                detail: "The username or password is incorrect.");
                        mustChangePassword = cred.MustChange != 0;   // break-glass local sign-in (AD unavailable)
                        passwordSet = true;
                    }
                }
                else
                {
                    // Local password check against the ABIS credential store. A user WITH a credential
                    // must supply a matching password; a user WITHOUT one signs in passwordless during
                    // the rollout (unless Auth:Jwt:RequirePassword forces enrollment).
                    var cred = await repo.GetUserCredentialAsync(user.LoginId ?? login, ct);
                    if (cred is not null)
                    {
                        if (!PasswordHashing.Verify(body.Password ?? string.Empty, cred.PasswordHash))
                            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid credentials",
                                detail: "The username or password is incorrect.");
                        mustChangePassword = cred.MustChange != 0;
                    }
                    else if (jwt.RequirePassword)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Password not set",
                            detail: "No password is set for this account. Ask an administrator to set your initial password.");
                    }
                    passwordSet = cred is not null;
                }

                var name = $"{user.UserFirstName} {user.UserLastName}".Trim();
                var token = IssueUserToken(jwt, user.LoginId ?? login, string.IsNullOrWhiteSpace(name) ? login : name, user.UserId);
                return Results.Ok(new { token, login = user.LoginId ?? login, name, expiresInSeconds = 8 * 3600,
                    mustChangePassword, passwordSet });

                // DOMAIN\user or user@domain → bare sAMAccountName (for AD input).
                static string StripAdDomain(string s)
                {
                    var slash = s.LastIndexOf('\\');
                    if (slash >= 0) s = s[(slash + 1)..];
                    var at = s.IndexOf('@');
                    if (at >= 0) s = s[..at];
                    return s.Trim();
                }
            })
           .WithTags("Meta").WithName("Login").AllowAnonymous().RequireRateLimiting("auth-login")
           .WithSummary("Sign in with an ABIS user login + password (verified against Active Directory when Auth:Ldap is enabled, else the local credential store); returns a bearer token that drives RBAC.")
           .Produces(StatusCodes.Status200OK).ProducesValidationProblem()
           .Produces(StatusCodes.Status401Unauthorized).Produces(StatusCodes.Status403Forbidden)
           .Produces(StatusCodes.Status501NotImplemented);

        // The signed-in user rotates their OWN password (also used to satisfy a must-change on first
        // sign-in). Requires a user bearer token — a machine API-key caller has no user and is
        // rejected. Verifies the current password when one is set, then stores the new PBKDF2 hash.
        app.MapPost("/auth/change-password", async (ChangePasswordRequest body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                var login = ResolveLogin(ctx);
                if (string.IsNullOrWhiteSpace(login))
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "No user context",
                        detail: "Sign in as a user to change a password.");
                var newPw = body.NewPassword ?? string.Empty;
                if (newPw.Length is < 8 or > 100)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["Password must be 8–100 characters."] });
                var cred = await repo.GetUserCredentialAsync(login, ct);
                if (cred is not null && !PasswordHashing.Verify(body.CurrentPassword ?? string.Empty, cred.PasswordHash))
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Wrong current password",
                        detail: "The current password is incorrect.");
                await repo.SetUserCredentialAsync(login, PasswordHashing.Hash(newPw), mustChange: false, updatedBy: login, ct);
                return Results.Ok(new { changed = true });
            })
           .WithTags("Meta").WithName("ChangePassword").RequireAuthorization()
           .WithSummary("Change the signed-in user's own ABIS password (sets it if none exists yet).")
           .Produces(StatusCodes.Status200OK).ProducesValidationProblem()
           .Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status401Unauthorized);

        // All /api endpoints require an authenticated caller (see ApiKey auth).
        // /health and Swagger remain anonymous. The 401 is declared once for the
        // whole group so it appears on every operation in the contract.
        var api = app.MapGroup("/api").RequireAuthorization().RequireRateLimiting(RateLimitOptions.PolicyName);
        api.WithMetadata(new ProducesResponseTypeAttribute(StatusCodes.Status401Unauthorized));

        // App-wide authorization gate (legacy f_security_door parity). For every mutating
        // request under a mapped domain tag, an OIDC end-user must hold Write (level 1) on
        // the mapped feature; a null login (API-key service account) bypasses, matching the
        // rollout policy. Reads (GET) are never gated here. This runs after authentication,
        // so ctx.User / X-User-Login is resolved. See FeatureByTag.
        api.AddEndpointFilter(async (fctx, next) =>
        {
            var http = fctx.HttpContext;
            var method = http.Request.Method;
            if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method))
            {
                var tag = http.GetEndpoint()?.Metadata.GetMetadata<ITagsMetadata>()?.Tags is { Count: > 0 } tags ? tags[0] : null;
                if (tag is not null && FeatureByTag.TryGetValue(tag, out var feature))
                {
                    var repo = http.RequestServices.GetRequiredService<IAbisRepository>();
                    if (await RequireFeatureAsync(http, repo, feature, 1, http.RequestAborted) is { } deny)
                        return deny;
                }
            }
            return await next(fctx);
        });

        // ---- Jobs -------------------------------------------------------
        api.MapGet("/jobs", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? status = null, bool? completed = null, string? search = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("jobs", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetJobsAsync(page, pageSize, status, completed, search, orderBy, ct));
            })
           .WithName("ListJobs").WithTags("Jobs")
           .WithSummary("List production jobs (paged; filter by status, completed=Done vs active, or search job/order #).")
           .Produces<PagedResult<AbJob>>().ProducesValidationProblem();

        api.MapGet("/jobs/{abJobNum:long}", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetJobAsync(abJobNum, ct) is { } job
                    ? Results.Ok(job)
                    : Results.NotFound())
           .WithName("GetJob").WithTags("Jobs")
           .WithSummary("Get one production job by id.")
           .Produces<AbJob>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/jobs/{abJobNum:long}/coils", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobCoilsAsync(abJobNum, ct)))
           .WithName("GetJobCoils").WithTags("Jobs")
           .WithSummary("List the coils a job has processed.")
           .Produces<IEnumerable<ProcessCoil>>();

        api.MapGet("/jobs/{abJobNum:long}/skids", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobSheetSkidsAsync(abJobNum, ct)))
           .WithName("GetJobSkids").WithTags("Jobs")
           .WithSummary("List the finished sheet skids produced by a job.")
           .Produces<IEnumerable<SheetSkid>>();

        api.MapGet("/jobs/{abJobNum:long}/scrap", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobScrapAsync(abJobNum, ct)))
           .WithName("GetJobScrap").WithTags("Jobs")
           .WithSummary("List the scrap skids generated by a job.")
           .Produces<IEnumerable<ScrapSkid>>();

        api.MapGet("/jobs/{abJobNum:long}/partial-skids", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobPartialSkidsAsync(abJobNum, ct)))
           .WithName("GetJobPartialSkids").WithTags("Jobs")
           .WithSummary("List a job's in-process partial skids.")
           .Produces<IEnumerable<PartialSkid>>();

        api.MapPost("/jobs", async (JobWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // The order line must exist — ab_job FKs to order_item. Return a clean 400 rather
                // than letting the insert fail as ORA-02291 → 500 (live-only: SQLite doesn't
                // enforce the FK). Validate() has already guaranteed both refs are present.
                if (await repo.GetOrderItemAsync(body.OrderAbcNum!.Value, body.OrderItemNum!.Value, ct) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["orderItemNum"] = ["orderAbcNum/orderItemNum must reference an existing order line."],
                    });
                var created = await repo.CreateJobAsync(body, ct);
                return Results.Created($"/api/jobs/{created.AbJobNum}", created);
            })
           .WithName("CreateJob").WithTags("Jobs")
           .WithSummary("Create a production job (requires the order refs it belongs to).")
           .Produces<AbJob>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPatch("/jobs/{abJobNum:long}", async (long abJobNum, JobPatch body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                // Legacy w_stacker_job_details:498 — a finished job (job_status 0 = Done) is
                // terminal ("this job is done, nothing can be modified now"); it flows into invoicing.
                if (await repo.GetJobAsync(abJobNum, ct) is { JobStatus: 0 })
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Job is done",
                        detail: $"Job {abJobNum} is done and cannot be modified.");
                return await WithIfMatch(ctx, json, () => repo.GetJobAsync(abJobNum, ct), () => repo.PatchJobAsync(abJobNum, body, ct));
            })
           .WithName("PatchJob").WithTags("Jobs")
           .WithSummary("Update a job's status, notes, men, or finish time (409 if the job is done). Supports If-Match.")
           .Produces<AbJob>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status412PreconditionFailed);

        // ---- Coils (inventory) -----------------------------------------
        api.MapGet("/coils", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? status = null,
                string? alloy = null, string? location = null, long? customerId = null,
                string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("coils", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetCoilsAsync(page, pageSize, status, alloy, location, customerId, orderBy, ct));
            })
           .WithName("ListCoils").WithTags("Coils")
           .WithSummary("List raw input coils (paged, filterable, sortable).")
           .Produces<PagedResult<Coil>>().ProducesValidationProblem();

        // Inventory rollup: weight on hand grouped by alloy or location.
        api.MapGet("/coils/summary", async (IAbisRepository repo, CancellationToken ct, string groupBy = "alloy") =>
            {
                var g = groupBy.ToLowerInvariant();
                if (g is not ("alloy" or "location"))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                        { ["groupBy"] = ["groupBy must be 'alloy' or 'location'."] });
                return Results.Ok(await repo.GetCoilInventorySummaryAsync(g, ct));
            })
           .WithName("CoilInventorySummary").WithTags("Coils")
           .WithSummary("Coil inventory weight rollup grouped by alloy or location.")
           .Produces<IEnumerable<CoilInventoryGroup>>().ProducesValidationProblem();

        api.MapGet("/coils/{coilAbcNum:long}", async (long coilAbcNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCoilAsync(coilAbcNum, ct) is { } coil
                    ? Results.Ok(coil)
                    : Results.NotFound())
           .WithName("GetCoil").WithTags("Coils")
           .WithSummary("Get one coil by id.")
           .Produces<Coil>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/coils/{coilAbcNum:long}/processing", async (long coilAbcNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCoilProcessingAsync(coilAbcNum, ct)))
           .WithName("GetCoilProcessing").WithTags("Coils")
           .WithSummary("List a coil's processing history (the jobs that consumed it).")
           .Produces<IEnumerable<CoilProcessing>>();

        api.MapPost("/coils", async (CoilWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // Reject a duplicate coil: same original number for the same customer + MID
                // ("Duplicated coil original number.", w_receiving_dock:494).
                if (!string.IsNullOrWhiteSpace(body.CoilOrgNum) &&
                    await repo.CoilExistsByKeyAsync(body.CoilOrgNum!, body.CustomerId, body.CoilMidNum, ct))
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Duplicate coil",
                        detail: "A coil with this original number already exists for this customer and MID (duplicated coil original number).");
                // Referenced customers must exist — coil FKs both customer_id and coil_from_cust_id
                // to customer. Return a clean 400 rather than a bare ORA-02291 → 500 (live-only:
                // SQLite doesn't enforce the FK).
                if (body.CustomerId is > 0 && await repo.GetCustomerAsync(body.CustomerId.Value, ct) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["customerId"] = ["customerId must reference an existing customer."],
                    });
                if (body.CoilFromCustId is > 0 && await repo.GetCustomerAsync(body.CoilFromCustId.Value, ct) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["coilFromCustId"] = ["coilFromCustId must reference an existing customer."],
                    });
                var created = await repo.CreateCoilAsync(body, ct);
                return Results.Created($"/api/coils/{created.CoilAbcNum}", created);
            })
           .WithName("CreateCoil").WithTags("Coils")
           .WithSummary("Create a coil on receipt (rejects a duplicate org+customer+MID).")
           .Produces<Coil>(StatusCodes.Status201Created).Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        api.MapPatch("/coils/{coilAbcNum:long}", async (long coilAbcNum, CoilPatch body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                // Legacy w_inv_coil (391-404): a coil that is Done(0), Shipped(10), or
                // Transferred(13) is terminal — its detail can't be modified.
                if (await repo.GetCoilAsync(coilAbcNum, ct) is { CoilStatus: 0 or 10 or 13 } t)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Coil is terminal",
                        detail: $"Coil {coilAbcNum} is {t.CoilStatus switch { 0 => "done", 10 => "shipped", _ => "transferred" }} and cannot be modified.");
                return await WithIfMatch(ctx, json, () => repo.GetCoilAsync(coilAbcNum, ct), () => repo.PatchCoilAsync(coilAbcNum, body, ct));
            })
           .WithName("PatchCoil").WithTags("Coils")
           .WithSummary("Update a coil's status, location, or notes (409 if the coil is done/shipped/transferred). Supports If-Match.")
           .Produces<Coil>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status412PreconditionFailed);

        // ---- Orders -----------------------------------------------------
        api.MapGet("/orders", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, string? po = null,
                string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("orders", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetOrdersAsync(page, pageSize, customerId, po, orderBy, ct));
            })
           .WithName("ListOrders").WithTags("Orders")
           .WithSummary("List customer orders (paged, filterable, sortable).")
           .Produces<PagedResult<CustomerOrder>>().ProducesValidationProblem();

        api.MapGet("/orders/{orderAbcNum:long}", async (long orderAbcNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetOrderAsync(orderAbcNum, ct) is { } order
                    ? Results.Ok(order)
                    : Results.NotFound())
           .WithName("GetOrder").WithTags("Orders")
           .WithSummary("Get one order header by id.")
           .Produces<CustomerOrder>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/orders/{orderAbcNum:long}/items", async (long orderAbcNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOrderItemsByOrderAsync(orderAbcNum, ct)))
           .WithName("GetOrderItemsForOrder").WithTags("Orders")
           .WithSummary("List the line items for an order.")
           .Produces<IEnumerable<OrderItem>>();

        // Order-entry screen read model: header + customer + line items.
        api.MapGet("/orders/{orderAbcNum:long}/full", async (long orderAbcNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetOrderDetailAsync(orderAbcNum, ct) is { } detail
                    ? Results.Ok(detail)
                    : Results.NotFound())
           .WithName("GetOrderDetail").WithTags("Orders")
           .WithSummary("Get an order with its customer and line items (order-entry read model).")
           .Produces<OrderDetail>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/orders", async (CustomerOrderWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateOrderAsync(body, ct);
                return Results.Created($"/api/orders/{created.OrderAbcNum}", created);
            })
           .WithName("CreateOrder").WithTags("Orders")
           .WithSummary("Create an order header.")
           .Produces<CustomerOrder>(StatusCodes.Status201Created).ProducesValidationProblem();

        // Order-entry "save": create the header and its line items in one transaction.
        api.MapPost("/orders/with-items", async (OrderCreateWithItems body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateOrderWithItemsAsync(body, ct);
                return Results.Created($"/api/orders/{created.Order.OrderAbcNum}", created);
            })
           .WithName("CreateOrderWithItems").WithTags("Orders")
           .WithSummary("Create an order header and its line items in one transaction.")
           .Produces<OrderDetail>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/orders/{orderAbcNum:long}", async (long orderAbcNum, CustomerOrderWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetOrderAsync(orderAbcNum, ct), () => repo.UpdateOrderAsync(orderAbcNum, body, ct));
            })
           .WithName("UpdateOrder").WithTags("Orders")
           .WithSummary("Replace an order header. Supports If-Match.")
           .Produces<CustomerOrder>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Order items ------------------------------------------------
        api.MapGet("/order-items", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? alloy = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("orderItems", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetOrderItemsAsync(page, pageSize, alloy, orderBy, ct));
            })
           .WithName("ListOrderItems").WithTags("OrderItems")
           .WithSummary("List order line items (paged, sortable).")
           .Produces<PagedResult<OrderItem>>().ProducesValidationProblem();

        // order_item has a composite key (order + line number), so the single-item
        // routes are nested under the owning order (see docs/DATA_MODEL.md, #10).
        api.MapGet("/orders/{orderAbcNum:long}/items/{orderItemNum:long}", async (long orderAbcNum, long orderItemNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetOrderItemAsync(orderAbcNum, orderItemNum, ct) is { } item
                    ? Results.Ok(item)
                    : Results.NotFound())
           .WithName("GetOrderItem").WithTags("OrderItems")
           .WithSummary("Get one order line item by its composite key (order + line number).")
           .Produces<OrderItem>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/orders/{orderAbcNum:long}/items", async (long orderAbcNum, OrderItemWrite body, IAbisRepository repo, HttpContext ctx, CancellationToken ct) =>
            {
                StampTrimOverrideUser(body, ctx);
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                NormalizeTrimAndPieces(body);
                var created = await repo.CreateOrderItemAsync(orderAbcNum, body, ct);
                return Results.Created($"/api/orders/{orderAbcNum}/items/{created.OrderItemNum}", created);
            })
           .WithName("CreateOrderItem").WithTags("OrderItems")
           .WithSummary("Add a line item to an order (line number assigned per order).")
           .Produces<OrderItem>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/orders/{orderAbcNum:long}/items/{orderItemNum:long}", async (long orderAbcNum, long orderItemNum, OrderItemWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                StampTrimOverrideUser(body, ctx);
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                NormalizeTrimAndPieces(body);
                return await WithIfMatch(ctx, json, () => repo.GetOrderItemAsync(orderAbcNum, orderItemNum, ct), () => repo.UpdateOrderItemAsync(orderAbcNum, orderItemNum, body, ct));
            })
           .WithName("UpdateOrderItem").WithTags("OrderItems")
           .WithSummary("Replace an order line item (by order + line number). Supports If-Match.")
           .Produces<OrderItem>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Order-item blank geometry (the shape's dimensions) --------
        api.MapGet("/orders/{orderAbcNum:long}/items/{orderItemNum:long}/shape", async (long orderAbcNum, long orderItemNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetOrderItemShapeAsync(orderAbcNum, orderItemNum, ct) is { } shape
                    ? Results.Ok(shape)
                    : Results.NotFound())
           .WithName("GetOrderItemShape").WithTags("OrderItems")
           .WithSummary("Get an order line's blank geometry — the shape's dimensions (value + tolerances) and dies.")
           .Produces<OrderItemShape>().Produces(StatusCodes.Status404NotFound);

        api.MapPut("/orders/{orderAbcNum:long}/items/{orderItemNum:long}/shape", async (long orderAbcNum, long orderItemNum, OrderItemShapeWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                // The shape must be a known dimensioned shape; distinguishes a bad shape (400)
                // from a missing order line (404).
                if (ShapeGeometry.Resolve(body.ShapeType) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["shapeType"] = [$"Unknown shape type '{body.ShapeType}'. See /api/lookups/shape-types."],
                    });
                return await repo.UpsertOrderItemShapeAsync(orderAbcNum, orderItemNum, body, ct) is { } saved
                    ? Results.Ok(saved)
                    : Results.NotFound();
            })
           .WithName("PutOrderItemShape").WithTags("OrderItems")
           .WithSummary("Set an order line's blank geometry for its shape (upsert; aligns the line's sheet_type).")
           .Produces<OrderItemShape>().Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // ---- Parts (part-number master) --------------------------------
        api.MapGet("/parts", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, string? alloy = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("parts", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetPartsAsync(page, pageSize, customerId, alloy, orderBy, ct));
            })
           .WithName("ListParts").WithTags("Parts")
           .WithSummary("List part-number master records (paged, sortable; filter by customerId/alloy).")
           .Produces<PagedResult<Part>>().ProducesValidationProblem();

        api.MapGet("/parts/{partNumId:long}", async (long partNumId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetPartAsync(partNumId, ct) is { } part
                    ? Results.Ok(part)
                    : Results.NotFound())
           .WithName("GetPart").WithTags("Parts")
           .WithSummary("Get one part-number record by id.")
           .Produces<Part>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/parts", async (PartWrite body, IAbisRepository repo, HttpContext ctx, CancellationToken ct) =>
            {
                StampTrimOverrideUser(body, ctx);
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                NormalizeTrimAndPieces(body);
                var created = await repo.CreatePartAsync(body, ct);
                return Results.Created($"/api/parts/{created.PartNumId}", created);
            })
           .WithName("CreatePart").WithTags("Parts")
           .WithSummary("Create a part-number record (server-assigned id; requires customerId).")
           .Produces<Part>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/parts/{partNumId:long}", async (long partNumId, PartWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                StampTrimOverrideUser(body, ctx);
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // Legacy w_part_num_management: a part already applied to one or more orders
                // cannot be modified in place — it must be revised. Block with 409 Conflict.
                if (await repo.IsPartInUseAsync(partNumId, ct))
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Part in use",
                        detail: "Can't modify this part because it has already been applied to one or more orders. Create a revision instead.");
                NormalizeTrimAndPieces(body);
                return await WithIfMatch(ctx, json, () => repo.GetPartAsync(partNumId, ct), () => repo.UpdatePartAsync(partNumId, body, ct));
            })
           .WithName("UpdatePart").WithTags("Parts")
           .WithSummary("Replace a part-number record (blocked with 409 if the part is applied to any order). Supports If-Match.")
           .Produces<Part>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // Part-master blank geometry (same shapes as order items; dimensions only, no dies).
        api.MapGet("/parts/{partNumId:long}/shape", async (long partNumId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetPartShapeAsync(partNumId, ct) is { } shape
                    ? Results.Ok(shape)
                    : Results.NotFound())
           .WithName("GetPartShape").WithTags("Parts")
           .WithSummary("Get a part-master's blank geometry — the shape's dimensions (value + tolerances).")
           .Produces<PartShape>().Produces(StatusCodes.Status404NotFound);

        api.MapPut("/parts/{partNumId:long}/shape", async (long partNumId, PartShapeWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (ShapeGeometry.Resolve(body.ShapeType) is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["shapeType"] = [$"Unknown shape type '{body.ShapeType}'. See /api/lookups/shape-types."],
                    });
                // Same modify-in-use guard as the part record: geometry of an applied part is
                // frozen (legacy w_part_num_management modifies the whole part or not at all).
                if (await repo.IsPartInUseAsync(partNumId, ct))
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Part in use",
                        detail: "Can't modify this part's geometry because it has already been applied to one or more orders. Create a revision instead.");
                return await repo.UpsertPartShapeAsync(partNumId, body, ct) is { } saved
                    ? Results.Ok(saved)
                    : Results.NotFound();
            })
           .WithName("PutPartShape").WithTags("Parts")
           .WithSummary("Set a part-master's blank geometry for its shape (upsert; aligns the part's sheet_type; 409 if the part is applied to any order).")
           .Produces<PartShape>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        // ---- Dies (die / tooling) --------------------------------------
        api.MapGet("/dies", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? status = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("dies", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetDiesAsync(page, pageSize, status, orderBy, ct));
            })
           .WithName("ListDies").WithTags("Dies")
           .WithSummary("List dies/tooling (paged, sortable; filter by status).")
           .Produces<PagedResult<Die>>().ProducesValidationProblem();

        api.MapGet("/dies/{dieId:long}", async (long dieId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetDieAsync(dieId, ct) is { } die
                    ? Results.Ok(die)
                    : Results.NotFound())
           .WithName("GetDie").WithTags("Dies")
           .WithSummary("Get one die by id.")
           .Produces<Die>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/dies", async (DieWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateDieAsync(body, ct);
                return Results.Created($"/api/dies/{created.DieId}", created);
            })
           .WithName("CreateDie").WithTags("Dies")
           .WithSummary("Create a die/tooling record (server-assigned id; requires dieName).")
           .Produces<Die>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/dies/{dieId:long}", async (long dieId, DieWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetDieAsync(dieId, ct), () => repo.UpdateDieAsync(dieId, body, ct));
            })
           .WithName("UpdateDie").WithTags("Dies")
           .WithSummary("Replace a die/tooling record. Supports If-Match.")
           .Produces<Die>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Shipments -------------------------------------------------
        api.MapGet("/shipments", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("shipments", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetShipmentsAsync(page, pageSize, customerId, orderBy, ct));
            })
           .WithName("ListShipments").WithTags("Shipments")
           .WithSummary("List shipments / packing lists (paged, sortable; filter by customerId).")
           .Produces<PagedResult<Shipment>>().ProducesValidationProblem();

        api.MapGet("/shipments/{packingList:long}", async (long packingList, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetShipmentAsync(packingList, ct) is { } shipment
                    ? Results.Ok(shipment)
                    : Results.NotFound())
           .WithName("GetShipment").WithTags("Shipments")
           .WithSummary("Get one shipment by packing-list number.")
           .Produces<Shipment>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/shipments", async (ShipmentWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateShipmentAsync(body, ct);
                return Results.Created($"/api/shipments/{created.PackingList}", created);
            })
           .WithName("CreateShipment").WithTags("Shipments")
           .WithSummary("Create a shipment header (packing-list and bill-of-lading numbers server-assigned).")
           .Produces<Shipment>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/shipments/{packingList:long}", async (long packingList, ShipmentWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetShipmentAsync(packingList, ct), () => repo.UpdateShipmentAsync(packingList, body, ct));
            })
           .WithName("UpdateShipment").WithTags("Shipments")
           .WithSummary("Replace a shipment header (packing-list and bill-of-lading numbers preserved). Supports If-Match.")
           .Produces<Shipment>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        api.MapPatch("/shipments/{packingList:long}", (long packingList, ShipmentStatusPatch body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
                WithIfMatch(ctx, json, () => repo.GetShipmentAsync(packingList, ct), () => repo.PatchShipmentAsync(packingList, body, ct)))
           .WithName("PatchShipment").WithTags("Shipments")
           .WithSummary("Update a shipment's dispatch status (status, vehicle status, sent/actual times, notes). Supports If-Match.")
           .Produces<Shipment>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed);

        // Guided close-out: mark a shipment / BOL shipped (sets the closed status + stamps sent+actual
        // dates) in one action, instead of hand-editing the raw status number on the dispatch form.
        api.MapPost("/shipments/{packingList:long}/close", async (long packingList, IAbisRepository repo, CancellationToken ct) =>
                await repo.CloseShipmentAsync(packingList, ct) is { } s ? Results.Ok(s) : Results.NotFound())
           .WithName("CloseShipment").WithTags("Shipments")
           .WithSummary("Close out a shipment / BOL — mark it shipped and stamp the sent + actual dates.")
           .Produces<Shipment>().Produces(StatusCodes.Status404NotFound);

        // ---- Packing-list line items (the skids a shipment carries) --------------------
        api.MapGet("/shipments/{packingList:long}/items", async (long packingList, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetShipmentAsync(packingList, ct) is null
                    ? Results.NotFound()
                    : Results.Ok(await repo.GetPackingItemsAsync(packingList, ct)))
           .WithName("ListPackingItems").WithTags("Shipments")
           .WithSummary("List the line items on a packing list (shipment) — SHEET (finished-sheet skids, enriched with weight/pieces/part/PO/coil — the same content the 856 (ASN) reports), SCRAP (scrap skids), and REJECT_COIL (rejected coils), each tagged with its itemType.")
           .Produces<IReadOnlyList<PackingLineItem>>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/shipments/{packingList:long}/items", async (long packingList, PackingItemWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                var result = await repo.AddPackingItemAsync(packingList, body.ItemType, body.RefNum, ct);
                return result.Status switch
                {
                    "created" => Results.Created($"/api/shipments/{packingList}/items/{result.Item!.ItemType}/{result.Item.PackingItemId}", result.Item),
                    "bad-type" => Results.ValidationProblem(new Dictionary<string, string[]> { ["itemType"] = ["itemType must be SHEET, SCRAP, or REJECT_COIL."] }),
                    "no-shipment" => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Shipment not found", detail: $"No packing list {packingList}."),
                    "no-ref" => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Skid not found", detail: $"No {body.ItemType} skid {body.RefNum}."),
                    "duplicate" => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already on this packing list", detail: $"{body.ItemType} skid {body.RefNum} is already a line item on packing list {packingList}."),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError, title: "Add failed"),
                };
            })
           .WithName("AddPackingItem").WithTags("Shipments")
           .WithSummary("Add a line item to a packing list — itemType SHEET (finished sheet) / SCRAP (scrap) with refNum = the skid number, or REJECT_COIL with refNum = the coil abc number. The item id + packaging ticket are server-assigned. 400 bad type; 404 if the shipment or skid/coil is missing; 409 if it's already on this list. Config/data only — nothing transmits.")
           .Produces<PackingLineItem>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);

        api.MapDelete("/shipments/{packingList:long}/items/{itemType}/{itemId:long}", async (long packingList, string itemType, long itemId, IAbisRepository repo, CancellationToken ct) =>
                await repo.DeletePackingItemAsync(packingList, itemType, itemId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeletePackingItem").WithTags("Shipments")
           .WithSummary("Remove a line item (by itemType SHEET/SCRAP + its id) from a packing list.")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // ---- Receiving BOLs --------------------------------------------
        api.MapGet("/receiving-bols", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, int? status = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("receivingBols", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetReceivingBolsAsync(page, pageSize, customerId, status, orderBy, ct));
            })
           .WithName("ListReceivingBols").WithTags("Receiving")
           .WithSummary("List inbound receiving BOLs (paged, sortable; filter by customerId/status).")
           .Produces<PagedResult<ReceivingBol>>().ProducesValidationProblem();

        api.MapGet("/receiving-bols/{receivingBolId:long}", async (long receivingBolId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetReceivingBolAsync(receivingBolId, ct) is { } bol
                    ? Results.Ok(bol)
                    : Results.NotFound())
           .WithName("GetReceivingBol").WithTags("Receiving")
           .WithSummary("Get one receiving BOL by id.")
           .Produces<ReceivingBol>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/receiving-bols", async (ReceivingBolWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateReceivingBolAsync(body, ct);
                return Results.Created($"/api/receiving-bols/{created.ReceivingBolId}", created);
            })
           .WithName("CreateReceivingBol").WithTags("Receiving")
           .WithSummary("Create an inbound receiving BOL (requires bol and customerId).")
           .Produces<ReceivingBol>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/receiving-bols/{receivingBolId:long}", async (long receivingBolId, ReceivingBolWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetReceivingBolAsync(receivingBolId, ct), () => repo.UpdateReceivingBolAsync(receivingBolId, body, ct));
            })
           .WithName("UpdateReceivingBol").WithTags("Receiving")
           .WithSummary("Replace a receiving BOL. Supports If-Match.")
           .Produces<ReceivingBol>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Receiving BOL line items (legacy coil_receiving.pbl) ----
        api.MapGet("/receiving-bols/{receivingBolId:long}/detail", async (long receivingBolId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetReceivingBolDetailAsync(receivingBolId, ct) is { } d ? Results.Ok(d) : Results.NotFound())
           .WithName("GetReceivingBolDetail").WithTags("Receiving")
           .WithSummary("A receiving BOL with its coil line items (header + lines).")
           .Produces<ReceivingBolDetail>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/receiving-bols/{receivingBolId:long}/coils", async (long receivingBolId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetReceivingBolCoilsAsync(receivingBolId, ct)))
           .WithName("GetReceivingBolCoils").WithTags("Receiving")
           .WithSummary("The coil line items on a receiving BOL.")
           .Produces<IReadOnlyList<ReceivingBolCoil>>();

        api.MapPost("/receiving-bols/{receivingBolId:long}/coils", async (long receivingBolId, ReceivingBolCoilWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.CoilOrgNum))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["coilOrgNum"] = ["coilOrgNum is required."] });
                if (CashDateFormatError(body.CashDate) is { } cashErr)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["cashDate"] = [cashErr] });
                var created = await repo.AddReceivingBolCoilAsync(receivingBolId, body, ct);
                return created is null
                    ? Results.NotFound(new { message = $"Receiving BOL {receivingBolId} not found." })
                    : Results.Created($"/api/receiving-bols/{receivingBolId}/coils/{created.CoilId}", created);
            })
           .WithName("AddReceivingBolCoil").WithTags("Receiving")
           .WithSummary("Add a coil line to a receiving BOL (coil_id assigned server-side).")
           .Produces<ReceivingBolCoil>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        api.MapDelete("/receiving-bols/{receivingBolId:long}/coils/{coilId:int}", async (long receivingBolId, int coilId, IAbisRepository repo, CancellationToken ct) =>
                await repo.DeleteReceivingBolCoilAsync(receivingBolId, coilId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeleteReceivingBolCoil").WithTags("Receiving")
           .WithSummary("Remove a coil line from a receiving BOL.")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        api.MapPost("/receiving-bols/{receivingBolId:long}/mint", async (long receivingBolId, IAbisRepository repo, CancellationToken ct) =>
            {
                var result = await repo.MintBolCoilsAsync(receivingBolId, ct);
                if (result is null) return Results.NotFound();
                // Legacy w_coil_receiving:367 — a BOL with no coil lines can't be minted
                // ("please enter coil information before saving a BOL"); don't silently return Minted=0.
                if (result.Coils.Count == 0)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Empty BOL",
                        detail: $"Receiving BOL {receivingBolId} has no coil lines to mint.");
                return Results.Ok(result);
            })
           .WithName("MintBolCoils").WithTags("Receiving")
           .WithSummary("Mint coil inventory for the BOL's lines (legacy w_coil_receiving save) — creates COIL rows (status 2/new, 11/on-hold if damaged) and links them. Idempotent; 400 if the BOL has no coils.")
           .Produces<MintResult>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound);

        api.MapPost("/receiving-bols/{receivingBolId:long}/generate-861", async (long receivingBolId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                // Generation is an EDI write — gate on the EDI feature (service accounts pass through).
                if (await RequireFeatureAsync(ctx, repo, "EDI", 1, ct) is { } deny) return deny;
                var bol = await repo.GetReceivingBolAsync(receivingBolId, ct);
                if (bol is null) return Results.NotFound();
                var coils = await repo.GetReceivingBolCoilsAsync(receivingBolId, ct);
                if (coils.Count == 0)
                    return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Empty BOL",
                        detail: $"Receiving BOL {receivingBolId} has no coil lines; nothing to advise.");

                // Resolve the customer's 861 trading-partner profile (the config backbone) + their own DUNS for
                // N1*SU. No enabled 861 profile → 422 (configure it in the admin EDI setup).
                var customer = bol.CustomerId is null ? null : await repo.GetCustomerAsync(bol.CustomerId.Value, ct);
                var profile = bol.CustomerId is null ? null : await repo.GetEdiPartnerAsync(bol.CustomerId.Value, "861", ct);
                if (profile is null || !profile.Enabled)
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Not an 861 partner",
                        detail: $"Customer {bol.CustomerId} has no enabled 861 trading-partner profile (configure it in the admin EDI setup).");
                var supplierDuns = customer?.CustomerDunsNumberString ?? "";
                var supplierName = (customer?.CustomerShortName ?? "").Trim();   // N1*MF/N1*SU party name (Novelis)

                // One 861 per BOL — the stored payload is the idempotency guard.
                if (await repo.GetEdi861ForBolAsync(receivingBolId, ct) is { } existing)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already generated",
                        detail: $"An 861 was already generated for BOL {receivingBolId} (edi_file_id {existing.EdiFileId}, {existing.EdiFileName}). It is stored, not transmitted.");

                // Build + persist the X12 + tracking row and mark the BOL 861-generated. Never transmits.
                var result = await repo.PersistEdi861Async(bol, coils, profile, supplierDuns, supplierName, DateTime.Now, ct);
                return Results.Ok(result);
            })
           .WithName("GenerateReceiving861").WithTags("Receiving")
           .WithSummary("Generate + persist the 861 (Receiving Advice) X12 for a received BOL — built, integrated and stored, but NEVER transmitted (the VAN SFTP stays the legacy owner). 400 if the BOL has no coils, 422 if the customer isn't a configured 861 partner (Novelis/Aleris), 409 if already generated. View the payload at /edi/transactions/{ediFileId}/payload.")
           .Produces<Edi861Result>().Produces(StatusCodes.Status400BadRequest).Produces(StatusCodes.Status404NotFound)
           .Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status422UnprocessableEntity);

        // ---- Coil evaluation / QC (legacy coil_eval w_qc_sheet) ----
        api.MapGet("/coil-eval/coils", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetQcCoilsAsync(abJobNum, ct)))
           .WithName("GetQcCoils").WithTags("CoilEval")
           .WithSummary("Coils on a job to evaluate (coil ⋈ process_coil).")
           .Produces<IReadOnlyList<QcCoilRow>>();

        api.MapGet("/coil-eval/skids/{sheetSkidNum:long}/dimension-checks", async (long sheetSkidNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetDimensionChecksAsync(sheetSkidNum, ct)))
           .WithName("GetDimensionChecks").WithTags("CoilEval")
           .WithSummary("Dimensional QC checks recorded on a sheet skid.")
           .Produces<IReadOnlyList<SheetSkidDimensionCheck>>();

        api.MapPost("/coil-eval/skids/{sheetSkidNum:long}/dimension-checks", async (long sheetSkidNum, DimensionCheckWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateDimensionCheckAsync(sheetSkidNum, body, ct);
                return Results.Created($"/api/coil-eval/skids/{sheetSkidNum}/dimension-checks/{created.DimensionCheckNum}", created);
            })
           .WithName("CreateDimensionCheck").WithTags("CoilEval")
           .WithSummary("Record a dimensional QC check on a sheet-skid piece (in-spec pass/fail).")
           .Produces<SheetSkidDimensionCheck>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/coil-eval/jobs/{abJobNum:long}/eval-scrap", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetEvalScrapAsync(abJobNum, ct)))
           .WithName("GetEvalScrap").WithTags("CoilEval")
           .WithSummary("Scrap items found during evaluation for a job (joined to the scrap-type catalog).")
           .Produces<IReadOnlyList<EvalScrap>>();

        api.MapPost("/coil-eval/eval-scrap", async (EvalScrapWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (body.CoilAbcNum is null or <= 0 || body.AbJobNum is null or <= 0 || body.ScrapItemType is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["evalScrap"] = ["coilAbcNum, abJobNum and scrapItemType are required."],
                    });
                return Results.Ok(await repo.UpsertEvalScrapAsync(body, ct));
            })
           .WithName("UpsertEvalScrap").WithTags("CoilEval")
           .WithSummary("Record (upsert) a scrap item found during coil evaluation.")
           .Produces<EvalScrap>().ProducesValidationProblem();

        // ---- Production folder (legacy prod-folder w_production_folder) ----
        api.MapGet("/prod-folder/jobs/{abJobNum:long}", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetProductionFolderAsync(abJobNum, ct) is { } f ? Results.Ok(f) : Results.NotFound())
           .WithName("GetProductionFolder").WithTags("ProdFolder")
           .WithSummary("A job's production-folder summary (header + coil/skid/note counts).")
           .Produces<ProductionFolder>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/prod-folder/jobs/{abJobNum:long}/notes", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobFolderNotesAsync(abJobNum, ct)))
           .WithName("GetJobFolderNotes").WithTags("ProdFolder")
           .WithSummary("The e-folder notes on a job (with author name).")
           .Produces<IReadOnlyList<JobFolderNote>>();

        api.MapPost("/prod-folder/jobs/{abJobNum:long}/notes", async (long abJobNum, JobFolderNoteWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                // Don't file a note against a phantom job — legacy rejects it with
                // "Job X does not exist." (w_e_car_folder:537) before anything else.
                if (await repo.GetJobAsync(abJobNum, ct) is null)
                    return Results.NotFound();
                // The author: the resolved OIDC user, else the body's userId (dev API).
                long? userId = body.UserId;
                if (userId is null && ResolveLogin(ctx) is { } login)
                    userId = (await repo.GetSecurityUserByLoginAsync(login, ct))?.UserId;
                if (userId is null or <= 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["userId"] = ["userId is required (or authenticate as a known user)."] });
                var created = await repo.AddJobFolderNoteAsync(abJobNum, userId.Value, body.Notes, ct);
                return Results.Created($"/api/prod-folder/jobs/{abJobNum}/notes", created);
            })
           .WithName("AddJobFolderNote").WithTags("ProdFolder")
           .WithSummary("Add a note to a job's e-folder (author from the OIDC user or body userId).")
           .Produces<JobFolderNote>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // ---- Stacker line board / error log (legacy stacker_110) ----
        api.MapGet("/stacker/board", async (IAbisRepository repo, CancellationToken ct, long? lineNum = null) =>
                Results.Ok(await repo.GetStackerBoardAsync(lineNum, ct)))
           .WithName("GetStackerBoard").WithTags("Stacker")
           .WithSummary("A line's stacker board: jobs on the line with coil/skid counts (read-only monitor).")
           .Produces<IReadOnlyList<StackerBoardRow>>();

        api.MapGet("/stacker/line-errors", async (IAbisRepository repo, CancellationToken ct, long? lineNum = null, DateTime? from = null, DateTime? to = null) =>
                Results.Ok(await repo.GetLineErrorsAsync(lineNum, from, to, ct)))
           .WithName("GetLineErrors").WithTags("Stacker")
           .WithSummary("The line/stacker error log (error_evt ⋈ error_type), newest first.")
           .Produces<IReadOnlyList<LineErrorRow>>();

        api.MapPost("/stacker/line-errors", async (LineErrorWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (body.ErrorTypeId is null || string.IsNullOrWhiteSpace(body.ErrorUser))
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["lineError"] = ["errorTypeId and errorUser are required."],
                    });
                var created = await repo.CreateLineErrorAsync(body, ct);
                return Results.Created($"/api/stacker/line-errors/{created.ErrorEvtId}", created);
            })
           .WithName("CreateLineError").WithTags("Stacker")
           .WithSummary("Log a line/stacker error event.")
           .Produces<LineErrorRow>(StatusCodes.Status201Created).ProducesValidationProblem();

        // ---- EDI (outbound X12 transaction ledger + transmission log) --
        api.MapGet("/edi/transactions", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, string? transactionTypeId = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("ediTransactions", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetEdiTransactionsAsync(page, pageSize, customerId, transactionTypeId, orderBy, ct));
            })
           .WithName("ListEdiTransactions").WithTags("EDI")
           .WithSummary("List outbound EDI transactions, newest first (paged, sortable; filter by customerId/transactionTypeId).")
           .Produces<PagedResult<EdiTransaction>>().ProducesValidationProblem();

        api.MapGet("/edi/transactions/{ediFileId:long}", async (long ediFileId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetEdiTransactionAsync(ediFileId, ct) is { } tx
                    ? Results.Ok(tx)
                    : Results.NotFound())
           .WithName("GetEdiTransaction").WithTags("EDI")
           .WithSummary("Get one outbound EDI transaction by its EDI file id.")
           .Produces<EdiTransaction>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/edi/transactions/{ediFileId:long}/payload", async (long ediFileId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetEdiPayloadAsync(ediFileId, ct) is { } p
                    ? Results.Text(p.Payload ?? "", "text/plain")
                    : Results.NotFound())
           .WithName("GetEdiPayload").WithTags("EDI")
           .WithSummary("The stored X12 payload for a generated EDI transaction, as plain text. Generation only — nothing here transmits.")
           .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status404NotFound);

        api.MapPost("/edi/870/generate", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct, long customerId = 1980) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "EDI", 1, ct) is { } deny) return deny;
                // Resolve the customer's 870 trading-partner profile (the config backbone). No enabled 870
                // profile → 422. The body layout is chosen by profile.Variant (aleris batches everything into
                // one file; novelis produces one file per job).
                var profile = await repo.GetEdiPartnerAsync(customerId, "870", ct);
                if (profile is null || !profile.Enabled)
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Not an 870 partner",
                        detail: $"Customer {customerId} has no enabled 870 trading-partner profile (configure it in the admin EDI setup).");
                // Constellium (customer 2776) is per-COIL: one interchange/file per (job, coil), its own assemble/persist.
                if (string.Equals(profile.Variant, "constellium", StringComparison.OrdinalIgnoreCase))
                {
                    var cbatch = await repo.AssembleEdi870ConstBatchAsync(customerId, ct);
                    return Results.Ok(await repo.PersistEdi870ConstAsync(cbatch, profile, DateTime.Now, ct));
                }
                var batch = await repo.AssembleEdi870BatchAsync(customerId, profile.Variant, ct);
                var result = await repo.PersistEdi870Async(batch, profile, DateTime.Now, ct);
                return Results.Ok(result);
            })
           .WithName("GenerateEdi870").WithTags("EDI")
           .WithSummary("Generate + persist the 870 (Order/Coil Status) batch for a customer (default Aleris 1980) — every not-yet-reported shippable item + finished-job scrap, built and stored but NEVER transmitted. Returns status 'nothing' when there's nothing to report; view the payload at /edi/transactions/{ediFileId}/payload. Marks reported items/jobs so they aren't sent twice.")
           .Produces<Edi870Result>().Produces(StatusCodes.Status422UnprocessableEntity);

        api.MapPost("/edi/856/generate", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct, long packingList = 0) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "EDI", 1, ct) is { } deny) return deny;
                if (packingList <= 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["packingList"] = ["packingList is required."] });
                // Assemble the shipment first so we know the customer, then resolve their 856 partner profile.
                var probe = await repo.AssembleEdi856Async(packingList, null, ct);
                if (probe is null)
                    return Results.NotFound();
                var profile = probe.CustomerId is { } cid ? await repo.GetEdiPartnerAsync(cid, "856", ct) : null;
                if (profile is null || !profile.Enabled)
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Not an 856 partner",
                        detail: $"The shipment's customer has no enabled 856 trading-partner profile (configure it in the admin EDI setup).");
                // One 856 per packing list — the stored payload is the idempotency guard.
                if (await repo.GetEdi856ForPackingListAsync(packingList, ct) is { } existing)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Already generated",
                        detail: $"An 856 was already generated for packing list {packingList} (edi_file_id {existing.EdiFileId}, {existing.EdiFileName}). It is stored, not transmitted.");
                // Re-assemble with the resolved variant (drives the Constellium per-item fields), then persist.
                var shp = await repo.AssembleEdi856Async(packingList, profile.Variant, ct);
                var result = await repo.PersistEdi856Async(shp!, profile, packingList, DateTime.Now, ct);
                return Results.Ok(result);
            })
           .WithName("GenerateEdi856").WithTags("EDI")
           .WithSummary("Generate + persist the 856 (Advance Ship Notice) for a shipment's packing list — the shipment header + one item per packed skid, built and stored but NEVER transmitted. 404 if the packing list has no shipment; 422 if its customer isn't an 856 partner; 409 if already generated. View the payload at /edi/transactions/{ediFileId}/payload.")
           .Produces<Edi856Result>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status422UnprocessableEntity).Produces(StatusCodes.Status409Conflict);

        api.MapPost("/edi/846/generate", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct, long customerId = 3061) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "EDI", 1, ct) is { } deny) return deny;
                var profile = await repo.GetEdiPartnerAsync(customerId, "846", ct);
                if (profile is null || !profile.Enabled)
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Not an 846 partner",
                        detail: $"Customer {customerId} has no enabled 846 trading-partner profile (configure it in the admin EDI setup).");
                var snap = await repo.AssembleEdi846Async(customerId, ct);
                var result = await repo.PersistEdi846Async(snap, profile, DateTime.Now, ct);
                return Results.Ok(result);
            })
           .WithName("GenerateEdi846").WithTags("EDI")
           .WithSummary("Generate + persist the 846 (Inventory Advice) for a customer (default Cleveland-Cliffs 3061) — a full on-hand inventory snapshot (skids + coils), built and stored but NEVER transmitted. Returns status 'nothing' when there's no on-hand inventory; view the payload at /edi/transactions/{ediFileId}/payload. 422 if the customer isn't a configured 846 partner. The 846 is a snapshot — it may be regenerated (no dedup guard).")
           .Produces<Edi846Result>().Produces(StatusCodes.Status422UnprocessableEntity);

        api.MapGet("/edi/partners", async (IAbisRepository repo, CancellationToken ct, string? transactionSet = null) =>
                Results.Ok(await repo.ListEdiPartnersAsync(transactionSet, ct)))
           .WithName("ListEdiPartners").WithTags("EDI")
           .WithSummary("The per-(customer, transaction set) EDI trading-partner profiles — the config backbone that lets each customer have different requirements for their 861/870/846/… documents. Optionally filter by transactionSet.")
           .Produces<IReadOnlyList<EdiPartnerProfile>>();

        api.MapGet("/edi/log", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? customerId = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("ediLog", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetEdiLogAsync(page, pageSize, customerId, orderBy, ct));
            })
           .WithName("ListEdiLog").WithTags("EDI")
           .WithSummary("List EDI transmission-log entries, newest first (paged, sortable; filter by customerId).")
           .Produces<PagedResult<EdiLogEntry>>().ProducesValidationProblem();

        api.MapGet("/edi/997/waiting", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 50, long? customerId = null) =>
                Results.Ok(await repo.GetEdi997WaitingAsync(page, pageSize, customerId, DateTime.Now, ct)))
           .WithName("Edi997Waiting").WithTags("EDI")
           .WithSummary("Outbound transactions still awaiting a 997 functional acknowledgment (fa_received_time IS NULL), oldest first — the in-app form of the legacy check_997.sh email. Each is bucketed by age: fresh (<2h), waiting (2–24h, what legacy chased), overdue (>24h). Read-only.")
           .Produces<Edi997WaitingReport>();

        api.MapPost("/edi/997/ingest", async (Edi997IngestWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "EDI", 1, ct) is { } deny) return deny;
                if (string.IsNullOrWhiteSpace(body.Payload))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["payload"] = ["A 997 payload is required."] });
                var result = await repo.IngestEdi997Async(body.Payload, body.SourceName?.Trim(), DateTime.Now, ct);
                return Results.Ok(result);
            })
           .WithName("Edi997Ingest").WithTags("EDI")
           .WithSummary("Ingest an inbound 997 (Functional Acknowledgment) and reconcile its acks against the outbound ledger — stamps fa_received_time / fa_receive_status on each matched transaction (matched by group control number = edi_file_id). Parse + store only; never transmits. Returns a matched/unmatched + accept/reject summary.")
           .Produces<Edi997IngestResult>().ProducesValidationProblem();

        // ---- Scan log (shop-floor tracking) ----------------------------
        api.MapGet("/scan-logs", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? abJobNum = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("scanLogs", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetScanLogsAsync(page, pageSize, abJobNum, orderBy, ct));
            })
           .WithName("ListScanLogs").WithTags("ScanLog")
           .WithSummary("List shop-floor scan events, newest first (paged, sortable; filter by abJobNum).")
           .Produces<PagedResult<ScanLog>>().ProducesValidationProblem();

        api.MapGet("/scan-logs/{scanId:long}", async (long scanId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetScanLogAsync(scanId, ct) is { } scan
                    ? Results.Ok(scan)
                    : Results.NotFound())
           .WithName("GetScanLog").WithTags("ScanLog")
           .WithSummary("Get one scan event by id.")
           .Produces<ScanLog>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/scan-logs", async (ScanLogWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateScanLogAsync(body, ct);
                return Results.Created($"/api/scan-logs/{created.ScanId}", created);
            })
           .WithName("CreateScanLog").WithTags("ScanLog")
           .WithSummary("Record a shop-floor scan event (append-only; requires abJobNum, scanStation, note).")
           .Produces<ScanLog>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/jobs/{abJobNum:long}/scans", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetJobScansAsync(abJobNum, ct)))
           .WithName("GetJobScans").WithTags("Jobs")
           .WithSummary("List shop-floor scan events for a job.")
           .Produces<IEnumerable<ScanLog>>();

        // ---- Maintenance log -------------------------------------------
        api.MapGet("/maint-logs", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? status = null, long? groupDepartmentId = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("maintLogs", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetMaintLogsAsync(page, pageSize, status, groupDepartmentId, orderBy, ct));
            })
           .WithName("ListMaintLogs").WithTags("Maintenance")
           .WithSummary("List maintenance log entries, newest first (paged, sortable; filter by status/groupDepartmentId).")
           .Produces<PagedResult<MaintLog>>().ProducesValidationProblem();

        api.MapGet("/maint-logs/{maintLogId:long}", async (long maintLogId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetMaintLogAsync(maintLogId, ct) is { } entry
                    ? Results.Ok(entry)
                    : Results.NotFound())
           .WithName("GetMaintLog").WithTags("Maintenance")
           .WithSummary("Get one maintenance log entry by id.")
           .Produces<MaintLog>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/maint-logs", async (MaintLogWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateMaintLogAsync(body, ct);
                return Results.Created($"/api/maint-logs/{created.MaintLogId}", created);
            })
           .WithName("CreateMaintLog").WithTags("Maintenance")
           .WithSummary("Create a maintenance log entry (requires probDateTime, probDetails, author).")
           .Produces<MaintLog>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/maint-logs/{maintLogId:long}", async (long maintLogId, MaintLogWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetMaintLogAsync(maintLogId, ct), () => repo.UpdateMaintLogAsync(maintLogId, body, ct));
            })
           .WithName("UpdateMaintLog").WithTags("Maintenance")
           .WithSummary("Replace a maintenance log entry. Supports If-Match.")
           .Produces<MaintLog>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Carriers --------------------------------------------------
        api.MapGet("/carriers", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? status = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("carriers", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetCarriersAsync(page, pageSize, status, orderBy, ct));
            })
           .WithName("ListCarriers").WithTags("Carriers")
           .WithSummary("List carriers / trucking partners (paged, sortable; filter by status).")
           .Produces<PagedResult<Carrier>>().ProducesValidationProblem();

        api.MapGet("/carriers/{carrierId:long}", async (long carrierId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCarrierAsync(carrierId, ct) is { } carrier
                    ? Results.Ok(carrier)
                    : Results.NotFound())
           .WithName("GetCarrier").WithTags("Carriers")
           .WithSummary("Get one carrier by id.")
           .Produces<Carrier>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/carriers", async (CarrierWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateCarrierAsync(body, ct);
                return Results.Created($"/api/carriers/{created.CarrierId}", created);
            })
           .WithName("CreateCarrier").WithTags("Carriers")
           .WithSummary("Create a carrier (server-assigned id; requires carrierFullName).")
           .Produces<Carrier>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/carriers/{carrierId:long}", async (long carrierId, CarrierWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetCarrierAsync(carrierId, ct), () => repo.UpdateCarrierAsync(carrierId, body, ct));
            })
           .WithName("UpdateCarrier").WithTags("Carriers")
           .WithSummary("Replace a carrier. Supports If-Match.")
           .Produces<Carrier>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Shifts ----------------------------------------------------
        api.MapGet("/shifts", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? lineNum = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("shifts", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetShiftsAsync(page, pageSize, lineNum, orderBy, ct));
            })
           .WithName("ListShifts").WithTags("Shifts")
           .WithSummary("List production shifts, newest first (paged, sortable; filter by lineNum).")
           .Produces<PagedResult<Shift>>().ProducesValidationProblem();

        api.MapGet("/shifts/{shiftNum:long}", async (long shiftNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetShiftAsync(shiftNum, ct) is { } shift
                    ? Results.Ok(shift)
                    : Results.NotFound())
           .WithName("GetShift").WithTags("Shifts")
           .WithSummary("Get one shift by id.")
           .Produces<Shift>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/shifts", async (ShiftWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // One shift per (line, schedule_type, day): a scheduled shift already exists is a
                // conflict — use the update instead (w_daily_production_modify_schedule:543).
                if (body is { LineNum: { } line, ScheduleType: { } sched, StartTime: { } start }
                    && await repo.ShiftExistsAsync(line, sched, start, ct))
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Shift already exists",
                        detail: "A shift already exists for this line, schedule type, and date. Update the existing shift instead.");
                var created = await repo.CreateShiftAsync(body, ct);
                return Results.Created($"/api/shifts/{created.ShiftNum}", created);
            })
           .WithName("CreateShift").WithTags("Shifts")
           .WithSummary("Create a production shift (one per line + schedule type + day).")
           .Produces<Shift>(StatusCodes.Status201Created).Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        api.MapPut("/shifts/{shiftNum:long}", async (long shiftNum, ShiftWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetShiftAsync(shiftNum, ct), () => repo.UpdateShiftAsync(shiftNum, body, ct));
            })
           .WithName("UpdateShift").WithTags("Shifts")
           .WithSummary("Replace a production shift. Supports If-Match.")
           .Produces<Shift>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Downtime instances ----------------------------------------
        api.MapGet("/downtime", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, long? abJobNum = null, long? shiftNum = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("downtime", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetDowntimeInstancesAsync(page, pageSize, abJobNum, shiftNum, orderBy, ct));
            })
           .WithName("ListDowntime").WithTags("Downtime")
           .WithSummary("List downtime instances, newest first (paged, sortable; filter by abJobNum/shiftNum).")
           .Produces<PagedResult<DowntimeInstance>>().ProducesValidationProblem();

        api.MapGet("/downtime/{instanceNum:long}", async (long instanceNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetDowntimeInstanceAsync(instanceNum, ct) is { } dt
                    ? Results.Ok(dt)
                    : Results.NotFound())
           .WithName("GetDowntimeInstance").WithTags("Downtime")
           .WithSummary("Get one downtime instance by id.")
           .Produces<DowntimeInstance>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/downtime", async (DowntimeInstanceWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateDowntimeInstanceAsync(body, ct);
                return Results.Created($"/api/downtime/{created.InstanceNum}", created);
            })
           .WithName("CreateDowntimeInstance").WithTags("Downtime")
           .WithSummary("Log a downtime instance.")
           .Produces<DowntimeInstance>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/downtime/{instanceNum:long}", async (long instanceNum, DowntimeInstanceWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetDowntimeInstanceAsync(instanceNum, ct), () => repo.UpdateDowntimeInstanceAsync(instanceNum, body, ct));
            })
           .WithName("UpdateDowntimeInstance").WithTags("Downtime")
           .WithSummary("Replace a downtime instance. Supports If-Match.")
           .Produces<DowntimeInstance>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        api.MapGet("/downtime/{instanceNum:long}/segments", async (long instanceNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetDowntimeSegmentsAsync(instanceNum, ct)))
           .WithName("GetDowntimeSegments").WithTags("Downtime")
           .WithSummary("The cause-segments (reason + duration) logged against a downtime instance.")
           .Produces<IReadOnlyList<DowntimeSegment>>();

        api.MapPost("/downtime/{instanceNum:long}/segments", async (long instanceNum, DowntimeSegmentWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (body.CauseId is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["causeId"] = ["A downtime cause is required."] });
                if (body.DurationSeconds is < 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["durationSeconds"] = ["Duration cannot be negative."] });
                var seg = await repo.AddDowntimeSegmentAsync(instanceNum, body, ct);
                return seg is null ? Results.NotFound()
                    : Results.Created($"/api/downtime/{instanceNum}/segments/{seg.Id}", seg);
            })
           .WithName("AddDowntimeSegment").WithTags("Downtime")
           .WithSummary("Add a cause-segment (reason + duration) to a downtime instance.")
           .Produces<DowntimeSegment>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // ---- Truck appointments (ABIS-owned; replaces the plant's Excel truck schedule) --------
        api.MapGet("/truck-appointments", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 50, string? direction = null, int? status = null, DateTime? from = null, DateTime? to = null) =>
                Results.Ok(await repo.GetTruckAppointmentsAsync(page, pageSize, direction, status, from, to, ct)))
           .WithName("ListTruckAppointments").WithTags("Trucks")
           .WithSummary("List truck appointments (paged; filter by direction / status / scheduled-date range).")
           .Produces<PagedResult<TruckAppointment>>();

        api.MapGet("/truck-appointments/{id:long}", async (long id, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetTruckAppointmentAsync(id, ct) is { } a ? Results.Ok(a) : Results.NotFound())
           .WithName("GetTruckAppointment").WithTags("Trucks")
           .WithSummary("Get one truck appointment.").Produces<TruckAppointment>().Produces(StatusCodes.Status404NotFound);

        // Driver self-sign-in kiosk: find an appointment by BOL / ref number or appointment id (so a
        // driver locates their own without listing the board). The :long constraint above keeps this
        // distinct from GET /{id}.
        api.MapGet("/truck-appointments/lookup", async (string q, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.LookupTruckAppointmentsAsync(q ?? string.Empty, ct)))
           .WithName("LookupTruckAppointments").WithTags("Trucks")
           .WithSummary("Look up truck appointments by BOL / ref number or appointment id (driver kiosk).")
           .Produces<IReadOnlyList<TruckAppointment>>();

        api.MapPost("/truck-appointments", async (TruckAppointmentWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (ValidateTruck(body) is { } problems) return Results.ValidationProblem(problems);
                var created = await repo.CreateTruckAppointmentAsync(body, ResolveLogin(ctx), ct);
                return Results.Created($"/api/truck-appointments/{created.AppointmentId}", created);
            })
           .WithName("CreateTruckAppointment").WithTags("Trucks")
           .WithSummary("Schedule a truck appointment.").Produces<TruckAppointment>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/truck-appointments/{id:long}", async (long id, TruckAppointmentWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (ValidateTruck(body) is { } problems) return Results.ValidationProblem(problems);
                return await repo.UpdateTruckAppointmentAsync(id, body, ct) is { } a ? Results.Ok(a) : Results.NotFound();
            })
           .WithName("UpdateTruckAppointment").WithTags("Trucks")
           .WithSummary("Edit a truck appointment.").Produces<TruckAppointment>().Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        api.MapPost("/truck-appointments/{id:long}/check-in", async (long id, TruckCheckInBody? body, IAbisRepository repo, CancellationToken ct) =>
                await repo.CheckInTruckAsync(id, body?.DriverName, body?.DriverPhone, ct) is { } a ? Results.Ok(a) : Results.NotFound())
           .WithName("CheckInTruck").WithTags("Trucks")
           .WithSummary("Gate/kiosk check-in — stamp arrival + set status 'Parked out back'; an optional body captures the driver name/phone (kiosk sign-in).").Produces<TruckAppointment>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/truck-appointments/{id:long}/check-out", async (long id, IAbisRepository repo, CancellationToken ct) =>
            {
                // Guard: a truck can't sign out before it has signed in (protects the unattended kiosk
                // from checking out a never-arrived appointment).
                var existing = await repo.GetTruckAppointmentAsync(id, ct);
                if (existing is null) return Results.NotFound();
                if (existing.CheckinTime is null)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Not checked in",
                        detail: "This truck hasn't checked in yet, so it can't check out.");
                var a = await repo.CheckOutTruckAsync(id, ct);
                if (a is null) return Results.NotFound();
                // Truck→BOL link: an OUTBOUND truck leaving the gate closes its linked shipment/BOL
                // (the appointment carries an optional SHIPMENT ref = the packing-list number).
                // Best-effort — a missing/unlinked shipment is a no-op, never fails the check-out.
                if (string.Equals(a.Direction, "OUTBOUND", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a.RefType, "SHIPMENT", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(a.RefId, out var packingList))
                    await repo.CloseShipmentAsync(packingList, ct);
                return Results.Ok(a);
            })
           .WithName("CheckOutTruck").WithTags("Trucks")
           .WithSummary("Gate check-out — stamp departure + set status Signed-out; an outbound truck also closes its linked BOL. 409 if it never checked in.").Produces<TruckAppointment>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);

        api.MapPatch("/truck-appointments/{id:long}/status", async (long id, TruckStatusPatch body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (body.Status is not (0 or 1 or 2 or 3 or 4 or 5 or 6 or 9))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["status must be 0–6 or 9."] });
                return await repo.SetTruckStatusAsync(id, body.Status.Value, ct) is { } a ? Results.Ok(a) : Results.NotFound();
            })
           .WithName("SetTruckStatus").WithTags("Trucks")
           .WithSummary("Set a truck's location status (0 Pending, 1 Late, 2 Parked, 3–5 Sent to Bldg 1–3, 6 Signed out, 9 Cancelled).")
           .Produces<TruckAppointment>().Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // ---- Sketches --------------------------------------------------
        api.MapGet("/sketches", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? status = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("sketches", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetSketchesAsync(page, pageSize, status, orderBy, ct));
            })
           .WithName("ListSketches").WithTags("Sketches")
           .WithSummary("List part sketches/drawings (paged, sortable; filter by status). Excludes the binary image.")
           .Produces<PagedResult<Sketch>>().ProducesValidationProblem();

        api.MapGet("/sketches/{sketchId:long}", async (long sketchId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetSketchAsync(sketchId, ct) is { } sketch
                    ? Results.Ok(sketch)
                    : Results.NotFound())
           .WithName("GetSketch").WithTags("Sketches")
           .WithSummary("Get one sketch header by id (no image).")
           .Produces<Sketch>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/sketches", async (SketchWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateSketchAsync(body, ct);
                return Results.Created($"/api/sketches/{created.SketchId}", created);
            })
           .WithName("CreateSketch").WithTags("Sketches")
           .WithSummary("Create a sketch header (server-assigned id; requires sketchName; image not written via API).")
           .Produces<Sketch>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/sketches/{sketchId:long}", async (long sketchId, SketchWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetSketchAsync(sketchId, ct), () => repo.UpdateSketchAsync(sketchId, body, ct));
            })
           .WithName("UpdateSketch").WithTags("Sketches")
           .WithSummary("Replace a sketch header (image left untouched). Supports If-Match.")
           .Produces<Sketch>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Test results (QA) -----------------------------------------
        api.MapGet("/test-results", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? testType = null, string? position = null,
                DateTime? from = null, DateTime? to = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("testResults", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetTestResultsAsync(page, pageSize, testType, position, from, to, orderBy, ct));
            })
           .WithName("ListTestResults").WithTags("TestResults")
           .WithSummary("List posted mechanical test results (paged, filterable, sortable).")
           .Produces<PagedResult<TestResult>>().ProducesValidationProblem();

        api.MapPost("/test-results", async (TestResultWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                var problems = new Dictionary<string, string[]>();
                if (body.CoilAbcNum is null or <= 0) problems["coilAbcNum"] = ["coilAbcNum is required."];
                if (string.IsNullOrWhiteSpace(body.Position)) problems["position"] = ["position is required."];
                if (problems.Count > 0) return Results.ValidationProblem(problems);
                var created = await repo.CreateTestResultAsync(body, ct);
                return created is null
                    ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Coil not found", detail: $"No coil {body.CoilAbcNum}.")
                    : Results.Created($"/api/test-results?position={Uri.EscapeDataString(body.Position!)}", created);
            })
           .WithName("CreateTestResult").WithTags("TestResults")
           .WithSummary("Record a posted mechanical test result (pst_test_result) for a coil — YTS/UTS/elongation/n/r + thickness/width at a sample position. coilAbcNum + position are required; created_date is stamped server-side. 404 if the coil is missing. This is the write that lets the read-only test-results list populate.")
           .Produces<TestResult>(StatusCodes.Status201Created).Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        // In-progress / working-set test results (companion to the posted table).
        api.MapGet("/temp-test-results", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, int? testType = null, string? position = null,
                DateTime? from = null, DateTime? to = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("tempTestResults", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetTempTestResultsAsync(page, pageSize, testType, position, from, to, orderBy, ct));
            })
           .WithName("ListTempTestResults").WithTags("TestResults")
           .WithSummary("List in-progress (working-set) mechanical test results.")
           .Produces<PagedResult<TempTestResult>>().ProducesValidationProblem();

        // ---- Customers (read + write) ----------------------------------
        api.MapGet("/customers", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? name = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("customers", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetCustomersAsync(page, pageSize, name, orderBy, ct));
            })
           .WithName("ListCustomers").WithTags("Customers")
           .WithSummary("List customers (paged, sortable).")
           .Produces<PagedResult<Customer>>().ProducesValidationProblem();

        api.MapGet("/customers/{customerId:long}", async (long customerId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCustomerAsync(customerId, ct) is { } customer
                    ? Results.Ok(customer)
                    : Results.NotFound())
           .WithName("GetCustomer").WithTags("Customers")
           .WithSummary("Get one customer by id.")
           .Produces<Customer>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/customers/{customerId:long}/contacts", async (long customerId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCustomerContactsAsync(customerId, ct)))
           .WithName("GetCustomerContacts").WithTags("Customers")
           .WithSummary("List the contacts for a customer.")
           .Produces<IEnumerable<CustomerContact>>();

        api.MapGet("/customer-contacts/{contactId:long}", async (long contactId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCustomerContactAsync(contactId, ct) is { } contact
                    ? Results.Ok(contact)
                    : Results.NotFound())
           .WithName("GetCustomerContact").WithTags("Customers")
           .WithSummary("Get one customer contact by id.")
           .Produces<CustomerContact>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/customers/{customerId:long}/contacts", async (long customerId, CustomerContactWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateCustomerContactAsync(customerId, body, ct);
                return Results.Created($"/api/customer-contacts/{created.ContactId}", created);
            })
           .WithName("CreateCustomerContact").WithTags("Customers")
           .WithSummary("Add a contact to a customer (server-assigned id; requires lastName).")
           .Produces<CustomerContact>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/customer-contacts/{contactId:long}", async (long contactId, CustomerContactWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetCustomerContactAsync(contactId, ct), () => repo.UpdateCustomerContactAsync(contactId, body, ct));
            })
           .WithName("UpdateCustomerContact").WithTags("Customers")
           .WithSummary("Replace a customer contact (owning customer unchanged). Supports If-Match.")
           .Produces<CustomerContact>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        api.MapPost("/customers", async (CustomerWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateCustomerAsync(body, ct);
                return Results.Created($"/api/customers/{created.CustomerId}", created);
            })
           .WithName("CreateCustomer").WithTags("Customers")
           .WithSummary("Create a customer.")
           .Produces<Customer>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapPut("/customers/{customerId:long}", async (long customerId, CustomerWrite body, IAbisRepository repo, HttpContext ctx, IOptions<JsonOptions> json, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                return await WithIfMatch(ctx, json, () => repo.GetCustomerAsync(customerId, ct), () => repo.UpdateCustomerAsync(customerId, body, ct));
            })
           .WithName("UpdateCustomer").WithTags("Customers")
           .WithSummary("Replace a customer. Supports If-Match.")
           .Produces<Customer>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status412PreconditionFailed).ProducesValidationProblem();

        // ---- Skids ------------------------------------------------------
        api.MapGet("/sheet-skids", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("sheetSkids", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetSheetSkidsAsync(page, pageSize, orderBy, ct));
            })
           .WithName("ListSheetSkids").WithTags("Skids")
           .WithSummary("List finished sheet skids (paged, sortable).")
           .Produces<PagedResult<SheetSkid>>().ProducesValidationProblem();

        api.MapGet("/sheet-skids/{sheetSkidNum:long}", async (long sheetSkidNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetSheetSkidAsync(sheetSkidNum, ct) is { } skid
                    ? Results.Ok(skid)
                    : Results.NotFound())
           .WithName("GetSheetSkid").WithTags("Skids")
           .WithSummary("Get one sheet skid by id.")
           .Produces<SheetSkid>().Produces(StatusCodes.Status404NotFound);

        // ---- Documents (server-rendered printable HTML; skid tags first) ----
        api.MapGet("/documents/sheet-skid/{sheetSkidNum:long}", async (long sheetSkidNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetSheetSkidAsync(sheetSkidNum, ct) is { } skid
                    ? Results.Content(HtmlDocuments.SheetSkidTag(skid), "text/html; charset=utf-8")
                    : Results.NotFound())
           .WithName("SheetSkidTag").WithTags("Documents")
           .WithSummary("Printable sheet-skid tag (HTML with a Code 39 barcode).")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/documents/scrap-skid/{scrapSkidNum:long}", async (long scrapSkidNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetScrapSkidAsync(scrapSkidNum, ct) is { } skid
                    ? Results.Content(HtmlDocuments.ScrapSkidTag(skid), "text/html; charset=utf-8")
                    : Results.NotFound())
           .WithName("ScrapSkidTag").WithTags("Documents")
           .WithSummary("Printable scrap-skid tag (HTML with a Code 39 barcode).")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/documents/coil-label/{coilAbcNum:long}", async (long coilAbcNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCoilAsync(coilAbcNum, ct) is { } coil
                    ? Results.Content(HtmlDocuments.CoilLabel(coil), "text/html; charset=utf-8")
                    : Results.NotFound())
           .WithName("CoilLabel").WithTags("Documents")
           .WithSummary("Printable coil ABC label (HTML with a Code 39 barcode) — the coil-receiving scanner tag.")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/documents/transfer-certificate/{certificateNum:long}", async (long certificateNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCoilOwnershipTransferCertificateAsync(certificateNum, ct) is { } cert
                    ? Results.Content(HtmlDocuments.TransferCertificate(cert), "text/html; charset=utf-8")
                    : Results.NotFound())
           .WithName("TransferCertificate").WithTags("Documents")
           .WithSummary("Printable coil-ownership transfer certificate (toll-processing document) as HTML.")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        // The full invoice document (legacy w_invoice / d_report_invoice_data): customer/enduser/PO,
        // shape spec, alloy/temper/gauge, and every weight bucket with the exact rejected-coil rule.
        // Optional invoiceNum stamps a saved invoice's number + date onto the document.
        api.MapGet("/documents/invoice/{abJobNum:long}", async (long abJobNum, string? invoiceNum, IAbisRepository repo, CancellationToken ct) =>
            {
                var comp = await repo.GetInvoiceComputationAsync(abJobNum, ct);
                if (comp is null) return Results.NotFound();
                var saved = string.IsNullOrWhiteSpace(invoiceNum) ? null : await repo.GetInvoiceAsync(abJobNum, invoiceNum, ct);
                return Results.Content(HtmlDocuments.InvoiceDoc(comp, saved), "text/html; charset=utf-8");
            })
           .WithName("InvoiceDocument").WithTags("Documents")
           .WithSummary("Printable invoice for a job (weight rollups + spec block). Optional invoiceNum stamps the saved number/date.")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/documents/packing-list/{packingList:long}", async (long packingList, IAbisRepository repo, CancellationToken ct) =>
            {
                var shipment = await repo.GetShipmentAsync(packingList, ct);
                if (shipment is null) return Results.NotFound();
                var items = await repo.GetPackingItemsAsync(packingList, ct);
                var customerName = shipment.CustomerId is { } cid ? (await repo.GetCustomerAsync(cid, ct))?.CustomerName : null;
                return Results.Content(HtmlDocuments.PackingTicket(shipment, items, customerName), "text/html; charset=utf-8");
            })
           .WithName("PackingListDocument").WithTags("Documents")
           .WithSummary("Printable packing list / ticket for a shipment — the header + every line item it carries (sheet / scrap / reject-coil) with weight + piece totals.")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapGet("/documents/bol/{packingList:long}", async (long packingList, IAbisRepository repo, CancellationToken ct) =>
            {
                var shipment = await repo.GetShipmentAsync(packingList, ct);
                if (shipment is null) return Results.NotFound();
                var carrier = shipment.CarrierId is { } carId ? await repo.GetCarrierAsync(carId, ct) : null;
                var customer = shipment.CustomerId is { } cid ? await repo.GetCustomerAsync(cid, ct) : null;
                var shipTo = shipment.DesShCustId is { } shId ? await repo.GetCustomerAsync(shId, ct) : null;
                var items = await repo.GetPackingItemsAsync(packingList, ct);
                return Results.Content(HtmlDocuments.BillOfLading(shipment, carrier, customer, shipTo, items), "text/html; charset=utf-8");
            })
           .WithName("BolDocument").WithTags("Documents")
           .WithSummary("Printable bill of lading for a shipment — ship-from / ship-to / carrier + the freight summary (handling units + total net/gross weight) + signature lines.")
           .Produces(StatusCodes.Status200OK, contentType: "text/html").Produces(StatusCodes.Status404NotFound);

        api.MapPost("/sheet-skids", async (SheetSkidWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // A finished sheet skid must hang off a job that resolves to an order — legacy
                // refuses a job number with no order ("Can not find order number from job number!!",
                // w_wh_business:831). Rejects both a phantom job and a job with no order line.
                if (await repo.GetJobAsync(body.AbJobNum, ct) is not { OrderAbcNum: > 0 })
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["abJobNum"] = ["abJobNum must reference an existing job that belongs to an order."],
                    });
                var created = await repo.CreateSheetSkidAsync(body, ct);
                return Results.Created($"/api/sheet-skids/{created.SheetSkidNum}", created);
            })
           .WithName("CreateSheetSkid").WithTags("Skids")
           .WithSummary("Create a finished sheet skid (its job must belong to an order).")
           .Produces<SheetSkid>(StatusCodes.Status201Created).ProducesValidationProblem();

        // Warehouse-side update of a finished sheet skid (the legacy w_wh_* windows):
        // location / warehouse ticket / status. Partial — only non-null fields apply.
        api.MapPatch("/sheet-skids/{sheetSkidNum:long}/warehouse", async (long sheetSkidNum, SheetSkidWarehousePatch body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // Legacy w_inv_skid:1096 — a shipped skid (skid_sheet_status 0 = GONE) is terminal:
                // "shipped to customer already, no change can be made on it anymore."
                if (await repo.GetSheetSkidAsync(sheetSkidNum, ct) is { SkidSheetStatus: 0 })
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Skid shipped",
                        detail: $"Sheet skid {sheetSkidNum} has shipped (status GONE) and cannot be modified.");
                return await repo.UpdateSheetSkidWarehouseAsync(sheetSkidNum, body, ct) is { } updated
                    ? Results.Ok(updated)
                    : Results.NotFound();
            })
           .WithName("UpdateSheetSkidWarehouse").WithTags("Warehouse")
           .WithSummary("Warehouse update of a sheet skid (location / ticket / status; 409 if the skid has shipped).")
           .Produces<SheetSkid>().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        // ---- Accounting / Invoicing -------------------------------------
        // The rejected/rebanded coils that drive a job's invoice (legacy w_invoice).
        api.MapGet("/accounting/rej-reband-coils", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetInvoiceCoilsAsync(abJobNum, ct)))
           .WithName("GetInvoiceCoils").WithTags("Accounting")
           .WithSummary("Rejected (3) / rebanded (7) coils for a job's invoice, each with its exact billed weight.")
           .Produces<IReadOnlyList<InvoiceCoil>>();

        // The computed invoice for a job: header + spec + every weight bucket
        // (net/unapplied/rejected/rebanded/processed/scrap/tare/offal & %). The rejected/rebanded
        // figures use the exact legacy MAX billed-weight rule, not the naive process_end_wt sum.
        api.MapGet("/accounting/invoices/{abJobNum:long}/computation", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetInvoiceComputationAsync(abJobNum, ct) is { } comp
                    ? Results.Ok(comp)
                    : Results.NotFound())
           .WithName("GetInvoiceComputation").WithTags("Accounting")
           .WithSummary("Computed invoice for a job (weight buckets + spec) with exact rejected-coil billing.")
           .Produces<InvoiceComputation>().Produces(StatusCodes.Status404NotFound);

        // Saved invoice records for a job (legacy w_invoice Save).
        api.MapGet("/accounting/invoices", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetInvoicesAsync(abJobNum, ct)))
           .WithName("GetInvoices").WithTags("Accounting")
           .WithSummary("Saved invoice records for a job.")
           .Produces<IReadOnlyList<Invoice>>();

        api.MapGet("/accounting/invoices/{abJobNum:long}/{invoiceNum}", async (long abJobNum, string invoiceNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetInvoiceAsync(abJobNum, invoiceNum, ct) is { } inv
                    ? Results.Ok(inv)
                    : Results.NotFound())
           .WithName("GetInvoice").WithTags("Accounting")
           .WithSummary("Get one saved invoice by job + invoice number.")
           .Produces<Invoice>().Produces(StatusCodes.Status404NotFound);

        // Save an invoice (number + date + notes). 404 unknown job; 409 duplicate (ab_job_num, invoice_num).
        api.MapPost("/accounting/invoices", async (InvoiceWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var result = await repo.CreateInvoiceAsync(body, ct);
                return result.Outcome switch
                {
                    InvoiceSaveOutcome.Created => Results.Created(
                        $"/api/accounting/invoices/{result.Invoice!.AbJobNum}/{Uri.EscapeDataString(result.Invoice.InvoiceNum)}", result.Invoice),
                    InvoiceSaveOutcome.JobNotFound => Results.Problem(statusCode: StatusCodes.Status404NotFound,
                        title: "Job not found", detail: $"No job with ab_job_num {body.AbJobNum}."),
                    InvoiceSaveOutcome.Duplicate => Results.Problem(statusCode: StatusCodes.Status409Conflict,
                        title: "Invoice exists", detail: $"Invoice '{body.InvoiceNum?.Trim()}' already exists for job {body.AbJobNum}."),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError),
                };
            })
           .WithName("CreateInvoice").WithTags("Accounting")
           .WithSummary("Save an invoice record (number + date + notes) for a job.")
           .Produces<Invoice>(StatusCodes.Status201Created).ProducesValidationProblem()
           .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);

        // ---- Reporting (daily production) -------------------------------
        // Per-line production roll-up over an optional date window (by job start).
        api.MapGet("/reporting/production-summary", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetProductionSummaryAsync(f, t, ct));
            })
           .WithName("GetProductionSummary").WithTags("Reporting")
           .WithSummary("Per-line production summary (job count, avg yield, processed weight). Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<ProductionSummaryRow>>();

        api.MapGet("/reporting/line-efficiency", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetLineEfficiencyAsync(f, t, ct));
            })
           .WithName("GetLineEfficiency").WithTags("Reporting")
           .WithSummary("Per-line efficiency: jobs, processed weight, avg yield, and downtime. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<LineEfficiencyRow>>();

        api.MapGet("/reporting/monthly-production", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetMonthlyProductionAsync(f, t, ct));
            })
           .WithName("GetMonthlyProduction").WithTags("Reporting")
           .WithSummary("Production rolled up by month (YYYY-MM): jobs touched + processed weight. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<MonthlyProductionRow>>();

        api.MapGet("/reporting/shift-production", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct, long? lineNum = null) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetShiftProductionAsync(f, t, lineNum, ct));
            })
           .WithName("GetShiftProduction").WithTags("Reporting")
           .WithSummary("Per-line, per-day processed weight from shift coils (SUM shift_coil.process_wt via SHIFT), optionally one line. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<ShiftProductionRow>>();

        api.MapGet("/reporting/downtime-by-cause", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct, long? lineNum = null) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetDowntimeByCauseAsync(f, t, lineNum, ct));
            })
           .WithName("GetDowntimeByCause").WithTags("Reporting")
           .WithSummary("Downtime minutes by cause code (SUM dt_instance_detail.duration/60 via dt_instance), optionally one line. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<DowntimeByCauseRow>>();

        // ---- Calculator (legacy w_order_entry suggested piece weight) ----
        api.MapPost("/calculator/piece-weight", async (PieceWeightRequest body, IAbisRepository repo, CancellationToken ct) =>
            {
                var e = new Dictionary<string, string[]>();
                if (string.IsNullOrWhiteSpace(body.ShapeType)) e["shapeType"] = ["shapeType is required."];
                if (body.Gauge is not > 0m) e["gauge"] = ["gauge is required and must be greater than zero."];
                // Density: an explicit value wins; otherwise look it up by alloy in METAL_DENSITY.
                var density = body.Density;
                if (density is null && !string.IsNullOrWhiteSpace(body.Alloy))
                    density = await repo.GetMetalDensityAsync(body.Alloy!.Trim(), ct);
                if (density is not > 0m)
                    e["density"] = ["Provide a positive density, or an alloy present in METAL_DENSITY."];
                decimal? area = null;
                if (!string.IsNullOrWhiteSpace(body.ShapeType) && BlankArea(body.ShapeType!, body) is var (a, areaErr))
                {
                    area = a;
                    if (areaErr is not null) e["dimensions"] = [areaErr];
                }
                if (e.Count > 0) return Results.ValidationProblem(e);

                var pieceWeight = Math.Round(area!.Value * body.Gauge!.Value * density!.Value, 4);
                int? piecesPerSkid = body.MaxSkidWt is > 0 && pieceWeight > 0m ? (int)(body.MaxSkidWt.Value / pieceWeight) : null;
                return Results.Ok(new PieceWeightResult
                {
                    ShapeType = body.ShapeType, Area = Math.Round(area.Value, 4), Gauge = body.Gauge.Value,
                    Density = density.Value, PieceWeight = pieceWeight, PiecesPerSkid = piecesPerSkid,
                });
            })
           .WithName("CalculatePieceWeight").WithTags("Calculator")
           .WithSummary("Piece-weight calculator: blank area (by shape) × gauge × alloy density (from METAL_DENSITY, or an explicit density). Optionally returns pieces per skid for a max skid weight.")
           .Produces<PieceWeightResult>().ProducesValidationProblem();

        // ---- Recovery (legacy w_recovery scrap/reband worksheet) ----
        api.MapGet("/recovery/jobs/{abJobNum:long}/coils", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetRecoveryCoilsByJobAsync(abJobNum, ct)))
           .WithName("GetRecoveryCoils").WithTags("Recovery")
           .WithSummary("A job's recovery-worksheet coils: reband / reject / special-attention / special-handling flags + product type.")
           .Produces<IReadOnlyList<RecoveryJobCoil>>();

        api.MapPut("/recovery/jobs/{abJobNum:long}/coils/{coilAbcNum:long}", async (long abJobNum, long coilAbcNum, RecoveryJobCoilWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                var e = new Dictionary<string, string[]>();
                // Flags are NUMBER(1,0) — only 0/1 (or null) are meaningful.
                if (body.SpecialAttention is not (null or 0 or 1)) e["specialAttention"] = ["specialAttention must be 0 or 1."];
                if (body.SpecialHandling is not (null or 0 or 1)) e["specialHandling"] = ["specialHandling must be 0 or 1."];
                if (body.CoilRejected is not (null or 0 or 1)) e["coilRejected"] = ["coilRejected must be 0 or 1."];
                if (body.CoilRebanded is not (null or 0 or 1)) e["coilRebanded"] = ["coilRebanded must be 0 or 1."];
                if (e.Count > 0) return Results.ValidationProblem(e);
                // The recovery record hangs off a processed coil — (coil, job) must exist in
                // process_coil (FK); return a clean 404 rather than an ORA-02291 500.
                if (!await repo.ProcessCoilExistsAsync(coilAbcNum, abJobNum, ct))
                    return Results.NotFound(new { message = $"Coil {coilAbcNum} was not processed on job {abJobNum}." });
                if (body.ProductTypeId is > 0 && !await repo.ProductTypeExistsAsync(body.ProductTypeId.Value, ct))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["productTypeId"] = ["productTypeId must reference an existing product type."] });
                var saved = await repo.UpsertRecoveryJobCoilAsync(coilAbcNum, abJobNum, body, ct);
                return Results.Ok(saved);
            })
           .WithName("UpsertRecoveryCoil").WithTags("Recovery")
           .WithSummary("Set a coil's recovery-worksheet flags for a job (upsert). The coil must have been processed on the job.")
           .Produces<RecoveryJobCoil>().Produces(StatusCodes.Status404NotFound).ProducesValidationProblem();

        api.MapGet("/recovery/jobs/{abJobNum:long}/report", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetRecoveryReportAsync(abJobNum, ct)))
           .WithName("GetRecoveryReport").WithTags("Recovery")
           .WithSummary("Daily recovery report for a job: each recovery coil's ship / scrap / rejected weights and yield. Weights come from the live f_get_coil_* functions on Oracle.")
           .Produces<IReadOnlyList<RecoveryReportRow>>();

        api.MapGet("/recovery/jobs/{abJobNum:long}/scrap-by-defect", async (long abJobNum, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetRecoveryScrapByDefectAsync(abJobNum, ct)))
           .WithName("GetRecoveryScrapByDefect").WithTags("Recovery")
           .WithSummary("A job's recovery scrap broken down by defect type (Pareto order, heaviest first) with each defect's share of the total.")
           .Produces<IReadOnlyList<RecoveryScrapDefectRow>>();

        // ---- Admin: scheduled-job registry (docs/ADMIN_SUBSYSTEM_PLAN.md #6). INERT by design —
        // these endpoints only store/read job DEFINITIONS. There is NO execution engine in this
        // phase, so nothing here schedules or fires anything; the legacy db01 crontab stays the
        // sole live owner until a single-owner cutover (see the no-live-firing guardrail). Gated on
        // the "Scheduler Admin" feature: reads need ReadOnly (0), mutations need Write (1). ----
        const string SchedFeature = "Scheduler Admin";

        api.MapGet("/admin/jobs", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, SchedFeature, 0, ct) is { } deny ? deny
                    : Results.Ok(await repo.GetScheduledJobsAsync(ct)))
           .WithName("GetScheduledJobs").WithTags("Admin")
           .WithSummary("List the admin scheduled-job definitions. INERT — no execution engine runs them in this phase.")
           .Produces<IReadOnlyList<ScheduledJob>>();

        api.MapGet("/admin/jobs/{id:long}", async (long id, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 0, ct) is { } deny) return deny;
                var job = await repo.GetScheduledJobAsync(id, ct);
                return job is null ? Results.NotFound() : Results.Ok(job);
            })
           .WithName("GetScheduledJob").WithTags("Admin").Produces<ScheduledJob>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/admin/jobs", async (ScheduledJobWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 1, ct) is { } deny) return deny;
                if (ValidateScheduledJob(body) is { } e) return Results.ValidationProblem(e);
                if (await repo.ScheduledJobNameExistsAsync(body.JobName!.Trim(), null, ct))
                    return Results.Conflict(new { message = $"A scheduled job named '{body.JobName!.Trim()}' already exists." });
                var created = await repo.CreateScheduledJobAsync(body, ct);
                return Results.Created($"/api/admin/jobs/{created.ScheduledJobId}", created);
            })
           .WithName("CreateScheduledJob").WithTags("Admin")
           .WithSummary("Define a scheduled job (imported or native). Storing it does NOT schedule or run anything — there is no execution engine in this phase.")
           .Produces<ScheduledJob>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status409Conflict);

        api.MapPut("/admin/jobs/{id:long}", async (long id, ScheduledJobWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 1, ct) is { } deny) return deny;
                if (ValidateScheduledJob(body) is { } e) return Results.ValidationProblem(e);
                if (await repo.ScheduledJobNameExistsAsync(body.JobName!.Trim(), id, ct))
                    return Results.Conflict(new { message = $"A scheduled job named '{body.JobName!.Trim()}' already exists." });
                var updated = await repo.UpdateScheduledJobAsync(id, body, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
           .WithName("UpdateScheduledJob").WithTags("Admin")
           .Produces<ScheduledJob>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict);

        api.MapPost("/admin/jobs/{id:long}/enable", async (long id, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 1, ct) is { } deny) return deny;
                var job = await repo.SetScheduledJobEnabledAsync(id, true, ct);
                return job is null ? Results.NotFound() : Results.Ok(job);
            })
           .WithName("EnableScheduledJob").WithTags("Admin")
           .WithSummary("Set a job's enabled flag on. NOTE: the flag is stored only — it does not cause execution in this phase.")
           .Produces<ScheduledJob>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/admin/jobs/{id:long}/disable", async (long id, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 1, ct) is { } deny) return deny;
                var job = await repo.SetScheduledJobEnabledAsync(id, false, ct);
                return job is null ? Results.NotFound() : Results.Ok(job);
            })
           .WithName("DisableScheduledJob").WithTags("Admin").Produces<ScheduledJob>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/admin/jobs/{id:long}/runs", async (long id, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, SchedFeature, 0, ct) is { } deny) return deny;
                if (await repo.GetScheduledJobAsync(id, ct) is null) return Results.NotFound();
                return Results.Ok(await repo.GetScheduledJobRunsAsync(id, ct));
            })
           .WithName("GetScheduledJobRuns").WithTags("Admin")
           .WithSummary("A job's run history. Empty until a future execution engine records runs.")
           .Produces<IReadOnlyList<ScheduledJobRun>>().Produces(StatusCodes.Status404NotFound);

        // ---- Admin: EDI setup config (docs/ADMIN_SUBSYSTEM_PLAN.md #8 setup UI). Manages the
        // trading-partner / transaction-type config that is hand-maintained in DB tables today. This
        // is CONFIG ONLY — generation + VAN transport remain stubbed/absent, so nothing is generated
        // or transmitted; the legacy db01 EDI crontab stays the sole live owner (no-live-firing
        // guardrail). Gated on the existing "EDI" feature (reads elsewhere; writes need Write). ----
        const string EdiFeature = "EDI";

        api.MapPost("/admin/edi/types", async (EdiTypeWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                if (ValidateEdiType(body) is { } e) return Results.ValidationProblem(e);
                if (await repo.EdiTypeExistsAsync(body.EdiTypeId, body.EdiVersion!.Trim(), ct))
                    return Results.Conflict(new { message = $"EDI type {body.EdiTypeId}/{body.EdiVersion!.Trim()} already exists." });
                var created = await repo.CreateEdiTypeAsync(body, ct);
                return Results.Created($"/api/admin/edi/types/{created.EdiTypeId}/{created.EdiVersion}", created);
            })
           .WithName("CreateEdiType").WithTags("Admin")
           .WithSummary("Define an EDI transaction type + version (config only — transmits nothing).")
           .Produces<EdiType>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status409Conflict);

        api.MapPut("/admin/edi/types/{ediTypeId:int}/{ediVersion}", async (int ediTypeId, string ediVersion, EdiTypeWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                if (body.EdiTypeDescription is { Length: > 255 })
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["ediTypeDescription"] = ["ediTypeDescription must be 255 characters or fewer."] });
                var updated = await repo.UpdateEdiTypeDescriptionAsync(ediTypeId, ediVersion, body.EdiTypeDescription, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
           .WithName("UpdateEdiType").WithTags("Admin")
           .WithSummary("Update an EDI type's description.")
           .Produces<EdiType>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/admin/edi/customer-routes", async (CustomerEdiWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                if (await ValidateCustomerEdiAsync(body, repo, ct) is { } e) return Results.ValidationProblem(e);
                if (await repo.GetCustomerEdiOneAsync(body.CustomerEdiName!.Trim(), body.CustomerId, ct) is not null)
                    return Results.Conflict(new { message = $"EDI route '{body.CustomerEdiName!.Trim()}' for customer {body.CustomerId} already exists." });
                var created = await repo.CreateCustomerEdiAsync(body, ct);
                return Results.Created($"/api/admin/edi/customer-routes/{created.CustomerId}/{created.CustomerEdiName}", created);
            })
           .WithName("CreateCustomerEdiRoute").WithTags("Admin")
           .WithSummary("Define a trading-partner EDI route (config only). Validates the customer + referenced EDI type exist.")
           .Produces<CustomerEdi>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status409Conflict);

        api.MapPut("/admin/edi/customer-routes/{customerId:long}/{customerEdiName}", async (long customerId, string customerEdiName, CustomerEdiWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                // Route keys come from the path; only the type/version/desc are mutable.
                body.CustomerEdiName = customerEdiName; body.CustomerId = customerId;
                if (await ValidateCustomerEdiTypeRefAsync(body, repo, ct) is { } e) return Results.ValidationProblem(e);
                var updated = await repo.UpdateCustomerEdiAsync(customerEdiName, customerId, body, ct);
                return updated is null ? Results.NotFound() : Results.Ok(updated);
            })
           .WithName("UpdateCustomerEdiRoute").WithTags("Admin")
           .Produces<CustomerEdi>().ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);

        api.MapDelete("/admin/edi/customer-routes/{customerId:long}/{customerEdiName}", async (long customerId, string customerEdiName, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                return await repo.DeleteCustomerEdiAsync(customerEdiName, customerId, ct)
                    ? Results.NoContent() : Results.NotFound();
            })
           .WithName("DeleteCustomerEdiRoute").WithTags("Admin")
           .WithSummary("Remove a trading-partner EDI route.")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        api.MapPut("/admin/edi/customers/{customerId:long}/861-flag", async (long customerId, Customer861FlagWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                var flag = body.Create861AtReceiving?.Trim().ToUpperInvariant();
                if (flag is not ("Y" or "N"))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["create861AtReceiving"] = ["create861AtReceiving must be 'Y' or 'N'."] });
                return await repo.SetCustomer861FlagAsync(customerId, flag, ct)
                    ? Results.Ok(new { customerId, create861AtReceiving = flag })
                    : Results.NotFound();
            })
           .WithName("SetCustomer861Flag").WithTags("Admin")
           .WithSummary("Set a customer's 'create 861 at receiving' flag (Y/N). Config only — generates no EDI.")
           .Produces(StatusCodes.Status200OK).ProducesValidationProblem().Produces(StatusCodes.Status404NotFound);

        // ---- EDI trading-partner profiles (abis_edi_partner) — the per-(customer, document) config backbone ----
        api.MapPut("/admin/edi/partners/{customerId:long}/{transactionSet}", async (long customerId, string transactionSet, EdiPartnerWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                var set = transactionSet.Trim();
                if (set is not ("861" or "870" or "846" or "856" or "863"))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["transactionSet"] = ["transactionSet must be one of 861/870/846/856/863."] });
                var login = ResolveLogin(ctx);
                var saved = await repo.UpsertEdiPartnerAsync(new EdiPartnerProfile
                {
                    CustomerId = customerId, TransactionSet = set, Enabled = body.Enabled, Variant = body.Variant,
                    ReceiverQualifier = body.ReceiverQualifier, ReceiverId = body.ReceiverId,
                    ComponentSeparator = body.ComponentSeparator, SegmentSuffix = body.SegmentSuffix,
                    EnvelopeVersion = body.EnvelopeVersion, GsFunctionalCode = body.GsFunctionalCode,
                    GsSenderCode = body.GsSenderCode, GsReceiverCode = body.GsReceiverCode,
                    FilePrefix = body.FilePrefix, ItemReference = body.ItemReference,
                    UpdatedBy = login,
                }, ct);
                return Results.Ok(saved);
            })
           .WithName("UpsertEdiPartner").WithTags("Admin")
           .WithSummary("Create or update a customer's EDI trading-partner profile for a document (861/870/846/856/863). Config only — sets how the document is framed; generates/sends nothing.")
           .Produces<EdiPartnerProfile>().ProducesValidationProblem();

        api.MapDelete("/admin/edi/partners/{customerId:long}/{transactionSet}", async (long customerId, string transactionSet, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                return await repo.DeleteEdiPartnerAsync(customerId, transactionSet.Trim(), ct) ? Results.NoContent() : Results.NotFound();
            })
           .WithName("DeleteEdiPartner").WithTags("Admin")
           .WithSummary("Remove a customer's EDI trading-partner profile for a document.")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound);

        // Diagnostic: send a test email to confirm the SMTP path AND the test-recipient override. During the
        // test phase Email:OverrideRecipient redirects every email to one inbox, so ActualRecipients on the
        // response shows where it really went regardless of the To you pass.
        api.MapPost("/admin/email/test", async (EmailTestRequest body, HttpContext ctx, IAbisRepository repo, Abis.Api.Email.IEmailSender email, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, EdiFeature, 1, ct) is { } deny) return deny;
                var to = string.IsNullOrWhiteSpace(body.To) ? "someone@example.com" : body.To!.Trim();
                var r = await email.SendAsync(new Abis.Api.Email.EmailMessage(
                    new[] { to },
                    string.IsNullOrWhiteSpace(body.Subject) ? "ABIS test email" : body.Subject!.Trim(),
                    string.IsNullOrWhiteSpace(body.Body) ? "This is a test email from ABIS." : body.Body!), ct);
                return Results.Ok(new EmailTestResult { Sent = r.Sent, ActualRecipients = r.ActualRecipients.ToArray(), Detail = r.Detail });
            })
           .WithName("SendTestEmail").WithTags("Admin")
           .WithSummary("Send a test email — verifies SMTP + the global test-recipient override (all mail → Email:OverrideRecipient).")
           .Produces<EmailTestResult>();

        // #7 server/service console — view + safe restarts only (docs/SERVER_CONSOLE.md). Gated on the
        // "Server Admin" feature AND Admin:ServerConsole:Enabled (503 when disabled); the mutating restart
        // additionally needs AllowRestart + the sudoers allowlist. Units are validated against a fixed
        // allowlist so nothing user-supplied reaches systemctl.
        const string ConsoleFeature = "Server Admin";
        IResult ConsoleDisabled() => Results.Json(new { status = "server console disabled", hint = "set Admin:ServerConsole:Enabled=true" }, statusCode: StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/admin/console/services", async (HttpContext ctx, IAbisRepository repo, ServerConsoleService console, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, ConsoleFeature, 0, ct) is { } deny) return deny;
                if (!console.Enabled) return ConsoleDisabled();
                return Results.Ok(new { restartAllowed = console.RestartAllowed, services = await console.GetServicesAsync(ct) });
            })
           .WithName("GetServerServices").WithTags("Admin")
           .WithSummary("Server console: status of the allowlisted systemd units (abis, nginx). Read-only.")
           .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/admin/console/services/{unit}/logs", async (string unit, int? tail, HttpContext ctx, IAbisRepository repo, ServerConsoleService console, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, ConsoleFeature, 0, ct) is { } deny) return deny;
                if (!console.Enabled) return ConsoleDisabled();
                if (!console.IsAllowedUnit(unit)) return Results.NotFound(new { unit, status = "unit not in the allowlist" });
                var r = await console.GetLogsAsync(unit, tail ?? 200, ct);
                return Results.Ok(new { unit, ok = r.Ok, text = r.Ok ? r.Stdout : r.Stderr });
            })
           .WithName("GetServerServiceLogs").WithTags("Admin")
           .WithSummary("Server console: tail an allowlisted unit's journal (read-only). ?tail=N (clamped).")
           .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status503ServiceUnavailable);

        api.MapPost("/admin/console/services/{unit}/restart", async (string unit, HttpContext ctx, IAbisRepository repo, ServerConsoleService console, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, ConsoleFeature, 1, ct) is { } deny) return deny;
                if (!console.Enabled) return ConsoleDisabled();
                if (!console.IsAllowedUnit(unit)) return Results.NotFound(new { unit, status = "unit not in the allowlist" });
                var r = await console.RestartAsync(unit, ct);
                return r.Ok ? Results.Ok(r) : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Restart failed", detail: r.Detail);
            })
           .WithName("RestartServerService").WithTags("Admin")
           .WithSummary("Server console: restart an allowlisted unit (mutating — needs AllowRestart + the sudoers allowlist). 409 if not permitted / failed.")
           .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/admin/console/host/cron", async (HttpContext ctx, IAbisRepository repo, ServerConsoleService console, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, ConsoleFeature, 0, ct) is { } deny) return deny;
                if (!console.Enabled) return ConsoleDisabled();
                var r = await console.GetHostCronAsync(ct);
                return r.Available
                    ? Results.Ok(new { available = true, text = r.Text })
                    : Results.Json(new { available = false, error = r.Error }, statusCode: StatusCodes.Status503ServiceUnavailable);
            })
           .WithName("GetHostCron").WithTags("Admin")
           .WithSummary("Server console: read-only view of the DB-host crontab (via a command-locked channel). 503 until configured.")
           .Produces(StatusCodes.Status200OK).Produces(StatusCodes.Status403Forbidden).Produces(StatusCodes.Status503ServiceUnavailable);

        api.MapGet("/reporting/downtime", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct, long? lineNum = null) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetProductionDowntimeAsync(f, t, lineNum, ct));
            })
           .WithName("GetProductionDowntime").WithTags("Reporting")
           .WithSummary("Downtime events (optionally one line), with duration minutes. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<ProductionDowntimeRow>>();

        api.MapGet("/reporting/on-time", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetOnTimeDeliveryAsync(f, t, ct));
            })
           .WithName("GetOnTimeDelivery").WithTags("Reporting")
           .WithSummary("Per-line on-time delivery (jobs finished on/before due date). Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<OnTimeRow>>();

        api.MapGet("/reporting/customer-shipments", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetCustomerShipmentsAsync(f, t, ct));
            })
           .WithName("GetCustomerShipments").WithTags("Reporting")
           .WithSummary("Per-customer shipment roll-up (total / shipped / open + last ship date). Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<CustomerShipmentRow>>();

        api.MapGet("/reporting/open-shipments", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOpenShipmentsAsync(ct)))
           .WithName("GetOpenShipments").WithTags("Reporting")
           .WithSummary("Open (not-yet-sent) shipments with customer, carrier, and scheduled date.")
           .Produces<IReadOnlyList<OpenShipmentRow>>();

        api.MapGet("/reporting/customer-orders", async (IAbisRepository repo, CancellationToken ct, long? customerId = null) =>
                Results.Ok(await repo.GetCustomerOrdersReportAsync(customerId, ct)))
           .WithName("GetCustomerOrdersReport").WithTags("Reporting")
           .WithSummary("Customer orders with PO / sales-order references (optionally one customer).")
           .Produces<IReadOnlyList<CustomerOrderReportRow>>();

        api.MapGet("/reporting/customer-skid-count", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCustomerSkidCountsAsync(ct)))
           .WithName("GetCustomerSkidCount").WithTags("Reporting")
           .WithSummary("Per-customer finished sheet-skid counts + total net weight.")
           .Produces<IReadOnlyList<CustomerSkidCountRow>>();

        api.MapGet("/reporting/coil-inventory", async (IAbisRepository repo, CancellationToken ct, int? status = null) =>
                Results.Ok(await repo.GetCoilInventoryAsync(status, ct)))
           .WithName("GetCoilInventory").WithTags("Reporting")
           .WithSummary("Coil inventory by alloy: count + total net/balance weight (optional status filter).")
           .Produces<IReadOnlyList<CoilInventoryRow>>();

        api.MapGet("/reporting/coil-on-hold", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOnHoldCoilsAsync(ct)))
           .WithName("GetOnHoldCoils").WithTags("Reporting")
           .WithSummary("On-hold coils (coil_status = 3) with location, owner, and balance weight.")
           .Produces<IReadOnlyList<OnHoldCoilRow>>();

        api.MapGet("/reporting/skid-inventory", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSkidInventoryAsync(ct)))
           .WithName("GetSkidInventory").WithTags("Reporting")
           .WithSummary("Finished sheet-skid inventory by status: count + total net weight.")
           .Produces<IReadOnlyList<SkidInventoryRow>>();

        api.MapGet("/reporting/unmatched-coils", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetUnmatchedCoilsAsync(ct)))
           .WithName("GetUnmatchedCoils").WithTags("Reporting")
           .WithSummary("Coils not referenced by any process_coil — orphan / unmatched inventory.")
           .Produces<IReadOnlyList<UnmatchedCoilRow>>();

        api.MapGet("/reporting/qa-mechanical", async (DateTime? from, DateTime? to, IAbisRepository repo, CancellationToken ct) =>
            {
                var (f, t) = ResolveReportWindow(from, to);
                return Results.Ok(await repo.GetQaMechanicalAsync(f, t, ct));
            })
           .WithName("GetQaMechanical").WithTags("Reporting")
           .WithSummary("Mechanical test results by test type: count + average YTS/UTS/elongation. Defaults to the last 365 days when unbounded.")
           .Produces<IReadOnlyList<QaMechanicalRow>>();

        api.MapGet("/reporting/scrap-summary", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetScrapSummaryAsync(ct)))
           .WithName("GetScrapSummary").WithTags("Reporting")
           .WithSummary("Scrap by type (code/defect) with skid count + total net weight.")
           .Produces<IReadOnlyList<ScrapSummaryRow>>();

        api.MapGet("/reporting/scrap-by-job", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetScrapByJobAsync(ct)))
           .WithName("GetScrapByJob").WithTags("Reporting")
           .WithSummary("Scrap by job: skid count + total net weight.")
           .Produces<IReadOnlyList<ScrapByJobRow>>();

        api.MapGet("/reporting/production-order", async (IAbisRepository repo, CancellationToken ct,
                long? abJobNum = null, long? orderAbcNum = null, long? customerId = null, DateTime? from = null, DateTime? to = null) =>
            {
                // A scoped report (the job traveler) — require at least one scope so it never
                // dumps or full-scans every job in ab_job.
                if (abJobNum is null && orderAbcNum is null && customerId is null && from is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["filter"] = ["Provide at least one scope: abJobNum, orderAbcNum, customerId, or from (date)."],
                    });
                return Results.Ok(await repo.GetProductionOrderReportAsync(abJobNum, orderAbcNum, customerId, from, to, ct));
            })
           .WithName("GetProductionOrderReport").WithTags("Reporting")
           .WithSummary("Production-order report (job traveler): per-job header + customer / order / order-line specs. Requires a scope filter (job, order, customer, or date).")
           .Produces<IReadOnlyList<ProductionOrderReportRow>>().ProducesValidationProblem();

        api.MapGet("/reporting/customer-skid-inventory", async (IAbisRepository repo, CancellationToken ct,
                long? customerId = null, int? status = null) =>
            {
                // Customer-scoped (sheet_skid has no customer column; resolved via the job/order join).
                // Require customerId so it never full-scans every skid.
                if (customerId is null or <= 0)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["customerId"] = ["customerId is required."],
                    });
                return Results.Ok(await repo.GetCustomerSkidInventoryAsync(customerId.Value, status, ct));
            })
           .WithName("GetCustomerSkidInventory").WithTags("Reporting")
           .WithSummary("A customer's finished sheet-skid inventory (via job → order), with optional skid status filter. Requires customerId.")
           .Produces<IReadOnlyList<CustomerSkidInventoryRow>>().ProducesValidationProblem();

        // ---- Quality / Recovery (customer-defect setup) -----------------
        api.MapGet("/quality/scrap-types", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetScrapTypesAsync(ct)))
           .WithName("GetScrapTypes").WithTags("Quality")
           .WithSummary("The scrap/defect type catalog.").Produces<IReadOnlyList<ScrapType>>();

        api.MapGet("/quality/product-types", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetProductTypesAsync(ct)))
           .WithName("GetProductTypes").WithTags("Quality")
           .WithSummary("The product-type lookup.").Produces<IReadOnlyList<ProductType>>();

        api.MapGet("/quality/recovery-customers", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetRecoveryCustomersAsync(ct)))
           .WithName("GetRecoveryCustomers").WithTags("Quality")
           .WithSummary("Customers configured for recovery reporting.").Produces<IReadOnlyList<RecoveryCustomer>>();

        api.MapGet("/quality/customer-defects", async (long customerId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCustomerDefectsAsync(customerId, ct)))
           .WithName("GetCustomerDefects").WithTags("Quality")
           .WithSummary("The scrap/defect types a customer tracks.").Produces<IReadOnlyList<CustomerDefect>>();

        // ---- OPC log (legacy w_opc_log) ---------------------------------
        api.MapGet("/opc-log/logs", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOpcLogsAsync(ct)))
           .WithName("GetOpcLogs").WithTags("OpcLog")
           .WithSummary("OPC log sessions.").Produces<IReadOnlyList<OpcLog>>();

        api.MapGet("/opc-log/{opcLogId:long}/details", async (long opcLogId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOpcLogDetailsAsync(opcLogId, ct)))
           .WithName("GetOpcLogDetails").WithTags("OpcLog")
           .WithSummary("Captured OPC readings (host/device/item/value/quality) for a log.").Produces<IReadOnlyList<OpcLogDetail>>();

        api.MapGet("/opc-log/items", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetOpcItemsAsync(ct)))
           .WithName("GetOpcItems").WithTags("OpcLog")
           .WithSummary("The distinct OPC item names seen — the real tag catalog (informs Edge:Opc:Tags).").Produces<IReadOnlyList<string>>();

        // ---- Sales / quotes (legacy w_sales_main, w_new_quote, w_edit_quote) ----
        api.MapGet("/sales/quotes", async (IAbisRepository repo, CancellationToken ct, string? search = null) =>
                Results.Ok(await repo.GetSalesQuotesAsync(search, ct)))
           .WithName("GetSalesQuotes").WithTags("Sales")
           .WithSummary("Pending sales / quote list (customer, contact, latest win probability).")
           .Produces<IReadOnlyList<SalesQuoteListRow>>();

        api.MapGet("/sales/quotes/{quoteId:long}/{revisionId:long}", async (long quoteId, long revisionId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetSalesQuoteAsync(quoteId, revisionId, ct) is { } q ? Results.Ok(q) : Results.NotFound())
           .WithName("GetSalesQuote").WithTags("Sales")
           .WithSummary("A quote header (a specific revision of a quote).")
           .Produces<SalesQuote>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/sales/quotes", async (SalesQuoteWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                // A quote is for a customer; pre-check existence so a bad id is a clean 400, not an
                // ORA-02291 500 on Oracle.
                var errors = new Dictionary<string, string[]>();
                if (body.CustomerId is not > 0)
                    errors["customerId"] = ["customerId is required."];
                else if (await repo.GetCustomerAsync(body.CustomerId.Value, ct) is null)
                    errors["customerId"] = [$"customer {body.CustomerId} does not exist."];
                if (errors.Count > 0) return Results.ValidationProblem(errors);
                var created = await repo.CreateSalesQuoteAsync(body, ct);
                return Results.Created($"/api/sales/quotes/{created.QuoteId}/{created.QuoteRevisionId}", created);
            })
           .WithName("CreateSalesQuote").WithTags("Sales")
           .WithSummary("Create a new sales quote (revision 1).")
           .Produces<SalesQuote>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/sales/contacts", async (IAbisRepository repo, CancellationToken ct, long? customerId = null) =>
                Results.Ok(await repo.GetSalesContactsAsync(customerId, ct)))
           .WithName("GetSalesContacts").WithTags("Sales")
           .WithSummary("The sales contact address book (optionally filtered to a customer).")
           .Produces<IReadOnlyList<SalesContact>>();

        api.MapGet("/sales/quotes/{quoteId:long}/{revisionId:long}/events", async (long quoteId, long revisionId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSalesRemindersAsync(quoteId, revisionId, ct)))
           .WithName("GetSalesReminders").WithTags("Sales")
           .WithSummary("Scheduled follow-ups / reminders for a quote.")
           .Produces<IReadOnlyList<SalesReminder>>();

        api.MapPost("/sales/quotes/{quoteId:long}/{revisionId:long}/events", async (long quoteId, long revisionId, SalesReminderWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateSalesReminderAsync(quoteId, revisionId, body, ct);
                return Results.Created($"/api/sales/quotes/{quoteId}/{revisionId}/events/{created.EventId}", created);
            })
           .WithName("CreateSalesReminder").WithTags("Sales")
           .WithSummary("Log a follow-up / reminder against a quote.")
           .Produces<SalesReminder>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/sales/quotes/{quoteId:long}/{revisionId:long}/probability", async (long quoteId, long revisionId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSalesProbabilityAsync(quoteId, revisionId, ct)))
           .WithName("GetSalesProbability").WithTags("Sales")
           .WithSummary("Win-probability review history for a quote.")
           .Produces<IReadOnlyList<SalesProbability>>();

        api.MapPost("/sales/quotes/{quoteId:long}/{revisionId:long}/probability", async (long quoteId, long revisionId, SalesProbabilityWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateSalesProbabilityAsync(quoteId, revisionId, body, ct);
                return Results.Created($"/api/sales/quotes/{quoteId}/{revisionId}/probability/{created.ProbabilityId}", created);
            })
           .WithName("CreateSalesProbability").WithTags("Sales")
           .WithSummary("Record a win-probability review on a quote.")
           .Produces<SalesProbability>(StatusCodes.Status201Created).ProducesValidationProblem();

        // ---- Coil ownership transfer (legacy w_coil_ownership_transfer) ----
        api.MapGet("/coil-ownership/transfers", async (IAbisRepository repo, CancellationToken ct, long? customerId = null) =>
                Results.Ok(await repo.GetCoilOwnershipTransfersAsync(customerId, ct)))
           .WithName("GetCoilOwnershipTransfers").WithTags("CoilOwnership")
           .WithSummary("The coil-ownership transfer ledger (optionally scoped to a customer).")
           .Produces<IReadOnlyList<CoilOwnershipTransfer>>();

        api.MapGet("/coil-ownership/transfers/{certificateNum:long}/certificate", async (long certificateNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetCoilOwnershipTransferCertificateAsync(certificateNum, ct) is { } cert ? Results.Ok(cert) : Results.NotFound())
           .WithName("GetCoilOwnershipTransferCertificate").WithTags("CoilOwnership")
           .WithSummary("The printable transfer certificate (full customer addresses + coil details).")
           .Produces<CoilOwnershipTransferCertificate>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/coil-ownership/transferable-coils", async (IAbisRepository repo, CancellationToken ct, long? customerId = null, string? search = null) =>
                Results.Ok(await repo.GetTransferableCoilsAsync(customerId, search, ct)))
           .WithName("GetTransferableCoils").WithTags("CoilOwnership")
           .WithSummary("Coils eligible to transfer, with their current owner (the coil picker).")
           .Produces<IReadOnlyList<TransferableCoil>>();

        api.MapPost("/coil-ownership/transfers", async (CoilOwnershipTransferWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                // A transfer to the coil's *current* owner changes nothing but would still mint a
                // certificate and write coil_from_cust_id = customer_id — reject the no-op. (Legacy
                // required a new customer but didn't check it differed from the current owner.)
                if (body.CoilAbcNumOrig is { } coilId && await repo.GetCoilAsync(coilId, ct) is { CustomerId: { } owner }
                    && owner == body.CustomerIdNew)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "No ownership change",
                        detail: $"Coil {coilId} is already owned by customer {body.CustomerIdNew}; nothing to transfer.");
                // Provenance: stamp the certificate's performed-by from the authenticated principal
                // (legacy used sqlca.logid), so an OIDC end-user can't spoof it. An API-key service
                // account has no login, so it keeps the body value.
                body.TransferPerformedBy = ResolveLogin(ctx) ?? body.TransferPerformedBy;
                var created = await repo.CreateCoilOwnershipTransferAsync(body, ct);
                return created is null
                    ? Results.NotFound(new { message = $"Coil {body.CoilAbcNumOrig} not found." })
                    : Results.Created($"/api/coil-ownership/transfers/{created.CertificateNum}/certificate", created);
            })
           .WithName("CreateCoilOwnershipTransfer").WithTags("CoilOwnership")
           .WithSummary("Record a coil-ownership transfer: issues a certificate, MINTS a new coil for the new owner (status New, carrying the original's attributes) and marks the original coil Transferred — it does not mutate ownership in place. The new coil_abc_num is server-assigned. 409 if the new owner already owns the coil.")
           .Produces<CoilOwnershipTransfer>(StatusCodes.Status201Created)
           .Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).ProducesValidationProblem();

        // ---- Security / authorization (legacy security.pbl) ----
        api.MapGet("/security/users", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSecurityUsersAsync(ct)))
           .WithName("GetSecurityUsers").WithTags("Security")
           .WithSummary("The application user roster.").Produces<IReadOnlyList<SecurityUser>>();

        api.MapGet("/security/users/{userId:long}", async (long userId, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetSecurityUserAsync(userId, ct) is { } u ? Results.Ok(u) : Results.NotFound())
           .WithName("GetSecurityUser").WithTags("Security")
           .WithSummary("One application user.").Produces<SecurityUser>().Produces(StatusCodes.Status404NotFound);

        api.MapGet("/security/users/{userId:long}/groups", async (long userId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetUserGroupsAsync(userId, ct)))
           .WithName("GetUserGroups").WithTags("Security")
           .WithSummary("The groups a user belongs to.").Produces<IReadOnlyList<SecurityGroup>>();

        api.MapGet("/security/users/{userId:long}/permissions", async (long userId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetUserEffectivePermissionsAsync(userId, ct)))
           .WithName("GetUserEffectivePermissions").WithTags("Security")
           .WithSummary("A user's effective per-feature permissions (MAX of direct + group grants).")
           .Produces<IReadOnlyList<EffectivePermission>>();

        // The CALLER's effective permissions — resolves the OIDC login (or X-User-Login dev
        // header) to a security_user. Empty when the caller is a service account / unknown.
        api.MapGet("/security/me/permissions", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                var login = ResolveLogin(ctx);
                if (login is null) return Results.Ok(Array.Empty<EffectivePermission>());
                var u = await repo.GetSecurityUserByLoginAsync(login, ct);
                return Results.Ok(u is null
                    ? Array.Empty<EffectivePermission>()
                    : (await repo.GetUserEffectivePermissionsAsync(u.UserId, ct)).ToArray());
            })
           .WithName("GetMyPermissions").WithTags("Security")
           .WithSummary("The calling user's effective permissions (resolved from the OIDC login / X-User-Login).")
           .Produces<IReadOnlyList<EffectivePermission>>();

        // Whether the caller is allowed a feature at a level — exposes the gate for the UI
        // to drive enable/read-only/hide decisions (server remains the source of truth).
        api.MapGet("/security/me/allowed", async (HttpContext ctx, IAbisRepository repo, CancellationToken ct, string feature = "", int level = 1) =>
                Results.Ok(new FeatureAllowedResult { Feature = feature, Level = level, Allowed = await RequireFeatureAsync(ctx, repo, feature, level, ct) is null }))
           .WithName("GetMyAllowed").WithTags("Security")
           .WithSummary("Whether the caller has at least the given privilege on a feature.")
           .Produces<FeatureAllowedResult>();

        api.MapGet("/security/groups", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSecurityGroupsAsync(ct)))
           .WithName("GetSecurityGroups").WithTags("Security")
           .WithSummary("The security groups / roles.").Produces<IReadOnlyList<SecurityGroup>>();

        api.MapGet("/security/applications", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetSecurityApplicationsAsync(ct)))
           .WithName("GetSecurityApplications").WithTags("Security")
           .WithSummary("The protected feature catalog.").Produces<IReadOnlyList<SecurityApplication>>();

        // The security-admin writes are gated by the "User Control" feature (Write). An
        // API-key service account bypasses (login null); a real OIDC user must hold the grant.
        api.MapPost("/security/users", async (SecurityUserWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                if (string.IsNullOrWhiteSpace(body.LoginId))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["loginId"] = ["loginId is required."] });
                // A user must carry a name (legacy w_user_new:120 "No user name entered!" — first OR last).
                if (string.IsNullOrWhiteSpace(body.UserFirstName) && string.IsNullOrWhiteSpace(body.UserLastName))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["a first or last name is required."] });
                // login_id must be unique (legacy w_user_new:111 "Duplicated user login name!").
                // A case-insensitive dup would make GetSecurityUserByLoginAsync (QuerySingleOrDefault
                // + LOWER) throw and GetEffectivePrivilege MAX across two rows — the auth bridge.
                if (await repo.GetSecurityUserByLoginAsync(body.LoginId, ct) is not null)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Duplicate login",
                        detail: $"A user with login '{body.LoginId}' already exists.");
                var created = await repo.CreateSecurityUserAsync(body, ct);
                return Results.Created($"/api/security/users/{created.UserId}", created);
            })
           .WithName("CreateSecurityUser").WithTags("Security")
           .WithSummary("Create an application user (requires User Control; 409 on a duplicate login).").Produces<SecurityUser>(StatusCodes.Status201Created).ProducesValidationProblem().Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status403Forbidden);

        // An administrator sets/resets a user's initial password (stored as a PBKDF2 hash in the
        // ABIS credential store; must_change=1 forces the user to change it on next sign-in).
        // Gated by "User Control" like the other security-admin writes.
        api.MapPost("/security/users/{userId:long}/password", async (long userId, SetPasswordRequest body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                var pw = body.Password ?? string.Empty;
                if (pw.Length is < 8 or > 100)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = ["Password must be 8–100 characters."] });
                var user = await repo.GetSecurityUserAsync(userId, ct);
                if (user is null || string.IsNullOrWhiteSpace(user.LoginId)) return Results.NotFound();
                await repo.SetUserCredentialAsync(user.LoginId, PasswordHashing.Hash(pw), mustChange: true, updatedBy: ResolveLogin(ctx), ct);
                return Results.NoContent();
            })
           .WithName("SetUserPassword").WithTags("Security")
           .WithSummary("Set/reset a user's initial password (requires User Control; the user must change it on next sign-in).")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden).ProducesValidationProblem();

        // Edit an application user (name / status / notes; and login if it moves). "User Control" gated.
        api.MapPut("/security/users/{userId:long}", async (long userId, SecurityUserWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                if (string.IsNullOrWhiteSpace(body.LoginId))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["loginId"] = ["loginId is required."] });
                if (string.IsNullOrWhiteSpace(body.UserFirstName) && string.IsNullOrWhiteSpace(body.UserLastName))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["userName"] = ["a first or last name is required."] });
                // Guard a login rename against colliding with a different user.
                if (await repo.GetSecurityUserByLoginAsync(body.LoginId, ct) is { } dup && dup.UserId != userId)
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Duplicate login",
                        detail: $"Another user already uses login '{body.LoginId}'.");
                return await repo.UpdateSecurityUserAsync(userId, body, ct) ? Results.NoContent() : Results.NotFound();
            })
           .WithName("UpdateSecurityUser").WithTags("Security")
           .WithSummary("Edit an application user's login/name/status (requires User Control; 409 on a colliding login).")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status403Forbidden).ProducesValidationProblem();

        // Remove an application user + their grants, group memberships, and password credential.
        // (Prefer setting status inactive via PUT if you want to keep the record.) "User Control" gated.
        api.MapDelete("/security/users/{userId:long}", async (long userId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.DeleteSecurityUserAsync(userId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeleteSecurityUser").WithTags("Security")
           .WithSummary("Remove an application user and their grants/groups/credential (requires User Control).")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        api.MapPost("/security/groups", async (SecurityGroupWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : Results.Created("/api/security/groups", await repo.CreateSecurityGroupAsync(body, ct)))
           .WithName("CreateSecurityGroup").WithTags("Security")
           .WithSummary("Create a security group (requires User Control).").Produces<SecurityGroup>(StatusCodes.Status201Created).Produces(StatusCodes.Status403Forbidden);

        api.MapPost("/security/applications", async (SecurityApplicationWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : Results.Created("/api/security/applications", await repo.CreateSecurityApplicationAsync(body, ct)))
           .WithName("CreateSecurityApplication").WithTags("Security")
           .WithSummary("Create a protected feature (requires User Control).").Produces<SecurityApplication>(StatusCodes.Status201Created).Produces(StatusCodes.Status403Forbidden);

        api.MapPut("/security/users/{userId:long}/applications/{applicationId:long}", async (long userId, long applicationId, GrantWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                // Privilege is the legacy security level: 0 = ReadOnly, 1 = Write (d_user_app).
                if (body.Privilege is not (null or 0 or 1))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["privilege"] = ["privilege must be 0 (ReadOnly) or 1 (Write)."] });
                return await repo.SetUserApplicationGrantAsync(userId, applicationId, body.Privilege ?? 0, ct)
                    ? Results.NoContent() : Results.NotFound();
            })
           .WithName("SetUserApplicationGrant").WithTags("Security")
           .WithSummary("Set a user's privilege on a feature (0 = ReadOnly, 1 = Write; requires User Control).")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden).ProducesValidationProblem();

        api.MapPut("/security/groups/{groupId:long}/applications/{applicationId:long}", async (long groupId, long applicationId, GrantWrite body, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                if (body.Privilege is not (null or 0 or 1))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["privilege"] = ["privilege must be 0 (ReadOnly) or 1 (Write)."] });
                return await repo.SetGroupApplicationGrantAsync(groupId, applicationId, body.Privilege ?? 0, ct)
                    ? Results.NoContent() : Results.NotFound();
            })
           .WithName("SetGroupApplicationGrant").WithTags("Security")
           .WithSummary("Set a group's privilege on a feature (0 = ReadOnly, 1 = Write; requires User Control).")
           .Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden).ProducesValidationProblem();

        api.MapPost("/security/users/{userId:long}/groups/{groupId:long}", async (long userId, long groupId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.AddUserToGroupAsync(userId, groupId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("AddUserToGroup").WithTags("Security")
           .WithSummary("Add a user to a group (requires User Control).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        api.MapDelete("/security/users/{userId:long}/groups/{groupId:long}", async (long userId, long groupId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.RemoveUserFromGroupAsync(userId, groupId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("RemoveUserFromGroup").WithTags("Security")
           .WithSummary("Remove a user from a group (requires User Control).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        // Clear a user's DIRECT grant on a feature (they may still inherit it via a group). This is
        // the "remove grant" the set-grant PUT can't express (that only writes 0/1). User Control gated.
        api.MapDelete("/security/users/{userId:long}/applications/{applicationId:long}", async (long userId, long applicationId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.DeleteUserApplicationGrantAsync(userId, applicationId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeleteUserApplicationGrant").WithTags("Security")
           .WithSummary("Clear a user's direct grant on a feature (requires User Control).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        // A group's own feature grants — the primary RBAC lever (most privilege is assigned to groups).
        api.MapGet("/security/groups/{groupId:long}/applications", async (long groupId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetGroupApplicationGrantsAsync(groupId, ct)))
           .WithName("GetGroupApplicationGrants").WithTags("Security")
           .WithSummary("A group's per-feature grants (0 = ReadOnly, 1 = Write).").Produces<IReadOnlyList<EffectivePermission>>();

        // The members of a group (for the group editor).
        api.MapGet("/security/groups/{groupId:long}/members", async (long groupId, IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetGroupMembersAsync(groupId, ct)))
           .WithName("GetGroupMembers").WithTags("Security")
           .WithSummary("The users who belong to a group.").Produces<IReadOnlyList<SecurityUser>>();

        // Clear a group's grant on a feature. User Control gated.
        api.MapDelete("/security/groups/{groupId:long}/applications/{applicationId:long}", async (long groupId, long applicationId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.DeleteGroupApplicationGrantAsync(groupId, applicationId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeleteGroupApplicationGrant").WithTags("Security")
           .WithSummary("Clear a group's grant on a feature (requires User Control).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        // Remove a group and its memberships + grants. User Control gated.
        api.MapDelete("/security/groups/{groupId:long}", async (long groupId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
                await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny ? deny
                    : await repo.DeleteSecurityGroupAsync(groupId, ct) ? Results.NoContent() : Results.NotFound())
           .WithName("DeleteSecurityGroup").WithTags("Security")
           .WithSummary("Remove a security group and its memberships/grants (requires User Control).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status403Forbidden);

        // Remove a protected feature and every grant referencing it. Blocks deleting "User Control"
        // itself — that feature gates this very screen, so removing it would permanently lock out
        // every OIDC admin (only a service API key could recover). User Control gated.
        api.MapDelete("/security/applications/{applicationId:long}", async (long applicationId, HttpContext ctx, IAbisRepository repo, CancellationToken ct) =>
            {
                if (await RequireFeatureAsync(ctx, repo, "User Control", 1, ct) is { } deny) return deny;
                var app = (await repo.GetSecurityApplicationsAsync(ct)).FirstOrDefault(a => a.ApplicationId == applicationId);
                if (app is null) return Results.NotFound();
                if (string.Equals(app.ApplicationName, "User Control", StringComparison.OrdinalIgnoreCase))
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "Protected feature",
                        detail: "The 'User Control' feature gates security administration and cannot be deleted.");
                return await repo.DeleteSecurityApplicationAsync(applicationId, ct) ? Results.NoContent() : Results.NotFound();
            })
           .WithName("DeleteSecurityApplication").WithTags("Security")
           .WithSummary("Remove a protected feature and its grants (requires User Control; the User Control feature itself is protected).").Produces(StatusCodes.Status204NoContent).Produces(StatusCodes.Status404NotFound).Produces(StatusCodes.Status409Conflict).Produces(StatusCodes.Status403Forbidden);

        api.MapGet("/scrap-skids", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("scrapSkids", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetScrapSkidsAsync(page, pageSize, orderBy, ct));
            })
           .WithName("ListScrapSkids").WithTags("Skids")
           .WithSummary("List scrap skids (paged, sortable).")
           .Produces<PagedResult<ScrapSkid>>().ProducesValidationProblem();

        api.MapGet("/scrap-skids/{scrapSkidNum:long}", async (long scrapSkidNum, IAbisRepository repo, CancellationToken ct) =>
                await repo.GetScrapSkidAsync(scrapSkidNum, ct) is { } skid
                    ? Results.Ok(skid)
                    : Results.NotFound())
           .WithName("GetScrapSkid").WithTags("Skids")
           .WithSummary("Get one scrap skid by id.")
           .Produces<ScrapSkid>().Produces(StatusCodes.Status404NotFound);

        api.MapPost("/scrap-skids", async (ScrapSkidWrite body, IAbisRepository repo, CancellationToken ct) =>
            {
                if (Validate(body) is { } problems)
                    return Results.ValidationProblem(problems);
                var created = await repo.CreateScrapSkidAsync(body, ct);
                return Results.Created($"/api/scrap-skids/{created.ScrapSkidNum}", created);
            })
           .WithName("CreateScrapSkid").WithTags("Skids")
           .WithSummary("Create a scrap skid.")
           .Produces<ScrapSkid>(StatusCodes.Status201Created).ProducesValidationProblem();

        api.MapGet("/partial-skids", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("partialSkids", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetPartialSkidsAsync(page, pageSize, orderBy, ct));
            })
           .WithName("ListPartialSkids").WithTags("Skids")
           .WithSummary("List in-process partial skids (paged, sortable).")
           .Produces<PagedResult<PartialSkid>>().ProducesValidationProblem();

        // ---- Lookups (reference data for data-entry screens) -----------
        api.MapGet("/lookups/alloys", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetAlloysAsync(ct)))
           .WithName("ListAlloys").WithTags("Lookups")
           .WithSummary("List distinct alloys (reference data for dropdowns).")
           .Produces<IEnumerable<string>>();

        api.MapGet("/lookups/shape-types", (IAbisRepository repo) =>
                Results.Ok(repo.GetShapeTypes()))
           .WithName("ListShapeTypes").WithTags("Lookups")
           .WithSummary("Blank shape catalog: each shape's dimension schema (names + which carry a tolerance) and die count — drives a dynamic per-shape form.")
           .Produces<IReadOnlyList<ShapeTypeInfo>>();

        api.MapGet("/lookups/lines", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetLinesAsync(ct)))
           .WithName("ListLines").WithTags("Lookups")
           .WithSummary("List production lines (referenced by jobs, coils, downtime).")
           .Produces<IEnumerable<ProductionLine>>();

        api.MapGet("/lookups/groupdepartments", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetGroupDepartmentsAsync(ct)))
           .WithName("ListGroupDepartments").WithTags("Lookups")
           .WithSummary("List maintenance groups/departments (referenced by maintenance logs).")
           .Produces<IEnumerable<GroupDepartment>>();

        api.MapGet("/lookups/downtime-causes", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetDowntimeCausesAsync(ct)))
           .WithName("ListDowntimeCauses").WithTags("Lookups")
           .WithSummary("List downtime causes/reasons (master data for the downtime feature).")
           .Produces<IEnumerable<DowntimeCause>>();

        api.MapGet("/lookups/transportation-methods", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetTransportationMethodsAsync(ct)))
           .WithName("ListTransportationMethods").WithTags("Lookups")
           .WithSummary("List transportation method codes (referenced by shipments).")
           .Produces<IEnumerable<TransportationMethod>>();

        api.MapGet("/lookups/equipment-types", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetEquipmentTypesAsync(ct)))
           .WithName("ListEquipmentTypes").WithTags("Lookups")
           .WithSummary("List shipping equipment type codes (referenced by shipments).")
           .Produces<IEnumerable<EquipmentType>>();

        api.MapGet("/lookups/customer-types", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCustomerTypesAsync(ct)))
           .WithName("ListCustomerTypes").WithTags("Lookups")
           .WithSummary("List customer classifications (referenced by customers).")
           .Produces<IEnumerable<CustomerType>>();

        api.MapGet("/lookups/edi-types", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetEdiTypesAsync(ct)))
           .WithName("ListEdiTypes").WithTags("Lookups")
           .WithSummary("List EDI transaction-set types and X12 versions (table edi_type).")
           .Produces<IEnumerable<EdiType>>();

        api.MapGet("/lookups/customer-edi", async (IAbisRepository repo, CancellationToken ct) =>
                Results.Ok(await repo.GetCustomerEdiAsync(ct)))
           .WithName("ListCustomerEdi").WithTags("Lookups")
           .WithSummary("List customer EDI trading-partner configuration (table customer_edi).")
           .Produces<IEnumerable<CustomerEdi>>();

        // ---- Audit / action log ----------------------------------------
        api.MapGet("/audit-log", async (IAbisRepository repo, CancellationToken ct,
                int page = 1, int pageSize = 25, string? source = null, string? sort = null, string? dir = null) =>
            {
                if (!Sort.TryResolve("auditLog", sort, dir, out var orderBy, out var problems))
                    return Results.ValidationProblem(problems!);
                return Results.Ok(await repo.GetAuditLogAsync(page, pageSize, source, orderBy, ct));
            })
           .WithName("ListAuditLog").WithTags("Audit")
           .WithSummary("List the action/audit log, newest first.")
           .Produces<PagedResult<AuditEntry>>().ProducesValidationProblem();

        return app;
    }

    // Optimistic-concurrency wrapper for replace/patch endpoints. Reads the current row,
    // and — when the caller sent an If-Match validator — compares it against the row's
    // current weak ETag (the same content hash a GET carries). A mismatch means someone
    // else changed the row since the caller read it → 412. With no If-Match header the
    // write proceeds (the precondition is optional, per RFC 7232). The schema has no
    // row-version column, so this content-hash check is the only schema-free option.
    private static async Task<IResult> WithIfMatch<T>(
        HttpContext ctx, IOptions<JsonOptions> json,
        Func<Task<T?>> getCurrent, Func<Task<T?>> update) where T : class
    {
        var current = await getCurrent();
        if (current is null) return Results.NotFound();

        var ifMatch = ctx.Request.Headers.IfMatch.ToString();
        if (!string.IsNullOrEmpty(ifMatch))
        {
            var tag = ETagMiddleware.ForEntity(current, json.Value.SerializerOptions);
            var ok = ifMatch.Split(',').Any(t => { var v = t.Trim(); return v == tag || v == "*"; });
            if (!ok) return Results.StatusCode(StatusCodes.Status412PreconditionFailed);
        }

        var updated = await update();
        return updated is null ? Results.NotFound() : Results.Ok(updated);
    }

    // Lightweight per-field validators. Max lengths mirror the Oracle column widths in
    // docs/data-model/oracle_ddl.sql so over-long or missing-required input fails fast as
    // a 400 ProblemDetails instead of an opaque DB 500 (ORA-12899 / ORA-01400).
    private static void Req(Dictionary<string, string[]> e, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) e[field] = [$"{field} is required."];
    }

    private static void Req(Dictionary<string, string[]> e, string field, long? value)
    {
        if (value is null) e[field] = [$"{field} is required."];
    }

    private static void Req(Dictionary<string, string[]> e, string field, DateTime? value)
    {
        if (value is null) e[field] = [$"{field} is required."];
    }

    private static void Req(Dictionary<string, string[]> e, string field, decimal? value)
    {
        if (value is null) e[field] = [$"{field} is required."];
    }

    // Edge-trim tolerance (legacy w_order_entry:496-549 / w_part_num_new:523). When trimming is
    // required, the trim data must be complete and incoming >= trimmed (hard errors); the trim
    // amount must sit within the 1.5"-12" trimmer tolerance, overridable via
    // trimmed_width_overridden='Y' + an override user. Shared by order items and part masters.
    private static void AddEdgeTrimErrors(Dictionary<string, string[]> e, string? trimmingRequired,
        decimal? incoming, decimal? trimmed, int? trimType, string? overridden, string? overrideUser)
    {
        if (!string.Equals(trimmingRequired?.Trim(), "Y", StringComparison.OrdinalIgnoreCase)) return;
        if (incoming is null) e["incomingCoilWidth"] = ["incomingCoilWidth is required when trimming is required."];
        if (trimmed is null) e["trimmedCoilWidth"] = ["trimmedCoilWidth is required when trimming is required."];
        if (trimType is null) e["trimTypeCode"] = ["trimTypeCode is required when trimming is required."];
        if (incoming is { } inc && trimmed is { } trm)
        {
            var diff = inc - trm;
            var isOverridden = string.Equals(overridden?.Trim(), "Y", StringComparison.OrdinalIgnoreCase);
            if (diff < 0m)
                e["trimmedCoilWidth"] = ["Incoming coil width must be greater than trimmed coil width."];
            else if (diff is < 1.50m or > 12.00m && !isOverridden)
                e["trimmedCoilWidth"] = ["Trim (incoming − trimmed) is under trimmer tolerance (must be 1.5\"–12\"); resend with trimmedWidthOverridden='Y' to override."];
            else if (diff is < 1.50m or > 12.00m && string.IsNullOrWhiteSpace(overrideUser))
                e["trimmedWidthOverrideUser"] = ["trimmedWidthOverrideUser is required to override the trimmer tolerance."];
        }
    }

    // "At save" normalization shared by parts and order items (rank 23). Two legacy rules:
    //  - When trimming isn't required, the trim columns are cleared so a stale incoming/trimmed
    //    width can't linger on the record (w_part_num_new:562 wf_update_trimming_data(False)).
    //  - Pieces-per-skid is only a suggestion: when it wasn't supplied, derive it as
    //    Int(max_skid_wt / theoretical_unit_wt) (w_order_entry:1152) — an explicit value is kept.
    private static void NormalizeTrimAndPieces(ITrimNormalizable b)
    {
        if (!string.Equals(b.TrimmingRequired?.Trim(), "Y", StringComparison.OrdinalIgnoreCase))
        {
            b.IncomingCoilWidth = null;
            b.TrimmedCoilWidth = null;
            b.TrimTypeCode = null;
            b.TrimmedWidthOverridden = null;
            b.TrimmedWidthOverrideUser = null;
        }
        if (b.PiecesSkid is null or 0 && b.MaxSkidWt is > 0 && b.TheoreticalUnitWt is > 0m)
            b.PiecesSkid = (int)(b.MaxSkidWt.Value / b.TheoreticalUnitWt.Value);
    }

    // A cash_date is stored as an 8-digit MMDDYYYY string. Legacy validates month 1-12,
    // day 1-31, and a year inside the last two years [today-2 .. today]
    // (w_coil_detail_new:69-103). Returns an error message, or null when blank (presence is a
    // separate, customer-conditional rule — deferred) or well-formed.
    private static string? CashDateFormatError(string? cashDate)
    {
        var s = cashDate?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length != 8 || !s.All(char.IsDigit))
            return "cashDate must be an 8-digit MMDDYYYY string.";
        var month = int.Parse(s[..2]);
        var day = int.Parse(s.Substring(2, 2));
        var year = int.Parse(s[4..]);
        if (month is < 1 or > 12) return "cashDate month must be 01–12.";
        if (day is < 1 or > 31) return "cashDate day must be 01–31.";
        var currentYear = DateTime.Today.Year;
        if (year < currentYear - 2 || year > currentYear)
            return $"cashDate year must be between {currentYear - 2} and {currentYear}.";
        return null;
    }

    // A trimmer-tolerance override is attributable to whoever is signed in: stamp the
    // trimmed-width override user from the principal, never client input (legacy sets it to
    // sqlca.logid, w_order_entry:616). A null login (API-key service account) keeps the
    // supplied value. Runs before Validate so an authenticated overrider needn't send the field.
    private static void StampTrimOverrideUser(ITrimNormalizable body, HttpContext ctx)
    {
        if (string.Equals(body.TrimmedWidthOverridden?.Trim(), "Y", StringComparison.OrdinalIgnoreCase)
            && ResolveLogin(ctx) is { } login)
            body.TrimmedWidthOverrideUser = login;
    }

    // Default report window: the time-series reports filter by date, but their from/to are
    // optional, so an unbounded call scans the whole history on Oracle (harmless on SQLite —
    // see docs/REPORTING_PERFORMANCE.md). Cap an unbounded (or half-bounded) call to the last
    // year so it always filters; an explicit bound always wins, only the missing side defaults.
    private const int DefaultReportWindowDays = 365;
    private static (DateTime from, DateTime to) ResolveReportWindow(DateTime? from, DateTime? to)
    {
        var resolvedTo = to ?? DateTime.UtcNow;
        var resolvedFrom = from ?? resolvedTo.AddDays(-DefaultReportWindowDays);
        return (resolvedFrom, resolvedTo);
    }

    // Blank area (in²) for the piece-weight calculator, by shape (legacy w_order_entry:694-823):
    // L×W for rectangle/parallelogram/chevron/fender; (long+short)/2 × W for the trapezoids;
    // π·d²/4 for circle. Returns (area, null) or (null, error) when the shape's dims are missing.
    private static (decimal? area, string? error) BlankArea(string shapeType, PieceWeightRequest r)
    {
        var s = new string(shapeType.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        switch (s)
        {
            case "RECTANGLE": case "PARALLELOGRAM": case "CHEVRON": case "FENDER":
                if (r.Length is not > 0m || r.Width is not > 0m)
                    return (null, "length and width are required and must be greater than zero.");
                return (r.Length.Value * r.Width.Value, null);
            case "TRAPEZOID": case "LTRAPEZOID": case "LEFTTRAPEZOID": case "RTRAPEZOID": case "RIGHTTRAPEZOID":
                if (r.LongLength is not > 0m || r.ShortLength is not > 0m || r.Width is not > 0m)
                    return (null, "longLength, shortLength and width are required and must be greater than zero.");
                return ((r.LongLength.Value + r.ShortLength.Value) / 2m * r.Width.Value, null);
            case "CIRCLE":
                if (r.Diameter is not > 0m)
                    return (null, "diameter is required and must be greater than zero.");
                return (3.1415927m * (r.Diameter.Value * r.Diameter.Value) / 4m, null);
            default:
                return (null, $"Unsupported shapeType '{shapeType}' (supported: rectangle, parallelogram, chevron, fender, trapezoid, left/right trapezoid, circle).");
        }
    }

    // ---- Security enforcement (legacy f_security_door) ----
    // The caller's ABIS login: the OIDC preferred_username/name claim, or the X-User-Login
    // header (dev/testing). Null => an API-key service account (full trust, bypasses gates).
    private static string? ResolveLogin(HttpContext ctx)
    {
        var claim = ctx.User?.FindFirst("preferred_username")?.Value
                    ?? ctx.User?.FindFirst("name")?.Value;
        if (!string.IsNullOrWhiteSpace(claim)) return claim;
        var hdr = ctx.Request.Headers["X-User-Login"].ToString();
        return string.IsNullOrWhiteSpace(hdr) ? null : hdr;
    }

    // Sign a short-lived bearer for a resolved ABIS user, using the same symmetric key the
    // JWT bearer validation trusts. preferred_username = login_id so ResolveLogin picks it up.
    private static string IssueUserToken(JwtAuthOptions jwt, string login, string name, long userId)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey!)), SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: string.IsNullOrWhiteSpace(jwt.Issuer) ? null : jwt.Issuer,
            audience: string.IsNullOrWhiteSpace(jwt.Audience) ? null : jwt.Audience,
            claims:
            [
                new Claim("preferred_username", login),
                new Claim("name", name),
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            notBefore: now, expires: now.AddHours(8), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Per-feature gate: returns null when allowed, or a 403 result when the resolved user
    // lacks the required privilege. A null login (API-key service account) is allowed —
    // enforcement applies only to real end users (OIDC), matching the rollout policy.
    private static async Task<IResult?> RequireFeatureAsync(HttpContext ctx, IAbisRepository repo, string feature, int level, CancellationToken ct)
    {
        var login = ResolveLogin(ctx);
        if (login is null) return null; // service account / trusted internal caller
        var priv = await repo.GetEffectivePrivilegeAsync(login, feature, ct);
        return priv is { } p && p >= level
            ? null
            : Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: $"User '{login}' lacks the required privilege ({level}) on feature '{feature}'.");
    }

    private static void Max(Dictionary<string, string[]> e, string field, string? value, int max)
    {
        if (value is not null && value.Length > max) e[field] = [$"{field} must be {max} characters or fewer."];
    }

    private static Dictionary<string, string[]>? ValidateScheduledJob(ScheduledJobWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "jobName", body.JobName);
        Max(e, "jobName", body.JobName?.Trim(), 100);
        Req(e, "cronExpression", body.CronExpression);
        if (!string.IsNullOrWhiteSpace(body.CronExpression) && !IsPlausibleCron(body.CronExpression!))
            e["cronExpression"] = ["cronExpression must be a 5- or 6-field cron expression."];
        Max(e, "targetOperation", body.TargetOperation, 100);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? ValidateTruck(TruckAppointmentWrite body)
    {
        var e = new Dictionary<string, string[]>();
        var dir = body.Direction?.Trim().ToUpperInvariant();
        if (dir is not ("INBOUND" or "OUTBOUND")) e["direction"] = ["direction must be INBOUND or OUTBOUND."];
        if (body.ScheduledStart is { } s && body.ScheduledEnd is { } en && en < s)
            e["scheduledEnd"] = ["scheduledEnd cannot be before scheduledStart."];
        return e.Count == 0 ? null : e;
    }

    /// <summary>Loose cron shape check (definition-time validation only — nothing is ever fired
    /// from it in this phase): 5 or 6 whitespace-separated fields, each using only cron punctuation.</summary>
    private static bool IsPlausibleCron(string expr)
    {
        var fields = expr.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (5 or 6)) return false;
        foreach (var f in fields)
            if (!f.All(c => char.IsDigit(c) || c is '*' or '/' or '-' or ',')) return false;
        return true;
    }

    private static Dictionary<string, string[]>? ValidateEdiType(EdiTypeWrite body)
    {
        var e = new Dictionary<string, string[]>();
        if (body.EdiTypeId is <= 0 or > 999) e["ediTypeId"] = ["ediTypeId must be 1–999 (NUMBER(3))."];
        Req(e, "ediVersion", body.EdiVersion);
        Max(e, "ediVersion", body.EdiVersion?.Trim(), 18);
        Max(e, "ediTypeDescription", body.EdiTypeDescription, 255);
        return e.Count == 0 ? null : e;
    }

    private static async Task<Dictionary<string, string[]>?> ValidateCustomerEdiAsync(CustomerEdiWrite body, IAbisRepository repo, CancellationToken ct)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "customerEdiName", body.CustomerEdiName);
        Max(e, "customerEdiName", body.CustomerEdiName?.Trim(), 18);
        if (body.CustomerId <= 0) e["customerId"] = ["customerId is required."];
        Max(e, "ediVersion", body.EdiVersion?.Trim(), 18);
        Max(e, "customerEdiDesc", body.CustomerEdiDesc, 255);
        // The route hangs off a real customer — a bad id would be an ORA-02291 500 on Oracle.
        if (!e.ContainsKey("customerId") && await repo.GetCustomerAsync(body.CustomerId, ct) is null)
            e["customerId"] = ["customerId must reference an existing customer."];
        await AppendEdiTypeRefErrorAsync(e, body, repo, ct);
        return e.Count == 0 ? null : e;
    }

    /// <summary>Update-time validation for a customer EDI route: the (name, customerId) key is fixed
    /// by the path, so only the mutable type/version/desc are checked.</summary>
    private static async Task<Dictionary<string, string[]>?> ValidateCustomerEdiTypeRefAsync(CustomerEdiWrite body, IAbisRepository repo, CancellationToken ct)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "ediVersion", body.EdiVersion?.Trim(), 18);
        Max(e, "customerEdiDesc", body.CustomerEdiDesc, 255);
        await AppendEdiTypeRefErrorAsync(e, body, repo, ct);
        return e.Count == 0 ? null : e;
    }

    /// <summary>When a route names a type + version, that pair must exist in edi_type (no dangling route).</summary>
    private static async Task AppendEdiTypeRefErrorAsync(Dictionary<string, string[]> e, CustomerEdiWrite body, IAbisRepository repo, CancellationToken ct)
    {
        if (body.EdiTypeId is { } tid && !string.IsNullOrWhiteSpace(body.EdiVersion)
            && !e.ContainsKey("ediVersion") && !await repo.EdiTypeExistsAsync(tid, body.EdiVersion!.Trim(), ct))
            e["ediTypeId"] = ["ediTypeId/ediVersion must reference an existing EDI type."];
    }

    /// <summary>Returns a ProblemDetails error dictionary, or null when valid.</summary>
    private static Dictionary<string, string[]>? Validate(InvoiceWrite body)
    {
        var e = new Dictionary<string, string[]>();
        if (body.AbJobNum <= 0) e["abJobNum"] = ["abJobNum is required."];
        Req(e, "invoiceNum", body.InvoiceNum);
        Max(e, "invoiceNum", body.InvoiceNum?.Trim(), 32);   // invoice_num VARCHAR2(32)
        Max(e, "notes", body.Notes, 2048);                   // notes VARCHAR2(2048)
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CustomerWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "customerName", body.CustomerName);
        Max(e, "customerName", body.CustomerName, 60);
        Max(e, "customerShortName", body.CustomerShortName, 18);
        Max(e, "customerCity", body.CustomerCity, 18);
        Max(e, "customerState", body.CustomerState, 30);
        Max(e, "customerZip", body.CustomerZip, 18);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(OrderItemWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "enduserPartNum", body.EnduserPartNum);
        Max(e, "enduserPartNum", body.EnduserPartNum, 22);
        Req(e, "sheetType", body.SheetType);              // sheet_type is CHAR(18) NOT NULL
        Max(e, "sheetType", body.SheetType, 18);
        Max(e, "alloy2", body.Alloy2, 8);
        Max(e, "temper", body.Temper, 8);
        Max(e, "surface", body.Surface, 255);
        Max(e, "flatness", body.Flatness, 255);
        Max(e, "materialEndUse", body.MaterialEndUse, 255);
        Max(e, "orderItemDesc", body.OrderItemDesc, 255);

        // Edge-trim tolerance (legacy w_order_entry:496-549). When trimming is required the
        // trim data must be complete and incoming ≥ trimmed (both HARD errors → Return 0/-8).
        // The trim amount (incoming − trimmed) must sit within the 1.5"–12" trimmer tolerance
        // (Alex Gerlants 06/16/2017, per Dan Polkinhorne) — but that breach is OVERRIDABLE:
        // legacy prompts Yes/No and, on override, stamps trimmed_width_overridden='Y' +
        // trimmed_width_override_user and logs it. We mirror that: out-of-tolerance is a 400
        // unless trimmedWidthOverridden='Y' is sent, in which case an override user is required.
        AddEdgeTrimErrors(e, body.TrimmingRequired, body.IncomingCoilWidth, body.TrimmedCoilWidth,
            body.TrimTypeCode, body.TrimmedWidthOverridden, body.TrimmedWidthOverrideUser);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(DimensionCheckWrite body)
    {
        // Input hygiene for the dimensional QC gate (table sheet_skid_dimension_check).
        // NOTE: the authoritative pass/fail — comparing each measured value to the skid's
        // shape nominal ± tolerance — lives in the legacy binary DataWindow d_skid_dim_check
        // and is NOT reconstructable from the vendored source; it is deferred to a
        // live-Oracle-verified increment (see docs/NEXT_STEPS.md). Until then in_spec is a
        // human-entered flag, so we at least refuse to record a garbage or empty check that
        // the repository would silently default to in_spec=1 (pass).
        var e = new Dictionary<string, string[]>();
        Max(e, "checkedBy", body.CheckedBy, 30);
        Max(e, "note", body.Note, 255);
        // Require the auditor: a QC record with no "checked by" is not traceable.
        Req(e, "checkedBy", body.CheckedBy);
        // in_spec is a pass/fail flag: only 0 (fail) or 1 (pass) are meaningful.
        if (body.InSpec is not (null or 0 or 1))
            e["inSpec"] = ["inSpec must be 0 (fail) or 1 (pass)."];
        // Don't record a blank check — at least one measurement must be present.
        if (body.Gauge is null && body.Width is null && body.LengthOper is null &&
            body.LengthDrive is null && body.Square is null && body.HeadDimension is null)
            e["measurements"] = ["At least one measurement (gauge, width, lengthOper, lengthDrive, square, headDimension) is required."];
        // A physical measurement can't be zero or negative.
        Positive(e, "gauge", body.Gauge);
        Positive(e, "width", body.Width);
        Positive(e, "lengthOper", body.LengthOper);
        Positive(e, "lengthDrive", body.LengthDrive);
        Positive(e, "square", body.Square);
        Positive(e, "headDimension", body.HeadDimension);
        // Absolute measurement bounds (legacy u_tabpg_skid_dim_check: pc 1..99, gauge 0..1,
        // width 5..199, square 0..9, lengths 1..999). Upper bounds on top of the positive checks.
        if (body.PcNumber is { } pc && (pc < 1 || pc > 99)) e["pcNumber"] = ["pcNumber must be between 1 and 99."];
        if (body.Gauge is > 1m) e["gauge"] = ["gauge must be at most 1."];
        if (body.Width is { } w && (w < 5m || w > 199m)) e["width"] = ["width must be between 5 and 199."];
        if (body.Square is > 9m) e["square"] = ["square must be at most 9."];
        if (body.LengthOper is > 999m) e["lengthOper"] = ["lengthOper must be at most 999."];
        if (body.LengthDrive is > 999m) e["lengthDrive"] = ["lengthDrive must be at most 999."];
        return e.Count == 0 ? null : e;
    }

    private static void Positive(Dictionary<string, string[]> e, string field, decimal? v)
    {
        if (v is { } d && d <= 0m) e[field] = [$"{field} must be greater than zero."];
    }

    private static Dictionary<string, string[]>? Validate(PartWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "customerId", body.CustomerId);
        Max(e, "enduserPartNum", body.EnduserPartNum, 22);
        Max(e, "sheetType", body.SheetType, 18);
        Max(e, "alloy", body.Alloy, 8);
        Max(e, "temper", body.Temper, 8);
        // The same edge-trim rule as order items applies to a part's trimming spec.
        // (sheetType / gauge / enduser_id / sector are NOT required here: Validate(PartWrite) is
        // shared by the full-replace PUT, and parts are built up incrementally — enforce those at
        // a create-specific/finalize point, verified against live Oracle.)
        AddEdgeTrimErrors(e, body.TrimmingRequired, body.IncomingCoilWidth, body.TrimmedCoilWidth,
            body.TrimTypeCode, body.TrimmedWidthOverridden, body.TrimmedWidthOverrideUser);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(JobWrite body)
    {
        var e = new Dictionary<string, string[]>();
        // A production job belongs to an order line — legacy refuses a job/production order with
        // no order ("NO ABC Order specified in the production order", w_stacker_job_details:491),
        // and the sheet-weight rollup resolves the order_item by (order_abc_num, order_item_num).
        if (body.OrderAbcNum is null or <= 0)
            e["orderAbcNum"] = ["orderAbcNum is required (a job belongs to an order)."];
        if (body.OrderItemNum is null or <= 0)
            e["orderItemNum"] = ["orderItemNum is required (a job targets an order line)."];
        // Material yield drives the sheet-weight rollup; a zero/negative yield is rejected
        // ("Invalid yield value.", w_stacker_job_details:272). Optional at create, positive when set.
        Positive(e, "materialYield", body.MaterialYield);
        // material_yield is stored as NUMBER(2,2) — a ratio in (0, 0.99] (live data is 0.99, i.e.
        // 99%). Reject an out-of-range value with a clean 400 rather than letting it overflow the
        // column as ORA-01438 → 500 (found deploying to codi-ABIS: a percentage-style 92.5 blew up).
        if (body.MaterialYield is > 0.99m)
            e["materialYield"] = ["materialYield must be a ratio of 0.99 or less (e.g. 0.92 for 92% yield)."];
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CarrierWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "carrierFullName", body.CarrierFullName);
        Max(e, "carrierFullName", body.CarrierFullName, 60);
        Max(e, "scac", body.Scac, 8);
        Max(e, "carrierTypeCode", body.CarrierTypeCode, 36);
        Max(e, "carrierCity", body.CarrierCity, 18);
        Max(e, "carrierState", body.CarrierState, 30);
        Max(e, "carrierPhoneNumber", body.CarrierPhoneNumber, 18);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(DieWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "dieName", body.DieName);
        Max(e, "dieName", body.DieName, 32);
        Max(e, "toolNum", body.ToolNum, 32);
        Max(e, "partName", body.PartName, 64);
        Max(e, "location", body.Location, 32);
        Max(e, "description", body.Description, 64);
        Max(e, "owner", body.Owner, 32);   // the one string field that was unbounded
        // engineered_scrap_y_n is a Y/N flag (legacy w_die_new / die.engineered_scrap_y_n CHAR(1)).
        if (!string.IsNullOrWhiteSpace(body.EngineeredScrapYN) && body.EngineeredScrapYN.Trim().ToUpperInvariant() is not ("Y" or "N"))
            e["engineeredScrapYN"] = ["engineeredScrapYN must be Y or N."];
        // gross_weight is stored as a whole number (legacy d_die_new integer column).
        if (body.GrossWeight is { } gw && gw != decimal.Truncate(gw))
            e["grossWeight"] = ["grossWeight must be a whole number."];
        // NOTE: num_of_parts_per_hit / status / location value-sets are NOT hard-enforced —
        // the worklist's {1,2,3} / {0,1,2} / {BLDG #1..3} don't match the real data (seed uses
        // location RACK-*), so they need live-Oracle confirmation before an enum gate.
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(SketchWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "sketchName", body.SketchName);
        Max(e, "sketchName", body.SketchName, 16);
        Max(e, "sketchNotes", body.SketchNotes, 1024);
        Max(e, "sketchSysNote", body.SketchSysNote, 255);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CustomerContactWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "lastName", body.LastName);
        Max(e, "lastName", body.LastName, 18);
        Max(e, "firstName", body.FirstName, 18);
        Max(e, "department", body.Department, 18);
        Max(e, "city", body.City, 18);
        Max(e, "state", body.State, 30);
        Max(e, "phone1", body.Phone1, 18);
        Max(e, "email1", body.Email1, 50);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(ReceivingBolWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "bol", body.Bol);
        Max(e, "bol", body.Bol, 32);
        Req(e, "customerId", body.CustomerId);
        Max(e, "createdBy", body.CreatedBy, 32);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(ScanLogWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "abJobNum", body.AbJobNum);
        Req(e, "scanStation", body.ScanStation);
        Max(e, "scanStation", body.ScanStation, 16);
        Req(e, "note", body.Note);
        Max(e, "note", body.Note, 128);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(MaintLogWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "probDateTime", body.ProbDateTime);
        Req(e, "probDetails", body.ProbDetails);
        Max(e, "probDetails", body.ProbDetails, 1024);
        Req(e, "author", body.Author);
        Max(e, "author", body.Author, 64);
        Max(e, "maintLogStatus", body.MaintLogStatus, 128);
        Max(e, "systemEquipment", body.SystemEquipment, 128);
        Max(e, "subsystemEquipment", body.SubsystemEquipment, 128);
        Max(e, "itemDevice", body.ItemDevice, 128);
        Max(e, "actions", body.Actions, 1024);
        Max(e, "reportedBy", body.ReportedBy, 64);
        Max(e, "assignedTo", body.AssignedTo, 128);
        Max(e, "completedBy", body.CompletedBy, 128);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CustomerOrderWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "origCustomerPo", body.OrigCustomerPo);
        Max(e, "origCustomerPo", body.OrigCustomerPo, 36);
        Max(e, "enduserPo", body.EnduserPo, 36);
        Max(e, "scrapHandingType", body.ScrapHandingType, 18);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(OrderCreateWithItems body)
    {
        var e = new Dictionary<string, string[]>();
        if (Validate(body.Order) is { } oe)
            foreach (var kv in oe) e[$"order.{kv.Key}"] = kv.Value;
        for (var i = 0; i < body.Items.Count; i++)
            if (Validate(body.Items[i]) is { } ie)
                foreach (var kv in ie) e[$"items[{i}].{kv.Key}"] = kv.Value;
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CoilWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "coilAlloy2", body.CoilAlloy2);
        Max(e, "coilAlloy2", body.CoilAlloy2, 8);
        Max(e, "coilTemper", body.CoilTemper, 8);
        Max(e, "coilLocation", body.CoilLocation, 18);
        Max(e, "coilMidNum", body.CoilMidNum, 18);
        Max(e, "coilOrgNum", body.CoilOrgNum, 32);
        Max(e, "coilNotes", body.CoilNotes, 255);
        Max(e, "icra", body.Icra, 18);
        Max(e, "lotNum", body.LotNum, 18);
        // Coil identity + weight integrity (legacy w_coil_detail_new:381-391 requires net_wt,
        // net_balance, width non-null; w_receiving_dock:351 requires org_num len >= 4). net_wt +
        // width feed billing/derivations so they must be present and positive; net_wt_balance is
        // NOT required here — it defaults to net_wt on create and is legitimately 0 for a fully
        // consumed coil. org_num is the coil's business id.
        Req(e, "netWt", body.NetWt);
        Positive(e, "netWt", body.NetWt);
        Req(e, "coilWidth", body.CoilWidth);
        Positive(e, "coilWidth", body.CoilWidth);
        Req(e, "coilOrgNum", body.CoilOrgNum);
        if (!string.IsNullOrWhiteSpace(body.CoilOrgNum) && body.CoilOrgNum.Trim().Length < 4)
            e["coilOrgNum"] = ["coilOrgNum must be at least 4 characters."];
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(SheetSkidWrite body)
    {
        var e = new Dictionary<string, string[]>();
        if (body.AbJobNum <= 0) e["abJobNum"] = ["abJobNum is required."];
        Max(e, "sheetSkidDisplayNum", body.SheetSkidDisplayNum, 16);
        // Weight sanity (legacy w_stacker_skid_edit:87-95: tare 0..8000, net 0..30000;
        // w_wh_business:809 requires a non-zero net). A finished skid must carry a positive weight.
        Req(e, "sheetNetWt", body.SheetNetWt);
        if (body.SheetNetWt is { } n && (n <= 0m || n > 30000m))
            e["sheetNetWt"] = ["sheetNetWt must be greater than 0 and at most 30000."];
        if (body.SheetTareWt is { } t && (t < 0m || t > 8000m))
            e["sheetTareWt"] = ["sheetTareWt must be between 0 and 8000."];
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(SheetSkidWarehousePatch body)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "skidLocation", body.SkidLocation, 18);
        Max(e, "skidTicketIfWhed", body.SkidTicketIfWhed, 32);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(ScrapSkidWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Req(e, "scrapAbJobNum", body.ScrapAbJobNum);
        Max(e, "scrapAbJobNum", body.ScrapAbJobNum, 18);
        Max(e, "scrapAlloy2", body.ScrapAlloy2, 8);
        Max(e, "scrapTemper", body.ScrapTemper, 8);
        Max(e, "scrapLocation", body.ScrapLocation, 18);
        Max(e, "scrapNotes", body.ScrapNotes, 255);
        // A scrap skid must carry a real net weight — legacy refuses a null/zero skid net
        // ("Skid Net Weight must be populated", w_office_skid_entry:5413). Tare stays optional
        // but cannot be negative.
        if (body.ScrapNetWt is null or <= 0m)
            e["scrapNetWt"] = ["scrapNetWt is required and must be greater than zero (skid net weight must be populated)."];
        if (body.ScrapTareWt is < 0m)
            e["scrapTareWt"] = ["scrapTareWt cannot be negative."];
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(ShipmentWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "vehicleId", body.VehicleId, 32);
        Max(e, "shipmentNotes", body.ShipmentNotes, 255);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(ShiftWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "operatorInitial", body.OperatorInitial, 10);
        Max(e, "note", body.Note, 1024);
        // A shift must have a start time — legacy treats a null start/end as "Invalid Date Info"
        // (w_daily_production:197). End time stays optional: a shift is opened when it starts and
        // closed when it ends. But when an end IS given it cannot precede the start
        // (w_shift_info_new:130 "Shift ending time is before starting time." -> RETURN -1).
        Req(e, "startTime", body.StartTime);
        if (body.StartTime is { } s && body.EndTime is { } end && end < s)
            e["endTime"] = ["endTime cannot be before startTime."];
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(DowntimeInstanceWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "note", body.Note, 255);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(SalesReminderWrite body)
    {
        var e = new Dictionary<string, string[]>();
        Max(e, "eventNotes", body.EventNotes, 1024);
        Max(e, "eventStatus", body.EventStatus, 16);
        Max(e, "userId", body.UserId, 32);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(SalesProbabilityWrite body)
    {
        var e = new Dictionary<string, string[]>();
        if (body.SalesProbabilityPercent is < 0 or > 100)
            e["salesProbabilityPercent"] = ["salesProbabilityPercent must be between 0 and 100."];
        Max(e, "probabilityNote", body.ProbabilityNote, 1024);
        return e.Count == 0 ? null : e;
    }

    private static Dictionary<string, string[]>? Validate(CoilOwnershipTransferWrite body)
    {
        var e = new Dictionary<string, string[]>();
        if (body.CoilAbcNumOrig is null or <= 0)
            e["coilAbcNumOrig"] = ["coilAbcNumOrig is required (the coil to transfer)."];
        if (body.CustomerIdNew is null or <= 0)
            e["customerIdNew"] = ["customerIdNew is required (the new owner)."];
        Max(e, "transferPerformedBy", body.TransferPerformedBy, 32);
        Max(e, "authorizationNote", body.AuthorizationNote, 255);
        Max(e, "notes", body.Notes, 255);
        return e.Count == 0 ? null : e;
    }
}
