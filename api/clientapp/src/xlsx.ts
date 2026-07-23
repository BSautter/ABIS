// Minimal, dependency-free .xlsx (OOXML SpreadsheetML) writer. Produces a real .xlsx that Excel,
// LibreOffice, and Google Sheets open natively — a STORED (uncompressed) ZIP of the five required
// parts with inline strings (no shared-string table, no styles). Enough for tabular report export,
// and — unlike the CSV path — numbers stay numeric so Excel sums/sorts them without a re-type.
//
// Why hand-rolled: the app ships no bundler and the Artifact/serve CSP blocks external scripts, so a
// SheetJS-style dependency isn't an option. A STORED zip needs no DEFLATE, just correct CRC32s +
// headers, which is a few dozen lines.
//
// Compiled by `tsc` to wwwroot/ui/app/xlsx.js.

// A cell is a number (→ numeric cell) or text (→ inline string); null/blank renders as an empty cell.
export type Cell = number | string | null | undefined;

const enc = new TextEncoder();

const xmlEsc = (s: string): string =>
  s.replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[c] as string));

// ---- CRC32 (required for every ZIP entry, even STORED) ----
const CRC_TABLE = ((): Uint32Array => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = (c & 1) ? (0xedb88320 ^ (c >>> 1)) : (c >>> 1);
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(bytes: Uint8Array): number {
  let c = 0xffffffff;
  for (let i = 0; i < bytes.length; i++) c = CRC_TABLE[(c ^ bytes[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

// ---- Worksheet XML ----
// Excel column name for a 0-based index: 0→A, 25→Z, 26→AA, …
function colName(index: number): string {
  let s = '';
  let i = index + 1;
  while (i > 0) { const m = (i - 1) % 26; s = String.fromCharCode(65 + m) + s; i = ((i - m) / 26) | 0; }
  return s;
}
function cellXml(v: Cell, ref: string): string {
  if (v == null || v === '') return '';
  if (typeof v === 'number' && Number.isFinite(v)) return `<c r="${ref}"><v>${v}</v></c>`;
  return `<c r="${ref}" t="inlineStr"><is><t xml:space="preserve">${xmlEsc(String(v))}</t></is></c>`;
}
function sheetXml(headers: string[], rows: Cell[][]): string {
  const all: Cell[][] = [headers, ...rows];
  const body = all.map((cells, r) =>
    `<row r="${r + 1}">${cells.map((v, c) => cellXml(v, colName(c) + (r + 1))).join('')}</row>`).join('');
  return '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
    + '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">'
    + `<sheetData>${body}</sheetData></worksheet>`;
}

// ---- Fixed package parts ----
const CONTENT_TYPES =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
  + '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
  + '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>'
  + '<Default Extension="xml" ContentType="application/xml"/>'
  + '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>'
  + '<Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>'
  + '</Types>';
const RELS =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
  + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
  + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>'
  + '</Relationships>';
const WORKBOOK_RELS =
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
  + '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">'
  + '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>'
  + '</Relationships>';
const workbookXml = (sheetName: string): string =>
  '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>'
  + '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">'
  + `<sheets><sheet name="${xmlEsc(sheetName)}" sheetId="1" r:id="rId1"/></sheets></workbook>`;

// ---- STORED (uncompressed) ZIP container ----
interface ZipEntry { name: string; data: Uint8Array; }
const u16 = (n: number) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff]);
const u32 = (n: number) => new Uint8Array([n & 0xff, (n >>> 8) & 0xff, (n >>> 16) & 0xff, (n >>> 24) & 0xff]);
function concat(parts: Uint8Array[]): Uint8Array {
  let len = 0; for (const p of parts) len += p.length;
  const out = new Uint8Array(len); let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}
function zip(entries: ZipEntry[]): Uint8Array {
  const DOS_DATE = 0x21; // 1980-01-01 — a fixed valid MS-DOS date (bits: year<<9 | month<<5 | day)
  const locals: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;
  for (const e of entries) {
    const nameBytes = enc.encode(e.name);
    const crc = crc32(e.data);
    const local = concat([
      u32(0x04034b50), u16(20), u16(0), u16(0), u16(0), u16(DOS_DATE),
      u32(crc), u32(e.data.length), u32(e.data.length),
      u16(nameBytes.length), u16(0), nameBytes, e.data,
    ]);
    locals.push(local);
    central.push(concat([
      u32(0x02014b50), u16(20), u16(20), u16(0), u16(0), u16(0), u16(DOS_DATE),
      u32(crc), u32(e.data.length), u32(e.data.length),
      u16(nameBytes.length), u16(0), u16(0), u16(0), u16(0), u32(0), u32(offset), nameBytes,
    ]));
    offset += local.length;
  }
  const centralStart = offset;
  let centralSize = 0; for (const c of central) centralSize += c.length;
  const eocd = concat([
    u32(0x06054b50), u16(0), u16(0), u16(entries.length), u16(entries.length),
    u32(centralSize), u32(centralStart), u16(0),
  ]);
  return concat([...locals, ...central, eocd]);
}

// Build the raw .xlsx bytes for a single sheet (no DOM — usable/testable outside the browser).
// Sheet names are capped at Excel's 31 chars and stripped of the characters Excel forbids ( []:*?/\ ).
export function buildXlsx(sheetName: string, headers: string[], rows: Cell[][]): Uint8Array {
  const safeSheet = (sheetName.replace(/[\[\]:*?/\\]/g, ' ').trim().slice(0, 31)) || 'Sheet1';
  return zip([
    { name: '[Content_Types].xml', data: enc.encode(CONTENT_TYPES) },
    { name: '_rels/.rels', data: enc.encode(RELS) },
    { name: 'xl/workbook.xml', data: enc.encode(workbookXml(safeSheet)) },
    { name: 'xl/_rels/workbook.xml.rels', data: enc.encode(WORKBOOK_RELS) },
    { name: 'xl/worksheets/sheet1.xml', data: enc.encode(sheetXml(headers, rows)) },
  ]);
}

// Build a one-sheet .xlsx and trigger a browser download.
export function exportXlsx(filename: string, sheetName: string, headers: string[], rows: Cell[][]): void {
  const bytes = buildXlsx(sheetName, headers, rows);
  const blob = new Blob([bytes], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename.endsWith('.xlsx') ? filename : `${filename}.xlsx`;
  a.click();
  URL.revokeObjectURL(url);
}
