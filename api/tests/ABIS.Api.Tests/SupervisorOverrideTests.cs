using Abis.Api.Security;
using Xunit;

namespace Abis.Api.Tests;

/// <summary>
/// The PIN rules that replace legacy's shared secret.
///
/// <para>Legacy compares the typed digits in plain text against
/// <c>ProfileString(gs_downtime_ini_file, "OPCItems", "is_shift_super_password", "1234")</c> — one
/// shared value, in an INI file on each DAS PC, defaulting to <c>1234</c>. There is no rule about
/// what it may be, because there is nobody to enforce one against.</para>
///
/// <para>These are pure-function tests because the rules are the part that has to be right before
/// anything else matters: a PIN policy that admits <c>1234</c> reproduces the exact secret this work
/// exists to retire, and it would do so while every other test still passed.</para>
/// </summary>
public sealed class SupervisorPinRuleTests
{
    [Fact]
    public void The_legacy_default_is_refused_BY_NAME()
    {
        // Not merely as "a run of digits" — as itself. It is the value the plant has typed for years,
        // so it is the first thing a supervisor asked to pick one will reach for and the first thing
        // anyone on the floor will try. The message says why, because a refusal nobody understands
        // gets worked around.
        var problem = SupervisorOverride.ValidatePin("1234");
        Assert.NotNull(problem);
        Assert.Contains("shared default", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0000")]
    [InlineData("1111")]
    [InlineData("999999")]
    public void A_single_repeated_digit_is_refused(string pin) =>
        Assert.NotNull(SupervisorOverride.ValidatePin(pin));

    [Theory]
    [InlineData("2345")]      // ascending
    [InlineData("9876")]      // descending
    [InlineData("456789")]
    public void A_run_of_consecutive_digits_is_refused(string pin) =>
        Assert.NotNull(SupervisorOverride.ValidatePin(pin));

    [Fact]
    public void A_PIN_with_a_letter_in_it_is_refused_because_the_panel_cannot_type_it()
    {
        // The gate is a numeric keypad on a touch screen — legacy's is a statictext driven by
        // w_enter_num. A PIN containing a letter is one its holder cannot enter at the only place it
        // is used, which turns into a support call at 2am rather than a security improvement.
        var problem = SupervisorOverride.ValidatePin("12a4");
        Assert.NotNull(problem);
        Assert.Contains("digits", problem, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("123")]                 // too short
    [InlineData("1234567890123")]       // too long
    [InlineData("")]
    [InlineData(null)]
    public void Length_is_bounded_at_both_ends(string? pin) =>
        Assert.NotNull(SupervisorOverride.ValidatePin(pin));

    [Theory]
    [InlineData("8471")]
    [InlineData("2906")]
    [InlineData("174350")]
    public void An_ordinary_PIN_is_accepted(string pin) =>
        Assert.Null(SupervisorOverride.ValidatePin(pin));

    [Fact]
    public void The_gated_actions_are_the_ones_legacy_actually_gates()
    {
        // Taken from the live call sites, not from the backlog's description of them. The backlog said
        // "Operation Panel, change-coil, offline sheet"; the call sites say the balance override at
        // end-coil, the shift override, the Operation Panel and the offline sheet.
        Assert.Contains("end-coil-out-of-balance", SupervisorOverride.Actions.Keys);
        Assert.Contains("shift-override", SupervisorOverride.Actions.Keys);
        Assert.Contains("operation-panel", SupervisorOverride.Actions.Keys);
        Assert.Contains("offline-sheet", SupervisorOverride.Actions.Keys);

        Assert.True(SupervisorOverride.IsKnownAction("End-Coil-Out-Of-Balance"), "actions match case-insensitively");
        Assert.False(SupervisorOverride.IsKnownAction("delete-everything"));
        Assert.False(SupervisorOverride.IsKnownAction(null));
    }

    [Fact]
    public void The_balance_tolerance_is_the_legacy_half_a_percent()
    {
        // ir_hl_percent > 0.5 disables the save at u_tabpg_end_coil.sru:757. It is a plant tolerance,
        // not a rounding choice, so it is pinned rather than left inline where it would drift from the
        // console's copy of the same number.
        Assert.Equal(0.5m, SupervisorOverride.EndCoilBalanceTolerancePercent);
    }
}
