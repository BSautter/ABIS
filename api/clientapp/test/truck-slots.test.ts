import { describe, it, expect } from 'vitest';
import { slotStarts, slotLabel, slotWindow, SLOT_MINUTES } from '../src/truck-slots.js';

describe('truck check-in slots', () => {
  it('runs 04:00 to 17:30 in half hours', () => {
    const s = slotStarts();
    expect(s[0]).toBe('04:00');
    expect(s[1]).toBe('04:30');
    expect(s.at(-1)).toBe('17:30');
    expect(s).toHaveLength(28);
  });

  // The boundary that decides whether the plant's gate hours are respected: a slot starting at
  // 18:00 would end at 18:30, half an hour after the yard closes.
  it('never offers a window that runs past 6 PM', () => {
    expect(slotStarts()).not.toContain('18:00');
    for (const t of slotStarts()) {
      const { end } = slotWindow('2026-08-20', t);
      expect(end.getHours() * 60 + end.getMinutes()).toBeLessThanOrEqual(18 * 60);
    }
  });

  it('labels a slot the way the yard says it', () => {
    expect(slotLabel('04:00')).toBe('04:00 - 04:30');
    expect(slotLabel('17:30')).toBe('17:30 - 18:00');
  });

  it('derives the end from the start so a window is always exactly 30 minutes', () => {
    for (const t of slotStarts()) {
      const { start, end } = slotWindow('2026-08-20', t);
      expect((end.getTime() - start.getTime()) / 60_000).toBe(SLOT_MINUTES);
    }
  });

  // A bare "YYYY-MM-DD" parses as UTC while a date-with-time parses as local. Mixing the two shifts
  // a window by the UTC offset — which for this plant would move an 04:00 slot to the evening
  // before. Building from the parts keeps it local, whatever the runner's timezone.
  it('keeps the window on the local date it was picked for', () => {
    const { start } = slotWindow('2026-08-20', '04:00');
    expect(start.getFullYear()).toBe(2026);
    expect(start.getMonth()).toBe(7);   // August, 0-based
    expect(start.getDate()).toBe(20);
    expect(start.getHours()).toBe(4);
    expect(start.getMinutes()).toBe(0);
  });

  it('crosses midnight correctly at the end of a month', () => {
    const { start, end } = slotWindow('2026-08-31', '17:30');
    expect(start.getDate()).toBe(31);
    expect(end.getDate()).toBe(31);
    expect(end.getHours()).toBe(18);
  });
});
