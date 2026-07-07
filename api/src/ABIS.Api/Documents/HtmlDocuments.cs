using System.Text;
using Abis.Api.Models;

namespace Abis.Api.Documents;

/// <summary>
/// The greenfield document/print layer: renders **self-contained, printable HTML** for
/// physical shop-floor documents (skid tags first; invoices/BOLs/certs to follow on the
/// same base). Server-rendered so a document is canonical and reusable (view, print,
/// email, archive) rather than re-derived per client. No external dependencies — inline
/// CSS with an <c>@media print</c> block and an inline Code 39 barcode SVG.
/// </summary>
public static class HtmlDocuments
{
    /// <summary>A finished sheet-skid tag (the label that rides the skid through the plant).</summary>
    public static string SheetSkidTag(SheetSkid s)
    {
        var gross = (s.SheetNetWt ?? 0) + (s.SheetTareWt ?? 0);
        var body = $"""
            <div class="tag">
              <div class="tagHead"><span class="brand">ALUMINUM BLANKING CO.</span><span class="kind">SHEET SKID TAG</span></div>
              <div class="big">{Esc(s.SheetSkidDisplayNum) ?? s.SheetSkidNum.ToString()}</div>
              {Barcode(s.SheetSkidNum.ToString())}
              <table class="kv">
                <tr><th>Skid #</th><td>{s.SheetSkidNum}</td><th>Job #</th><td>{Opt(s.AbJobNum)}</td></tr>
                <tr><th>Net wt</th><td>{Wt(s.SheetNetWt)}</td><th>Tare wt</th><td>{Wt(s.SheetTareWt)}</td></tr>
                <tr><th>Gross wt</th><td>{Wt(gross)}</td><th>Pieces</th><td>{Opt(s.SkidPieces)}</td></tr>
                <tr><th>Date</th><td>{Dt(s.SkidDate)}</td><th>Location</th><td>{Esc(s.SkidLocation) ?? "—"}</td></tr>
                <tr><th>Status</th><td>{Opt(s.SkidSheetStatus)}</td><th>WH ticket</th><td>{Esc(s.SkidTicketIfWhed) ?? "—"}</td></tr>
              </table>
            </div>
            """;
        return Doc($"Sheet Skid {s.SheetSkidNum}", body);
    }

    /// <summary>A scrap-skid tag.</summary>
    public static string ScrapSkidTag(ScrapSkid s)
    {
        var gross = (s.ScrapNetWt ?? 0) + (s.ScrapTareWt ?? 0);
        var body = $"""
            <div class="tag">
              <div class="tagHead"><span class="brand">ALUMINUM BLANKING CO.</span><span class="kind">SCRAP SKID TAG</span></div>
              <div class="big">{s.ScrapSkidNum}</div>
              {Barcode(s.ScrapSkidNum.ToString())}
              <table class="kv">
                <tr><th>Scrap skid #</th><td>{s.ScrapSkidNum}</td><th>Job</th><td>{Esc(s.ScrapAbJobNum) ?? "—"}</td></tr>
                <tr><th>Alloy</th><td>{Esc(s.ScrapAlloy2) ?? "—"}</td><th>Temper</th><td>{Esc(s.ScrapTemper) ?? "—"}</td></tr>
                <tr><th>Net wt</th><td>{Wt(s.ScrapNetWt)}</td><th>Tare wt</th><td>{Wt(s.ScrapTareWt)}</td></tr>
                <tr><th>Gross wt</th><td>{Wt(gross)}</td><th>Type</th><td>{Opt(s.ScrapType)}</td></tr>
                <tr><th>Date</th><td>{Dt(s.ScrapDate)}</td><th>Location</th><td>{Esc(s.ScrapLocation) ?? "—"}</td></tr>
              </table>
            </div>
            """;
        return Doc($"Scrap Skid {s.ScrapSkidNum}", body);
    }

    // ---- shared rendering -------------------------------------------------

    private static string Doc(string title, string body) => $$"""
        <!DOCTYPE html>
        <html lang="en"><head><meta charset="utf-8" /><title>{{Esc(title)}}</title>
        <style>
          * { box-sizing: border-box; }
          body { font: 13px/1.4 system-ui, sans-serif; color:#111; margin:0; background:#f6f8fa; }
          .tag { width: 4in; margin: 12px auto; border: 2px solid #111; border-radius: 6px; padding: 10px 14px; background:#fff; }
          .tagHead { display:flex; justify-content:space-between; align-items:baseline; border-bottom:2px solid #111; padding-bottom:4px; }
          .brand { font-weight:700; letter-spacing:.03em; }
          .kind { font-size:11px; font-weight:700; color:#333; }
          .big { font-size:34px; font-weight:800; text-align:center; letter-spacing:.06em; margin:8px 0 4px; }
          svg.bc { display:block; margin:0 auto 6px; }
          table.kv { width:100%; border-collapse:collapse; margin-top:6px; }
          table.kv th { text-align:left; color:#555; font-weight:600; font-size:11px; width:16%; padding:3px 4px; border-bottom:1px solid #ddd; }
          table.kv td { padding:3px 4px; border-bottom:1px solid #ddd; font-weight:600; }
          @media print { body { background:#fff; } .tag { border-width:2px; margin:0; } @page { margin: 6mm; } }
        </style></head><body>{{body}}</body></html>
        """;

    // Inline Code 39 barcode as SVG (digits + a few symbols; '*' start/stop framing).
    private static string Barcode(string data, int narrow = 2, int height = 46)
    {
        const string chars = "0123456789-. $/+%";
        // Code 39 patterns: 9 elements per char (B S B S B S B S B), n=narrow w=wide.
        var pat = new Dictionary<char, string>
        {
            ['0'] = "nnnwwnwnn", ['1'] = "wnnwnnnnw", ['2'] = "nnwwnnnnw", ['3'] = "wnwwnnnnn",
            ['4'] = "nnnwwnnnw", ['5'] = "wnnwwnnnn", ['6'] = "nnwwwnnnn", ['7'] = "nnnwnnwnw",
            ['8'] = "wnnwnnwnn", ['9'] = "nnwwnnwnn", ['-'] = "wnnnnwnnw", ['.'] = "wnnnnwnnn",
            [' '] = "nnwnnwnnw", ['$'] = "wnwnwnnnn", ['/'] = "wnwnnnwnn", ['+'] = "wnnnwnwnn",
            ['%'] = "nnwnwnwnn", ['*'] = "nwnnwnwnn",
        };
        var framed = "*" + new string(data.ToUpperInvariant().Where(c => chars.Contains(c)).ToArray()) + "*";
        var sb = new StringBuilder();
        var x = 0;
        foreach (var c in framed)
        {
            if (!pat.TryGetValue(c, out var p)) continue;
            for (var i = 0; i < p.Length; i++)
            {
                var w = p[i] == 'w' ? narrow * 3 : narrow;
                if (i % 2 == 0) sb.Append($"<rect x='{x}' y='0' width='{w}' height='{height}' />");
                x += w;
            }
            x += narrow; // inter-character gap
        }
        return $"<svg class='bc' width='{x}' height='{height}' viewBox='0 0 {x} {height}' fill='#000' xmlns='http://www.w3.org/2000/svg'>{sb}</svg>";
    }

    private static string Wt(decimal? v) => v is null ? "—" : $"{v.Value:0.#} lb";
    private static string Dt(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
    private static string Opt(object? v) => v?.ToString() ?? "—";
    private static string? Esc(string? s) => s is null ? null : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
