// ABIS table tools — reusable client-side column sorting + a per-table filter box for the shared data
// tables (`table.tbl`). Zero-per-page: the shell calls observeTables() on the content area, which
// enhances every current + future `table.tbl` as pages render them. It works purely on the rendered
// DOM (reads cell text), so it needs nothing from each page's data model, and it survives the common
// pattern where a page reloads only a table's <tbody> (the <table> element and its filter persist).
// Each enhanced table's filter re-applier, so a <tbody> reload can re-hide filtered-out rows.
const refilters = new WeakMap();
// A placeholder row (a single cell spanning the table, e.g. "No data") isn't real data — never sort,
// filter, or count it; keep it in place.
const isDataRow = (tr) => !tr.querySelector('td[colspan], th[colspan]');
const cellText = (tr, col) => (tr.cells[col]?.textContent ?? '').trim();
// Parse a numeric cell tolerant of grouping/currency/units ("1,980", "$4,000", "12.5 lb"). Null = not
// a number (sorts after real numbers).
function toNum(s) {
    const t = s.replace(/[,$\s]/g, '').replace(/[^\d.eE+-].*$/, '');
    if (t === '' || t === '-' || t === '.')
        return null;
    const n = Number(t);
    return Number.isFinite(n) ? n : null;
}
const looksDate = (s) => /\d{4}-\d\d-\d\d|\d{1,2}[/]\d{1,2}[/]\d{2,4}/.test(s);
function compare(a, b, numeric) {
    if (numeric) {
        const na = toNum(a), nb = toNum(b);
        if (na != null && nb != null)
            return na - nb;
        if (na != null)
            return -1; // real numbers before blanks/dashes
        if (nb != null)
            return 1;
    }
    else if (looksDate(a) && looksDate(b)) {
        const da = Date.parse(a), db = Date.parse(b);
        if (!Number.isNaN(da) && !Number.isNaN(db))
            return da - db;
    }
    // Locale compare with numeric collation handles embedded numbers ("job 2" < "job 10") sensibly.
    return a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' });
}
function sortBy(table, col, dir, numeric) {
    const tbody = table.tBodies[0];
    if (!tbody)
        return;
    const rows = [...tbody.rows];
    const data = rows.filter(isDataRow);
    const placeholders = rows.filter((r) => !isDataRow(r));
    const mul = dir === 'ascending' ? 1 : -1;
    // Stable sort by the chosen column; re-append moves the existing <tr> nodes (row click handlers,
    // e.g. tap-to-reprint, are preserved — the elements aren't recreated).
    data.map((tr, i) => [tr, i])
        .sort(([ra, ia], [rb, ib]) => mul * compare(cellText(ra, col), cellText(rb, col), numeric) || (ia - ib))
        .forEach(([tr]) => tbody.appendChild(tr));
    placeholders.forEach((p) => tbody.appendChild(p)); // keep any placeholder row last
}
/** Enhance one data table: sortable headers + a filter box. Idempotent (guarded per element). */
export function enhanceTable(table) {
    if (table.dataset.enh)
        return;
    const thead = table.tHead;
    const tbody = table.tBodies[0];
    if (!thead || !thead.rows.length || !tbody)
        return; // needs a header + body (skips key/value panels)
    table.dataset.enh = '1';
    const headRow = thead.rows[thead.rows.length - 1]; // the column-label row
    [...headRow.cells].forEach((th) => {
        th.style.cursor = 'pointer';
        th.style.userSelect = 'none';
        if (!th.title)
            th.title = 'Sort by this column';
        th.setAttribute('role', 'button');
    });
    // Sort via delegation on the <table> so it survives a <tbody> innerHTML reload.
    table.addEventListener('click', (e) => {
        const th = e.target.closest('th');
        if (!th || th.parentElement !== headRow)
            return;
        const col = [...headRow.cells].indexOf(th);
        if (col < 0)
            return;
        const dir = th.getAttribute('aria-sort') === 'ascending' ? 'descending' : 'ascending';
        sortBy(table, col, dir, th.classList.contains('num'));
        [...headRow.cells].forEach((c) => { c.removeAttribute('aria-sort'); c.querySelector('.th-sort')?.remove(); });
        th.setAttribute('aria-sort', dir);
        const ind = document.createElement('span');
        ind.className = 'th-sort';
        ind.textContent = dir === 'ascending' ? ' ▲' : ' ▼';
        ind.style.cssText = 'opacity:.65;font-size:10px';
        th.appendChild(ind);
    });
    // Filter box, inserted above the table. Hides non-matching data rows; a <tbody> reload re-applies it.
    const bar = document.createElement('div');
    bar.className = 'tbl-filter';
    bar.style.cssText = 'display:flex;align-items:center;gap:10px;margin:0 0 8px';
    const input = document.createElement('input');
    input.type = 'search';
    input.placeholder = 'Filter…';
    input.setAttribute('aria-label', 'Filter table rows');
    input.style.cssText = 'padding:5px 9px;font-size:13px;border-radius:8px;border:1px solid var(--line-2,#8886);background:var(--surface,transparent);color:inherit;width:210px;max-width:60%';
    const count = document.createElement('span');
    count.style.cssText = 'font-size:12px;opacity:.7';
    bar.append(input, count);
    const apply = () => {
        const q = input.value.trim().toLowerCase();
        let shown = 0, total = 0;
        for (const tr of [...tbody.rows]) {
            if (!isDataRow(tr))
                continue;
            total++;
            const match = !q || (tr.textContent ?? '').toLowerCase().includes(q);
            tr.style.display = match ? '' : 'none';
            if (match)
                shown++;
        }
        count.textContent = q ? `${shown} of ${total}` : '';
    };
    refilters.set(table, apply);
    input.addEventListener('input', apply);
    // Place the filter above the table's horizontal-scroll wrapper (the common
    // `<div style="overflow-x:auto"><table>` pattern) so it never scrolls off on a wide table.
    const parent = table.parentElement;
    const anchor = parent && /auto|scroll/.test(getComputedStyle(parent).overflowX) ? parent : table;
    anchor.insertAdjacentElement('beforebegin', bar);
}
/**
 * Enhance every `table.tbl` under `root` now, and watch for tables (and reloaded rows) added later.
 * Called once by the shell — pages need no per-page wiring.
 */
export function observeTables(root) {
    root.querySelectorAll('table.tbl').forEach(enhanceTable);
    const obs = new MutationObserver((records) => {
        const reload = new Set();
        for (const rec of records) {
            rec.addedNodes.forEach((n) => {
                if (!(n instanceof HTMLElement))
                    return;
                if (n.matches('table.tbl'))
                    enhanceTable(n);
                n.querySelectorAll('table.tbl').forEach(enhanceTable);
            });
            // Rows re-rendered into an enhanced table's <tbody> → re-apply its active filter.
            const tbl = rec.target?.closest?.('table.tbl[data-enh]');
            if (tbl)
                reload.add(tbl);
        }
        reload.forEach((t) => refilters.get(t)?.());
    });
    obs.observe(root, { childList: true, subtree: true });
}
