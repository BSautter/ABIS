namespace Abis.Api.Scheduling;

/// <summary>Minute-granularity cron matcher for the scheduler engine. Supports the standard 5-field
/// form <c>minute hour day-of-month month day-of-week</c> (a leading 6th seconds field is ignored),
/// with <c>*</c>, lists (<c>1,15</c>), ranges (<c>1-5</c>), and steps (<c>*/5</c>, <c>0-30/10</c>).
/// Day-of-week is 0–6 (Sun=0); a <c>7</c> is treated as Sunday. When BOTH day-of-month and
/// day-of-week are restricted, either matching makes the day due (standard cron rule).</summary>
public static class CronSchedule
{
    public static bool IsDue(string? cron, DateTime utc)
    {
        if (string.IsNullOrWhiteSpace(cron)) return false;
        var f = cron.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (f.Length == 6) f = f[1..];      // drop a leading seconds field — we run at minute granularity
        if (f.Length != 5) return false;

        var minute = Match(f[0], utc.Minute, 0, 59);
        var hour = Match(f[1], utc.Hour, 0, 23);
        var month = Match(f[3], utc.Month, 1, 12);

        var domRestricted = f[2].Trim() != "*";
        var dowRestricted = f[4].Trim() != "*";
        var dom = Match(f[2], utc.Day, 1, 31);
        var dow = Match(f[4].Replace("7", "0"), (int)utc.DayOfWeek, 0, 6);   // Sun=0 (and 7→0)
        var dayOk = domRestricted && dowRestricted ? dom || dow : dom && dow;

        return minute && hour && month && dayOk;
    }

    private static bool Match(string field, int value, int lo, int hi)
    {
        foreach (var part in field.Split(','))
            if (MatchPart(part.Trim(), value, lo, hi)) return true;
        return false;
    }

    private static bool MatchPart(string part, int value, int lo, int hi)
    {
        var step = 1;
        var slash = part.IndexOf('/');
        if (slash >= 0)
        {
            if (!int.TryParse(part[(slash + 1)..], out step) || step <= 0) return false;
            part = part[..slash];
        }
        int start, end;
        if (part is "*" or "") { start = lo; end = hi; }
        else if (part.Contains('-'))
        {
            var r = part.Split('-');
            if (r.Length != 2 || !int.TryParse(r[0], out start) || !int.TryParse(r[1], out end)) return false;
        }
        else if (int.TryParse(part, out var single)) { start = end = single; }
        else return false;

        if (start < lo || end > hi || start > end) return false;
        return value >= start && value <= end && (value - start) % step == 0;
    }
}
