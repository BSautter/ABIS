import { describe, it, expect } from 'vitest';

const { endCoilBalancePercent, needsBalanceOverride, END_COIL_BALANCE_TOLERANCE_PERCENT } =
  await import('../src/supervisor-pin.js');

/**
 * The end-coil balance check — the arithmetic behind the one override that matters.
 *
 * A coil's weights are meant to account for themselves: what left as finished skids, plus what left
 * as scrap, plus what is still on the coil, should equal what was on it to begin with. Legacy
 * computes the shortfall as a percentage and, above 0.5%, refuses to let the coil be closed without
 * a shift supervisor — "Please re-check and change skid or scrap weight, and try again! Or ask your
 * shift supervisor override it!" (u_tabpg_end_coil.sru:757).
 *
 * Getting this wrong is expensive in both directions: too sensitive and every coil needs a
 * supervisor, so the override becomes a formality nobody reads; too slack and material walks out of
 * the inventory unnoticed.
 */
describe('endCoilBalancePercent', () => {
  it('is zero when the weights account for themselves', () => {
    // 10,000 lb in; 8,000 of finished skids, 1,500 of scrap, 500 still on the coil.
    expect(endCoilBalancePercent(10000, 8000, 1500, 500)).toBe(0);
  });

  it('measures the shortfall against the STARTING weight, not the total handled', () => {
    // 50 lb unaccounted for on a 10,000 lb coil is 0.5%, not 0.5% of anything else. The denominator
    // is il_old_nt in legacy's formula and it is the only figure that makes the tolerance mean the
    // same thing on a big coil and a small one.
    expect(endCoilBalancePercent(10000, 8000, 1450, 500)).toBeCloseTo(0.5, 6);
  });

  it('treats material APPEARING as the same size of problem as material vanishing', () => {
    // ABS in legacy's formula. Weights that add up to more than went in are just as wrong, and are
    // usually a double-counted skid — which is exactly the mistake the supervisor is being asked to
    // look at before it is written off.
    const short = endCoilBalancePercent(10000, 8000, 1400, 500);   // 100 lb missing
    const over = endCoilBalancePercent(10000, 8000, 1600, 500);    // 100 lb extra
    expect(short).toBeCloseTo(over!, 10);
  });

  it('returns null rather than dividing by a missing starting weight', () => {
    expect(endCoilBalancePercent(0, 100, 0, 0)).toBeNull();
    expect(endCoilBalancePercent(null, 100, 0, 0)).toBeNull();
    expect(endCoilBalancePercent(undefined, 100, 0, 0)).toBeNull();
  });

  it('treats absent component weights as zero, not as unknown', () => {
    // A coil with no scrap recorded has no scrap, and the operator saving it means that.
    expect(endCoilBalancePercent(10000, 10000, null, 0)).toBe(0);
  });
});

describe('needsBalanceOverride', () => {
  it('needs a supervisor only ABOVE the tolerance, not at it', () => {
    // Legacy's test is `ir_hl_percent > 0.5`, strictly greater. A coil sitting exactly on the
    // tolerance is within it, and flipping that comparison would send every borderline coil looking
    // for a supervisor.
    expect(needsBalanceOverride(END_COIL_BALANCE_TOLERANCE_PERCENT)).toBe(false);
    expect(needsBalanceOverride(0.51)).toBe(true);
    expect(needsBalanceOverride(0.49)).toBe(false);
  });

  it('an unknown balance is NOT treated as out of balance', () => {
    // A figure nobody can compute is not evidence of a discrepancy — blocking on it would stop
    // coils being closed because of missing reference data rather than missing metal.
    expect(needsBalanceOverride(null)).toBe(false);
  });

  it('matches the legacy tolerance', () => {
    expect(END_COIL_BALANCE_TOLERANCE_PERCENT).toBe(0.5);
  });
});
