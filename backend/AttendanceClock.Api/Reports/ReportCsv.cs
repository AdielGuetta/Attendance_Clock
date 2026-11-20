using System.Globalization;
using System.Text;
using AttendanceClock.Api.Domain;
using AttendanceClock.Api.Time;

namespace AttendanceClock.Api.Reports;

/// <summary>
/// Builds the CSV export (Plan 15): one row per shift, in a form Excel opens correctly and safely.
/// Built on the server, so the file uses exactly the same numbers as the screen.
/// </summary>
public static class ReportCsv
{
    public static readonly string[] Header =
        ["Date", "Employee", "Clock In", "Clock Out", "Zone", "Length (h:mm)", "Minutes", "Location", "Flags",
         "Clock In source", "Clock Out source"];

    public static string Build(IEnumerable<Shift> shifts)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var s in shifts)
        {
            var minutes = s.ClockOut is null ? (int?)null : ReportService.Minutes(s);
            AppendRow(csv,
            [
                ZurichTime.ToZurichDate(s.ClockIn).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                s.Employee.DisplayName,
                WallTime(s.ClockIn),
                s.ClockOut is { } o ? WallTime(o) : "in progress",
                // The zone of the Clock In; the Clock Out's can differ only on the two nights the clocks change.
                Zone(s.ClockIn),
                minutes is { } m ? $"{m / 60}:{m % 60:00}" : "",
                minutes?.ToString(CultureInfo.InvariantCulture) ?? "",
                s.WorkLocation?.ToString() ?? "",
                Flags(s),
                s.ClockInSource,
                s.ClockOutSource ?? "",
            ]);
        }
        return csv.ToString();
    }

    /// <summary>Zurich wall time as recorded (the stored value already carries the Zurich offset, PRD D-6).</summary>
    private static string WallTime(DateTimeOffset t) => t.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Zone(DateTimeOffset t) => t.Offset == TimeSpan.FromHours(2) ? "CEST" : "CET";

    private static string Flags(Shift s)
    {
        var flags = new List<string>();
        if (ReportService.IsOverLimit(s)) flags.Add("Over 14 h");
        if (s.Flags.HasFlag(ShiftFlags.Corrected)) flags.Add("Corrected");
        return string.Join(" / ", flags);
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string> cells)
    {
        csv.Append(string.Join(",", cells.Select(Escape)));
        csv.Append("\r\n"); // CSV standard (RFC 4180) line ending; Excel reads it everywhere
    }

    /// <summary>
    /// 1. Formula guard ("CSV injection"): a cell starting with = + - @ (or tab / return) would run as a formula when the
    ///    file is opened in Excel, e.g. a name typed as "=HYPERLINK(...)". A leading ' makes Excel show it as plain text.
    /// 2. Quoting: a cell with a comma, quote or line break is wrapped in quotes, with inner quotes doubled.
    /// </summary>
    public static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0]))
            value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
