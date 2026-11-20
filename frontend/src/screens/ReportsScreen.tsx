import { useCallback, useEffect, useState } from "react";
import { api, ApiError, type EmployeeSummary, type Report } from "../api";
import { barPercent, datesBetween, presetPeriod, PRESETS, reportFileName, type Period, type PresetId } from "../reports";
import { formatDay, formatHM, formatZurichNow } from "../time";
import { useZurichClock } from "../useZurichClock";

/**
 * Manager-only reports (Plan 15): pick a period (and optionally one employee), see summary tiles, per-employee and
 * per-day totals, and download the same data as a CSV file. The server computes everything with the same rules as
 * My week and the Team tab, so the numbers always match.
 */
export default function ReportsScreen() {
  const { now } = useZurichClock();
  const today = now ? formatZurichNow(now).dateIso : null; // Zurich "today" from the server-seeded clock
  const [preset, setPreset] = useState<PresetId | "custom">("thisMonth");
  const [period, setPeriod] = useState<Period | null>(null);
  const [custom, setCustom] = useState<Period>({ from: "", to: "" });
  const [employeeId, setEmployeeId] = useState<number | undefined>();
  const [employees, setEmployees] = useState<EmployeeSummary[]>([]);
  const [report, setReport] = useState<Report | null>(null);
  const [loading, setLoading] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Start on "this month" as soon as we know today's Zurich date.
  useEffect(() => { if (today && !period) setPeriod(presetPeriod("thisMonth", today)); }, [today, period]);
  useEffect(() => { api.admin.employees().then(setEmployees).catch(() => {}); }, []);

  const load = useCallback(async () => {
    if (!period) return;
    setLoading(true);
    setError(null);
    try {
      setReport(await api.admin.report(period.from, period.to, employeeId));
    } catch (err) {
      setError(err instanceof ApiError ? `${err.title}${err.detail ? " " + err.detail : ""}` : "Couldn't load the report.");
    } finally {
      setLoading(false);
    }
  }, [period, employeeId]);
  useEffect(() => { void load(); }, [load]);

  const choosePreset = (id: PresetId) => {
    setPreset(id);
    if (today) setPeriod(presetPeriod(id, today));
  };
  const customValid = custom.from !== "" && custom.to !== "" && custom.from <= custom.to;

  const downloadCsv = async () => {
    if (!period) return;
    setDownloading(true);
    setError(null);
    try {
      const blob = await api.admin.exportReport(period.from, period.to, employeeId);
      // Hand the file to the browser as a download: a temporary link to the blob, clicked once, then cleaned up.
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = reportFileName(period);
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't download the file.");
    } finally {
      setDownloading(false);
    }
  };

  const t = report?.totals;
  const byDate = new Map(report?.days.map((d) => [d.date, d]) ?? []);
  const maxDay = Math.max(0, ...(report?.days.map((d) => d.totalMinutes) ?? []));
  const filteredName = employeeId ? employees.find((e) => e.id === employeeId)?.displayName : null;

  return (
    <section className="team-screen reports">
      <div className="panel">
        <div className="filters">
          <div className="segmented" role="group" aria-label="Period">
            {PRESETS.map((p) => (
              <button key={p.id} className={preset === p.id ? "selected" : ""} aria-pressed={preset === p.id}
                onClick={() => choosePreset(p.id)} disabled={!today}>{p.label}</button>
            ))}
            <button className={preset === "custom" ? "selected" : ""} aria-pressed={preset === "custom"}
              onClick={() => { setPreset("custom"); setCustom(period ?? custom); }}>Custom</button>
          </div>
          <select value={employeeId ?? ""} onChange={(e) => setEmployeeId(e.target.value ? Number(e.target.value) : undefined)}
            aria-label="Employee">
            <option value="">All employees</option>
            {employees.map((e) => <option key={e.id} value={e.id}>{e.displayName}{e.isActive ? "" : " (inactive)"}</option>)}
          </select>
          <button className="primary download" onClick={() => void downloadCsv()} disabled={!period || downloading}>
            {downloading ? "Preparing…" : "⬇ Download CSV"}
          </button>
        </div>
        {preset === "custom" && (
          <div className="filters custom-range">
            <label>From <input type="date" value={custom.from} onChange={(e) => setCustom({ ...custom, from: e.target.value })} /></label>
            <label>To <input type="date" value={custom.to} onChange={(e) => setCustom({ ...custom, to: e.target.value })} /></label>
            <button className="ghost" disabled={!customValid} onClick={() => setPeriod(custom)}>Show</button>
            {custom.from && custom.to && !customValid && <span className="field-error">"From" must be on or before "To".</span>}
          </div>
        )}
        <h2 className="report-title">
          {period ? `${formatDay(period.from)} – ${formatDay(period.to)} ${period.to.slice(0, 4)}` : "Report"}
          {filteredName && <> · {filteredName} <button className="link" onClick={() => setEmployeeId(undefined)}>show everyone</button></>}
          {loading && <span className="muted small"> · loading…</span>}
        </h2>
        {error && <div className="banner error" role="alert">{error}</div>}
      </div>

      {t && (
        <>
          <div className="tiles">
            <Tile label="Total hours" value={formatHM(t.totalMinutes)} />
            <Tile label="Shifts" value={t.shifts} />
            <Tile label="Employees who worked" value={t.employeesWorked} />
            <Tile label="🏢 Office hours" value={formatHM(t.officeMinutes)} />
            <Tile label="🏠 Remote hours" value={formatHM(t.remoteMinutes)} />
            {t.unknownLocationMinutes > 0 && <Tile label="Hours, location — (older shifts)" value={formatHM(t.unknownLocationMinutes)} />}
            <Tile label="Shifts over 14 h" value={t.overLimitShifts} warn={t.overLimitShifts > 0} />
            <Tile label="Corrected shifts" value={t.correctedShifts} />
            <Tile label="Still open" value={t.openShifts} warn={t.openShifts > 0} />
            <Tile label="Fix requests waiting" value={t.pendingRequests} warn={t.pendingRequests > 0} />
          </div>

          <div className="panel">
            <div className="panel-head"><h2>Per employee</h2></div>
            {report.employees.length === 0 ? <p className="muted">No shifts in this period.</p> : (
              <div className="table-scroll"><table className="shift-table">
                <thead><tr><th>Employee</th><th>Hours</th><th>Shifts</th><th>🏢 Office</th><th>🏠 Remote</th><th>Over 14 h</th><th>Corrected</th></tr></thead>
                <tbody>
                  {report.employees.map((e) => (
                    <tr key={e.employeeId}>
                      <td>
                        <button className="link" onClick={() => setEmployeeId(e.employeeId)} title="Show only this employee">{e.name}</button>
                        {e.openShifts > 0 && <span className="muted small"> · {e.openShifts} open</span>}
                      </td>
                      <td className="nums"><strong>{formatHM(e.totalMinutes)}</strong></td>
                      <td className="nums">{e.shifts}</td>
                      <td className="nums">{formatHM(e.officeMinutes)}</td>
                      <td className="nums">{formatHM(e.remoteMinutes)}</td>
                      <td className="nums">{e.overLimitShifts > 0 ? <span className="badge over-limit">{e.overLimitShifts}</span> : 0}</td>
                      <td className="nums">{e.correctedShifts}</td>
                    </tr>
                  ))}
                </tbody>
              </table></div>
            )}
          </div>

          <div className="panel">
            <div className="panel-head"><h2>Per day</h2></div>
            <ul className="day-bars">
              {datesBetween(report.from, report.to).map((date) => {
                const d = byDate.get(date);
                return (
                  <li key={date} className={d ? "" : "empty"}>
                    <span className="day-bar-date">{formatDay(date)}</span>
                    <span className="day-bar-track"><span className="day-bar" style={{ width: `${barPercent(d?.totalMinutes ?? 0, maxDay)}%` }} /></span>
                    <span className="day-bar-value nums">{d ? `${formatHM(d.totalMinutes)} · ${d.shifts}` : "-"}</span>
                  </li>
                );
              })}
            </ul>
            <p className="fineprint">Hours = finished shifts, each rounded to the nearest minute, counted on the day they started
              (Zurich time). Shifts still open aren't in the hours. The CSV has one row per shift, open ones included.</p>
          </div>
        </>
      )}
    </section>
  );
}

function Tile({ label, value, warn }: { label: string; value: string | number; warn?: boolean }) {
  return (
    <div className={`tile ${warn ? "warn" : ""}`}>
      <div className="tile-value nums">{value}</div>
      <div className="tile-label">{label}</div>
    </div>
  );
}
