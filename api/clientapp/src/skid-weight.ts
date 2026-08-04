// The rules that decide what weight may be written to a finished skid.
//
// Extracted from the DAS console's Pull button so they can be tested. They previously sat inline in a
// page module with import-time side effects, which meant the logic deciding what lands in
// `sheet_net_wt` — the figure invoicing and the 856 ASN are built from — could only be checked by
// driving a browser by hand.
//
// Every rule here came from the legacy stacker window (`w_da_sheet_110_stacker.srw`) or from a live
// observation, and each is a way the button could otherwise record a number the scale never gave.

import type { ConveyorResult, StackerResult } from './edge.js';

/**
 * The conveyor cells that mean "a stack is on the scale".
 *
 * Legacy read the scale only with the stack at location 3 or 4, else refused with "Stack not on
 * Conveyor1!, Can not read scale." Cells 3 and 4 are `StackOnConveyor1` and `StackLeavingConveyor1`.
 * Still correct after the plant removed wrapper 2 and everything past it — the scale never moved
 * (plant, 2026-08-02).
 */
export const SCALE_CELLS = [3, 4] as const;

/** Legacy's plausibility band: `if ll_nw < 10 or ll_nw > 39000 then ... "Invalid weight!!"`. */
export const MIN_SKID_LB = 10;
export const MAX_SKID_LB = 39000;

export type WeightDecision =
  | { ok: true; netLb: number; via: string }
  | { ok: false; reason: string };

/**
 * Decide whether the conveyor scale's reading may be recorded as this skid's NET weight.
 *
 * Net, not gross: a bare stack on the conveyor has no pallet under it, and legacy wrote this reading
 * straight to `sheet_net_wt` via `update_sheet_skid_wt`. (The inv_coil FLOOR scale is the one that
 * reads gross, because a skid there is sitting on its pallet.)
 *
 * Refusals carry the reason the operator needs, because every one of them ends the same way — weigh
 * the skid and type it in — and "failed" alone would not tell them whether to wait, fix a setting, or
 * call someone.
 */
export function decideConveyorWeight(args: {
  lineName: string;
  scaleTag: string;
  conveyor: Pick<ConveyorResult, 'reachable' | 'configured' | 'cells'>;
  stacker: Pick<StackerResult, 'reachable' | 'scaleWeight' | 'via'>;
}): WeightDecision {
  const { lineName, scaleTag, conveyor, stacker } = args;

  if (!conveyor.reachable)
    return { ok: false, reason: 'Edge unreachable on every host — weigh the skid and enter the weight manually.' };

  if (!conveyor.configured)
    return { ok: false, reason: `${lineName} has no conveyor scale — weigh the skid and enter the weight manually.` };

  // `occupied === true` only. A null cell is an unreadable sensor, and treating unknown as "on the
  // scale" is how you record the weight of an empty belt: an idle BL 110 reads ScaleSkidWt = 0 with
  // every cell clear, verified live.
  if (!SCALE_CELLS.some((c) => conveyor.cells.get(c)?.occupied === true))
    return { ok: false, reason: 'No stack on Conveyor 1 yet — the scale reads nothing until the stack reaches it.' };

  // The scale must be bound to THIS line. The edge's /stacker defaults resolve to stacker110, so a
  // console left on the defaults would hand BL 110's weight to another line's skid.
  if (!scaleTag)
    return {
      ok: false,
      reason: 'Set this line’s conveyor scale tag in the ⚖ Scale bar above (e.g. stacker110.ScaleSkidWt, or use 🔎) '
        + 'before pulling — otherwise the edge would answer with another line’s scale.',
    };

  // null is unknown, never zero. A configured tag reading Bad quality comes back null — BL 84's
  // stripped OPC branch reads exactly that way.
  if (!stacker.reachable || stacker.scaleWeight == null)
    return { ok: false, reason: 'The conveyor scale did not answer — weigh the skid and enter the weight manually.' };

  if (stacker.scaleWeight < MIN_SKID_LB || stacker.scaleWeight > MAX_SKID_LB)
    return {
      ok: false,
      reason: `The conveyor scale reads ${stacker.scaleWeight.toLocaleString()} lb, outside the `
        + `${MIN_SKID_LB}–${MAX_SKID_LB.toLocaleString()} lb range a skid can be — check the scale.`,
    };

  return { ok: true, netLb: stacker.scaleWeight, via: stacker.via };
}
