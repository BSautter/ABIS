using System.Diagnostics;

namespace Abis.Api.Admin;

/// <summary>Config for the #7 server/service console (see docs/SERVER_CONSOLE.md). The whole feature is
/// <b>OFF by default</b>: every endpoint 503s until <see cref="Enabled"/>, and the mutating restart is a
/// no-op until <see cref="AllowRestart"/> AND the operator installs the sudoers NOPASSWD allowlist — so
/// shipping this code grants no new privilege on its own. From <c>Admin:ServerConsole:*</c>.</summary>
public sealed class ServerConsoleOptions
{
    public const string SectionName = "Admin:ServerConsole";

    /// <summary>Master switch. When false every console endpoint returns 503 "console disabled".</summary>
    public bool Enabled { get; set; }

    /// <summary>The systemd units the console may inspect / restart. Any other unit is rejected — the unit
    /// name is never interpolated into a shell, and only these fixed values reach <c>systemctl</c>.</summary>
    public string[] AllowedUnits { get; set; } = ["abis", "nginx"];

    /// <summary>Whether the restart action is permitted. Also needs the OS to authorize the service user to
    /// restart the allowlisted units — a polkit rule (default, keeps the unit's NoNewPrivileges hardening)
    /// or a sudoers allowlist (set <see cref="RestartCommand"/> to a sudo prefix). When false, restart 409s.</summary>
    public bool AllowRestart { get; set; }

    /// <summary>The command prefix used to restart a unit; the unit name is appended. Empty (default) =
    /// <c>systemctl restart</c> — invoked WITHOUT sudo so it works under a hardened
    /// <c>NoNewPrivileges=true</c> unit via a polkit rule (see docs/SERVER_CONSOLE.md). For a
    /// non-hardened box you may instead set this to <c>["sudo","-n","systemctl","restart"]</c> with a
    /// matching sudoers allowlist. argv only — no shell. (Empty default so config binding replaces it
    /// cleanly — a non-empty default array is not overwritten by config.)</summary>
    public string[] RestartCommand { get; set; } = [];

    /// <summary>Max journal lines one log request may tail.</summary>
    public int LogTailMax { get; set; } = 1000;

    /// <summary>The argv used to read the DB-host crontab read-only — e.g. an ssh command-locked key that
    /// runs only <c>crontab -l</c>. Empty = the host-cron view is not configured (returns 503). This is an
    /// admin-set config value, never user input.</summary>
    public string[] HostCronCommand { get; set; } = [];

    /// <summary>Seconds before a console command is abandoned (killed).</summary>
    public int CommandTimeoutSeconds { get; set; } = 15;
}

public sealed record ServiceStatus(string Unit, bool Available, bool Active, string State, string SubState, string? MainPid, string? Since);
public sealed record CommandOutcome(bool Ok, int ExitCode, string Stdout, string Stderr);
public sealed record RestartOutcome(bool Ok, string Unit, string Detail);
public sealed record HostCronResult(bool Available, string Text, string? Error);

/// <summary>Runs an external process from a fixed file + argument list (never a shell string, so there is
/// no command-injection surface). Abstracted so the console service is unit-testable with a fake.</summary>
public interface IProcessRunner
{
    Task<CommandOutcome> RunAsync(string file, IReadOnlyList<string> args, int timeoutSeconds, CancellationToken ct);
}

/// <summary>Real process runner: argv only (UseShellExecute=false), drains stdout/stderr concurrently to
/// avoid a full-pipe deadlock, and kills the process if it exceeds the timeout.</summary>
public sealed class ProcessRunner : IProcessRunner
{
    public async Task<CommandOutcome> RunAsync(string file, IReadOnlyList<string> args, int timeoutSeconds, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        try { if (!proc.Start()) return new CommandOutcome(false, -1, "", "failed to start process"); }
        catch (Exception ex) { return new CommandOutcome(false, -1, "", ex.Message); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, 120)));
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
        var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
        try
        {
            await proc.WaitForExitAsync(cts.Token);
            return new CommandOutcome(proc.ExitCode == 0, proc.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return new CommandOutcome(false, -1, "", $"command timed out after {timeoutSeconds}s");
        }
    }
}

