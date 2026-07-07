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

    /// <summary>A coil ABC label — the tag that rides an inbound/processed coil (the label
    /// the legacy coil-receiving scanner prints to the Zebra printer).</summary>
    public static string CoilLabel(Coil c)
    {
        var body = $"""
            <div class="tag">
              <div class="tagHead"><span class="brand">ALUMINUM BLANKING CO.</span><span class="kind">COIL ABC LABEL</span></div>
              <div class="big">{c.CoilAbcNum}</div>
              {Barcode(c.CoilAbcNum.ToString())}
              <table class="kv">
                <tr><th>ABC #</th><td>{c.CoilAbcNum}</td><th>Org #</th><td>{Esc(c.CoilOrgNum) ?? "—"}</td></tr>
                <tr><th>Lot #</th><td>{Esc(c.LotNum) ?? "—"}</td><th>Mill id</th><td>{Esc(c.CoilMidNum) ?? "—"}</td></tr>
                <tr><th>Alloy</th><td>{Esc(c.CoilAlloy2) ?? "—"}</td><th>Temper</th><td>{Esc(c.CoilTemper) ?? "—"}</td></tr>
                <tr><th>Gauge</th><td>{Num(c.CoilGauge)}</td><th>Width</th><td>{Num(c.CoilWidth)}</td></tr>
                <tr><th>Net wt</th><td>{Wt(c.NetWt)}</td><th>Balance</th><td>{Wt(c.NetWtBalance)}</td></tr>
                <tr><th>Customer</th><td>{Opt(c.CustomerId)}</td><th>Location</th><td>{Esc(c.CoilLocation) ?? "—"}</td></tr>
                <tr><th>Received</th><td>{Dt(c.DateReceived)}</td><th>Status</th><td>{Opt(c.CoilStatus)}</td></tr>
              </table>
            </div>
            """;
        return Doc($"Coil {c.CoilAbcNum}", body);
    }

    /// <summary>A formal certificate of coil-ownership transfer — the toll-processing document
    /// that moves a coil's ownership from one customer to another (legacy
    /// <c>d_coil_ownership_transfer_certificate</c>). Full-page, letterhead-style, with a
    /// transferor→transferee block, coil details, authorization, and signature lines.</summary>
    public static string TransferCertificate(CoilOwnershipTransferCertificate c)
    {
        string Party(string role, string? name, string? shortName, long? id, string? city, string? state, string? zip) => $"""
            <div class="party">
              <div class="role">{role}</div>
              <div class="pname">{Esc(name) ?? Esc(shortName) ?? "—"}</div>
              <div class="paddr">{(Esc(shortName) is { } sn && !string.IsNullOrWhiteSpace(name) ? sn + " · " : "")}Customer #{Opt(id)}</div>
              <div class="paddr">{Loc(city, state, zip)}</div>
            </div>
            """;
        var body = $"""
            <div class="cert">
              <div class="certHead">
                <div class="brand">ALUMINUM BLANKING CO.</div>
                <div class="certTitle">CERTIFICATE OF COIL OWNERSHIP TRANSFER</div>
                <div class="certNo">Certificate #{c.CertificateNum}</div>
              </div>
              {Barcode(c.CertificateNum.ToString())}
              <div class="parties">
                {Party("TRANSFEROR (from)", c.CustomerFullNameOrig, c.CustomerShortNameOrig, c.CustomerIdOrig, c.CustomerCityOrig, c.CustomerStateOrig, c.CustomerZipOrig)}
                <div class="arrow">&#8594;</div>
                {Party("TRANSFEREE (to)", c.CustomerFullNameNew, c.CustomerShortNameNew, c.CustomerIdNew, c.CustomerCityNew, c.CustomerStateNew, c.CustomerZipNew)}
              </div>
              <table class="kv">
                <tr><th>Coil ABC # (orig)</th><td>{Opt(c.CoilAbcNumOrig)}</td><th>Coil ABC # (new)</th><td>{Opt(c.CoilAbcNumNew)}</td></tr>
                <tr><th>Org #</th><td>{Esc(c.CoilOrgNum) ?? "—"}</td><th>Lot #</th><td>{Esc(c.LotNum) ?? "—"}</td></tr>
                <tr><th>Alloy</th><td>{Esc(c.CoilAlloy2) ?? "—"}</td><th>Temper</th><td>{Esc(c.CoilTemper) ?? "—"}</td></tr>
                <tr><th>Gauge</th><td>{Num(c.CoilGauge)}</td><th>Width</th><td>{Num(c.CoilWidth)}</td></tr>
                <tr><th>Net wt</th><td>{Wt(c.NetWt)}</td><th>Balance</th><td>{Wt(c.NetWtBalance)}</td></tr>
              </table>
              <div class="auth">
                <div><span class="lbl">Authorization</span>{Esc(c.AuthorizationNote) ?? "—"}</div>
                <div><span class="lbl">Notes</span>{Esc(c.Notes) ?? "—"}</div>
              </div>
              <div class="sign">
                <div class="sigcol"><div class="sigline"></div>Performed by: {Esc(c.TransferPerformedBy) ?? "—"}</div>
                <div class="sigcol"><div class="sigline"></div>Date: {Dt(c.TransferDatetime)}</div>
              </div>
            </div>
            """;
        return Doc($"Transfer Certificate {c.CertificateNum}", body);
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
          .cert { width: 7.5in; max-width: 100%; margin: 12px auto; border: 3px double #111; padding: 22px 28px; background:#fff; }
          .certHead { text-align:center; border-bottom:2px solid #111; padding-bottom:10px; margin-bottom:6px; }
          .certHead .brand { font-size:16px; letter-spacing:.08em; font-weight:800; }
          .certTitle { font-size:20px; font-weight:800; letter-spacing:.04em; margin:6px 0 2px; }
          .certNo { font-size:12px; color:#444; }
          .parties { display:flex; align-items:center; gap:16px; margin:12px 0; }
          .party { flex:1; border:1px solid #bbb; border-radius:6px; padding:10px 12px; }
          .party .role { font-size:10px; letter-spacing:.06em; color:#666; font-weight:700; }
          .party .pname { font-size:15px; font-weight:700; margin-top:2px; }
          .party .paddr { font-size:12px; color:#333; }
          .parties .arrow { font-size:26px; font-weight:800; }
          .auth { margin-top:12px; font-size:12px; }
          .auth .lbl { font-weight:700; color:#555; margin-right:6px; }
          .sign { display:flex; gap:40px; margin-top:34px; }
          .sigcol { flex:1; font-size:12px; }
          .sigline { border-top:1px solid #111; margin-bottom:4px; height:26px; }
          @media print { body { background:#fff; } .tag, .cert { margin:0; } @page { margin: 6mm; } }
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

    private static string Loc(string? city, string? state, string? zip)
    {
        var joined = string.Join(", ", new[] { Esc(city), Esc(state), Esc(zip) }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        return joined.Length == 0 ? "—" : joined;
    }

    private static string Wt(decimal? v) => v is null ? "—" : $"{v.Value:0.#} lb";
    private static string Num(decimal? v) => v?.ToString("0.####") ?? "—";
    private static string Dt(DateTime? d) => d?.ToString("yyyy-MM-dd") ?? "—";
    private static string Opt(object? v) => v?.ToString() ?? "—";
    private static string? Esc(string? s) => s is null ? null : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
