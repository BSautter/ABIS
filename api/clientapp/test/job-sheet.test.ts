import { describe, it, expect } from 'vitest';

const { jobSheetHtml } = await import('../src/job-sheet.js');

/**
 * The job sheet's rendering.
 *
 * Every figure on this document is a number someone cuts metal to, which makes the interesting
 * assertions the ones about what happens when a figure is ABSENT, and about the two warnings across
 * the top. Neither is visible in a passing screenshot; both are visible on a ruined skid.
 */
describe('jobSheetHtml', () => {
  const base = {
    abJobNum: 1001,
    lineDesc: 'BL 84',
    customer: 'NOVELIS-OSWEGO',
    endUser: 'OGIHARA',
    sheetType: 'Trapezoid',
    width: { name: 'width', value: 61.22, plusTol: 0.157, minusTol: 0 },
    length: { name: 'longLength', value: 84.016, plusTol: 0.138, minusTol: 0 },
    gauge: 0.0354,
    coils: [],
    partials: [],
    packagingSpecs: [],
    partialSkidNote: '',
  } as never;

  const render = (over: Record<string, unknown> = {}): string =>
    jobSheetHtml({ ...(base as object), ...over } as never);

  // ---- Absence ---------------------------------------------------------------------------

  it('renders a missing dimension as a dash, never as a zero', () => {
    // A circle has no length. "0.000" beside a tolerance reads as a dimension to cut to, and it is
    // the one rendering mistake here that produces scrap rather than a support call.
    const html = render({ length: undefined });
    expect(html).toContain('—');
    expect(html).not.toMatch(/Length[\s\S]{0,120}<b>0<\/b>/);
  });

  it('names the dimension the shape actually has', () => {
    // "Width: 36.5" on a circle is a lie the operator cannot check without opening the order.
    expect(render({ width: { name: 'diameter', value: 36.5 } })).toContain('Diameter');
    expect(render({ length: { name: 'longLength', value: 84 } })).toContain('Long length');
  });

  it('prints a missing tolerance as an explicit zero rather than leaving the slot empty', () => {
    // Legacy prints "+ 0.000" / "- 0.000". A blank where a tolerance belongs reads as "not
    // measured"; "no tolerance given" is a different statement and the sheet distinguishes them.
    const html = render({ width: { name: 'width', value: 24 } });
    expect(html).toContain('+0');
    expect(html).toContain('-0');
  });

  // ---- The warnings ------------------------------------------------------------------------

  it('shows EDGE TRIMMING REQUIRED only when the job is trimmed', () => {
    expect(render({ trimmingRequired: true })).toContain('EDGE TRIMMING REQUIRED');
    expect(render({ trimmingRequired: false })).not.toContain('EDGE TRIMMING REQUIRED');
  });

  it('shows the foreman warning only when the trimmed width was overridden', () => {
    expect(render({ trimmingRequired: true, trimmedWidthOverridden: true }))
      .toContain('CONTACT FOREMAN BEFORE RUNNING');
    expect(render({ trimmingRequired: true, trimmedWidthOverridden: false }))
      .not.toContain('CONTACT FOREMAN');
  });

  // ---- By-lot ------------------------------------------------------------------------------

  it('an ordinary job states pieces per skid; a by-lot job says to read the coil table', () => {
    expect(render({ piecesPerSkid: 293 })).toContain('293');
    expect(render({ byLot: true, piecesPerSkid: 293 })).toContain('See below');
  });

  it('the coil table gains its per-coil columns only on a by-lot job', () => {
    const coils = [{ lotNum: 'W1475178', coilOrgNum: 'F14655', coilAbcNum: 234534, processQuantity: 24019, skids: 5, piecesPerSkid: 200 }];
    expect(render({ coils })).not.toContain('Pc./skid</th>');
    expect(render({ coils, byLot: true })).toContain('Pc./skid</th>');
  });

  // ---- Lists ---------------------------------------------------------------------------------

  it('keeps the numbered packaging lines on their own numbers', () => {
    // They are referred to by number on the floor. Compacting out the empty ones renumbers the rest,
    // so "packaging 4" on a phone call stops meaning the same thing as "packaging 4" on the sheet.
    const html = render({ packagingSpecs: ['Stretch wrap', undefined, 'Corner boards'] });
    expect(html).toContain('value="1"');
    expect(html).toContain('value="3"');
    expect(html).not.toContain('value="2"');
  });

  it('omits a list entirely when every line of it is empty', () => {
    expect(render({ packagingSpecs: [undefined, undefined] })).not.toContain('Packaging');
  });

  // ---- Partials --------------------------------------------------------------------------------

  it('shows the carried-in partial table only when there is something carried in', () => {
    expect(render({})).not.toContain('carried in from another job');
    expect(render({ partials: [{ sheetSkidNum: 844792, madeOnJob: '124314', lotNum: 'W1475318' }] }))
      .toContain('carried in from another job');
  });

  // ---- Escaping ---------------------------------------------------------------------------------

  it('escapes free text, which on this document includes operator-written notes', () => {
    const html = render({ jobNotes: '<script>alert(1)</script>' });
    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;script&gt;');
  });
});