/// <summary>The #7 server/service console: view systemd service status + tail the journal (read-only) and
/// restart the allowlisted units (mutating, gated). Every method is inert unless the config enables it and
/// (for restart / host-cron) the operator has installed the sudoers allowlist / SSH channel.</summary>
public sealed class ServerConsoleService(ServerConsoleOptions opts, IProcessRunner runner, ILogger<ServerConsoleService> log)
{
    public bool Enabled => opts.Enabled;
    public bool RestartAllowed => opts.AllowRestart;

    /// <summary>Is this unit one the console is allowed to touch? Ordinal exact-match against the allowlist.</summary>
    public bool IsAllowedUnit(string unit) => opts.AllowedUnits.Contains(unit, StringComparer.Ordinal);

    public async Task<IReadOnlyList<ServiceStatus>> GetServicesAsync(CancellationToken ct)
    {
        var list = new List<ServiceStatus>(opts.AllowedUnits.Length);
        foreach (var unit in opts.AllowedUnits) list.Add(await GetServiceAsync(unit, ct));
        return list;
    }

    public async Task<ServiceStatus> GetServiceAsync(string unit, CancellationToken ct)
    {
        // One structured read: systemctl show emits KEY=VALUE lines (no privilege needed to query). On a
        // non-Linux host systemctl won't start → the runner returns exit -1 → we report unavailable.
        var r = await runner.RunAsync("systemctl",
            ["show", unit, "--property=ActiveState,SubState,MainPID,ActiveEnterTimestamp", "--no-pager"],
            opts.CommandTimeoutSeconds, ct);
        if (r.ExitCode < 0)
            return new ServiceStatus(unit, Available: false, Active: false, "unavailable", "", null, null);
        var kv = ParseKeyValues(r.Stdout);
        var state = kv.GetValueOrDefault("ActiveState", "unknown");
        var pid = kv.GetValueOrDefault("MainPID");
        return new ServiceStatus(
            unit, Available: true, Active: state == "active", state,
            kv.GetValueOrDefault("SubState", ""),
            pid is null or "0" ? null : pid,
            kv.TryGetValue("ActiveEnterTimestamp", out var t) && t.Length > 0 ? t : null);
    }

    public async Task<CommandOutcome> GetLogsAsync(string unit, int tail, CancellationToken ct)
    {
        var n = Math.Clamp(tail, 1, opts.LogTailMax);
        return await runner.RunAsync("journalctl",
            ["-u", unit, "-n", n.ToString(), "--no-pager", "--output=short-iso"], opts.CommandTimeoutSeconds, ct);
    }

    public async Task<RestartOutcome> RestartAsync(string unit, CancellationToken ct)
    {
        if (!opts.AllowRestart)
            return new RestartOutcome(false, unit, "restart is not enabled (set Admin:ServerConsole:AllowRestart=true + a polkit rule or sudoers allowlist)");
        // Empty config = the polkit default `systemctl restart <unit>` (no sudo) → authorized by a polkit
        // rule, so it works under the unit's NoNewPrivileges hardening (sudo would be blocked). The unit is
        // allowlist-validated by the caller.
        string[] cmd = opts.RestartCommand.Length > 0 ? opts.RestartCommand : ["systemctl", "restart"];
        var args = cmd.Skip(1).Append(unit).ToArray();
        var r = await runner.RunAsync(cmd[0], args, opts.CommandTimeoutSeconds, ct);
        log.LogWarning("Server-console restart of unit '{Unit}' → exit {Code}", unit, r.ExitCode);
        var detail = r.Ok ? "restarted" : (r.Stderr.Trim() is { Length: > 0 } e ? e : $"restart failed (exit {r.ExitCode})");
        return new RestartOutcome(r.Ok, unit, detail);
    }

    public async Task<HostCronResult> GetHostCronAsync(CancellationToken ct)
    {
        if (opts.HostCronCommand.Length == 0)
            return new HostCronResult(false, "", "host-cron view is not configured (set Admin:ServerConsole:HostCronCommand)");
        var r = await runner.RunAsync(opts.HostCronCommand[0], opts.HostCronCommand.Skip(1).ToArray(), opts.CommandTimeoutSeconds, ct);
        return r.Ok
            ? new HostCronResult(true, r.Stdout, null)
            : new HostCronResult(false, r.Stdout, r.Stderr.Trim() is { Length: > 0 } e ? e : $"exit {r.ExitCode}");
    }

    private static Dictionary<string, string> ParseKeyValues(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var i = line.IndexOf('=');
            if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
        }
        return d;
    }
}
