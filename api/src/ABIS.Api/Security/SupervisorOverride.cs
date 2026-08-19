namespace Abis.Api.Security;

/// <summary>
/// The shop-floor supervisor override — what legacy gates behind <c>w_super_validation</c>, and the
/// rules that replace its shared plaintext PIN.
///
/// <para><b>What legacy does.</b> A response window with an on-screen numeric keypad compares the
/// typed digits in plain text:</para>
/// <code>
/// if parent.st_password.text = is_pw then CloseWithReturn(parent, 1)
/// </code>
/// <para>where <c>is_pw</c> is
/// <c>ProfileString(gs_downtime_ini_file, "OPCItems", "is_shift_super_password", "1234")</c> — one
/// shared secret, in an INI file on each DAS PC, defaulting to <c>1234</c>. Not per-supervisor, not in
/// the database, not hashed, and with no limit on attempts. The window even has a "Shift supervisor"
/// caption with a name field beside it, which is never populated and reads "none" forever: legacy has
/// nowhere to get the supervisor's identity from.</para>
///
/// <para><b>What is kept and what is not.</b> <i>Whether</i> an override is gated is plant behaviour
/// and stays exactly where legacy puts it. <i>How</i> it authenticates is replaced: a per-supervisor
/// PIN, hashed, rate-limited, and — the part legacy could never do — <b>attributed</b>.</para>
/// </summary>
public static class SupervisorOverride
{
    /// <summary>
    /// The actions legacy actually gates, taken from the live call sites rather than from the backlog's
    /// description of them. Each is the exact override an operator is asking permission for, which is
    /// what makes the audit row worth reading a month later.
    ///
    /// <para><b>The important one is <see cref="EndCoilOutOfBalance"/>.</b> At end-coil legacy computes
    /// <c>ir_hl_percent</c> — the weight it cannot account for, as a percentage of the coil's starting
    /// net weight — and if it exceeds 0.5% it DISABLES the OK button, shows an Override button, and
    /// says: "Please re-check and change skid or scrap weight, and try again! Or ask your shift
    /// supervisor override it!" (<c>u_tabpg_end_coil.sru:757</c>). Supervisor validation sets
    /// <c>ii_super = 1</c>, which is the only thing that re-enables the save. So the PIN's real subject
    /// is: <i>who decided this coil's material could be written off as unaccounted for.</i></para>
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Actions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // u_tabpg_end_coil.sru:632 -> ii_super = 1, bypassing the >0.5% balance check at :757.
            ["end-coil-out-of-balance"] = "Close a coil whose weights do not balance",
            // w_da_sheet.srw:1294, inside wf_shift_override, before Open(iw_override).
            ["shift-override"] = "Override the shift end time",
            // w_operation_panel.srw:425 — the panel's login tab.
            ["operation-panel"] = "Open the Operation Panel",
            // w_da_offline_sheet.srw:1934 — the same gate on the offline station.
            ["offline-sheet"] = "Use the offline production sheet",
        };

    public const string EndCoilOutOfBalance = "end-coil-out-of-balance";

    /// <summary>The balance discrepancy above which legacy refuses to close a coil without a
    /// supervisor: <c>ir_hl_percent &gt; 0.5</c>. Held here as a named constant because it is a plant
    /// tolerance rather than a rounding choice, and because the DAS console needs the same number.</summary>
    public const decimal EndCoilBalanceTolerancePercent = 0.5m;

    public static bool IsKnownAction(string? action) =>
        !string.IsNullOrWhiteSpace(action) && Actions.ContainsKey(action.Trim());

    // ---- PIN rules ------------------------------------------------------------------------

    /// <summary>Shortest PIN accepted. Four is what the plant is used to typing; anything shorter is
    /// not a secret at all.</summary>
    public const int MinPinLength = 4;

    /// <summary>Longest PIN accepted. A PIN is entered on a touch panel with gloves on — past this it
    /// stops being usable and people write it on the machine.</summary>
    public const int MaxPinLength = 12;

    /// <summary>Consecutive failures before the PIN locks. Deliberately small: the search space of a
    /// four-digit PIN is 10,000, so a generous allowance is not a speed bump.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long a locked PIN stays locked. Long enough to make guessing pointless, short
    /// enough that a supervisor who fat-fingered it twice is not out of action for a shift.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Whether a PIN is acceptable. Digits only — it is typed on a numeric keypad, so a PIN with a
    /// letter in it is one nobody can enter at the panel it exists for.
    ///
    /// <para>Trivial sequences are refused by name, and <b>"1234" most of all</b>: it is the value
    /// legacy shipped as its default, so it is the first thing anyone on the floor will try and the
    /// first thing a supervisor will reach for when asked to pick one. Allowing it would reproduce the
    /// exact secret this work exists to retire.</para>
    /// </summary>
    public static string? ValidatePin(string? pin)
    {
        if (string.IsNullOrWhiteSpace(pin))
            return "A PIN is required.";
        if (pin.Length < MinPinLength || pin.Length > MaxPinLength)
            return $"The PIN must be {MinPinLength}–{MaxPinLength} digits.";
        if (!pin.All(char.IsAsciiDigit))
            return "The PIN must be digits only — it is typed on the panel's numeric keypad.";
        // Checked BEFORE the generic run-of-digits rule, which would otherwise swallow it. The generic
        // message ("not a run of consecutive digits") is true but teaches nothing; naming the legacy
        // default is what stops the person choosing it from choosing 1235 next.
        if (pin is "1234")
            return "1234 was the shared default this replaces — choose something else.";
        if (pin.Distinct().Count() == 1)
            return "The PIN cannot be a single repeated digit.";
        if (IsRunOfDigits(pin))
            return "The PIN cannot be a run of consecutive digits.";
        return null;
    }

    private static bool IsRunOfDigits(string pin)
    {
        var up = true;
        var down = true;
        for (var i = 1; i < pin.Length; i++)
        {
            if (pin[i] != pin[i - 1] + 1) up = false;
            if (pin[i] != pin[i - 1] - 1) down = false;
        }
        return up || down;
    }
}
