// Truck check-in windows — fixed 30-minute slots between 04:00 and 18:00, the plant's gate hours.
//
// Scheduling is "pick a date, pick a slot" rather than two free datetime boxes. Two boxes can
// disagree with each other, can describe a three-hour window, and can put a truck on the yard at
// 02:00; a slot cannot do any of those. The end time is DERIVED from the slot and never entered,
// which is what makes every window exactly 30 minutes rather than merely usually 30 minutes.
//
// The last slot STARTS at 17:30 so no window runs past 6 PM — 28 slots a day.
//
// Pure + DOM-free so it can be unit-tested; truck-scheduling.ts imports it.
export const SLOT_MINUTES = 30;
export const SLOT_FIRST_HOUR = 4;
export const SLOT_LAST_HOUR = 18;
const hhmm = (minutes) => `${String(Math.floor(minutes / 60)).padStart(2, '0')}:${String(minutes % 60).padStart(2, '0')}`;
/** Every slot start as "HH:MM", 04:00 through 17:30. */
export function slotStarts() {
    const out = [];
    for (let m = SLOT_FIRST_HOUR * 60; m + SLOT_MINUTES <= SLOT_LAST_HOUR * 60; m += SLOT_MINUTES)
        out.push(hhmm(m));
    return out;
}
/** The slot as the yard says it: "04:00 - 04:30". */
export function slotLabel(start) {
    const [h, m] = start.split(':').map(Number);
    return `${start} - ${hhmm(h * 60 + m + SLOT_MINUTES)}`;
}
/**
 * A local date ("YYYY-MM-DD") plus a slot start ("HH:MM") as the window's two instants.
 *
 * Built from the date's parts rather than `new Date("2026-08-20T04:00")`, because a bare
 * "YYYY-MM-DD" parses as UTC while a date-with-time parses as local — mixing the two silently
 * shifts a window by the UTC offset, which in this plant's timezone would move a 04:00 slot to the
 * previous evening.
 */
export function slotWindow(date, start) {
    const [y, mo, d] = date.split('-').map(Number);
    const [h, mi] = start.split(':').map(Number);
    const from = new Date(y, mo - 1, d, h, mi, 0, 0);
    return { start: from, end: new Date(from.getTime() + SLOT_MINUTES * 60000) };
}
