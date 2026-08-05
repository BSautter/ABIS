#!/usr/bin/env python3
"""Extract object source (DataWindows, windows, user objects) from a PowerBuilder 9 .pbl.

WHY THIS EXISTS
`legacy/src/` vendors 761 .srd DataWindows exported from the .pbl libraries, but the export was
partial: the `silverdome*` / `aaaa` CORE libraries were left out for size (~1.1 GB with binaries, see
legacy/src/README.md), and some objects in the vendored modules were missed too. The 6x10 shipping
label is one of them - `u_default_barcode.sru` is vendored and populates ~54 named controls, but the
DataWindow carrying their POSITIONS lives in silverdome7.pbl and was never exported.

Without geometry a label port is guesswork, and a label only fails visibly on paper.

WHAT A .PBL ACTUALLY IS (measured, not documented)
A sequence of 512-byte blocks. Each begins with the marker `DAT*` followed by 6 header bytes; the
remaining 502 bytes are payload. Object source is stored as PLAIN TEXT in that payload, so simply
dropping the 10-byte headers and concatenating reconstructs it. The blocking is why a naive `grep`
finds a control name but the text around it looks corrupted - a word is split across a boundary
("band=hea" + header + "der").

Entries begin with `release 9;`, preceded by the object's comment.

WHAT THIS DOES NOT DO
It does not produce a re-importable .srd - there is no `$PBExportHeader$` line and no attempt to find
the true object name, which lives in the library directory rather than beside the source. This is for
READING: geometry, fonts, and the retrieve SQL. Use the PowerBuilder IDE's Library painter if you need
a genuine export.

    python tools/pbl_extract.py <file.pbl> --list
    python tools/pbl_extract.py <file.pbl> --containing name=shipping_date_large_t -o out/
"""
import argparse
import pathlib
import re
import sys

BLOCK = 512
HEADER = 10          # b'DAT*' + 6


def deblock(raw: bytes) -> bytes:
    """Strip the 512-byte block headers and return the contiguous payload."""
    out = bytearray()
    for i in range(0, len(raw), BLOCK):
        block = raw[i:i + BLOCK]
        if block[:4] == b'DAT*':
            out += block[HEADER:]
        else:
            out += block          # not a data block (directory/other) - keep it, callers grep anyway
    return bytes(out)


def entries(payload: bytes):
    """Yield (comment, body) per object. Entries start at 'release 9;'; the comment precedes it."""
    starts = [m.start() for m in re.finditer(rb'release 9;', payload)]
    for i, s in enumerate(starts):
        end = starts[i + 1] if i + 1 < len(starts) else len(payload)
        # The comment is the printable run immediately before the marker.
        m = re.search(rb'([ -~]{3,80})$', payload[max(0, s - 100):s])
        yield (m.group(1).decode('latin-1').strip() if m else ''), payload[s:end]


def describe(body: bytes) -> str:
    du = re.search(rb'datawindow\(units=(\d+)', body)
    det = re.search(rb'detail\(height=(\d+)', body)
    prn = re.search(rb'print\.printername="([^"]*)"', body)
    ctrls = len(re.findall(rb'\b(?:text|compute|column|bitmap)\(band=', body))
    return (f"units={du.group(1).decode() if du else '-':<2} "
            f"detailH={det.group(1).decode() if det else '-':<7} "
            f"controls={ctrls:<4} "
            f"printer={(prn.group(1).decode('latin-1') if prn else '')[:28]}")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('pbl', type=pathlib.Path)
    ap.add_argument('--list', action='store_true', help='list every object entry')
    ap.add_argument('--containing', help='only entries whose source contains this literal')
    ap.add_argument('-o', '--out', type=pathlib.Path, help='write matching entries here as .txt')
    a = ap.parse_args()

    if not a.pbl.exists():
        print(f'no such file: {a.pbl}', file=sys.stderr)
        return 2

    payload = deblock(a.pbl.read_bytes())
    needle = a.containing.encode() if a.containing else None

    n = 0
    for i, (comment, body) in enumerate(entries(payload)):
        if needle and needle not in body:
            continue
        # Skip entries with no DataWindow header when we are clearly after layouts.
        if needle and b'datawindow(units=' not in body:
            continue
        n += 1
        if a.list or needle:
            print(f'[{i:>4}] {len(body):>8}b  {describe(body)}  {comment[:46]}')
        if a.out:
            a.out.mkdir(parents=True, exist_ok=True)
            safe = re.sub(r'[^A-Za-z0-9_.-]', '_', comment)[:60] or f'entry_{i}'
            (a.out / f'{i:04d}_{safe}.txt').write_bytes(body)

    print(f'\n{n} entr{"y" if n == 1 else "ies"} matched'
          + (f'; written to {a.out}' if a.out else ''))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
